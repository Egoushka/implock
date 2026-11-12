using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Implock.Cli;

/// <summary>
/// Builds a compilation from the .cs files of one project directory and runs the generator over it.
/// Limits: no NuGet package resolution and no MSBuild evaluation; references are the runtime's own assemblies.
/// </summary>
internal sealed class LoadedProject
{
    private static readonly string[] ImplicitUsings =
    {
        "System", "System.Collections.Generic", "System.IO", "System.Linq",
        "System.Net.Http", "System.Threading", "System.Threading.Tasks",
    };

    private readonly CSharpParseOptions _parseOptions = new(LanguageVersion.Latest, DocumentationMode.None);
    private readonly ImmutableArray<SyntaxTree> _trees;
    private readonly CSharpCompilationOptions _options;
    private readonly ImmutableArray<MetadataReference> _references;

    private LoadedProject(string directory, ImmutableArray<SyntaxTree> trees, CSharpCompilationOptions options, ImmutableArray<MetadataReference> references)
    {
        Directory = directory;
        _trees = trees;
        _options = options;
        _references = references;
    }

    public string Directory { get; }

    public string LockPath => Path.Combine(Directory, LockFile.FileName);

    public static LoadedProject Load(string? path)
    {
        var target = path ?? ".";
        string directory;
        string? csproj = null;
        if (File.Exists(target) && target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            csproj = Path.GetFullPath(target);
            directory = Path.GetDirectoryName(csproj)!;
        }
        else if (System.IO.Directory.Exists(target))
        {
            directory = Path.GetFullPath(target);
            csproj = System.IO.Directory.EnumerateFiles(directory, "*.csproj").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        }
        else
        {
            throw new UsageException("project not found: " + target);
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None);
        var trees = new List<SyntaxTree>();
        foreach (var file in SourceFiles(directory))
        {
            var text = SourceText.From(File.ReadAllText(file), System.Text.Encoding.UTF8);
            trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, Path.GetRelativePath(directory, file).Replace('\\', '/')));
        }

        var nullable = ReadFlag(directory, csproj, "Nullable");
        var implicitUsings = ReadFlag(directory, csproj, "ImplicitUsings");
        if (implicitUsings)
        {
            var global = string.Concat(ImplicitUsings.Select(u => "global using global::" + u + ";\n"));
            trees.Add(CSharpSyntaxTree.ParseText(global, parseOptions, "implicit-usings.g.cs"));
        }

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        return new LoadedProject(directory, trees.ToImmutableArray(), options, PlatformReferences());
    }

    public string? ReadLockText()
    {
        return File.Exists(LockPath) ? File.ReadAllText(LockPath) : null;
    }

    public CSharpCompilation Compile() => CSharpCompilation.Create("implock.project", _trees, _references, _options);

    public GeneratorRun Run(string lockText)
    {
        var driver = CSharpGeneratorDriver.Create(
            new[] { new ImplockGenerator().AsSourceGenerator() },
            new[] { new MemoryText(LockFile.FileName, lockText) },
            _parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(Compile(), out var compilation, out var diagnostics);
        return new GeneratorRun((CSharpCompilation)compilation, diagnostics);
    }

    private static List<string> SourceFiles(string directory)
    {
        var files = new List<string>();
        Collect(directory, files);
        files.Sort(StringComparer.Ordinal);
        return files;

        static void Collect(string dir, List<string> into)
        {
            into.AddRange(System.IO.Directory.EnumerateFiles(dir, "*.cs"));
            foreach (var sub in System.IO.Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name is "bin" or "obj" || name.StartsWith('.'))
                {
                    continue;
                }

                Collect(sub, into);
            }
        }
    }

    private static bool ReadFlag(string directory, string? csproj, string name)
    {
        var pattern = new Regex("<" + name + @">\s*(enable|true)\s*</" + name + ">", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (csproj is not null && pattern.IsMatch(File.ReadAllText(csproj)))
        {
            return true;
        }

        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props) && pattern.IsMatch(File.ReadAllText(props)))
            {
                return true;
            }
        }

        return false;
    }

    internal static ImmutableArray<MetadataReference> PlatformReferences()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    }
}

internal sealed class GeneratorRun
{
    public GeneratorRun(CSharpCompilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Compilation = compilation;
        Diagnostics = diagnostics;
    }

    public CSharpCompilation Compilation { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

internal sealed class MemoryText : AdditionalText
{
    private readonly SourceText _text;

    public MemoryText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text);
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}
