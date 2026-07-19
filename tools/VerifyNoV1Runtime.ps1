[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$rg = Get-Command rg -ErrorAction Stop

function Invoke-RgSearch {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = @(& $rg.Source @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -gt 1) {
        throw "rg failed with exit code $exitCode`n$($output -join [Environment]::NewLine)"
    }

    if ($exitCode -eq 1) {
        return @()
    }

    return $output
}

function Assert-PathAbsent {
    param([Parameter(Mandatory)][string] $RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (Test-Path -LiteralPath $path) {
        throw "V1 deletion gate failed: '$RelativePath' still exists."
    }
}

$removedPaths = @(
    'generators/MCServerLauncher.Daemon.Generators',
    'src/MCServerLauncher.Daemon/Remote/Action',
    'src/MCServerLauncher.Daemon/Remote/WsActionPlugin.cs',
    'src/MCServerLauncher.Daemon/Remote/WsEventPlugin.cs',
    'src/MCServerLauncher.Daemon/Remote/Event/EventService.cs',
    'src/MCServerLauncher.Daemon/Remote/Event/IEventService.cs',
    'src/MCServerLauncher.Common/ProtoType/Action',
    'src/MCServerLauncher.Common/ProtoType/Event',
    'src/MCServerLauncher.Common/ProtoType/Notification',
    'src/MCServerLauncher.Common/ProtoType/Relay',
    'src/MCServerLauncher.Common/ProtoType/Status',
    'src/MCServerLauncher.Common/.Resources/proto_type.py',
    'src/MCServerLauncher.Common/.Resources/proto_type.py.old',
    'src/MCServerLauncher.Common/.Resources/proto_type.yml',
    'src/MCServerLauncher.DaemonClient/Api/Daemon.cs',
    'src/MCServerLauncher.DaemonClient/Api/IDaemon.cs',
    'src/MCServerLauncher.DaemonClient/Api/DaemonExtensions.cs',
    'src/MCServerLauncher.DaemonClient/Connection/ClientConnection.cs',
    'src/MCServerLauncher.DaemonClient/Connection/TouchSocketClientTransport.cs',
    'src/MCServerLauncher.DaemonClient/WebSocketPlugin/WsReceivedPlugin.cs',
    'src/MCServerLauncher.Daemon/.Resources/Docs/protocol/topics/action.md',
    'src/MCServerLauncher.Daemon/.Resources/Docs/protocol/topics/actions.md',
    'src/MCServerLauncher.Daemon/.Resources/Docs/protocol/topics/action-errcode.md',
    'src/MCServerLauncher.Daemon/.Resources/Docs/protocol/topics/event.md'
)

foreach ($removedPath in $removedPaths) {
    Assert-PathAbsent $removedPath
}

$solutionMatches = @(Invoke-RgSearch @('-n', 'MCServerLauncher\.Daemon\.Generators|7CC6FA63-C9AE-4D25-B551-433AEFD3DBE4', 'MCServerLauncher.slnx'))
if ($solutionMatches.Count -ne 0) {
    throw "V1 deletion gate failed: the generator remains in the solution.`n$($solutionMatches -join [Environment]::NewLine)"
}

$runtimePattern = @(
    '/api/v1',
    '\bActionType\b',
    '\bActionRequest\b',
    '\bActionResponse\b',
    '\bActionError\b',
    '\bActionRetcode\b',
    '\bActionRequestStatus\b',
    '\bIActionParameter\b',
    '\bIActionResult\b',
    '\bEventType\b',
    '\bEventPacket\b',
    '\bIEventMeta\b',
    '\bIEventData\b',
    '\bNotificationPacket\b',
    '\bRelayPacket\b',
    '\bJsonPayloadBuffer\b',
    '\bWsActionPlugin\b',
    '\bWsEventPlugin\b',
    '\bWsReceivedPlugin\b',
    '\bTouchSocketClientTransport\b',
    '\bLegacyActionErrorMapper\b',
    '\bLegacySystemActionAdapter\b',
    '\bDaemonStjReflectionFallbackPolicy\b',
    '\bDaemonClientStjReflectionFallbackPolicy\b',
    'MCServerLauncher\.Daemon\.Generators'
) -join '|'

$searchTargets = @(
    'src',
    'tests',
    'benchmarks',
    'tools',
    'MCServerLauncher.slnx',
    'CONTRIBUTING.md',
    'README.md',
    'README_ZH.md'
) | Where-Object { Test-Path -LiteralPath (Join-Path $repoRoot $_) }

$searchArguments = @(
    '-n',
    '--hidden',
    '--glob', '!**/bin/**',
    '--glob', '!**/obj/**',
    '--glob', '!benchmarks/baselines/v1.json',
    '--glob', '!tests/MCServerLauncher.ProtocolTests/Fixtures/Rpc/**',
    '--glob', '!tools/VerifyNoV1Runtime.ps1',
    $runtimePattern
) + $searchTargets

Push-Location $repoRoot
try {
    $runtimeMatches = @(Invoke-RgSearch $searchArguments)
}
finally {
    Pop-Location
}

if ($runtimeMatches.Count -ne 0) {
    throw "V1 deletion gate failed: runtime references remain.`n$($runtimeMatches -join [Environment]::NewLine)"
}

Write-Host 'V1 deletion gate passed: no runtime references remain.'
exit 0
