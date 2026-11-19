using Port.Prepare;
using Port.SetupHelper;

try
{
    if (args.Length == 3 && args[1] == "--destination" && args[0] is "verify-installed" or "rollback" or "finalize")
    {
        switch (args[0])
        {
            case "verify-installed": InstallerEngine.Verify(args[2]); break;
            case "rollback": InstallerEngine.Rollback(args[2]); break;
            case "finalize": InstallerEngine.FinalizeInstall(args[2]); break;
        }
        Console.WriteLine("INSTALL_" + args[0].ToUpperInvariant());
        return 0;
    }
    if (args.Length == 5 && args[0] == "validate-package" && args[1] == "--package" && args[3] == "--versions")
    {
        PreparationEngine.ValidatePackage(args[2], args[4]);
        Console.WriteLine("PACKAGE_VALID");
        return 0;
    }
    if (args.Length >= 7 && args[0] == "install" && args.Length % 2 == 1)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
            if (!values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate setup option.");
        if (!new[] { "--destination", "--payload", "--package" }.All(values.ContainsKey) ||
            values.Keys.Except(new[] { "--destination", "--payload", "--package", "--cancel-file" }).Any())
            throw new ArgumentException("Invalid setup options.");
        InstallerEngine.Install(values["--destination"], values["--payload"], values["--package"], values.GetValueOrDefault("--cancel-file"));
        Console.WriteLine("INSTALL_COMMITTED");
        return 0;
    }
    Console.Error.WriteLine("Usage: Port.SetupHelper install --destination <current> --payload <folder> --package <LIVE> [--cancel-file <file>] | verify-installed|rollback|finalize --destination <current> | validate-package --package <LIVE> --versions <supported-versions.json>");
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine($"SETUP_FAILED {error.GetType().Name}: {error.Message}");
    return 1;
}
