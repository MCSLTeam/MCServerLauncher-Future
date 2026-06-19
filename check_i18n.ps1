param(
    [string]$WpfRoot = "src/MCServerLauncher.WPF",
    [string]$TranslationsRoot = "src/MCServerLauncher.WPF/Translations",
    [string[]]$Cultures = @("en-US", "ja-JP", "ru-RU", "zh-CN", "zh-HK", "zh-TW"),
    [switch]$Json,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function New-StringSet {
    return ,([System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal))
}

function Add-Location {
    param(
        [hashtable]$Locations,
        [string]$Key,
        [string]$Path,
        [int]$Line
    )

    if (-not $Locations.ContainsKey($Key)) {
        $Locations[$Key] = [System.Collections.Generic.List[string]]::new()
    }

    $Locations[$Key].Add("${Path}:${Line}")
}

function Get-ResxEntries {
    param([string]$Path)

    [xml]$xml = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $entries = [ordered]@{}

    foreach ($node in $xml.root.data) {
        if ($null -ne $node.name -and -not [string]::IsNullOrWhiteSpace($node.name)) {
            $entries[$node.name] = [string]$node.value
        }
    }

    return ,$entries
}

function ConvertTo-RelativePath {
    param([string]$Path)

    $basePath = (Get-Location).Path

    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [Uri]::new($basePath)
    $pathUri = [Uri]::new($Path)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Find-UsedI18nKeys {
    param([string]$Root)

    $keys = New-StringSet
    $locations = @{}
    $dynamicLocations = [System.Collections.Generic.List[string]]::new()
    $patterns = @(
        [regex]'Lang\.Tr\[\s*"(?<key>[^"\r\n]+)"\s*\]',
        [regex]'Lang\.ResourceManager\.GetString\(\s*"(?<key>[^"\r\n]+)"',
        [regex]'\{Binding\s+\[(?<key>[^\]\r\n]+)\][^\r\n]*Lang\.Tr'
    )
    $dynamicPattern = [regex]'Lang\.Tr\[\s*(?!")[^\]\r\n]+\]'

    $files = Get-ChildItem -LiteralPath $Root -Recurse -File -Include *.cs, *.xaml |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)\\' -and
            $_.Name -notmatch '\.g(\.i)?\.cs$' -and
            $_.Name -notmatch '\.Designer\.cs$'
        }

    foreach ($file in $files) {
        $relativePath = ConvertTo-RelativePath $file.FullName
        $lines = @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]

            foreach ($pattern in $patterns) {
                foreach ($match in $pattern.Matches($line)) {
                    $key = $match.Groups["key"].Value.Trim()

                    if ([string]::IsNullOrWhiteSpace($key)) {
                        continue
                    }

                    [void]$keys.Add($key)
                    Add-Location -Locations $locations -Key $key -Path $relativePath -Line ($i + 1)
                }
            }

            foreach ($match in $dynamicPattern.Matches($line)) {
                $dynamicLocations.Add("${relativePath}:$($i + 1): $($match.Value.Trim())")
            }
        }
    }

    return [pscustomobject]@{
        Keys = $keys
        Locations = $locations
        DynamicLocations = $dynamicLocations
    }
}

function Format-KeyList {
    param(
        [string[]]$Keys,
        [hashtable]$Locations
    )

    foreach ($key in $Keys) {
        Write-Host "  - $key"

        if ($Locations.ContainsKey($key)) {
            foreach ($location in ($Locations[$key] | Select-Object -First 5)) {
                Write-Host "      $location"
            }
        }
    }
}

$wpfRootPath = Resolve-Path -LiteralPath $WpfRoot
$translationsRootPath = Resolve-Path -LiteralPath $TranslationsRoot

$used = Find-UsedI18nKeys -Root $wpfRootPath.Path
$resources = [ordered]@{}
$resourceUnion = New-StringSet
$missingResourceFiles = [System.Collections.Generic.List[string]]::new()

foreach ($culture in $Cultures) {
    $path = Join-Path $translationsRootPath.Path "Lang.$culture.resx"

    if (-not (Test-Path -LiteralPath $path)) {
        $missingResourceFiles.Add($path)
        continue
    }

    $entries = Get-ResxEntries -Path $path
    $resources[$culture] = $entries

    foreach ($key in $entries.Keys) {
        [void]$resourceUnion.Add($key)
    }
}

