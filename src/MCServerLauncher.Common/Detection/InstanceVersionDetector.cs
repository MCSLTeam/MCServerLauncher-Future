using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Detection;

public static partial class InstanceVersionDetector
{
    private static readonly Regex VersionLikeRegex = VersionRegex();
    private static readonly Regex MetaInfVersionRegex = MetaInfVersionFileRegex();
    private static readonly Regex FileVersionRegex = FileVersionRegexFactory();
    private static readonly Regex BaseVersionRegex = BaseVersionRegexFactory();
    private static readonly Regex PhpConstVersionRegex = PhpConstVersionRegexFactory();
    private static readonly Regex SteamBuildIdRegex = SteamBuildIdRegexFactory();
    private static readonly Regex SteamLastUpdatedRegex = SteamLastUpdatedRegexFactory();

    private static readonly JavaDetectionRule[] JavaRules =
    [
        new(InstanceType.MCArclight, DetectArclightVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCFabric, DetectFabricVersion, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCSpongeVanilla, DetectSpongeVanillaVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCSpongeForge, DetectSpongeForgeVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCForge, DetectForgeVersion, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCNeoForge, DetectNeoForgeVersion, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCQuilt, DetectVersionJson, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCCleanroom, DetectVersionJson, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCTaiyitist, DetectVersionJson, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCPaper, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCLeaf, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCLeaves, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCFolia, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCCanvas, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCPufferfish, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCPurpur, DetectPatchPropertiesOrVersionJson, InstanceVersionDetectionSource.PatchProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCMohist, DetectMinecraftVersionProperties, InstanceVersionDetectionSource.VersionProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCBanner, DetectMinecraftVersionProperties, InstanceVersionDetectionSource.VersionProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCYouer, DetectMinecraftVersionProperties, InstanceVersionDetectionSource.VersionProperties, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCThermos, DetectForgeInstallProfileVersion, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCCrucible, DetectForgeInstallProfileVersion, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCCatServer, DetectCatServerVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Exact),
        new(InstanceType.MCCraftBukkit, DetectCraftBukkitVersion, InstanceVersionDetectionSource.MetaInfVersion, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCSpigot, DetectSpigotVersion, InstanceVersionDetectionSource.MetaInfVersion, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCSpongeNeo, DetectSpongeNeoVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCBungeeCord, DetectManifestVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCVelocity, DetectManifestVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCWaterfall, DetectManifestVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCTravertine, DetectManifestVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCGeyser, DetectManifestVersion, InstanceVersionDetectionSource.Manifest, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCViaVersion, DetectViaVersion, InstanceVersionDetectionSource.PluginYaml, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCDReforged, DetectDReforged, InstanceVersionDetectionSource.McdReforgedPluginJson, InstanceVersionDetectionConfidence.Strong),
        new(InstanceType.MCVanilla, DetectVersionJson, InstanceVersionDetectionSource.VersionJson, InstanceVersionDetectionConfidence.Exact)
    ];
    public static InstanceConfig Reconcile(InstanceConfig config, string workingDirectory)
    {
        var detection = Detect(config, workingDirectory);
        if (!detection.IsMatched)
            return config;

        var versionToPersist = detection.MatchedInstanceType.RequiresNumericMinecraftVersion()
            ? detection.NormalizedMcVersion ?? config.Version
            : detection.Version ?? config.Version;

        return config with
        {
            InstanceType = detection.MatchedInstanceType,
            Version = versionToPersist ?? string.Empty
        };
    }

    public static InstanceFactorySetting Reconcile(InstanceFactorySetting setting, Func<string, string>? resolveSource = null)
    {
        var detection = Detect(setting, resolveSource);
        if (!detection.IsMatched)
            return setting;

        var resolvedType = ShouldSwitchFactoryType(setting.InstanceType, detection.MatchedInstanceType)
            ? detection.MatchedInstanceType
            : setting.InstanceType;

        var versionToPersist = resolvedType.RequiresNumericMinecraftVersion()
            ? detection.NormalizedMcVersion ?? setting.Version
            : detection.Version ?? setting.Version;

        return setting with
        {
            InstanceType = resolvedType,
            Version = versionToPersist ?? string.Empty
        };
    }

    public static InstanceVersionDetectionResult Detect(InstanceConfig config, string workingDirectory)
    {
        try
        {
            var targetPath = Path.Combine(workingDirectory, config.Target);
            var candidateFiles = EnumerateCandidates(workingDirectory, targetPath, config.TargetType);
            return DetectCore(config.InstanceType, targetPath, workingDirectory, candidateFiles);
        }
        catch (Exception ex)
        {
            return InstanceVersionDetectionResult.NoMatch(config.InstanceType, ex.Message);
        }
    }

