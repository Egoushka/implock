namespace Implock.Cli.Tests;

public class CheckAndGenerateTests
{
    [Fact]
    public void Check_exits_1_on_a_missing_entry()
    {
        using var project = new TempProject();

        var result = Cli.Run("check", project.Directory);

        Assert.Equal(1, result.Code);
        Assert.Contains("IMPL001", result.Out + result.Err);
    }

    [Fact]
    public void Check_exits_0_when_every_method_is_locked()
    {
        using var project = new TempProject();
        Assert.Equal(0, Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody)).Code);

        var result = Cli.Run("check", project.Directory);

        Assert.Equal(0, result.Code);
        Assert.Contains("1 method", result.Out);
    }

    [Fact]
    public void Check_exits_1_when_the_spec_changed_after_the_lock_was_written()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));
        project.Write("Text.cs", TempProject.Source.Replace("Returns the text reversed.", "Returns the text reversed, keeping case."));

        var result = Cli.Run("check", project.Directory);

        Assert.Equal(1, result.Code);
        Assert.Contains("IMPL002", result.Out + result.Err);
    }

    [Fact]
    public void Check_exits_1_on_a_renamed_method_and_warns_about_the_orphan()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));
        project.Write("Text.cs", TempProject.Source.Replace("Reverse(", "Flip("));

        var result = Cli.Run("check", project.Directory);

        Assert.Equal(1, result.Code);
        Assert.Contains("IMPL001", result.Out + result.Err);
        Assert.Contains("IMPL003", result.Out + result.Err);
    }

    [Fact]
    public void Generate_with_a_fake_model_writes_a_valid_entry_that_builds()
    {
        using var project = new TempProject();

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));

        Assert.Equal(0, result.Code);
        var lockText = project.ReadLock();
        Assert.Contains("@@ method Demo.Text.Reverse(string)", lockText);
        Assert.Contains("| Array.Reverse(chars);", lockText);
        Assert.Equal(0, Cli.Run("check", project.Directory).Code);
    }

    [Fact]
    public void Generate_strips_a_markdown_fence_around_the_reply()
    {
        using var project = new TempProject();

        var fenced = "Here is the body:\n```csharp\n" + TempProject.ReverseBody + "\n```";
        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel(fenced));

        Assert.Equal(0, result.Code);
        Assert.DoesNotContain("```", project.ReadLock());
        Assert.DoesNotContain("Here is", project.ReadLock());
    }

    [Fact]
    public void Generating_the_lock_twice_gives_byte_identical_files()
    {
        using var project = new TempProject();
        var model = project.FakeModel(TempProject.ReverseBody);

        Cli.Run("generate", project.Directory, "--model", model);
        var first = File.ReadAllBytes(project.LockPath);
        File.Delete(project.LockPath);
        Cli.Run("generate", project.Directory, "--model", model);
        var second = File.ReadAllBytes(project.LockPath);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_leaves_a_complete_lock_untouched_and_does_not_call_the_model()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));
        var before = File.ReadAllBytes(project.LockPath);

        var result = Cli.Run("generate", project.Directory, "--model", "sh /nonexistent-model-command");

        Assert.Equal(0, result.Code);
        Assert.Equal(before, File.ReadAllBytes(project.LockPath));
        Assert.Contains("nothing to generate", result.Out);
    }

    [Fact]
    public void Generate_refuses_a_body_that_does_not_compile_and_writes_nothing()
    {
        using var project = new TempProject();

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return missingName;"));

        Assert.Equal(1, result.Code);
        Assert.False(File.Exists(project.LockPath));
        Assert.Contains("missingName", result.Err);
    }

    [Fact]
    public void Generate_refuses_a_body_that_closes_the_method_early()
    {
        using var project = new TempProject();

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return input;\n}\nstatic void Evil() {"));

        Assert.Equal(1, result.Code);
        Assert.False(File.Exists(project.LockPath));
    }

    [Fact]
    public void Generate_fails_when_the_model_command_fails()
    {
        using var project = new TempProject();
        project.Write("fail.sh", "cat > /dev/null\nexit 3\n");

        var result = Cli.Run("generate", project.Directory, "--model", "sh " + Path.Combine(project.Directory, "fail.sh"));

        Assert.Equal(2, result.Code);
        Assert.False(File.Exists(project.LockPath));
    }

    [Fact]
    public void The_prompt_carries_the_signature_doc_comment_containing_type_and_named_tests()
    {
        using var project = new TempProject();
        var promptFile = Path.Combine(project.Directory, "prompt.txt");

        Cli.Run("generate", project.Directory, "--model", project.RecordingModel(promptFile, TempProject.ReverseBody));

        var prompt = File.ReadAllText(promptFile);
        Assert.Contains("public static partial string Reverse(string input)", prompt);
        Assert.Contains("Returns the text reversed.", prompt);
        Assert.Contains("partial class Text", prompt);
        Assert.Contains("reverses: Reverse(\"abc\") == \"cba\"", prompt);
        Assert.Contains("data", prompt);
    }

    [Fact]
    public void Prune_removes_orphan_entries_and_only_with_the_flag()
    {
        using var project = new TempProject();
        var model = project.FakeModel(TempProject.ReverseBody);
        Cli.Run("generate", project.Directory, "--model", model);
        project.Write("Text.cs", TempProject.Source.Replace("Reverse(", "Flip("));
        Cli.Run("generate", project.Directory, "--model", project.FakeModel("return input;"));
        Assert.Contains("Demo.Text.Reverse(string)", project.ReadLock());

        var result = Cli.Run("generate", project.Directory, "--model", project.FakeModel("return input;"), "--prune");

        Assert.Equal(0, result.Code);
        Assert.DoesNotContain("Demo.Text.Reverse(string)", project.ReadLock());
        Assert.Contains("Demo.Text.Flip(string)", project.ReadLock());
    }

    [Fact]
    public void The_committed_sample_project_is_complete()
    {
        var result = Cli.Run("check", Path.Combine(Cli.RepoRoot(), "samples", "Implock.Sample"));

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void Unknown_commands_and_missing_arguments_exit_2()
    {
        Assert.Equal(2, Cli.Run("frobnicate").Code);
        Assert.Equal(2, Cli.Run("report", "--original", "a.cs").Code);
        Assert.Equal(2, Cli.Run("check", Path.Combine(Path.GetTempPath(), "implock-does-not-exist")).Code);
    }

    [Fact]
    public void Generate_refuses_a_spec_whose_type_does_not_resolve_and_exits_2()
    {
        using var project = new TempProject();
        project.Write("Text.cs", TempProject.Source.Replace("string input", "Some.Package.Widget input", StringComparison.Ordinal));
        var promptFile = Path.Combine(project.Directory, "prompt.txt");

        var result = Cli.Run("generate", project.Directory, "--model", project.RecordingModel(promptFile, TempProject.ReverseBody));

        Assert.Equal(2, result.Code);
        Assert.Contains("does not resolve", result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(promptFile));
        Assert.False(File.Exists(project.LockPath));
    }

    [Fact]
    public void Check_on_a_path_that_is_not_a_project_exits_2()
    {
        var result = Cli.Run("check", Path.Combine(Path.GetTempPath(), "implock-no-such-dir-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(2, result.Code);
        Assert.Contains("implock:", result.Err, StringComparison.Ordinal);
    }
}
