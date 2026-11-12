using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Implock;

/// <summary>Emits [Implement] bodies from implement.lock. It never calls a model and never touches the network.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ImplockGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("Implock.ImplementAttribute.g.cs", SourceText.From(Emitter.AttributeSource, Encoding.UTF8)));

        var specs = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax method && SpecScanner.IsCandidate(method),
                static (ctx, ct) => SpecScanner.TryCreate((MethodDeclarationSyntax)ctx.Node, ctx.SemanticModel, ct))
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        var locks = context.AdditionalTextsProvider
            .Where(static text => string.Equals(Path.GetFileName(text.Path), LockFile.FileName, StringComparison.Ordinal))
            .Select(static (text, ct) => (text.Path, Text: text.GetText(ct)?.ToString() ?? string.Empty))
            .Collect();

        context.RegisterSourceOutput(specs.Combine(locks), static (ctx, input) => Execute(ctx, input.Left, input.Right));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<MethodSpec> specs, ImmutableArray<(string Path, string Text)> locks)
    {
        var lockFile = new LockFile();
        var lockLocation = Location.None;

        if (locks.Length > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidLock, Location.None, "More than one implement.lock is part of the compilation; keep exactly one."));
            return;
        }

        if (locks.Length == 1)
        {
            lockLocation = Location.Create(locks[0].Path, default, default);
            var parsed = LockFile.Parse(locks[0].Text, out var error);
            if (parsed is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidLock, lockLocation, "implement.lock is malformed: " + error));
                return;
            }

            lockFile = parsed;
        }

        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!seen.Add(spec.Id))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidLock, spec.ToLocation(), "Two [Implement] methods share the id '" + spec.Id + "'."));
                continue;
            }

            if (!lockFile.Entries.TryGetValue(spec.Id, out var entry))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingEntry, spec.ToLocation(), spec.Id));
                continue;
            }

            if (!string.Equals(entry.SpecHash, spec.SpecHash, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StaleEntry, spec.ToLocation(), spec.Id));
                continue;
            }

            var problem = BodyValidator.Validate(entry.Body);
            if (problem is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidLock, spec.ToLocation(), "The lock body for '" + spec.Id + "' is refused: " + problem + "."));
                continue;
            }

            context.AddSource(HintName(spec.Id), SourceText.From(Emitter.Render(spec, entry.Body), Encoding.UTF8));
        }

        foreach (var id in lockFile.Entries.Keys.Where(k => !seen.Contains(k)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.OrphanEntry, lockLocation, id));
        }
    }

    internal static string HintName(string id)
    {
        var sb = new StringBuilder();
        foreach (var c in id)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        sb.Append('_').Append(StableHash(id)).Append(".implock.g.cs");
        return sb.ToString();
    }

    private static string StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