    public static InstanceVersionDetectionResult Detect(InstanceFactorySetting setting, Func<string, string>? resolveSource = null)
    {
        resolveSource ??= Path.GetFullPath;
        try
        {
            if (!TryResolveSourcePath(setting.Source, resolveSource, out var sourcePath))
                return InstanceVersionDetectionResult.NoMatch(setting.InstanceType, "Source path unavailable for detection");

            var workingDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            var candidateFiles = EnumerateSourceCandidates(sourcePath);
            return DetectCore(setting.InstanceType, sourcePath, workingDirectory, candidateFiles);
        }
        catch (Exception ex)
        {
            return InstanceVersionDetectionResult.NoMatch(setting.InstanceType, ex.Message);
        }
    }

    private static InstanceVersionDetectionResult DetectCore(
        InstanceType instanceType,
        string targetPath,
        string workingDirectory,
        IReadOnlyList<string> candidateFiles)
    {
        if (instanceType == InstanceType.Universal)
            return InstanceVersionDetectionResult.NoMatch(instanceType, "Universal instances skip detection");

        if (instanceType == InstanceType.MCJava)
            return DetectGenericJava(instanceType, candidateFiles, targetPath, workingDirectory);

        if (instanceType.IsMinecraftJavaRuntimeType() || IsJavaMetadataType(instanceType))
            return DetectSpecificJava(instanceType, candidateFiles, targetPath);

        return instanceType switch
        {
            InstanceType.MCBedrock => DetectBedrock(instanceType, candidateFiles, targetPath),
            InstanceType.MCBDS => DetectBedrock(instanceType, candidateFiles, targetPath, preferBds: true),
            InstanceType.MCNukkit => DetectManifestType(instanceType, candidateFiles, targetPath),
            InstanceType.MCCloudburst => DetectManifestType(instanceType, candidateFiles, targetPath),
            InstanceType.MCPocketMine => DetectPocketMine(instanceType, candidateFiles),
            InstanceType.Terraria => DetectTerraria(instanceType, candidateFiles, targetPath),
            InstanceType.TShock => DetectTShock(instanceType, candidateFiles, targetPath),
            InstanceType.TDSM => DetectTdsm(instanceType, candidateFiles, targetPath),
            InstanceType.SteamServer => DetectSteamServer(instanceType, candidateFiles, workingDirectory),
            _ => InstanceVersionDetectionResult.NoMatch(instanceType)
        };
    }

    private static InstanceVersionDetectionResult DetectGenericJava(
        InstanceType originalType,
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        string workingDirectory)
    {
        foreach (var rule in JavaRules)
        {
            if (!LikelyMatchesJavaType(rule.InstanceType, candidateFiles, targetPath))
                continue;

            var version = rule.Detector(candidateFiles, targetPath, workingDirectory);
            if (string.IsNullOrWhiteSpace(version) && rule.InstanceType == InstanceType.MCFabric)
                version = DetectFabricLauncherFilenameVersion(candidateFiles.Append(targetPath));
            if (string.IsNullOrWhiteSpace(version))
                continue;

            var normalizedMcVersion = rule.InstanceType.RequiresNumericMinecraftVersion()
                ? NormalizeMinecraftVersion(version)
                : null;

            return new InstanceVersionDetectionResult(
                originalType,
                rule.InstanceType,
                version,
                normalizedMcVersion,
                rule.Source,
                rule.Confidence,
                $"Matched {rule.InstanceType} by {rule.Source}"
            );
        }

        return InstanceVersionDetectionResult.NoMatch(originalType, "No Java detection rule matched");
    }

