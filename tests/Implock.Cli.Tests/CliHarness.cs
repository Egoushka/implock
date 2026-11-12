using System.Text;

namespace Implock.Cli.Tests;

internal sealed class TempProject : IDisposable
{
    public const string Source = """
        using System;

        namespace Demo;

        public static partial class Text
        {
            /// <summary>Returns the text reversed.</summary>
            [Implock.Implement("reverses: Reverse(\"abc\") == \"cba\"")]
            public static partial string Reverse(string input);
        }
        """;

    public const string ReverseBody = "var chars = input.ToCharArray();\nArray.Reverse(chars);\nreturn new string(chars);";

    public TempProject()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("implock-test-").FullName;
        File.WriteAllText(Path.Combine(Directory, "Text.cs"), Source);
    }

    public string Directory { get; }

    public string LockPath => Path.Combine(Directory, "implement.lock");

    public string ReadLock() => File.ReadAllText(LockPath);

    public void Write(string name, string content) => File.WriteAllText(Path.Combine(Directory, name), content);

    /// <summary>A model command that ignores the prompt and prints a fixed reply.</summary>
    public string FakeModel(string reply)
    {
        var script = Path.Combine(Directory, "fake-model.sh");
        File.WriteAllText(script, "cat > /dev/null\ncat <<'IMPLOCK_REPLY'\n" + reply + "\nIMPLOCK_REPLY\n");
        return "sh " + script;
    }

    /// <summary>A model command that saves the prompt it receives to <paramref name="promptFile"/>.</summary>
    public string RecordingModel(string promptFile, string reply)
    {
        var script = Path.Combine(Directory, "recording-model.sh");
        File.WriteAllText(script, "cat > \"$1\"\ncat <<'IMPLOCK_REPLY'\n" + reply + "\nIMPLOCK_REPLY\n");
        return "sh " + script + " " + promptFile;
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}

internal static class Cli
{
    public static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        using var o = new StringWriter(output);
        using var e = new StringWriter(error);
        var code = Program.Run(args, o, e);
        return (code, output.ToString(), error.ToString());
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Implock.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Implock.slnx not found above the test binaries.");
    }
}
