namespace Implock.Generator.Tests;

public class LockFileTests
{
    [Fact]
    public void Render_is_deterministic_and_independent_of_insertion_order()
    {
        var a = new LockFile();
        a.Set("B.M()", "bb", "return 2;");
        a.Set("A.M()", "aa", "return 1;");
        var b = new LockFile();
        b.Set("A.M()", "aa", "return 1;");
        b.Set("B.M()", "bb", "return 2;");

        Assert.Equal(a.Render(), b.Render());
        Assert.True(a.Render().IndexOf("A.M()", StringComparison.Ordinal) < a.Render().IndexOf("B.M()", StringComparison.Ordinal));
    }

    [Fact]
    public void Round_trips_multiline_bodies_including_blank_lines()
    {
        var file = new LockFile();
        file.Set("A.M()", "aa", "var x = 1;\n\nif (x > 0)\n{\n    return x;\n}\nreturn 0;");

        var parsed = LockFile.Parse(file.Render(), out var error);

        Assert.Null(error);
        Assert.Equal(file.Render(), parsed!.Render());
        Assert.Equal("var x = 1;\n\nif (x > 0)\n{\n    return x;\n}\nreturn 0;", parsed.Entries["A.M()"].Body);
    }

    [Fact]
    public void Parse_accepts_crlf_and_renders_lf()
    {
        var file = new LockFile();
        file.Set("A.M()", "aa", "return 1;");
        var crlf = file.Render().Replace("\n", "\r\n", StringComparison.Ordinal);

        var parsed = LockFile.Parse(crlf, out var error);

        Assert.Null(error);
        Assert.DoesNotContain('\r', parsed!.Render());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("implock 1\n\n@@ method A.M()\n| return 1;\n")]
    [InlineData("implock 9\n")]
    public void Parse_reports_an_error_with_a_line_for_bad_input(string text)
    {
        var parsed = LockFile.Parse(text, out var error);

        Assert.Null(parsed);
        Assert.Contains("line", error, StringComparison.Ordinal);
    }
}