    private static InstanceVersionDetectionResult DetectSpecificJava(
        InstanceType instanceType,
        IReadOnlyList<string> candidateFiles,
        string targetPath)
    {
        var rule = JavaRules.FirstOrDefault(rule => rule.InstanceType == instanceType);
        if (rule is null)
            return InstanceVersionDetectionResult.NoMatch(instanceType, $"No detector registered for {instanceType}");

        var version = rule.Detector(candidateFiles, targetPath, Path.GetDirectoryName(targetPath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(version))
            return InstanceVersionDetectionResult.NoMatch(instanceType, $"Detector {rule.Source} produced no version");

        var normalizedMcVersion = instanceType.RequiresNumericMinecraftVersion()
            ? NormalizeMinecraftVersion(version)
            : null;

        return new InstanceVersionDetectionResult(
            instanceType,
            instanceType,
            version,
            normalizedMcVersion,
            rule.Source,
            rule.Confidence,
            $"Matched {instanceType} by {rule.Source}"
        );
    }

    private static InstanceVersionDetectionResult DetectBedrock(
        InstanceType originalType,
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        bool preferBds = false)
    {
        if (ContainsAnyName(candidateFiles, "bedrock_server", "bedrock-server") ||
            ContainsAnyName([targetPath], "bedrock_server", "bedrock-server"))
        {
            var version = DetectVersionFromFileNames(candidateFiles.Append(targetPath));
            var detectedType = preferBds || version is not null ? InstanceType.MCBDS : InstanceType.MCBedrock;
            return new InstanceVersionDetectionResult(
                originalType,
                detectedType,
                version,
                null,
                InstanceVersionDetectionSource.BinaryStrings,
                InstanceVersionDetectionConfidence.Fallback,
                "Matched Bedrock server binary or archive name"
            );
        }

        if (originalType == InstanceType.MCBedrock)
        {
            var nukkit = DetectManifestType(InstanceType.MCNukkit, candidateFiles, targetPath);
            if (nukkit.IsMatched)
                return new InstanceVersionDetectionResult(
                    originalType,
                    nukkit.MatchedInstanceType,
                    nukkit.Version,
                    nukkit.NormalizedMcVersion,
                    nukkit.Source,
                    nukkit.Confidence,
                    nukkit.Evidence
                );

            var cloudburst = DetectManifestType(InstanceType.MCCloudburst, candidateFiles, targetPath);
            if (cloudburst.IsMatched)
                return new InstanceVersionDetectionResult(
                    originalType,
                    cloudburst.MatchedInstanceType,
                    cloudburst.Version,
                    cloudburst.NormalizedMcVersion,
                    cloudburst.Source,
                    cloudburst.Confidence,
                    cloudburst.Evidence
                );

            var pocketMine = DetectPocketMine(originalType, candidateFiles);
            if (pocketMine.IsMatched)
                return new InstanceVersionDetectionResult(
                    originalType,
                    pocketMine.MatchedInstanceType,
                    pocketMine.Version,
                    pocketMine.NormalizedMcVersion,
                    pocketMine.Source,
                    pocketMine.Confidence,
                    pocketMine.Evidence
                );
        }

        return InstanceVersionDetectionResult.NoMatch(originalType, "No Bedrock detection rule matched");
    }

    private static InstanceVersionDetectionResult DetectManifestType(
        InstanceType instanceType,
        IReadOnlyList<string> candidateFiles,
        string targetPath)
    {
        var version = DetectManifestVersion(candidateFiles, targetPath, string.Empty);
        return string.IsNullOrWhiteSpace(version)
            ? InstanceVersionDetectionResult.NoMatch(instanceType, "Manifest version not found")
            : new InstanceVersionDetectionResult(
                instanceType,
                instanceType,
                version,
                null,
                InstanceVersionDetectionSource.Manifest,
                InstanceVersionDetectionConfidence.Strong,
                $"Matched {instanceType} by manifest"
            );
    }

    private static InstanceVersionDetectionResult DetectPocketMine(
        InstanceType originalType,
        IReadOnlyList<string> candidateFiles)
    {
        foreach (var file in candidateFiles)
        {
            if (!File.Exists(file))
                continue;

            if (!file.EndsWith(".phar", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryReadTextEntry(file, "src/pocketmine/VersionInfo.php", out var versionInfo) ||
                TryReadTextEntry(file, "pocketmine/VersionInfo.php", out versionInfo))
            {
                var version = ExtractByRegex(versionInfo, BaseVersionRegex);
                if (!string.IsNullOrWhiteSpace(version))
                    return new InstanceVersionDetectionResult(
                        originalType,
                        InstanceType.MCPocketMine,
                        version,
                        null,
                        InstanceVersionDetectionSource.VersionInfoPhp,
                        InstanceVersionDetectionConfidence.Strong,
                        "Matched PocketMine VersionInfo.php"
                    );
            }

            if (TryReadTextEntry(file, "src/pocketmine/PocketMine.php", out var pocketMinePhp) ||
                TryReadTextEntry(file, "pocketmine/PocketMine.php", out pocketMinePhp))
            {
                var version = ExtractByRegex(pocketMinePhp, PhpConstVersionRegex);
                if (!string.IsNullOrWhiteSpace(version))
                    return new InstanceVersionDetectionResult(
                        originalType,
                        InstanceType.MCPocketMine,
                        version,
                        null,
                        InstanceVersionDetectionSource.VersionInfoPhp,
                        InstanceVersionDetectionConfidence.Fallback,
                        "Matched PocketMine VERSION constant"
                    );
            }
        }

        return InstanceVersionDetectionResult.NoMatch(originalType, "PocketMine version info not found");
    }

    private static InstanceVersionDetectionResult DetectTerraria(
        InstanceType instanceType,
        IReadOnlyList<string> candidateFiles,
        string targetPath)
    {
        foreach (var file in candidateFiles.Append(targetPath))
        {
            if (!File.Exists(file))
                continue;

            if (!Path.GetFileName(file).Contains("TerrariaServer", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(file).Contains("terraria-server", StringComparison.OrdinalIgnoreCase))
                continue;

            var version = TryGetFileVersion(file) ?? DetectVersionFromFileNames([file]);
            if (!string.IsNullOrWhiteSpace(version))
                return new InstanceVersionDetectionResult(
                    instanceType,
                    instanceType,
                    version,
                    null,
                    InstanceVersionDetectionSource.ExecutableMetadata,
                    InstanceVersionDetectionConfidence.Strong,
                    "Matched Terraria executable version"
                );
        }

        return InstanceVersionDetectionResult.NoMatch(instanceType, "Terraria executable version not found");
    }

    private static InstanceVersionDetectionResult DetectTShock(
        InstanceType instanceType,
        IReadOnlyList<string> candidateFiles,
        string targetPath)
    {
        foreach (var file in candidateFiles.Append(targetPath))
        {
            if (!File.Exists(file))
                continue;

            var name = Path.GetFileName(file);
            if (!name.Equals("TShockAPI.dll", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("OTAPI.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var version = TryGetFileVersion(file);
            if (!string.IsNullOrWhiteSpace(version))
                return new InstanceVersionDetectionResult(
                    instanceType,
                    instanceType,
                    version,
                    null,
                    InstanceVersionDetectionSource.ExecutableMetadata,
                    InstanceVersionDetectionConfidence.Strong,
                    $"Matched {name} file version"
                );
        }

        return InstanceVersionDetectionResult.NoMatch(instanceType, "TShock assembly version not found");
    }

    private static InstanceVersionDetectionResult DetectTdsm(
        InstanceType instanceType,
        IReadOnlyList<string> candidateFiles,
        string targetPath)
    {
        foreach (var file in candidateFiles.Append(targetPath))
        {
            if (!File.Exists(file))
                continue;

            if (!Path.GetFileName(file).Contains("tdsm", StringComparison.OrdinalIgnoreCase))
                continue;

            var version = TryGetFileVersion(file) ?? DetectVersionFromFileNames([file]);
            if (!string.IsNullOrWhiteSpace(version))
                return new InstanceVersionDetectionResult(
                    instanceType,
                    instanceType,
                    version,
                    null,
                    InstanceVersionDetectionSource.ExecutableMetadata,
                    InstanceVersionDetectionConfidence.Strong,
                    "Matched tdsm executable version"
                );
        }

        return InstanceVersionDetectionResult.NoMatch(instanceType, "TDSM version not found");
    }

    private static InstanceVersionDetectionResult DetectSteamServer(
        InstanceType instanceType,
        IReadOnlyList<string> candidateFiles,
        string workingDirectory)
    {
        foreach (var file in candidateFiles)
        {
            if (!File.Exists(file))
                continue;

            var name = Path.GetFileName(file);
            if (!name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(file);
            var buildId = ExtractByRegex(content, SteamBuildIdRegex) ?? ExtractByRegex(content, SteamLastUpdatedRegex);
            if (!string.IsNullOrWhiteSpace(buildId))
                return new InstanceVersionDetectionResult(
                    instanceType,
                    instanceType,
                    buildId,
                    null,
                    InstanceVersionDetectionSource.SteamManifest,
                    InstanceVersionDetectionConfidence.Strong,
                    "Matched Steam appmanifest"
                );
        }

        foreach (var candidate in Directory.EnumerateFiles(workingDirectory, "version.txt", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(workingDirectory, "steam.inf", SearchOption.AllDirectories)))
        {
            var version = FirstVersionToken(File.ReadAllText(candidate));
            if (!string.IsNullOrWhiteSpace(version))
                return new InstanceVersionDetectionResult(
                    instanceType,
                    instanceType,
                    version,
                    null,
                    InstanceVersionDetectionSource.Filename,
                    InstanceVersionDetectionConfidence.Fallback,
                    $"Matched {Path.GetFileName(candidate)}"
                );
        }

        return InstanceVersionDetectionResult.NoMatch(instanceType, "Steam server version not found");
    }

    private static string? DetectVersionJson(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadJsonVersion(candidateFiles, targetPath, "version.json", "id");
    }

    private static string? DetectFabricVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectVersionJson(candidateFiles, targetPath, workingDirectory)
               ?? TryReadPropertiesValue(candidateFiles, targetPath, "install.properties", "game-version")
               ?? TryReadFirstLineEntry(candidateFiles, targetPath, "META-INF/versions.list")?.Split('	', ' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static string? DetectForgeVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectVersionJson(candidateFiles, targetPath, workingDirectory)
               ?? DetectForgeInstallProfileVersion(candidateFiles, targetPath, workingDirectory)
               ?? DetectVersionFromNestedForgeUniversalJar(candidateFiles, targetPath)
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectNeoForgeVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectVersionJson(candidateFiles, targetPath, workingDirectory)
               ?? TryReadTextEntryFromCandidates(candidateFiles, targetPath, "META-INF/mods.toml", content =>
                   ExtractPropertyValue(content, "version"))
               ?? TryReadTextEntryFromCandidates(candidateFiles, targetPath, "META-INF/installer.json", content =>
                   TryReadJsonProperty(content, ["installer", "minecraft"]))
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectPatchPropertiesOrVersionJson(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectPatchPropertiesVersion(candidateFiles, targetPath, workingDirectory)
               ?? DetectVersionJson(candidateFiles, targetPath, workingDirectory)
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectPatchPropertiesVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadPropertiesValue(candidateFiles, targetPath, "patch.properties", "version") is { } version
            ? NormalizeMinecraftVersion(version) ?? version
            : null;
    }

    private static string? DetectMinecraftVersionProperties(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadPropertiesValue(candidateFiles, targetPath, "version.properties", "minecraft_version");
    }

    private static string? DetectMinecraftVersionPropertiesWithFilenameFallback(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectMinecraftVersionProperties(candidateFiles, targetPath, workingDirectory)
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectArclightVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, "META-INF/installer.json", content =>
                   TryReadJsonProperty(content, ["installer", "minecraft"]))
               ?? TryReadTextEntryFromCandidates(candidateFiles, targetPath, "META-INF/MANIFEST.MF", content =>
                   NormalizeMinecraftVersion(ExtractManifestAttribute(content, "Implementation-Version") ?? string.Empty))
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectCatServerVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadManifestAttribute(candidateFiles, targetPath, "Git-Branch")
               ?? TryReadTextEntryFromCandidates(candidateFiles, targetPath, "data/libraries.txt", content =>
                   FirstVersionToken(content))
               ?? DetectVersionFromNestedForgeUniversalJar(candidateFiles, targetPath)
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectSpongeVanillaVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectManifestVersion(candidateFiles, targetPath, workingDirectory);
    }

    private static string? DetectSpongeForgeVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectManifestVersion(candidateFiles, targetPath, workingDirectory);
    }

    private static string? DetectSpongeNeoVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return NormalizeMinecraftVersion(DetectManifestVersion(candidateFiles, targetPath, workingDirectory) ?? string.Empty)
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectForgeInstallProfileVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, "install_profile.json", content =>
                   TryReadJsonProperty(content, ["install", "minecraft"]))
               ?? TryReadTextEntryFromCandidates(candidateFiles, targetPath, "install_profile.json", content =>
                   TryReadJsonProperty(content, ["versionInfo", "inheritsFrom"]))
               ?? TryReadTextEntryFromCandidates(candidateFiles, targetPath, "install_profile.json", content =>
                   TryReadJsonProperty(content, ["versionInfo", "jar"]));
    }

    private static string? DetectVersionFromNestedForgeUniversalJar(IReadOnlyList<string> candidateFiles, string targetPath)
    {
        return TryReadNestedEntryNameVersion(candidateFiles, targetPath, name =>
            name.Contains("universal.jar", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("forge", StringComparison.OrdinalIgnoreCase));
    }

    private static string? DetectFabricLauncherFilenameVersion(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!fileName.Contains("mc.", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Contains("loader", StringComparison.OrdinalIgnoreCase))
                continue;

            var version = FirstVersionToken(fileName);
            if (!string.IsNullOrWhiteSpace(version))
                return version;
        }

        return null;
    }

    private static string? DetectCraftBukkitVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectMetaInfVersion(candidateFiles, targetPath)
               ?? DetectVersionJson(candidateFiles, targetPath, workingDirectory)
               ?? TryReadPomVersion(candidateFiles, targetPath, "META-INF/maven/org.bukkit/craftbukkit/pom.properties")
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectSpigotVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return DetectMetaInfVersion(candidateFiles, targetPath)
               ?? DetectVersionJson(candidateFiles, targetPath, workingDirectory)
               ?? TryReadPomVersion(candidateFiles, targetPath, "META-INF/maven/org.spigotmc/spigot/pom.properties")
               ?? DetectVersionFromFileNames(candidateFiles.Append(targetPath));
    }

    private static string? DetectManifestVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadManifestAttribute(candidateFiles, targetPath, "Implementation-Version");
    }

    private static string? DetectViaVersion(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadSimpleYamlValue(candidateFiles, targetPath, "plugin.yml", "version")
               ?? TryReadSimpleYamlValue(candidateFiles, targetPath, "bungee.yml", "version");
    }

    private static string? DetectDReforged(IReadOnlyList<string> candidateFiles, string targetPath, string workingDirectory)
    {
        return TryReadJsonVersion(candidateFiles, targetPath, "mcdreforged.plugin.json", "version");
    }

    private static IReadOnlyList<string> EnumerateCandidates(string workingDirectory, string targetPath, TargetType targetType)
    {
        var files = new List<string>();
        if (Directory.Exists(workingDirectory))
            files.AddRange(Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories));

        if (File.Exists(targetPath) && !files.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
            files.Add(targetPath);

        if (targetType == TargetType.Jar)
        {
            foreach (var jar in Directory.Exists(workingDirectory)
                         ? Directory.EnumerateFiles(workingDirectory, "*.jar", SearchOption.AllDirectories)
                         : [])
            {
                if (!files.Contains(jar, StringComparer.OrdinalIgnoreCase))
                    files.Add(jar);
            }
        }

        return files;
    }

    private static IReadOnlyList<string> EnumerateSourceCandidates(string sourcePath)
    {
        var files = new List<string>();
        if (Directory.Exists(sourcePath))
        {
            files.Add(sourcePath);
            files.AddRange(Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories));
            return files;
        }

        if (File.Exists(sourcePath))
            files.Add(sourcePath);

        return files;
    }

    private static bool TryResolveSourcePath(string source, Func<string, string> resolveSource, out string sourcePath)
    {
        sourcePath = string.Empty;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                return false;

            sourcePath = uri.LocalPath;
            return File.Exists(sourcePath);
        }

        sourcePath = resolveSource(source);
        return File.Exists(sourcePath);
    }

