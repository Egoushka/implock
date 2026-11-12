using System.Collections.Immutable;
using Implock;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Implock.Generator.Tests;

internal static class Harness
{
    public const string Slug = """
        using System;
        using Implock;

        namespace Demo;

        public static partial class Text
        {
            /// <summary>Lower-cases a title and joins words with dashes.</summary>
            [Implement("empty gives empty: Slugify(\"\") == \"\"")]
            public static partial string Slugify(string title);
        }
        """;

    public const string SlugBody = "return string.Join(\"-\", title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));";

    public static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "Fixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static string Hash(string source, int index = 0) => SpecScanner.Scan(Compile(source))[index].SpecHash;

    public static string Id(string source, int index = 0) => SpecScanner.Scan(Compile(source))[index].Id;

    public static string Lock(params (string Id, string Hash, string Body)[] entries)
    {
        var file = new LockFile();
        foreach (var e in entries)
        {
            file.Set(e.Id, e.Hash, e.Body);
        }

        return file.Render();
    }

    public sealed record Result(ImmutableArray<Diagnostic> Generator, ImmutableArray<Diagnostic> Compiler, ImmutableArray<string> Sources);

    public static Result Run(string source, string? lockText)
    {
        var compilation = Compile(source);
        ImmutableArray<AdditionalText> additional = lockText is null ? [] : [new MemoryText("implement.lock", lockText)];
        var driver = CSharpGeneratorDriver.Create([new ImplockGenerator().AsSourceGenerator()], additionalTexts: additional)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var sources = driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()).ToImmutableArray();
        var compiler = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("IMPL", StringComparison.Ordinal)).ToImmutableArray();
        return new Result(generatorDiagnostics, compiler, sources);
    }

    private static IEnumerable<MetadataReference> References()
    {
        var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trusted.Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
    }

    private sealed class MemoryText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
