# implock

C# partial methods marked `[Implement]` are specs; a model writes each body once into a committed `implement.lock`, and a Roslyn incremental generator emits it at build time. The generator never calls a model. Layout: `src/Implock.Generator` (netstandard2.0 analyzer), `src/Implock.Cli` (dotnet tool `implock`), `tests/`, `samples/`.

## Engineering contract

Commands that prove a change (CI runs the same, in this order):

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet pack src/Implock.Cli -c Release --no-build -o artifacts
```

The sample is part of the proof: `dotnet build` of `samples/Implock.Sample` must be warning-free and `implock check samples/Implock.Sample` must exit 0.

Public surface (a change here is breaking and needs a major version once a version ships):

- the attribute `Implock.ImplementAttribute` and its arguments;
- diagnostic ids IMPL001 to IMPL004 and their meaning (`AnalyzerReleases.*.md` tracks them);
- the `implement.lock` format (`implock 1` header) and the spec hash inputs: a change to either makes every existing lock stale;
- CLI commands, flags and exit codes (0 ok, 1 findings, 2 usage or failure).

Versioning: MinVer from git tags. No tag, no NuGet publish; publishing is the owner's call.

Hard rules:

- No API client, no HTTP, no network code in any project. The model is only the command given by `--model`, `IMPLOCK_MODEL` or the default `claude -p`.
- Tests and samples are synthetic and self-contained; CI never calls a real model (a scripted fake command stands in).
- Lock output is deterministic: ordinal sort, LF line endings, no timestamps.
- The spec hash must be identical whether or not doc comments are parsed (MSBuild without `/doc` reads `///` as a plain comment).
- Public repo: no hostnames, IPs, key names, home paths, employer or private repo names, and no time estimates.

## Kept on purpose

- The lock is plain text, not JSON: one body line per line, so a review diff reads like code (ADR 0001).
- `generate` verifies a body by running the generator and counting compile errors only; it does not run the spec's tests.
- `report` is bounded differential fuzzing, not proof; its limits are stated in the README.
- The sample lock bodies came from a scripted stand-in command, not a real model.
