using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is not (1 or 3 or 5) || args.Length % 2 == 0)
{
    Console.Error.WriteLine("Usage: inspect_360_assembly <extracted.exe|content.dll> [--output report.json] [--manifest supported-versions.json]");
    return 2;
}

string? output = null;
var manifestPath = Path.Combine("config", "supported-versions.json");
for (var i = 1; i < args.Length; i += 2)
{
    if (args[i] == "--output" && output is null) output = args[i + 1];
    else if (args[i] == "--manifest") manifestPath = args[i + 1];
    else
    {
        Console.Error.WriteLine($"Unknown or repeated option: {args[i]}");
        return 2;
    }
}

try
{
    var bytes = File.ReadAllBytes(args[0]);
    var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var package = manifest.RootElement.GetProperty("packages").EnumerateArray()
        .FirstOrDefault(p => p.GetProperty("assemblySha256").GetString() == sha256 ||
                             p.GetProperty("contentAssemblySha256").GetString() == sha256);
    if (package.ValueKind == JsonValueKind.Undefined)
        throw new InvalidDataException($"Unsupported managed assembly SHA-256 {sha256}.");
    var isMain = package.GetProperty("assemblySha256").GetString() == sha256;
    var expectedPath = package.GetProperty(isMain ? "assemblyPath" : "contentAssemblyPath").GetString()!;
    var expectedMvid = package.GetProperty(isMain ? "assemblyMvid" : "contentAssemblyMvid").GetString()!;

    using var pe = new PEReader(new MemoryStream(bytes));
    if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null)
        throw new BadImageFormatException("Input has no managed .NET metadata.");
    var md = pe.GetMetadataReader();
    var names = new MetadataNames(md);
    var assembly = md.GetAssemblyDefinition();
    var mvid = md.GetGuid(md.GetModuleDefinition().Mvid).ToString();
    if (!string.Equals(mvid, expectedMvid, StringComparison.OrdinalIgnoreCase) ||
        md.GetString(assembly.Name) != Path.GetFileNameWithoutExtension(expectedPath) ||
        assembly.Version.ToString() != "1.0.0.0")
        throw new InvalidDataException("Managed assembly identity or MVID differs from the supported version.");

    var assemblyRefs = md.AssemblyReferences.Select(h =>
    {
        var r = md.GetAssemblyReference(h);
        return new
        {
            token = Token(h), name = md.GetString(r.Name), version = r.Version.ToString(),
            culture = r.Culture.IsNil ? "" : md.GetString(r.Culture),
            publicKeyOrToken = Convert.ToHexString(md.GetBlobBytes(r.PublicKeyOrToken)).ToLowerInvariant()
        };
    }).ToArray();
    var typeRefs = md.TypeReferences.Select(h =>
    {
        var r = md.GetTypeReference(h);
        return new { token = Token(h), name = names.Type(h), scope = names.Scope(r.ResolutionScope) };
    }).ToArray();
    var memberRefs = md.MemberReferences.Select(h =>
    {
        var r = md.GetMemberReference(h);
        return new
        {
            token = Token(h), parent = names.Entity(r.Parent), name = md.GetString(r.Name),
            kind = r.GetKind().ToString(), signature = names.MemberSignature(r)
        };
    }).ToArray();
    var types = md.TypeDefinitions.Select(h =>
    {
        var type = md.GetTypeDefinition(h);
        return new
        {
            token = Token(h), name = names.Type(h),
            @base = type.BaseType.IsNil ? null : names.Entity(type.BaseType),
            interfaces = type.GetInterfaceImplementations()
                .Select(ih => names.Entity(md.GetInterfaceImplementation(ih).Interface)).ToArray(),
            explicitOverrides = type.GetMethodImplementations().Select(ih =>
            {
                var implementation = md.GetMethodImplementation(ih);
                return new { body = names.Member(implementation.MethodBody),
                    declaration = names.Member(implementation.MethodDeclaration) };
            }).ToArray(),
            virtualReuseSlotCandidates = type.GetMethods().Where(mh =>
            {
                var attributes = md.GetMethodDefinition(mh).Attributes;
                return attributes.HasFlag(MethodAttributes.Virtual) && !attributes.HasFlag(MethodAttributes.NewSlot);
            }).Select(mh => names.Member(mh)).ToArray(),
            virtualOverrides = type.GetMethods().Where(mh =>
            {
                var attributes = md.GetMethodDefinition(mh).Attributes;
                return attributes.HasFlag(MethodAttributes.Virtual) && !attributes.HasFlag(MethodAttributes.NewSlot);
            }).Select(mh => new
            {
                body = names.Member(mh),
                baseChain = BaseChain(md, names, type.BaseType)
            }).ToArray()
        };
    }).ToArray();
    var methods = md.MethodDefinitions.Select(h =>
    {
        var method = md.GetMethodDefinition(h);
        var il = method.RelativeVirtualAddress == 0 ? null : pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        var attributes = method.Attributes;
        var virtualSlot = !attributes.HasFlag(MethodAttributes.Virtual) ? "nonvirtual" :
            attributes.HasFlag(MethodAttributes.NewSlot) ? "new-slot" : "reuse-slot";
        return new
        {
            token = Token(h), type = names.Type(method.GetDeclaringType()), name = md.GetString(method.Name),
            signature = names.MethodSignature(method.DecodeSignature(names, null)),
            attributes = attributes.ToString(), virtualSlot, rva = method.RelativeVirtualAddress,
            ilSha256 = il is null ? null : Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant()
        };
    }).ToArray();
    var contentReaderRefs = typeRefs.Where(r => r.name.Contains("ContentReader", StringComparison.Ordinal) ||
                                                 r.name.Contains("ContentManager", StringComparison.Ordinal))
        .Select(r => new { r.token, r.name, r.scope }).ToArray();
    var contentReaderDefinitions = types.Where(t => t.name.Contains("ContentReader", StringComparison.Ordinal) ||
                                                    t.@base?.Contains("ContentTypeReader", StringComparison.Ordinal) == true)
        .Select(t => new { t.token, t.name, t.@base }).ToArray();
    var contentMemberRefs = memberRefs.Where(r => r.parent.Contains("ContentReader", StringComparison.Ordinal) ||
                                                  r.parent.Contains("ContentManager", StringComparison.Ordinal) ||
                                                  r.parent.Contains("ContentTypeReader", StringComparison.Ordinal))
        .ToArray();
    var serviceReferences = memberRefs.Where(r => r.parent.Contains("GamerServices", StringComparison.OrdinalIgnoreCase) ||
                                                   r.parent.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var report = new
    {
        schemaVersion = 2,
        input = new { name = Path.GetFileName(args[0]), bytes = bytes.Length, sha256 },
        assembly = new
        {
            name = md.GetString(assembly.Name), version = assembly.Version.ToString(), mvid,
            entryPointToken = $"0x{pe.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress:x8}",
            metadataVersion = md.MetadataVersion
        },
        counts = new
        {
            types = md.TypeDefinitions.Count, methods = md.MethodDefinitions.Count,
            assemblyRefs = md.AssemblyReferences.Count, typeRefs = md.TypeReferences.Count,
            memberRefs = md.MemberReferences.Count, resources = md.ManifestResources.Count
        },
        assemblyRefs, typeRefs, memberRefs, types, methods,
        contentReaderRefs, contentReaderDefinitions, contentMemberRefs, serviceReferences
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    if (output is null) Console.Write(json);
    else
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, json);
        Console.WriteLine($"ASSEMBLY_INVENTORY_OK {report.assembly.name} methods={report.counts.methods} memberRefs={report.counts.memberRefs}");
    }
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException or InvalidOperationException or
                                     JsonException or ArgumentException or KeyNotFoundException)
{
    Console.Error.WriteLine($"ASSEMBLY_INVENTORY_FAIL {error.GetType().Name}: {error.Message}");
    return 1;
}

static string Token(EntityHandle handle) => $"0x{MetadataTokens.GetToken(handle):x8}";

static string[] BaseChain(MetadataReader md, MetadataNames names, EntityHandle baseType)
{
    var chain = new List<string>();
    while (!baseType.IsNil)
    {
        chain.Add(names.Entity(baseType));
        if (baseType.Kind != HandleKind.TypeDefinition) break;
        baseType = md.GetTypeDefinition((TypeDefinitionHandle)baseType).BaseType;
    }
    return chain.ToArray();
}
