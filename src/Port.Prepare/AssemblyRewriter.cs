using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Port.Prepare;

internal static class AssemblyRewriter
{
    public static byte[] Rewrite(byte[] original, string expectedHash, string expectedMvid,
        JsonElement anchors, string fnaPath, string bridgePath, bool mainExecutable)
    {
        if (Convert.ToHexStringLower(SHA256.HashData(original)) != expectedHash)
            throw new InvalidDataException("Original game assembly hash differs from the supported version.");
        VerifyAnchors(original, expectedMvid, anchors);
        using var input = new MemoryStream(original, false);
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(input, new ReaderParameters { InMemory = true, ReadSymbols = false });
        if (!module.Attributes.HasFlag(Mono.Cecil.ModuleAttributes.Required32Bit))
            throw new InvalidDataException("Original game assembly no longer has the expected 32-bit requirement.");
        module.Attributes &= ~Mono.Cecil.ModuleAttributes.Required32Bit;
        var fnaName = AssemblyName.GetAssemblyName(fnaPath);
        var bridgeName = AssemblyName.GetAssemblyName(bridgePath);
        var fnaRef = Reference(fnaName);
        var bridgeRef = Reference(bridgeName);
        module.AssemblyReferences.Add(fnaRef);
        module.AssemblyReferences.Add(bridgeRef);
        var originalRefs = module.AssemblyReferences
            .Where(reference => reference.Name.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal)).ToArray();
        foreach (var type in module.GetTypeReferences())
        {
            if (type.Scope is not AssemblyNameReference scope || !originalRefs.Contains(scope)) continue;
            if (type.FullName == "Microsoft.Xna.Framework.Graphics.Color")
                type.Namespace = "Microsoft.Xna.Framework";
            type.Scope = type.Namespace == "Microsoft.Xna.Framework.GamerServices" ||
                         type.Namespace == "Microsoft.Xna.Framework.Storage" ||
                         type.FullName is "Microsoft.Xna.Framework.Graphics.EffectPool" or
                             "Microsoft.Xna.Framework.Graphics.SpriteBlendMode" or
                             "Microsoft.Xna.Framework.Graphics.SaveStateMode" or
                             "Microsoft.Xna.Framework.Graphics.RenderState" ? bridgeRef : fnaRef;
        }
        if (mainExecutable)
        {
            PatchLegacyGraphics(module, anchors);
            PatchContentLoads(module, anchors);
            PatchStorage(module, anchors);
            Patches.DesktopInputPatch.Apply(module, anchors);
            Patches.HelpOptionsPatch.Apply(module, anchors);
            Patches.UltrawidePatch.Apply(module, anchors);
        }
        foreach (var reference in originalRefs) module.AssemblyReferences.Remove(reference);
        using var output = new MemoryStream();
        module.Write(output, new WriterParameters { WriteSymbols = false });
        var result = output.ToArray();
        VerifyRetargeted(result, expectedMvid);
        return result;
    }

    private static AssemblyNameReference Reference(AssemblyName name) => new(name.Name!, name.Version!)
    {
        Culture = name.CultureName,
        PublicKeyToken = name.GetPublicKeyToken() ?? []
    };

    private static void PatchStorage(Mono.Cecil.ModuleDefinition module, JsonElement anchors)
    {
        var allowed = anchors.EnumerateArray()
            .Select(row => Convert.ToInt32(row.GetProperty("token").GetString()![2..], 16)).ToHashSet();
        var save = module.GetType("LowLevel.CLASS_SAVE");
        var data = save.NestedTypes.Single(type => type.Name == "CLASS_SAVEDATA");
        foreach (var (name, replacementName) in new[]
        {
            ("DoSaveGame", nameof(Port.Bridge.SaveStore.Save)),
            ("DoLoadGame", nameof(Port.Bridge.SaveStore.Load)),
            ("DoCreate", nameof(Port.Bridge.SaveStore.Create))
        })
        {
            var method = save.Methods.Single(method => method.Name == name);
            if (!allowed.Contains(method.MetadataToken.ToInt32()))
                throw new InvalidDataException($"Storage patch has no IL anchor: {method.FullName}");
            var replacement = new GenericInstanceMethod(module.ImportReference(typeof(Port.Bridge.SaveStore).GetMethod(replacementName)!));
            replacement.GenericArguments.Add(data);
            method.Body = new Mono.Cecil.Cil.MethodBody(method);
            var il = method.Body.GetILProcessor();
            foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, replacement);
            il.Emit(OpCodes.Ret);
        }
    }

    private static void PatchLegacyGraphics(Mono.Cecil.ModuleDefinition module, JsonElement anchors)
    {
        var allowed = anchors.EnumerateArray()
            .Select(row => Convert.ToInt32(row.GetProperty("token").GetString()![2..], 16))
            .ToHashSet();
        var bridge = typeof(Port.Bridge.LegacyGraphics);
        var effect = module.ImportReference(bridge.GetMethod(nameof(Port.Bridge.LegacyGraphics.CreateBasicEffect))!);
        var begin = module.ImportReference(bridge.GetMethod(nameof(Port.Bridge.LegacyGraphics.Begin))!);
        var render = module.ImportReference(bridge.GetMethod(nameof(Port.Bridge.LegacyGraphics.GetRenderState))!);
        var elements = module.ImportReference(bridge.GetMethod(nameof(Port.Bridge.LegacyGraphics.GetVertexElements))!);
        var declaration = module.ImportReference(bridge.GetMethod(nameof(Port.Bridge.LegacyGraphics.CreateVertexDeclaration))!);
        var magFilter = module.ImportReference(bridge.GetMethod(nameof(Port.Bridge.LegacyGraphics.SetMagFilter))!);
        int effects = 0, begins = 0, renders = 0, fields = 0, declarations = 0, filters = 0, addresses = 0;
        foreach (var type in Enumerate(module.Types))
        foreach (var method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is FieldReference field &&
                    field.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.VertexPositionColor" &&
                    field.Name == "VertexElements")
                {
                    if (!allowed.Contains(method.MetadataToken.ToInt32()))
                        throw new InvalidDataException($"Legacy vertex field patch has no IL anchor: {type.FullName}.{method.Name}");
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = elements;
                    fields++;
                    continue;
                }
                if (instruction.Operand is not MethodReference target) continue;
                MethodReference? replacement = null;
                if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.BasicEffect" &&
                    target.Name == ".ctor" && target.Parameters.Count == 2)
                {
                    replacement = effect;
                    effects++;
                }
                else if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch" &&
                         target.Name == "Begin" && target.Parameters.Count == 3)
                {
                    replacement = begin;
                    begins++;
                }
                else if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.GraphicsDevice" &&
                         target.Name == "get_RenderState" && target.Parameters.Count == 0)
                {
                    replacement = render;
                    renders++;
                }
                else if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.VertexDeclaration" &&
                         target.Name == ".ctor" && target.Parameters.Count == 2)
                {
                    replacement = declaration;
                    declarations++;
                }
                else if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SamplerState" &&
                         target.Name == "set_MagFilter" && target.Parameters.Count == 1)
                {
                    replacement = magFilter;
                    filters++;
                }
                else if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SamplerState" &&
                         target.Name is "set_AddressU" or "set_AddressV" && target.Parameters.Count == 1)
                {
                    replacement = module.ImportReference(bridge.GetMethod(target.Name == "set_AddressU"
                        ? nameof(Port.Bridge.LegacyGraphics.SetAddressU) : nameof(Port.Bridge.LegacyGraphics.SetAddressV))!);
                    addresses++;
                }
                if (replacement is null) continue;
                if (!allowed.Contains(method.MetadataToken.ToInt32()))
                    throw new InvalidDataException($"Legacy graphics patch has no IL anchor: {type.FullName}.{method.Name}");
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = replacement;
            }
        }
        if (effects != 1 || begins != 4 || renders != 12 || fields != 1 || declarations != 1 || filters != 1 || addresses != 8)
            throw new InvalidDataException($"Legacy graphics call-site count changed: effect={effects}, begin={begins}, render={renders}, fields={fields}, declarations={declarations}, filters={filters}, addresses={addresses}");
    }

    private static IEnumerable<Mono.Cecil.TypeDefinition> Enumerate(IEnumerable<Mono.Cecil.TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Enumerate(type.NestedTypes)) yield return nested;
        }
    }

    private static void PatchContentLoads(Mono.Cecil.ModuleDefinition module, JsonElement anchors)
    {
        var allowed = anchors.EnumerateArray()
            .Select(row => Convert.ToInt32(row.GetProperty("token").GetString()![2..], 16)).ToHashSet();
        var replacement = module.ImportReference(typeof(Port.Bridge.ContentProbe).GetMethod(nameof(Port.Bridge.ContentProbe.Load))!);
        int count = 0;
        foreach (var method in Enumerate(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is not GenericInstanceMethod target || target.Name != "Load" ||
                target.DeclaringType.FullName != "Microsoft.Xna.Framework.Content.ContentManager") continue;
            if (!allowed.Contains(method.MetadataToken.ToInt32()))
                throw new InvalidDataException($"Content probe has no IL anchor: {method.FullName}");
            var generic = new GenericInstanceMethod(replacement);
            generic.GenericArguments.Add(target.GenericArguments.Single());
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = generic;
            count++;
        }
        if (count != 4) throw new InvalidDataException($"Content load call-site count changed: {count}");
    }

    private static void VerifyAnchors(byte[] original, string expectedMvid, JsonElement anchors)
    {
        using var stream = new MemoryStream(original, false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("Game assembly has no .NET metadata.");
        var md = pe.GetMetadataReader();
        var mvid = md.GetGuid(md.GetModuleDefinition().Mvid).ToString();
        if (mvid != expectedMvid) throw new InvalidDataException($"Game assembly MVID differs: {mvid}");
        var names = new MetadataNames(md);
        foreach (var anchor in anchors.EnumerateArray())
        {
            int token = Convert.ToInt32(anchor.GetProperty("token").GetString()![2..], 16);
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.MethodDefinition)
                throw new InvalidDataException($"Invalid method anchor: 0x{token:X8}");
            var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            var expectedType = anchor.GetProperty("type").GetString();
            var expectedName = anchor.GetProperty("name").GetString();
            var expectedSignature = anchor.GetProperty("signature").GetString();
            var actualSignature = names.MethodSignature(method.DecodeSignature(names, null));
            if (names.Type(method.GetDeclaringType()) != expectedType || md.GetString(method.Name) != expectedName ||
                actualSignature != expectedSignature)
                throw new InvalidDataException($"Method signature guard failed at 0x{token:X8}");
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            if (il is null || Convert.ToHexStringLower(SHA256.HashData(il)) != anchor.GetProperty("ilSha256").GetString())
                throw new InvalidDataException($"Method IL guard failed at 0x{token:X8}");
        }
    }

    private static void VerifyRetargeted(byte[] rewritten, string expectedMvid)
    {
        using var stream = new MemoryStream(rewritten, false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("Rewritten assembly has no .NET metadata.");
        if (pe.PEHeaders.CorHeader?.Flags.HasFlag(CorFlags.Requires32Bit) != false)
            throw new InvalidDataException("Rewritten assembly still requires a 32-bit process.");
        var md = pe.GetMetadataReader();
        if (md.GetGuid(md.GetModuleDefinition().Mvid).ToString() != expectedMvid)
            throw new InvalidDataException("Rewriter changed the original assembly MVID.");
        var references = md.AssemblyReferences
            .Select(handle => md.GetString(md.GetAssemblyReference(handle).Name)).ToHashSet(StringComparer.Ordinal);
        if (references.Any(name => name.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal)) ||
            !references.Contains("FNA") || !references.Contains("Port.Bridge"))
            throw new InvalidDataException("Rewritten assembly has unexpected framework references.");
        var names = new MetadataNames(md);
        foreach (var handle in md.TypeReferences)
        {
            var type = md.GetTypeReference(handle);
            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
            var name = names.Type(handle);
            var scope = names.Scope(type.ResolutionScope);
            if (name.StartsWith("Microsoft.Xna.Framework.GamerServices.", StringComparison.Ordinal) ||
                name is "Microsoft.Xna.Framework.Storage.StorageDevice" or "Microsoft.Xna.Framework.Storage.StorageContainer" or
                    "Microsoft.Xna.Framework.Graphics.EffectPool" or "Microsoft.Xna.Framework.Graphics.SpriteBlendMode" or
                    "Microsoft.Xna.Framework.Graphics.SaveStateMode" or "Microsoft.Xna.Framework.Graphics.RenderState")
            {
                if (scope != "Port.Bridge") throw new InvalidDataException($"Unmapped bridge type: {name}");
            }
            else if (name.StartsWith("Microsoft.Xna.Framework.", StringComparison.Ordinal) && scope != "FNA")
                throw new InvalidDataException($"Unmapped framework type: {name}");
            if (name == "Microsoft.Xna.Framework.Graphics.Color")
                throw new InvalidDataException("Legacy graphics Color was not retargeted to FNA.");
        }
    }
}
