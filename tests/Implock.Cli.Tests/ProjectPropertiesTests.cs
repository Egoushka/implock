namespace Implock.Cli.Tests;

/// <summary>The loader reads the build's warning settings, so a body the build would refuse is refused here too.</summary>
public class ProjectPropertiesTests
{
    private const string NullableSource = """
        namespace Demo;

        public static partial class Text
        {
            [Implock.Implement("upper-cases: Upper(\"a\") == \"A\"")]
            public static partial string Upper(string? input);
        }
        """;

    /// <summary>Dereferences a maybe-null parameter: CS8602, a warning that TreatWarningsAsErrors turns into an error.</summary>
    private const string DereferencingBody = "return input.ToUpperInvariant();";

    [Fact]
    public void Generate_refuses_a_body_the_build_would_refuse_under_TreatWarningsAsErrors()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("CS8602", result.Err);
        Assert.False(File.Exists(project.LockPath));
    }

    [Fact]
    public void Generate_accepts_the_same_body_when_the_warning_is_not_an_error()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>CS8602</WarningsNotAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(0, result.Code);
        Assert.Contains("generated Demo.Text.Upper(string)", result.Out);
    }

    [Fact]
    public void WarningsAsErrors_lists_a_single_warning_and_accepts_bare_numbers_and_commas()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><WarningsAsErrors>CS0219, 8602</WarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("CS8602", result.Err);
    }

    [Fact]
    public void NoWarn_wins_over_TreatWarningsAsErrors()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>$(NoWarn);CS8602</NoWarn>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void The_csproj_expands_what_it_inherits_from_Directory_Build_props()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Directory.Build.props", "<Project><PropertyGroup><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsAsErrors>CS8602</WarningsAsErrors></PropertyGroup></Project>");
        project.Write("Demo.csproj", Csproj("<TreatWarningsAsErrors>false</TreatWarningsAsErrors><WarningsAsErrors>$(WarningsAsErrors);CS0219</WarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("CS8602", result.Err);
    }

    [Fact]
    public void A_warning_stays_a_warning_without_any_setting()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void Conditional_groups_and_properties_are_ignored()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><WarningsAsErrors Condition=\"'$(CI)' == 'true'\">CS8602</WarningsAsErrors></PropertyGroup><PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><TreatWarningsAsErrors>true</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void WarningsAsErrors_nullable_names_every_nullable_warning()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><WarningsAsErrors>Nullable</WarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("CS8602", result.Err);
    }

    [Fact]
    public void Nullable_annotations_mode_reaches_the_compiler_unchanged()
    {
        // Under `annotations` the `string?` in the spec is legal; a null-safe body gives the build nothing to promote.
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>annotations</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return (input ?? \"\").ToUpperInvariant();"));

        Assert.Equal(0, result.Code);
        Assert.Contains("generated Demo.Text.Upper(string)", result.Out);
    }

    [Fact]
    public void The_csproj_overrides_Directory_Build_props()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Directory.Build.props", "<Project><PropertyGroup><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
        project.Write("Demo.csproj", Csproj("<TreatWarningsAsErrors>false</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void The_nearer_Directory_Build_props_overrides_the_outer_one()
    {
        using var project = new TempProject();
        var inner = Path.Combine(project.Directory, "inner");
        Directory.CreateDirectory(inner);
        project.Write("Directory.Build.props", "<Project><PropertyGroup><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
        project.Write("inner/Directory.Build.props", "<Project><PropertyGroup><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup></Project>");
        project.Write("inner/Text.cs", NullableSource);
        project.Write("inner/Demo.csproj", Csproj(""));

        var result = Cli.Run("generate", inner, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void WarningsAsErrors_may_span_lines_with_tab_indentation()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enable</Nullable><WarningsAsErrors>\n\t\tCS0219;\n\t\tCS8602\n\t</WarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("CS8602", result.Err);
    }

    [Fact]
    public void A_Nullable_value_the_compiler_rejects_exits_2()
    {
        using var project = new TempProject();
        project.Write("Text.cs", NullableSource);
        project.Write("Demo.csproj", Csproj("<Nullable>enabel</Nullable>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(DereferencingBody));

        Assert.Equal(2, result.Code);
        Assert.Contains("enabel", result.Err);
        Assert.False(File.Exists(project.LockPath));
    }

    private static string Csproj(string properties) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" + properties + "</PropertyGroup></Project>";
}
