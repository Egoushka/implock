namespace Implock.Cli.Tests;

public class ReportTests
{
    private static string Sample(string name) => Path.Combine(Cli.RepoRoot(), "samples", "report", name);

    [Fact]
    public void Finds_the_planted_looser_case()
    {
        var result = Cli.Run("report", "--original", Sample("Original.cs"), "--generated", Sample("Generated.cs"), "--method", "ReportSample.Slugs.Slug");

        Assert.Equal(0, result.Code);
        Assert.Contains("generated looser: 3 input classes", result.Out);
        Assert.Contains("title=whitespace, maxLength=small", result.Out);
        Assert.Contains("ArgumentException", result.Out);
        Assert.DoesNotContain("title=empty", result.Out);
    }

    [Fact]
    public void Reports_zero_for_an_identical_pair()
    {
        var result = Cli.Run("report", "--original", Sample("Original.cs"), "--generated", Sample("Original.cs"), "--method", "ReportSample.Slugs.Slug");

        Assert.Equal(0, result.Code);
        Assert.Contains("generated looser: 0 input classes", result.Out);
    }

    [Fact]
    public void The_same_seed_gives_the_same_report()
    {
        var args = new[] { "report", "--original", Sample("Original.cs"), "--generated", Sample("Generated.cs"), "--method", "ReportSample.Slugs.Slug", "--seed", "7" };

        Assert.Equal(Cli.Run(args).Out, Cli.Run(args).Out);
    }

    [Fact]
    public void Fail_on_looser_turns_a_finding_into_exit_code_1()
    {
        var result = Cli.Run("report", "--original", Sample("Original.cs"), "--generated", Sample("Generated.cs"), "--method", "ReportSample.Slugs.Slug", "--fail-on-looser");

        Assert.Equal(1, result.Code);
    }

    [Fact]
    public void Reports_a_generated_body_that_is_stricter_separately()
    {
        var result = Cli.Run("report", "--original", Sample("Generated.cs"), "--generated", Sample("Original.cs"), "--method", "ReportSample.Slugs.Slug");

        Assert.Contains("generated looser: 0 input classes", result.Out);
        Assert.Contains("generated stricter: 3 input classes", result.Out);
    }

    [Fact]
    public void An_unknown_method_exits_2()
    {
        var result = Cli.Run("report", "--original", Sample("Original.cs"), "--generated", Sample("Generated.cs"), "--method", "ReportSample.Slugs.Nope");

        Assert.Equal(2, result.Code);
    }
}
