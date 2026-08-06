using System.Reflection;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var daemonPath = Path.Combine(repoRoot, "src", "MCServerLauncher.Daemon", "bin", "Release", "net10.0", "MCServerLauncher.Daemon.dll");
if (!File.Exists(daemonPath))
{
    Console.Error.WriteLine($"Daemon assembly not found at {daemonPath}. Build the tests first.");
    return 1;
}

var asm = Assembly.LoadFrom(daemonPath);
var manifestReaderType = asm.GetType("MCServerLauncher.Daemon.Plugins.PluginManifestReader", throwOnError: true)!;
var readMethod = manifestReaderType.GetMethod("ReadAndValidate", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
var generatedReaderType = asm.GetType("MCServerLauncher.Daemon.Plugins.GeneratedPluginMetadataReader", throwOnError: true)!;
var validateMethod = generatedReaderType.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

var fixturesRoot = Path.Combine(repoRoot, "tests", "Fixtures", "Plugins");
if (!Directory.Exists(fixturesRoot))
{
    Console.Error.WriteLine($"Fixtures root not found: {fixturesRoot}");
    return 1;
}

Console.WriteLine($"Inspecting fixtures in {fixturesRoot}");
foreach (var dir in Directory.EnumerateDirectories(fixturesRoot).OrderBy(p => p, StringComparer.Ordinal))
{
    Console.WriteLine($"\n== {Path.GetFileName(dir)} ==");
    try
    {
        var manifest = readMethod.Invoke(null, new object[] { dir, "1.0.0" });
        Console.WriteLine("ReadAndValidate: OK");
        try
        {
            validateMethod.Invoke(null, new object[] { manifest });
            Console.WriteLine("Generated metadata: OK");
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            var ie = tie.InnerException;
            var codeProp = ie.GetType().GetProperty("Code");
            var code = codeProp?.GetValue(ie) ?? "<no-code>";
            Console.WriteLine($"Generated metadata exception: {code}: {ie.Message}");
        }
    }
    catch (TargetInvocationException tie) when (tie.InnerException is not null)
    {
        var ie = tie.InnerException;
        var codeProp = ie.GetType().GetProperty("Code");
        var code = codeProp?.GetValue(ie) ?? "<no-code>";
        Console.WriteLine($"ReadAndValidate exception: {code}: {ie.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error: {ex}");
    }
}

return 0;
