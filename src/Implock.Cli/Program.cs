using System.Globalization;
using System.Reflection;

namespace Implock.Cli;

internal static class Program
{
    public const int Ok = 0;
    public const int Findings = 1;
    public const int Failure = 2;

    private const string Usage = """
        implock: model-written method bodies kept in a reviewed lock file.

        usage:
          implock check [PROJECT]
          implock generate [PROJECT] [--model "COMMAND"] [--prune]
          implock report --original FILE --generated FILE --method Type.Method
                         [--seed N] [--runs N] [--fail-on-looser]
          implock --help | --version

        PROJECT is a directory or a .csproj file; the default is the current directory.
        The model command reads the prompt on stdin and prints the method body on stdout.
        Its default is "claude -p"; the IMPLOCK_MODEL environment variable overrides it,
        and --model overrides both.

        exit codes: 0 ok, 1 findings (check, report --fail-on-looser, a refused body), 2 usage or error.
        """;

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            (args.Length == 0 ? error : output).WriteLine(Usage);
            return args.Length == 0 ? Failure : Ok;
        }

        if (args[0] == "--version")
        {
            output.WriteLine(Version());
            return Ok;
        }

        try
        {
            var options = Options.Parse(args.AsSpan(1));
            return args[0] switch
            {
                "check" => CheckCommand.Run(options, output, error),
                "generate" => GenerateCommand.Run(options, output, error),
                "report" => ReportCommand.Run(options, output, error),
                _ => UsageError(error, string.Create(CultureInfo.InvariantCulture, $"unknown command '{args[0]}'")),
            };
        }
        catch (UsageException ex)
        {
            return UsageError(error, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            error.WriteLine("implock: " + ex.Message);
            return Failure;
        }
    }

    private static int UsageError(TextWriter error, string message)
    {
        error.WriteLine("implock: " + message);
        error.WriteLine("run 'implock --help' for usage.");
        return Failure;
    }

    private static string Version()
    {
        var assembly = typeof(Program).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return info ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }
}

internal sealed class UsageException : Exception
{
    public UsageException(string message)
        : base(message)
    {
    }
}

/// <summary>Flags and positionals; a flag is "--name VALUE" or a known switch.</summary>
internal sealed class Options
{
    private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "--prune", "--fail-on-looser" };

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _switches = new(StringComparer.Ordinal);

    public List<string> Positionals { get; } = new();

    public static Options Parse(ReadOnlySpan<string> args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                options.Positionals.Add(arg);
            }
            else if (Switches.Contains(arg))
            {
                options._switches.Add(arg);
            }
            else if (i + 1 < args.Length)
            {
                options._values[arg] = args[++i];
            }
            else
            {
                throw new UsageException(string.Create(CultureInfo.InvariantCulture, $"'{arg}' needs a value"));
            }
        }

        return options;
    }

    public bool Has(string name) => _switches.Contains(name);

    public string? Value(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public string Required(string name) =>
        Value(name) ?? throw new UsageException(string.Create(CultureInfo.InvariantCulture, $"missing {name}"));

    public int Int(string name, int fallback)
    {
        var text = Value(name);
        if (text is null)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0
            ? n
            : throw new UsageException(string.Create(CultureInfo.InvariantCulture, $"{name} needs a non-negative whole number"));
    }

    /// <summary>Rejects flags the command does not know, so a typo is not silently ignored.</summary>
    public void Allow(params string[] known)
    {
        foreach (var name in _values.Keys.Concat(_switches))
        {
            if (!known.Contains(name, StringComparer.Ordinal))
            {
                throw new UsageException(string.Create(CultureInfo.InvariantCulture, $"unknown option '{name}'"));
            }
        }
    }
}
