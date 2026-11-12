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
    public void The_prompt_lists_sibling_member_signatures_and_signature_types_without_bodies()
    {
        using var project = new TempProject();
        project.Write("Text.cs", """
            namespace Demo;

            public enum Mood
            {
                Calm,
                Busy,
            }

            public readonly record struct Widget(string Name);

            public sealed class Snapshot
            {
                private int secret = 7;

                public int Count { get; set; }

                public string Label { get; set; } = "";

                private int Secret() => secret;
            }

            public static partial class Text
            {
                private static readonly int Limit = 3;

                private static string Helper(int n)
                {
                    return "DISTINCTIVE-HELPER-LITERAL" + n;
                }

                [Implock.Implement]
                public static partial int Score(Snapshot[] snapshots, System.Collections.Generic.List<Widget> widgets, Mood mood);
            }
            """);
        var promptFile = Path.Combine(project.Directory, "prompt.txt");

        Cli.Run("generate", project.Directory, "--model", project.RecordingModel(promptFile, "return 1;"));

        var prompt = File.ReadAllText(promptFile);
        Assert.Contains("Types in scope (signatures only, no bodies):", prompt);
        Assert.Contains("private static readonly int Limit", prompt);
        Assert.Contains("private static string Helper(int n)", prompt);
        Assert.Contains("public int Count { get; set; }", prompt);
        Assert.Contains("public string Label { get; set; }", prompt);
        Assert.Contains("class Snapshot", prompt);
        Assert.Contains("struct Widget", prompt);
        Assert.DoesNotContain("class List<T>", prompt);
        Assert.DoesNotContain("secret", prompt);
        Assert.DoesNotContain("Secret()", prompt);
        Assert.Contains("public enum Mood\n  Calm\n  Busy\n", prompt);
        Assert.DoesNotContain("DISTINCTIVE-HELPER-LITERAL", prompt);
        Assert.Equal(prompt.IndexOf("Score(", StringComparison.Ordinal), prompt.LastIndexOf("Score(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_prompt_omits_the_type_block_when_nothing_is_in_scope()
    {
        using var project = new TempProject();
        var promptFile = Path.Combine(project.Directory, "prompt.txt");

        Cli.Run("generate", project.Directory, "--model", project.RecordingModel(promptFile, TempProject.ReverseBody));

        Assert.DoesNotContain("Types in scope", File.ReadAllText(promptFile));
    }

    [Fact]
    public void Regen_regenerates_a_locked_method_and_replaces_its_body()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));
        var before = project.ReadLock();
        var promptFile = Path.Combine(project.Directory, "prompt.txt");

        var result = Cli.Run("generate", project.Directory, "--regen", "Demo.Text.Reverse(string)", "--model", project.RecordingModel(promptFile, "return input + input;"));

        Assert.Equal(0, result.Code);
        Assert.True(File.Exists(promptFile));
        Assert.Contains("generated Demo.Text.Reverse(string)", result.Out);
        var after = project.ReadLock();
        Assert.NotEqual(before, after);
        Assert.Contains("return input + input;", after);
        Assert.DoesNotContain("Array.Reverse", after);
    }

    [Fact]
    public void Regen_keeps_the_previous_body_when_the_new_one_does_not_compile()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));
        var before = File.ReadAllBytes(project.LockPath);

        var result = Cli.Run("generate", project.Directory, "--regen", "Demo.Text.Reverse(string)", "--model", project.FakeModel("return missingName;"));

        Assert.Equal(1, result.Code);
        Assert.Contains("missingName", result.Err);
        Assert.Equal(before, File.ReadAllBytes(project.LockPath));
    }

    [Fact]
    public void Regen_also_generates_methods_that_are_still_pending()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));
        project.Write("Twice.cs", """
            namespace Demo;

            public static partial class Text
            {
                [Implock.Implement]
                public static partial string Twice(string input);
            }
            """);

        var result = Cli.Run("generate", project.Directory, "--regen", "Demo.Text.Reverse(string)", "--model", project.FakeModel("return input + input;"));

        Assert.Equal(0, result.Code);
        Assert.Contains("generated Demo.Text.Reverse(string)", result.Out);
        Assert.Contains("generated Demo.Text.Twice(string)", result.Out);
        Assert.DoesNotContain("Array.Reverse", project.ReadLock());
    }

    [Fact]
    public void Regen_splits_the_id_list_only_on_commas_outside_brackets()
    {
        using var project = new TempProject();
        project.Write("Shapes.cs", """
            namespace Demo;

            public static partial class Text
            {
                [Implock.Implement]
                public static partial int Count(System.Collections.Generic.IReadOnlyDictionary<string, int> map);

                [Implock.Implement]
                public static partial int Pad(string text, int width);

                [Implock.Implement]
                public static partial int Cells(int[,] grid);
            }
            """);
        const string body = "throw new System.NotImplementedException();";
        var seeded = Cli.Run("generate", project.Directory, "--model", project.FakeModel(body));
        Assert.Equal(0, seeded.Code);
        string[] ids =
        [
            "Demo.Text.Pad(string, int)",
            "Demo.Text.Count(System.Collections.Generic.IReadOnlyDictionary<string, int>)",
            "Demo.Text.Cells(int[,])",
            "Demo.Text.Reverse(string)",
        ];

        var result = Cli.Run("generate", project.Directory, "--regen", string.Join(" , ", ids), "--model", project.FakeModel(body));

        Assert.Equal(0, result.Code);
        foreach (var id in ids) Assert.Contains("generated " + id, result.Out);
    }

    [Fact]
    public void Regen_with_no_id_exits_2()
    {
        using var project = new TempProject();

        var result = Cli.Run("generate", project.Directory, "--regen", ",", "--model", "sh /nonexistent-model-command");

        Assert.Equal(2, result.Code);
        Assert.Contains("implock: --regen needs at least one id", result.Err);
        Assert.False(File.Exists(project.LockPath));
    }

    [Fact]
    public void Regen_with_an_unknown_id_exits_2()
    {
        using var project = new TempProject();
        Cli.Run("generate", project.Directory, "--model", project.FakeModel(TempProject.ReverseBody));

        var result = Cli.Run("generate", project.Directory, "--regen", "Demo.Text.Nope()", "--model", "sh /nonexistent-model-command");

        Assert.Equal(2, result.Code);
        Assert.Contains("implock: --regen names no [Implement] method: Demo.Text.Nope()", result.Err);
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
