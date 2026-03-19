$zhCnPath = "MCServerLauncher.WPF/Translations/Lang.zh-CN.resx"
$otherLangs = @(
    "MCServerLauncher.WPF/Translations/Lang.en-US.resx",
    "MCServerLauncher.WPF/Translations/Lang.ja-JP.resx",
    "MCServerLauncher.WPF/Translations/Lang.ru-RU.resx",
    "MCServerLauncher.WPF/Translations/Lang.zh-HK.resx",
    "MCServerLauncher.WPF/Translations/Lang.zh-TW.resx"
)

[xml]$zhCnXml = Get-Content $zhCnPath
$zhCnKeys = $zhCnXml.root.data | Select-Object -ExpandProperty name

foreach ($langPath in $otherLangs) {
    [xml]$langXml = Get-Content $langPath
    $langKeys = $langXml.root.data | Select-Object -ExpandProperty name
    
    $missingKeys = $zhCnKeys | Where-Object { $_ -notin $langKeys }
    
    Write-Host "Missing in $langPath : $($missingKeys.Count)"
    if ($missingKeys.Count -gt 0) {
        foreach ($key in $missingKeys) {
            $val = ($zhCnXml.root.data | Where-Object { $_.name -eq $key }).value
            Write-Host "  - $key : $val"
        }
    }
}
