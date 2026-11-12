using Microsoft.CodeAnalysis;

namespace Implock;

internal static class Diagnostics
{
    private const string Category = "Implock";

    public static readonly DiagnosticDescriptor MissingEntry = new DiagnosticDescriptor(
        "IMPL001",
        "No lock entry for an [Implement] method",
        "No implement.lock entry for '{0}'; run 'implock generate'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StaleEntry = new DiagnosticDescriptor(
        "IMPL002",
        "The lock entry no longer matches the spec",
        "The implement.lock entry for '{0}' is stale because the signature, doc comment or tests changed; run 'implock generate'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrphanEntry = new DiagnosticDescriptor(
        "IMPL003",
        "Lock entry without a method",
        "The implement.lock entry '{0}' matches no [Implement] method; run 'implock generate --prune'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidLock = new DiagnosticDescriptor(
        "IMPL004",
        "The lock or a body in it is invalid",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
