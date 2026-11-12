using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Implock.Cli;

/// <summary>
/// Builds a compilation from the .cs files of one project directory and runs the generator over it.
/// Compiler settings come from the unconditional property groups of the project file and of every
/// Directory.Build.props above it (outermost first, so the project file wins), with $(Property) references
/// expanded to the value read so far. Limits: no NuGet package resolution and no MSBuild evaluation
/// (conditions, imports and property functions are not seen); references are the runtime's own assemblies.
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

        var properties = ReadProperties(directory, csproj);
        if (IsOn(properties, "ImplicitUsings"))
        {
            var global = string.Concat(ImplicitUsings.Select(u => "global using global::" + u + ";\n"));
            trees.Add(CSharpSyntaxTree.ParseText(global, parseOptions, "implicit-usings.g.cs"));
        }

        // The build hands these settings to csc, so the compiler's own parser reads them here: the same
        // id forms (bare numbers, the `nullable` group) and the same precedence as the real build.
        var arguments = new List<string> { "/noconfig", "/target:library" };
        if (properties.GetValueOrDefault("Nullable", string.Empty) is { Length: > 0 } nullable)
        {
            arguments.Add("/nullable:" + nullable);
        }

        if (IsOn(properties, "TreatWarningsAsErrors"))
        {
            arguments.Add("/warnaserror+");
        }

        AddList(arguments, "/warnaserror+:", properties, "WarningsAsErrors");
        AddList(arguments, "/warnaserror-:", properties, "WarningsNotAsErrors");
        AddList(arguments, "/nowarn:", properties, "NoWarn");
        var parsed = CSharpCommandLineParser.Default.Parse(arguments, directory, sdkDirectory: null);

        // No source list is given, so CS2008 (no sources) and CS1562 (no output name) always appear; anything else is a bad setting.
        var errors = parsed.Errors.Where(e => e.Id is not ("CS2008" or "CS1562")).ToList();
        if (errors.Count > 0)
        {
            throw new UsageException("cannot read compiler settings: " + string.Join("; ", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture))));
        }

        return new LoadedProject(directory, trees.ToImmutableArray(), parsed.CompilationOptions, PlatformReferences());
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

    private static readonly Regex PropertyReference = new(@"\$\((\w+)\)", RegexOptions.CultureInvariant);
    private static readonly Regex OtherExpression = new(@"\$\([^)]*\)", RegexOptions.CultureInvariant);

    private static Dictionary<string, string> ReadProperties(string directory, string? csproj)
    {
        var files = new List<string>();
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                files.Insert(0, props);
            }
        }

        if (csproj is not null)
        {
            files.Add(csproj);
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch (XmlException e)
            {
                throw new UsageException("cannot read " + file + ": " + e.Message);
            }

            var groups = document.Root?.Elements().Where(e => e.Name.LocalName == "PropertyGroup" && e.Attribute("Condition") is null)
                ?? Enumerable.Empty<XElement>();
            foreach (var property in groups.SelectMany(g => g.Elements()).Where(e => e.Attribute("Condition") is null))
            {
                var value = PropertyReference.Replace(property.Value, m => properties.GetValueOrDefault(m.Groups[1].Value, string.Empty));
                properties[property.Name.LocalName] = OtherExpression.Replace(value, string.Empty).Trim();
            }
        }

        return properties;
    }

    private static bool IsOn(Dictionary<string, string> properties, string name)
    {
        var value = properties.GetValueOrDefault(name, string.Empty);
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("enable", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly char[] ListSeparators = { ';', ',' };

    private static void AddList(List<string> arguments, string option, Dictionary<string, string> properties, string name)
    {
        // Like the build's Csc task: split on either separator and trim each id, so a list that spans lines still parses.
        var ids = properties.GetValueOrDefault(name, string.Empty)
            .Split(ListSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length > 0)
        {
            arguments.Add(option + string.Join(',', ids));
        }
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
