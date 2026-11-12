using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Implock.Cli;

internal static class GenerateCommand
{
    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        options.Allow("--model", "--prune", "--regen");
        var project = LoadedProject.Load(options.Positionals.FirstOrDefault());
        var original = project.ReadLockText() ?? "implock 1\n";
        var lockFile = LockFile.Parse(original, out var parseError);
        if (lockFile is null)
        {
            error.WriteLine("implock: " + LockFile.FileName + " is malformed: " + parseError);
            return Program.Failure;
        }

        var compilation = project.Run(original).Compilation;
        var specs = SpecScanner.Scan(compilation);
        var pending = specs
            .Where(s => !lockFile.Entries.TryGetValue(s.Id, out var e) || !string.Equals(e.SpecHash, s.SpecHash, StringComparison.Ordinal))
            .ToList();

        if (options.Value("--regen") is { } regen)
        {
            var ids = regen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
            if (ids.Count == 0)
            {
                throw new UsageException("--regen needs at least one id");
            }

            var known = specs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            if (ids.FirstOrDefault(id => !known.Contains(id)) is { } unknown)
            {
                throw new UsageException("--regen names no [Implement] method: " + unknown);
            }

            pending = specs.Where(s => ids.Contains(s.Id) || pending.Contains(s)).ToList();
        }

        if (options.Has("--prune"))
        {
            var live = new HashSet<string>(specs.Select(s => s.Id), StringComparer.Ordinal);
            foreach (var id in lockFile.Entries.Keys.Where(id => !live.Contains(id)).ToList())
            {
                lockFile.Remove(id);
                output.WriteLine("implock: pruned " + id);
            }
        }

        var failed = false;
        var environmentFailure = false;
        if (pending.Count == 0)
        {
            output.WriteLine("implock: nothing to generate");
        }
        else
        {
            var command = ModelRunner.Resolve(options.Value("--model"));
            foreach (var spec in pending)
            {
                if (spec.UnresolvedType is not null)
                {
                    error.WriteLine("implock: cannot lock " + spec.Id + ": the type '" + spec.UnresolvedType
                        + "' does not resolve here. The CLI sees only the .cs files and the runtime libraries, not NuGet packages or other projects.");
                    environmentFailure = true;
                    continue;
                }

                var reason = Fill(project, lockFile, spec, command, compilation, out var modelFailed);
                environmentFailure |= modelFailed;
                if (reason is null)
                {
                    output.WriteLine("implock: generated " + spec.Id);
                }
                else
                {
                    error.WriteLine("implock: refused " + spec.Id + ": " + reason);
                    failed = true;
                }
            }
        }

        var rendered = lockFile.Render();
        if (!string.Equals(rendered, original, StringComparison.Ordinal) && (project.ReadLockText() is not null || lockFile.Entries.Count > 0))
        {
            File.WriteAllText(project.LockPath, rendered);
        }

        return environmentFailure ? Program.Failure : failed ? Program.Findings : Program.Ok;
    }

    /// <summary>Asks the model for one body, checks it, and records it in <paramref name="lockFile"/>. Returns why it was refused, or null.</summary>
    private static string? Fill(LoadedProject project, LockFile lockFile, MethodSpec spec, string command, Compilation compilation, out bool modelFailed)
    {
        var (ok, stdout, stderr) = ModelRunner.Run(command, PromptBuilder.Build(spec, TypeContext.Build(compilation, spec)));
        modelFailed = !ok;
        if (!ok)
        {
            return "the model command failed. " + stderr.Trim();
        }

        var body = ModelOutput.ToBody(stdout);
        if (body is null)
        {
            return "the model returned no body.";
        }

        var invalid = BodyValidator.Validate(body);
        if (invalid is not null)
        {
            return invalid;
        }

        var previous = lockFile.Entries.TryGetValue(spec.Id, out var old) ? old : null;
        lockFile.Set(spec.Id, spec.SpecHash, body);
        var problem = Verify(project, lockFile, spec);
        if (problem is not null)
        {
            if (previous is null)
            {
                lockFile.Remove(spec.Id);
            }
            else
            {
                lockFile.Set(spec.Id, previous.SpecHash, previous.Body);
            }
        }

        return problem;
    }

    /// <summary>Compiles the project with the new entry and reports errors in this method's generated file only.</summary>
    private static string? Verify(LoadedProject project, LockFile lockFile, MethodSpec spec)
    {
        var run = project.Run(lockFile.Render());
        var hint = ImplockGenerator.HintName(spec.Id);
        var tree = run.Compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath.EndsWith(hint, StringComparison.Ordinal));
        if (tree is null)
        {
            var why = CheckCommandMessages(run, spec);
            return "the generator did not emit the body. " + why;
        }

        var errors = run.Compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree == tree)
            .Select(d => d.ToString())
            .ToList();
        return errors.Count == 0 ? null : "the body does not compile: " + string.Join("; ", errors);
    }

    private static string CheckCommandMessages(GeneratorRun run, MethodSpec spec) =>
        string.Join("; ", run.Diagnostics
            .Where(d => d.Id.StartsWith("IMPL", StringComparison.Ordinal) && (d.Location.SourceTree?.FilePath == spec.FilePath || d.Id == "IMPL004"))
            .Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture)));
}
