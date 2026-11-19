using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Hooks;

namespace Port.Prepare.Patches;

// Enhancement patch: thin calls into Port.Hooks, retaining the original method bodies.
internal static class DesktopInputPatch
{
    internal const string Id = DesktopInput.PatchId;

    internal static void Apply(ModuleDefinition module, JsonElement anchors)
    {
        var allowed = anchors.EnumerateArray()
            .Select(row => Convert.ToInt32(row.GetProperty("token").GetString()![2..], 16)).ToHashSet();
        var update = module.GetType("LowLevel.CLASS_GAMEPAD").Methods.Single(method => method.Name == "Update");
        if (!allowed.Contains(update.MetadataToken.ToInt32()))
            throw new InvalidDataException($"Desktop input hook has no original IL anchor: {update.FullName}");

        var before = Import(nameof(DesktopInput.BeforeUpdate));
        var after = Import(nameof(DesktopInput.AfterUpdate));
        var keyboard = Import(nameof(DesktopInput.GetKeyboardState));
        var il = update.Body.GetILProcessor();
        var first = update.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, before));
        int replaced = 0;
        foreach (var instruction in update.Body.Instructions)
            if (instruction.Operand is MethodReference method && method.DeclaringType.FullName == "Microsoft.Xna.Framework.Input.Keyboard" &&
                method.Name == "GetState" && method.Parameters.Count == 0)
            {
                instruction.Operand = keyboard;
                replaced++;
            }
        if (replaced != 1) throw new InvalidDataException($"Desktop keyboard sample site count changed: {replaced}");
        AppendHook(update, after);

        MethodReference Import(string name) => module.ImportReference(typeof(DesktopInput).GetMethod(name)!);
    }

    private static void AppendHook(MethodDefinition method, MethodReference hook)
    {
        var il = method.Body.GetILProcessor();
        foreach (var instruction in method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray())
        {
            // Reuse the ret as the hook entry so branches to a return also run the postfix.
            instruction.OpCode = OpCodes.Ldarg_0;
            il.InsertAfter(instruction, il.Create(OpCodes.Call, hook));
            il.InsertAfter(instruction.Next, il.Create(OpCodes.Ret));
        }
    }
}
