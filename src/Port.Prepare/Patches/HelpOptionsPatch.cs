using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Hooks;

namespace Port.Prepare.Patches;

internal static class HelpOptionsPatch
{
    internal const string Id = HelpOptions.PatchId;

    internal static void Apply(ModuleDefinition module, JsonElement anchors)
    {
        var allowed = anchors.EnumerateArray().Select(row => Convert.ToInt32(row.GetProperty("token").GetString()![2..], 16)).ToHashSet();
        var game = module.GetType("Scale.GameScale");
        var title = module.GetType("Scale.TITLE.TITLEMain");
        Postfix(Find(game, "Initialize"), nameof(HelpOptions.Initialize));
        var draw = Find(game, "Draw");
        Prefix(draw, nameof(HelpOptions.BeforeDraw), false);
        Postfix(draw, nameof(HelpOptions.AfterDraw));
        Prefix(Find(title, "ProcessSelection"), nameof(HelpOptions.Selection), true);
        Prefix(Find(title, "Display"), nameof(HelpOptions.TitleDisplay), true);

        MethodDefinition Find(TypeDefinition type, string name)
        {
            var method = type.Methods.Single(method => method.Name == name);
            if (!allowed.Contains(method.MetadataToken.ToInt32()))
                throw new InvalidDataException($"Help/options hook has no original IL anchor: {method.FullName}");
            return method;
        }
        MethodReference Import(string name) => module.ImportReference(typeof(HelpOptions).GetMethod(name)!);
        void Prefix(MethodDefinition method, string hook, bool handled)
        {
            var il = method.Body.GetILProcessor();
            var first = method.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, Import(hook)));
            if (handled)
            {
                il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
                il.InsertBefore(first, il.Create(OpCodes.Ret));
            }
        }
        void Postfix(MethodDefinition method, string hook)
        {
            var il = method.Body.GetILProcessor();
            foreach (var ret in method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray())
            {
                ret.OpCode = OpCodes.Ldarg_0;
                il.InsertAfter(ret, il.Create(OpCodes.Call, Import(hook)));
                il.InsertAfter(ret.Next, il.Create(OpCodes.Ret));
            }
        }
    }
}
