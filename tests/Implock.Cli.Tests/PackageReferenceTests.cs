using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Implock.Cli.Tests;

/// <summary>
/// With a restored project, the loader adds the compile assemblies of its NuGet packages, read from
/// obj/project.assets.json, so a spec or a body that uses a package type compiles as it does in the build.
/// </summary>
public class PackageReferenceTests
{
    private const string PackageSource = """
        namespace Fake.Lib;

        public static class Shouter
        {
            public static string Shout(string text) => text.ToUpperInvariant() + "!";
        }

        public sealed class Widget
        {
            public string Name { get; set; } = "";
        }
        """;

    private const string UsingSource = """
        using Fake.Lib;

        namespace Demo;

        public static partial class Text
        {
            /// <summary>Shouts the text.</summary>
            [Implock.Implement("shouts: Loud(\"a\") == \"A!\"")]
            public static partial string Loud(string input);
        }
        """;

    private const string ShoutBody = "return Shouter.Shout(input);";

    private const string WidgetSource = """
        namespace Demo;

        public static partial class Text
        {
            /// <summary>Returns the widget's name.</summary>
            [Implock.Implement("names: NameOf(new Fake.Lib.Widget { Name = \"w\" }) == \"w\"")]
            public static partial string NameOf(Fake.Lib.Widget widget);
        }
        """;

