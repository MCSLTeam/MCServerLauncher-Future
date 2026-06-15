; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID    | Category                      | Severity | Notes
-----------|-------------------------------|----------|----------------------------------------------
MCSLDAG001 | DaemonActionRegistryGenerator | Error    | Duplicate ActionHandler registration
MCSLDAG002 | DaemonActionRegistryGenerator | Error    | ActionHandler attribute is missing required arguments
MCSLDAG003 | DaemonActionRegistryGenerator | Error    | Annotated handler does not implement a supported handler interface
MCSLDAG004 | DaemonActionRegistryGenerator | Error    | Malformed dual-interface ActionHandler
MCSLDAG005 | DaemonActionRegistryGenerator | Error    | Unsupported ActionHandler shape

