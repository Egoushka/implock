using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Implock.Cli;

/// <summary>Differential fuzzing of an original body against a generated one: where does the generated body accept what the original rejects.</summary>
internal static class ReportCommand
{
    private const int MaxCombinations = 5000;

    private enum Verdict
    {
        Agree,
        Looser,
        Stricter,
        Differs,
    }

    private sealed class Tally
    {
        public int Runs;
        public int Looser;
        public int Stricter;
        public int Differs;
        public int OriginalThrows;
        public string Example = string.Empty;
    }

    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        options.Allow("--original", "--generated", "--method", "--seed", "--runs", "--fail-on-looser");
        var originalPath = options.Required("--original");
        var generatedPath = options.Required("--generated");
        var methodName = options.Required("--method");
        var seed = options.Int("--seed", 1);
        var runs = options.Int("--runs", 50);
        if (runs < 1)
        {
            throw new UsageException("--runs must be at least 1");
        }

        var original = Load(originalPath, "implock.report.original", error);
        var generated = Load(generatedPath, "implock.report.generated", error);
        if (original is null || generated is null)
        {
            return Program.Failure;
        }

        var left = Find(original, methodName, "original");
        var right = Find(generated, methodName, "generated");

        var parameters = left.GetParameters();
        var lists = new List<IReadOnlyList<InputClass>>();
        foreach (var parameter in parameters)
        {
            var classes = InputClasses.For(parameter.ParameterType)
                ?? throw new UsageException("parameter '" + parameter.Name + "' has unsupported type " + parameter.ParameterType.Name);
            lists.Add(classes);
        }

        var total = lists.Aggregate(1L, (acc, l) => acc * l.Count);
        if (total > MaxCombinations)
        {
            throw new UsageException("too many input classes (" + total.ToString(CultureInfo.InvariantCulture) + "; the limit is " + MaxCombinations.ToString(CultureInfo.InvariantCulture) + ")");
        }

        var rows = new List<(string Label, Tally Tally)>();
        for (var combo = 0; combo < total; combo++)
        {
            rows.Add(Explore(parameters, lists, combo, seed, runs, left, right));
        }

        return Print(rows, output, options.Has("--fail-on-looser"));
    }

    private static (string, Tally) Explore(ParameterInfo[] parameters, List<IReadOnlyList<InputClass>> lists, int combo, int seed, int runs, MethodInfo left, MethodInfo right)
    {
        var picks = new InputClass[lists.Count];
        var rest = combo;
        for (var i = lists.Count - 1; i >= 0; i--)
        {
            picks[i] = lists[i][rest % lists[i].Count];
            rest /= lists[i].Count;
        }

        var label = string.Join(", ", picks.Select((p, i) => parameters[i].Name + "=" + p.Name));
        var random = new Random(unchecked((seed * 7919) + combo));
        var tally = new Tally();
        for (var run = 0; run < runs; run++)
        {
            var args = picks.Select(p => p.Draw(random)).ToArray();
            var a = Safe(left, args);
            var b = Safe(right, args);
            tally.Runs++;
            if (a.StartsWith("EX:", StringComparison.Ordinal))
            {
                tally.OriginalThrows++;
            }

            switch (Classify(a, b))
            {
                case Verdict.Looser:
                    tally.Looser++;
                    if (tally.Example.Length == 0)
                    {
                        tally.Example = "(" + string.Join(", ", args.Select(InputClasses.Render)) + ") original " + a + ", generated " + b;
                    }

                    break;
                case Verdict.Stricter:
                    tally.Stricter++;
                    break;
                case Verdict.Differs:
                    tally.Differs++;
                    break;
                case Verdict.Agree:
                    break;
            }
        }

        return (label, tally);
    }

    private static Verdict Classify(string original, string generated)
    {
        var originalThrew = original.StartsWith("EX:", StringComparison.Ordinal);
        var generatedThrew = generated.StartsWith("EX:", StringComparison.Ordinal);
        if (originalThrew && generatedThrew)
        {
            return Verdict.Agree;
        }

        if (originalThrew)
        {
            return Verdict.Looser;
        }

        if (generatedThrew)
        {
            return Verdict.Stricter;
        }

        return string.Equals(original, generated, StringComparison.Ordinal) ? Verdict.Agree : Verdict.Differs;
    }

    private static string Safe(MethodInfo method, object?[] args)
    {
        try
        {
            return "V:" + InputClasses.Render(method.Invoke(null, args));
        }
        catch (TargetInvocationException ex)
        {
            return "EX:" + (ex.InnerException ?? ex).GetType().Name;
        }
    }

    private static int Print(List<(string Label, Tally Tally)> rows, TextWriter output, bool failOnLooser)
    {
        var looser = rows.Where(r => r.Tally.Looser > 0).ToList();
        var stricter = rows.Count(r => r.Tally.Stricter > 0);
        var differs = rows.Count(r => r.Tally.Differs > 0);

        output.WriteLine("| input class | runs | generated looser | original throws | example |");
        output.WriteLine("|---|---|---|---|---|");
        foreach (var (label, tally) in looser)
        {
            output.WriteLine("| " + label + " | " + Num(tally.Runs) + " | " + Num(tally.Looser) + " | " + Num(tally.OriginalThrows) + " | `" + tally.Example.Replace("|", "\\|", StringComparison.Ordinal) + "` |");
        }

        output.WriteLine();
        output.WriteLine("input classes explored: " + Num(rows.Count));
        output.WriteLine("generated looser: " + Num(looser.Count) + " input classes");
        output.WriteLine("generated stricter: " + Num(stricter) + " input classes");
        output.WriteLine("values differ: " + Num(differs) + " input classes");
        return failOnLooser && looser.Count > 0 ? Program.Findings : Program.Ok;
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static Assembly? Load(string path, string assemblyName, TextWriter error)
    {
        if (!File.Exists(path))
        {
            throw new UsageException("file not found: " + path);
        }

        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), path);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            LoadedProject.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Suppress));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                error.WriteLine("implock: " + path + ": " + d);
            }

            return null;
        }

        return Assembly.Load(stream.ToArray());
    }

    private static MethodInfo Find(Assembly assembly, string name, string role)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            throw new UsageException("--method must look like Namespace.Type.Method");
        }

        var type = assembly.GetType(name[..dot])
            ?? throw new UsageException("type " + name[..dot] + " not found in the " + role + " file");
        var found = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, name[(dot + 1)..], StringComparison.Ordinal))
            .ToList();
        if (found.Count != 1)
        {
            throw new UsageException("expected one public static method " + name + " in the " + role + " file, found " + Num(found.Count));
        }

        if (found[0].IsGenericMethodDefinition)
        {
            throw new UsageException("generic methods are not supported");
        }

        return found[0];
    }
}