    private static bool ShouldSwitchFactoryType(InstanceType originalType, InstanceType detectedType)
    {
        if (originalType == detectedType)
            return true;

        if (originalType == InstanceType.MCJava)
            return detectedType.IsMinecraftJavaRuntimeType();

        return false;
    }

    private static bool LikelyMatchesJavaType(InstanceType type, IReadOnlyList<string> candidateFiles, string targetPath)
    {
        var allNames = candidateFiles.Append(targetPath)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        if (type == InstanceType.MCVanilla)
            return true;

        return type switch
        {
            InstanceType.MCFabric => ContainsName(allNames, "fabric"),
            InstanceType.MCForge => ContainsName(allNames, "forge"),
            InstanceType.MCNeoForge => ContainsName(allNames, "neoforge"),
            InstanceType.MCQuilt => ContainsName(allNames, "quilt"),
            InstanceType.MCCleanroom => ContainsName(allNames, "cleanroom"),
            InstanceType.MCTaiyitist => ContainsName(allNames, "taiyitist"),
            InstanceType.MCPaper => ContainsName(allNames, "paper"),
            InstanceType.MCLeaf => ContainsName(allNames, "leaf"),
            InstanceType.MCLeaves => ContainsName(allNames, "leaves"),
            InstanceType.MCFolia => ContainsName(allNames, "folia"),
            InstanceType.MCCanvas => ContainsName(allNames, "canvas"),
            InstanceType.MCPufferfish => ContainsName(allNames, "pufferfish"),
            InstanceType.MCPurpur => ContainsName(allNames, "purpur"),
            InstanceType.MCMohist => ContainsName(allNames, "mohist"),
            InstanceType.MCBanner => ContainsName(allNames, "banner"),
            InstanceType.MCYouer => ContainsName(allNames, "youer"),
            InstanceType.MCThermos => ContainsName(allNames, "thermos"),
            InstanceType.MCCrucible => ContainsName(allNames, "crucible"),
            InstanceType.MCCatServer => ContainsName(allNames, "catserver"),
            InstanceType.MCArclight => ContainsName(allNames, "arclight"),
            InstanceType.MCSpongeVanilla => ContainsName(allNames, "spongevanilla"),
            InstanceType.MCSpongeForge => ContainsName(allNames, "spongeforge"),
            InstanceType.MCSpongeNeo => ContainsName(allNames, "spongeneo"),
            InstanceType.MCCraftBukkit => ContainsName(allNames, "craftbukkit"),
            InstanceType.MCSpigot => ContainsName(allNames, "spigot"),
            InstanceType.MCBungeeCord => ContainsName(allNames, "bungee"),
            InstanceType.MCVelocity => ContainsName(allNames, "velocity"),
            InstanceType.MCWaterfall => ContainsName(allNames, "waterfall"),
            InstanceType.MCTravertine => ContainsName(allNames, "travertine"),
            InstanceType.MCViaVersion => ContainsName(allNames, "viaversion"),
            InstanceType.MCGeyser => ContainsName(allNames, "geyser"),
            InstanceType.MCDReforged => ContainsName(allNames, "dreforged", "mcdreforged"),
            _ => true
        };
    }