$missingByCulture = [ordered]@{}
$extraByCulture = [ordered]@{}
$missingUsedByCulture = [ordered]@{}

foreach ($culture in $resources.Keys) {
    $entries = $resources[$culture]
    $entryKeys = New-StringSet

    foreach ($key in $entries.Keys) {
        [void]$entryKeys.Add($key)
    }

    $missingAgainstUnion = @($resourceUnion | Where-Object { -not $entryKeys.Contains($_) } | Sort-Object)
    $extraAgainstUsed = @($entryKeys | Where-Object { -not $used.Keys.Contains($_) } | Sort-Object)
    $missingUsed = @($used.Keys | Where-Object { -not $entryKeys.Contains($_) } | Sort-Object)

    $missingByCulture[$culture] = $missingAgainstUnion
    $extraByCulture[$culture] = $extraAgainstUsed
    $missingUsedByCulture[$culture] = $missingUsed
}

$usedMissingEverywhere = @($used.Keys | Where-Object { -not $resourceUnion.Contains($_) } | Sort-Object)

$result = [pscustomobject]@{
    SourceFileCount = (Get-ChildItem -LiteralPath $wpfRootPath.Path -Recurse -File -Include *.cs, *.xaml |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)\\' -and
            $_.Name -notmatch '\.g(\.i)?\.cs$' -and
            $_.Name -notmatch '\.Designer\.cs$'
        }).Count
    UsedKeyCount = $used.Keys.Count
    ResourceUnionKeyCount = $resourceUnion.Count
    MissingResourceFiles = @($missingResourceFiles)
    UsedKeysMissingEverywhere = $usedMissingEverywhere
    UsedKeysMissingByCulture = $missingUsedByCulture
    ResourceKeysMissingByCulture = $missingByCulture
    ResourceKeysUnusedBySource = $extraByCulture
    DynamicResourceLookups = @($used.DynamicLocations)
}

if ($Json) {
    $result | ConvertTo-Json -Depth 6
}
else {
    Write-Host "WPF i18n check"
    Write-Host "  Source files scanned: $($result.SourceFileCount)"
    Write-Host "  Used keys found:      $($result.UsedKeyCount)"
    Write-Host "  Resource key union:   $($result.ResourceUnionKeyCount)"

    if ($missingResourceFiles.Count -gt 0) {
        Write-Host ""
        Write-Host "Missing resource files:"
        foreach ($path in $missingResourceFiles) {
            Write-Host "  - $path"
        }
    }

    Write-Host ""
    Write-Host "Dynamic Lang.Tr lookups not statically checked: $($used.DynamicLocations.Count)"
    foreach ($location in $used.DynamicLocations) {
        Write-Host "  - $location"
    }

    Write-Host ""
    Write-Host "Used keys missing from every resource file: $($usedMissingEverywhere.Count)"
    Format-KeyList -Keys $usedMissingEverywhere -Locations $used.Locations

    Write-Host ""
    Write-Host "Used keys missing by culture:"
    foreach ($culture in $missingUsedByCulture.Keys) {
        $missing = $missingUsedByCulture[$culture]
        Write-Host "  $culture`: $($missing.Count)"
        Format-KeyList -Keys $missing -Locations $used.Locations
    }

    Write-Host ""
    Write-Host "Resource keys missing by culture compared with the six-language union:"
    foreach ($culture in $missingByCulture.Keys) {
        $missing = $missingByCulture[$culture]
        Write-Host "  $culture`: $($missing.Count)"
        foreach ($key in $missing) {
            Write-Host "    - $key"
        }
    }

    Write-Host ""
    Write-Host "Resource keys not referenced by scanned source files:"
    foreach ($culture in $extraByCulture.Keys) {
        Write-Host "  $culture`: $($extraByCulture[$culture].Count)"
    }
}

$hasMissing = $missingResourceFiles.Count -gt 0 -or
    $used.DynamicLocations.Count -gt 0 -or
    $usedMissingEverywhere.Count -gt 0 -or
    (@($missingUsedByCulture.Values | Where-Object { $_.Count -gt 0 }).Count -gt 0) -or
    (@($missingByCulture.Values | Where-Object { $_.Count -gt 0 }).Count -gt 0)

if ($Strict -and $hasMissing) {
    exit 1
}