    [Fact]
    public void A_using_of_a_package_namespace_compiles_when_the_project_is_restored()
    {
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource);
        project.Write("Demo.csproj", Csproj("net10.0"));
        var folder = AddPackage(project, "Fake.Lib", "lib/net10.0/Fake.Lib.dll", PackageSource);
        WriteAssets(project, folder, ("net10.0", Package("Fake.Lib", "lib/net10.0/Fake.Lib.dll")));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(ShoutBody));

        Assert.Equal(0, result.Code);
        Assert.Contains("generated Demo.Text.Loud(string)", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_an_assets_file_the_refusal_suggests_dotnet_restore()
    {
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource);
        project.Write("Demo.csproj", Csproj("net10.0"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(ShoutBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("dotnet restore", result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(project.LockPath));
    }

    [Fact]
    public void A_signature_that_uses_a_package_type_can_be_locked()
    {
        using var project = new TempProject();
        project.Write("Text.cs", WidgetSource);
        project.Write("Demo.csproj", Csproj("net10.0"));
        var folder = AddPackage(project, "Fake.Lib", "lib/net10.0/Fake.Lib.dll", PackageSource);
        WriteAssets(project, folder, ("net10.0", Package("Fake.Lib", "lib/net10.0/Fake.Lib.dll")));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return widget.Name;"));

        Assert.Equal(0, result.Code);
        Assert.Contains("generated Demo.Text.NameOf(Fake.Lib.Widget)", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unresolved_signature_type_without_an_assets_file_suggests_dotnet_restore()
    {
        using var project = new TempProject();
        project.Write("Text.cs", WidgetSource);
        project.Write("Demo.csproj", Csproj("net10.0"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return widget.Name;"));

        Assert.Equal(2, result.Code);
        Assert.Contains("does not resolve", result.Err, StringComparison.Ordinal);
        Assert.Contains("dotnet restore", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_target_named_by_TargetFramework_is_used()
    {
        // Ordinal order puts net10.0 first; the project targets net8.0, whose entry is the only one with the assembly.
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource);
        project.Write("Demo.csproj", Csproj("net8.0"));
        var folder = AddPackage(project, "Fake.Lib", "lib/net8.0/Fake.Lib.dll", PackageSource);
        WriteAssets(
            project,
            folder,
            ("net10.0", Package("Fake.Lib", "lib/net10.0/_._")),
            ("net8.0", Package("Fake.Lib", "lib/net8.0/Fake.Lib.dll")));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(ShoutBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void Placeholders_and_project_entries_are_skipped()
    {
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource);
        project.Write("Demo.csproj", Csproj("net10.0"));
        var folder = AddPackage(project, "Fake.Lib", "lib/net10.0/Fake.Lib.dll", PackageSource);
        var entries = new Dictionary<string, object>(Package("Fake.Lib", "lib/net10.0/Fake.Lib.dll"))
        {
            ["Empty.Pkg/1.0.0"] = new Dictionary<string, object> { ["type"] = "package", ["compile"] = new Dictionary<string, object> { ["lib/net10.0/_._"] = new { } } },
            ["Other.Project/1.0.0"] = new Dictionary<string, object> { ["type"] = "project", ["compile"] = new Dictionary<string, object> { ["bin/placeholder/Other.Project.dll"] = new { } } },
        };
        WriteAssets(project, folder, ("net10.0", entries));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(ShoutBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void A_package_copy_of_a_platform_assembly_loses_to_the_platform()
    {
        // The SDK's conflict resolution keeps the framework's copy; referencing both would make ImmutableList ambiguous.
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource.Replace("using Fake.Lib;", "using System.Collections.Immutable;", StringComparison.Ordinal));
        project.Write("Demo.csproj", Csproj("net10.0"));
        const string dll = "lib/net10.0/System.Collections.Immutable.dll";
        var folder = AddPackage(project, "System.Collections.Immutable", dll, "namespace System.Collections.Immutable { public static class ImmutableList { } }");
        WriteAssets(project, folder, ("net10.0", Package("System.Collections.Immutable", dll)));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return ImmutableList.Create(input)[0];"));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void A_package_missing_from_every_folder_is_named_in_the_refusal()
    {
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource);
        project.Write("Demo.csproj", Csproj("net10.0"));
        var folder = Path.Combine(project.Directory, "packages");
        Directory.CreateDirectory(folder);
        WriteAssets(project, folder, ("net10.0", Package("Fake.Lib", "lib/net10.0/Fake.Lib.dll")));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(ShoutBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("Fake.Lib.dll", result.Err, StringComparison.Ordinal);
        Assert.Contains("dotnet restore", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_assets_file_exits_2()
    {
        using var project = new TempProject();
        project.Write("Text.cs", UsingSource);
        project.Write("Demo.csproj", Csproj("net10.0"));
        Directory.CreateDirectory(Path.Combine(project.Directory, "obj"));
        project.Write("obj/project.assets.json", "{ \"targets\": ");

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(ShoutBody));

        Assert.Equal(2, result.Code);
        Assert.Contains("project.assets.json", result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(project.LockPath));
    }

    private static string Csproj(string framework) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>" + framework + "</TargetFramework></PropertyGroup></Project>";

    private static Dictionary<string, object> Package(string id, string compile) => new()
    {
        [id + "/1.0.0"] = new Dictionary<string, object>
        {
            ["type"] = "package",
            ["compile"] = new Dictionary<string, object> { [compile] = new { } },
        },
    };

    /// <summary>Compiles <paramref name="source"/> into a package folder laid out like the NuGet global packages folder; returns the folder.</summary>
    private static string AddPackage(TempProject project, string id, string compile, string source)
    {
        var folder = Path.Combine(project.Directory, "packages");
        var path = Path.Combine(folder, id.ToLowerInvariant(), "1.0.0", compile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitted = compilation.Emit(path);
        Assert.True(emitted.Success, string.Join("; ", emitted.Diagnostics));
        return folder;
    }

    private static void WriteAssets(TempProject project, string packageFolder, params (string Framework, Dictionary<string, object> Entries)[] targets)
    {
        var libraries = new Dictionary<string, object>();
        foreach (var key in targets.SelectMany(t => t.Entries).Where(e => e.Value is Dictionary<string, object> d && (string)d["type"] == "package").Select(e => e.Key).Distinct())
        {
            var (id, version) = (key.Split('/')[0], key.Split('/')[1]);
            libraries[key] = new { type = "package", path = id.ToLowerInvariant() + "/" + version };
        }

        var assets = new Dictionary<string, object>
        {
            ["version"] = 3,
            ["targets"] = targets.ToDictionary(t => t.Framework, t => (object)t.Entries),
            ["libraries"] = libraries,
            ["packageFolders"] = new Dictionary<string, object> { [Path.Combine(project.Directory, "missing-folder") + Path.DirectorySeparatorChar] = new { }, [packageFolder + Path.DirectorySeparatorChar] = new { } },
        };
        Directory.CreateDirectory(Path.Combine(project.Directory, "obj"));
        project.Write("obj/project.assets.json", JsonSerializer.Serialize(assets));
    }
}