    private static bool ContainsName(IEnumerable<string> names, params string[] markers)
    {
        return names.Any(name => markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsJavaMetadataType(InstanceType type)
    {
        return type is
            InstanceType.MCSpongeVanilla or
            InstanceType.MCSpongeForge or
            InstanceType.MCSpongeNeo or
            InstanceType.MCBungeeCord or
            InstanceType.MCVelocity or
            InstanceType.MCWaterfall or
            InstanceType.MCTravertine or
            InstanceType.MCViaVersion or
            InstanceType.MCGeyser or
            InstanceType.MCDReforged;
    }

    private static string? DetectMetaInfVersion(IReadOnlyList<string> candidateFiles, string targetPath)
    {
        foreach (var file in candidateFiles.Append(targetPath))
        {
            if (!File.Exists(file))
                continue;

            if (!TryOpenArchive(file, out var archive))
                continue;

            using (archive)
            {
                foreach (var child in archive.Entries.Where(e => e.FullName.StartsWith("META-INF/version/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(Path.GetFileName(e.FullName))))
                {
                    var version = ExtractByRegex(Path.GetFileName(child.FullName), MetaInfVersionRegex)
                                  ?? FirstVersionToken(Path.GetFileNameWithoutExtension(child.FullName));
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
        }

        return null;
    }

    private static string? TryReadPomVersion(IReadOnlyList<string> candidateFiles, string targetPath, string pomPath)
    {
        return TryReadPropertiesValue(candidateFiles, targetPath, pomPath, "version");
    }

    private static string? TryReadPropertiesValue(
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        string relativePath,
        string key)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, relativePath, content => ExtractPropertyValue(content, key));
    }

    private static string? TryReadManifestAttribute(
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        string key)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, "META-INF/MANIFEST.MF", content =>
        {
            foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(key + ':', StringComparison.OrdinalIgnoreCase))
                    return line[(key.Length + 1)..].Trim();
            }

            return null;
        });
    }

    private static string? TryReadSimpleYamlValue(
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        string relativePath,
        string key)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, relativePath, content =>
        {
            foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.TrimStart().StartsWith(key + ':', StringComparison.OrdinalIgnoreCase))
                    return line[(line.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
            }

            return null;
        });
    }

    private static string? TryReadJsonVersion(
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        string relativePath,
        string propertyName)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, relativePath, content =>
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                ? property.GetString()
                : null;
        });
    }

    private static string? TryReadFirstLineEntry(IReadOnlyList<string> candidateFiles, string targetPath, string relativePath)
    {
        return TryReadTextEntryFromCandidates(candidateFiles, targetPath, relativePath, content =>
            content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
    }

    private static string? TryReadTextEntryFromCandidates(
        IReadOnlyList<string> candidateFiles,
        string targetPath,
        string relativePath,
        Func<string, string?> projector)
    {
        foreach (var file in candidateFiles.Append(targetPath))
        {
            if (Directory.Exists(file))
            {
                var directPath = Path.Combine(file, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(directPath))
                    continue;

                var result = projector(File.ReadAllText(directPath));
                if (!string.IsNullOrWhiteSpace(result))
                    return result;

                continue;
            }

            if (!File.Exists(file))
                continue;

            if (TryReadTextEntry(file, relativePath, out var content))
            {
                var result = projector(content);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }

            if (Path.GetFileName(file).Equals(Path.GetFileName(relativePath), StringComparison.OrdinalIgnoreCase) &&
                (relativePath.Contains('/') == false || file.EndsWith(relativePath.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
            {
                var result = projector(File.ReadAllText(file));
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }

        return null;
    }

    private static bool TryReadTextEntry(string archiveOrFilePath, string entryPath, out string content)
    {
        content = string.Empty;
        if (File.Exists(archiveOrFilePath) && string.Equals(Path.GetFileName(archiveOrFilePath), Path.GetFileName(entryPath), StringComparison.OrdinalIgnoreCase))
        {
            content = File.ReadAllText(archiveOrFilePath);
            return true;
        }

        if (!TryOpenArchive(archiveOrFilePath, out var archive))
            return false;

        using (archive)
        {
            var entry = archive.GetEntry(entryPath) ?? archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, entryPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return false;

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
            content = reader.ReadToEnd();
            return true;
        }
    }

    private static bool TryOpenArchive(string filePath, out ZipArchive archive)
    {
        archive = null!;
        try
        {
            archive = ZipFile.OpenRead(filePath);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string? TryReadJsonProperty(string content, params string[] path)
    {
        using var document = JsonDocument.Parse(content);
        var current = document.RootElement;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    private static string? ExtractManifestAttribute(string content, string key)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(key + ':', StringComparison.OrdinalIgnoreCase))
                return line[(key.Length + 1)..].Trim();
        }

        return null;
    }

    private static string? TryReadNestedEntryNameVersion(IReadOnlyList<string> candidateFiles, string targetPath, Func<string, bool> predicate)
    {
        foreach (var file in candidateFiles.Append(targetPath))
        {
            if (!File.Exists(file) || !TryOpenArchive(file, out var archive))
                continue;

            using (archive)
            {
                var nested = archive.Entries.FirstOrDefault(entry => predicate(entry.FullName));
                if (nested is null)
                    continue;

                var version = FirstVersionToken(Path.GetFileNameWithoutExtension(nested.FullName));
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
        }

        return null;
    }

    private static string? ExtractPropertyValue(string content, string key)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || trimmed.StartsWith('!'))
                continue;

            if (!trimmed.StartsWith(key + '=', StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith(key + ':', StringComparison.OrdinalIgnoreCase))
                continue;

            var separatorIndex = trimmed.IndexOfAny(['=', ':']);
            if (separatorIndex < 0 || separatorIndex + 1 >= trimmed.Length)
                return null;

            return trimmed[(separatorIndex + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static string? DetectVersionFromFileNames(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            var version = FirstVersionToken(Path.GetFileNameWithoutExtension(filePath));
            if (!string.IsNullOrWhiteSpace(version))
                return version;
        }

        return null;
    }

    private static string? FirstVersionToken(string content)
    {
        return ExtractByRegex(content, VersionLikeRegex);
    }

    private static string? ExtractByRegex(string content, Regex regex)
    {
        var match = regex.Match(content);
        if (!match.Success)
            return null;

        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private static string? NormalizeMinecraftVersion(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
            return null;

        var match = VersionLikeRegex.Match(rawVersion);
        return match.Success ? match.Value : null;
    }

    private static string? TryGetFileVersion(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (!string.IsNullOrWhiteSpace(version))
                return version;
        }
        catch
        {
        }

        return DetectVersionFromPeStrings(path);
    }

    private static string? DetectVersionFromPeStrings(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var content = Encoding.UTF8.GetString(bytes);
            return ExtractByRegex(content, FileVersionRegex);
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsAnyName(IEnumerable<string> candidateFiles, params string[] markers)
    {
        foreach (var file in candidateFiles)
        {
            var fileName = Path.GetFileName(file);
            if (markers.Any(marker => fileName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    [GeneratedRegex("\\d+(?:\\.\\d+){1,3}", RegexOptions.Compiled)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("(?:^|[-_])((?:\\d+\\.){1,3}\\d+)(?:[-_]|$)", RegexOptions.Compiled)]
    private static partial Regex MetaInfVersionFileRegex();

    [GeneratedRegex("FileVersion\\D+(\\d+(?:\\.\\d+){1,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex FileVersionRegexFactory();

    [GeneratedRegex("BASE_VERSION\\s*[:=]\\s*['\"]([^'\"]+)['\"]", RegexOptions.Compiled)]
    private static partial Regex BaseVersionRegexFactory();

    [GeneratedRegex("VERSION\\s*[:=]\\s*['\"]([^'\"]+)['\"]", RegexOptions.Compiled)]
    private static partial Regex PhpConstVersionRegexFactory();

    [GeneratedRegex("\"buildid\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SteamBuildIdRegexFactory();

    [GeneratedRegex("\"LastUpdated\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SteamLastUpdatedRegexFactory();

    private sealed record JavaDetectionRule(
        InstanceType InstanceType,
        Func<IReadOnlyList<string>, string, string, string?> Detector,
        InstanceVersionDetectionSource Source,
        InstanceVersionDetectionConfidence Confidence);
}
