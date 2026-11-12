using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Implock.Cli;

internal static class CheckCommand
{
    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        options.Allow();
        var project = LoadedProject.Load(options.Positionals.FirstOrDefault());
        var run = project.Run(project.ReadLockText() ?? "implock 1\n");
        var findings = Print(run.Diagnostics, error);
        if (findings > 0)
        {
            foreach (var spec in SpecScanner.Scan(run.Compilation).Where(s => s.UnresolvedType is not null))
            {
                error.WriteLine("implock: cannot lock " + spec.Id + ": the type '" + spec.UnresolvedType
                    + "' does not resolve here. The CLI sees only the .cs files and the runtime libraries, not NuGet packages or other projects.");
            }

            return Program.Findings;
        }

        var methods = SpecScanner.Scan(run.Compilation).Length;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"implock: {methods} method(s) locked"));
        return Program.Ok;
    }

    /// <summary>Prints each implock diagnostic and returns how many are errors.</summary>
    internal static int Print(ImmutableArray<Diagnostic> diagnostics, TextWriter writer)
    {
        var errors = 0;
        foreach (var d in diagnostics.Where(d => d.Id.StartsWith("IMPL", StringComparison.Ordinal)))
        {
            var line = d.Location.GetLineSpan();
            var where = d.Location.IsInSource
                ? string.Create(CultureInfo.InvariantCulture, $"{line.Path}({line.StartLinePosition.Line + 1})")
                : LockFile.FileName;
            var severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{where}: {severity} {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"));
            if (d.Severity == DiagnosticSeverity.Error)
            {
                errors++;
            }
        }

        return errors;
    }
}
