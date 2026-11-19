using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Hooks;

namespace Port.Prepare.Patches;

internal static class UltrawidePatch
{
    internal const string Id = Ultrawide.PatchId;
    internal static void Apply(ModuleDefinition module, JsonElement anchors)
    {
        var allowed = anchors.EnumerateArray().Select(row => Convert.ToInt32(row.GetProperty("token").GetString()![2..], 16)).ToHashSet();
        var sprite = Find("LowLevel.CLASS_SPR", "Ark_SprDisplay");
        ReplaceWidth(sprite, OpCodes.Ldc_R4, 1280f, nameof(Ultrawide.GetWidthFloat));
        ReplaceWidth(Find("Scale.GAME.Level", "DrawTiles"), OpCodes.Ldc_I4, 1280, nameof(Ultrawide.GetWidth));
        Redirect(sprite, "Draw", nameof(Ultrawide.DrawSprite), true);
        Redirect(Find("LowLevel.CLASS_FNT", "Ark_FntDisplay"), "DrawString", nameof(Ultrawide.DrawText), false);

        // Level construction runs during Update, when FNA's viewport is the physical
        // backbuffer. Pin the simulation camera independently of that output size.
        var camera = Find("Scale.GAME.GAME_Camera", "GACM_Init");
        var il = camera.Body.GetILProcessor();
        var first = camera.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 1280));
        il.InsertBefore(first, il.Create(OpCodes.Starg, camera.Parameters[2]));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 720));
        il.InsertBefore(first, il.Create(OpCodes.Starg, camera.Parameters[3]));

        // The original scanner must hit-test against the same camera and full
        // cursor range as drawing, including objects in the added side strips.
        var scan = Find("Scale.GAME.Level", "ProcessScanMode");
        ReplaceWidths(scan, OpCodes.Ldc_R4, 1280f, nameof(Ultrawide.GetScanWidthFloat), 2);
        ReplaceWidths(scan, OpCodes.Ldc_I4, 1280, nameof(Ultrawide.GetScanWidth), 1);
        var scanIl = scan.Body.GetILProcessor();
        var scanFirst = scan.Body.Instructions[0];
        scanIl.InsertBefore(scanFirst, scanIl.Create(OpCodes.Ldarg_0));
        scanIl.InsertBefore(scanFirst, scanIl.Create(OpCodes.Call, Import(nameof(Ultrawide.BeforeScan))));
        foreach (var ret in scan.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray())
        {
            ret.OpCode = OpCodes.Ldarg_0;
            scanIl.InsertAfter(ret, scanIl.Create(OpCodes.Call, Import(nameof(Ultrawide.AfterScan))));
            scanIl.InsertAfter(ret.Next, scanIl.Create(OpCodes.Ret));
        }

        MethodDefinition Find(string type, string name)
        {
            var method = module.GetType(type).Methods.Single(method => method.Name == name);
            if (!allowed.Contains(method.MetadataToken.ToInt32()))
                throw new InvalidDataException($"Ultrawide patch has no original IL anchor: {method.FullName}");
            return method;
        }
        MethodReference Import(string name) => module.ImportReference(typeof(Ultrawide).GetMethod(name)!);
        void ReplaceWidth(MethodDefinition method, OpCode opcode, object value, string hook)
            => ReplaceWidths(method, opcode, value, hook, 1);
        void ReplaceWidths(MethodDefinition method, OpCode opcode, object value, string hook, int expected)
        {
            var sites = method.Body.Instructions.Where(instruction => instruction.OpCode == opcode && Equals(instruction.Operand, value)).ToArray();
            if (sites.Length != expected) throw new InvalidDataException($"Ultrawide width site count changed: {method.FullName}: {sites.Length}");
            foreach (var site in sites)
            {
                site.OpCode = OpCodes.Call;
                site.Operand = Import(hook);
            }
        }
        void Redirect(MethodDefinition method, string name, string hook, bool receiver)
        {
            var sites = method.Body.Instructions.Where(instruction => instruction.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch" && target.Name == name).ToArray();
            if (sites.Length != 1) throw new InvalidDataException($"Ultrawide drawing site count changed: {method.FullName}: {sites.Length}");
            if (receiver) method.Body.GetILProcessor().InsertBefore(sites[0], Instruction.Create(OpCodes.Ldarg_0));
            sites[0].OpCode = OpCodes.Call;
            sites[0].Operand = Import(hook);
        }
    }
}
