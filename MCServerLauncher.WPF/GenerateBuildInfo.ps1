param(
    [string]$OutputPath = "./Resources/BuildInfo"
)

$originalLocation = Get-Location
Set-Location ..

try {
    $gitHash = git rev-parse --short HEAD
    if ($LASTEXITCODE -ne 0) {
        $gitHash = "unknown"
    }
} catch {
    $gitHash = "unknown"
}

try {
    $gitBranch = git rev-parse --abbrev-ref HEAD
    if ($LASTEXITCODE -ne 0) {
        $gitBranch = "unknown"
    }
} catch {
    $gitBranch = "unknown"
}

Set-Location $originalLocation
$buildTime = [DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")

$buildInfo = @{
    commitHash = $gitHash
    branch = $gitBranch
    buildTime = $buildTime
}


$buildInfo | ConvertTo-Json -Depth 1 -Compress | Out-File -FilePath $OutputPath -Encoding UTF8