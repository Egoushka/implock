using System.Diagnostics;
using System.Text;

namespace Implock.Cli;

/// <summary>
/// Runs the configured model command: the prompt goes to stdin, the reply is read from stdout.
/// This is the only place implock reaches a model; there is no network code in this tool.
/// </summary>
internal static class ModelRunner
{
    public const string DefaultCommand = "claude -p";
    public const string EnvironmentVariable = "IMPLOCK_MODEL";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static string Resolve(string? flag) =>
        !string.IsNullOrWhiteSpace(flag) ? flag
        : Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } env ? env
        : DefaultCommand;

    public static (bool Ok, string Stdout, string Stderr) Run(string command, string prompt)
    {
        var words = Tokenize(command);
        if (words.Count == 0)
        {
            return (false, string.Empty, "the model command is empty");
        }

        var start = new ProcessStartInfo(words[0])
        {
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(false),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var word in words.Skip(1))
        {
            start.ArgumentList.Add(word);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("the process did not start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, string.Empty, "cannot start the model command '" + words[0] + "': " + ex.Message);
        }

        using (process)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => Append(stdout, e.Data);
            process.ErrorDataReceived += (_, e) => Append(stderr, e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                process.StandardInput.Write(prompt);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The model may exit without reading its input; its exit code decides.
            }

            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return (false, string.Empty, "the model command timed out");
            }

            process.WaitForExit();
            var ok = process.ExitCode == 0;
            lock (stdout)
            {
                return (ok, stdout.ToString(), stderr.ToString());
            }
        }
    }

    private static void Append(StringBuilder buffer, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (buffer)
        {
            buffer.Append(line).Append('\n');
        }
    }

    /// <summary>Splits on whitespace and honours single and double quotes. There is no shell.</summary>
    internal static List<string> Tokenize(string command)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var inWord = false;
        char quote = '\0';
        foreach (var c in command)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }
            }
            else
            {
                current.Append(c);
                inWord = true;
            }
        }

        if (inWord)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
