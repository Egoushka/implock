; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IMPL001 | Implock | Error | No lock entry for an [Implement] method
IMPL002 | Implock | Error | The lock entry no longer matches the spec
IMPL003 | Implock | Warning | Lock entry without a method
IMPL004 | Implock | Error | The lock or a body in it is invalid
