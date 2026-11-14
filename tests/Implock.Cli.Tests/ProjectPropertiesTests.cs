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

    private const string WaveSource = """
        namespace Demo;

        public static partial class Kinds
        {
            [Implock.Implement("no: IsMath(1) == false")]
            public static partial bool IsMath(object input);
        }
        """;

    /// <summary>`is` against a static type: CS7023, a warning of wave 5, so only warning level 5 or higher reports it.</summary>
    private const string WaveFiveBody = "return input is System.Math;";

    [Theory]
    [InlineData("")]
    [InlineData("<TargetFramework>net5.0</TargetFramework>")]
    [InlineData("<TargetFramework>netcoreapp3.1</TargetFramework><AnalysisLevel>preview</AnalysisLevel>")]
    [InlineData("<TargetFramework></TargetFramework><TargetFrameworks>net8.0;net48</TargetFrameworks>")]
    [InlineData("<TargetFramework></TargetFramework><TargetFrameworks>net48;net8.0</TargetFrameworks>")]
    [InlineData("<TargetFramework>net8.0-windows</TargetFramework>")]
    public void A_wave_warning_the_build_reports_is_refused_under_TreatWarningsAsErrors(string properties)
    {
        // The SDK sets WarningLevel to the major version of a .NETCoreApp target, or 9999 under AnalysisLevel preview.
        using var project = new TempProject();
        project.Write("Text.cs", WaveSource);
        project.Write("Demo.csproj", Csproj(properties + "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(WaveFiveBody));

        Assert.Equal(1, result.Code);
        Assert.Contains("CS7023", result.Err);
        Assert.False(File.Exists(project.LockPath));
    }

    [Theory]
    [InlineData("<WarningLevel>4</WarningLevel>")]
    [InlineData("<TargetFramework>net48</TargetFramework>")]
    [InlineData("<TargetFramework>netcoreapp3.1</TargetFramework>")]
    [InlineData("<TargetFramework>netstandard2.0</TargetFramework>")]
    public void A_wave_warning_below_the_build_level_is_kept(string properties)
    {
        // An explicit WarningLevel wins; .NET Framework is 4, netcoreapp3.1 is 3, and .NET Standard leaves the compiler default of 4.
        using var project = new TempProject();
        project.Write("Text.cs", WaveSource);
        project.Write("Demo.csproj", Csproj(properties + "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(WaveFiveBody));

        Assert.Equal(0, result.Code);
        Assert.Contains("generated Demo.Kinds.IsMath(object)", result.Out);
    }

    /// <summary>A lowercase `l` suffix: CS0078, a warning of level 4, so level 3 keeps it and level 4 reports it.</summary>
    private const string LevelFourBody = "return 1l == 0;";

    [Theory]
    [InlineData("<TargetFramework>net48</TargetFramework>", 1)]
    [InlineData("<TargetFramework>netstandard2.0</TargetFramework>", 1)]
    [InlineData("<TargetFramework>netcoreapp3.1</TargetFramework>", 0)]
    [InlineData("<TargetFramework></TargetFramework>", 1)]
    public void A_level_four_warning_follows_the_target_level(string properties, int code)
    {
        // .NET Framework and .NET Standard build at level 4; netcoreapp3.1 builds at level 3; no target framework keeps the compiler default of 4.
        using var project = new TempProject();
        project.Write("Text.cs", WaveSource);
        project.Write("Demo.csproj", Csproj(properties + "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(LevelFourBody));

        Assert.Equal(code, result.Code);
        Assert.Equal(code == 0, File.Exists(project.LockPath));
        if (code == 1)
        {
            Assert.Contains("CS0078", result.Err);
        }
    }

    [Fact]
    public void A_WarningLevel_the_compiler_rejects_exits_2()
    {
        using var project = new TempProject();
        project.Write("Text.cs", WaveSource);
        project.Write("Demo.csproj", Csproj("<WarningLevel>high</WarningLevel>"));

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(WaveFiveBody));

        Assert.Equal(2, result.Code);
        Assert.Contains("'warn' option", result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(project.LockPath));
    }

    private static string Csproj(string properties) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" + properties + "</PropertyGroup></Project>";
}
