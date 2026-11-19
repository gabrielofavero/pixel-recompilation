using Port.Prepare;

if (args.Length is not (6 or 7))
{
    Console.Error.WriteLine("Usage: prepare-cli <LIVE-package> <new-private-stage> <private-root> <supported-versions.json> <patch-anchors.json> <FNA.dll> [audio-decoder-directory]");
    return 2;
}
try
{
    var result = PreparationEngine.Prepare(new PreparationOptions(args[0], args[1], args[2], args[3], args[4], args[5], typeof(Microsoft.Xna.Framework.GamerServices.Guide).Assembly.Location, args.Length == 7 ? args[6] : null));
    Console.WriteLine($"PREPARE_OK files={result.Files} xnb={result.Xnbs} output={result.OutputDirectory} manifestSha256={result.ManifestSha256}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"PREPARE_FAIL {ex.Message}");
    return 1;
}
