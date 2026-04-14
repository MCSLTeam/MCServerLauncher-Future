param(
    [string]$Root = "."
)

$benchmarkProject = Get-ChildItem -Path $Root -Recurse -File -Filter '*.csproj' |
    Where-Object { $_.Name -match 'Benchmark' } |
    Select-Object -First 1

if ($null -eq $benchmarkProject) {
    throw 'No benchmark project found'
}

$benchmarkProject.FullName
