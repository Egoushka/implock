# ADR 0001: the lock is deterministic plain text

Status: accepted.

## Context

`implement.lock` holds the model-written bodies. People review it in pull requests, the generator reads it at build time inside the compiler, and CI must be able to regenerate it and compare bytes. The generator targets netstandard2.0 and should carry no dependency beyond Roslyn.

## Options

1. **JSON.** Familiar, but a body becomes one escaped string: a review diff shows a single changed line of `\n` and `\"`, and a JSON parser is an extra dependency inside the analyzer.
2. **One `.cs` file per method.** Reads well, but needs a place for the spec hash and the method id, and many small files clutter the repo.
3. **A line-based text file (chosen).** Header `implock 1`, then one block per method:

```
@@ method Namespace.Type.Method(int, string)
@@ spec <64 hex characters>
| body line
|
| body line
```

Entries are sorted by method id with ordinal comparison, lines end in LF, body lines are prefixed `| ` (an empty line is `|`), and there is one blank line between blocks. Nothing depends on the clock, the machine or the model.

## Decision

Option 3. A body diff reads as code. Parsing is a few dozen lines with no dependency. Determinism is a property of the writer: the same entries always render to the same bytes, which CI checks.

## Consequences

- The format is versioned by the header; a change to it, or to the spec hash inputs, is a breaking change.
- A malformed lock is the diagnostic IMPL004, not a crash.
- Lines starting with `@@` or `|` cannot be confused with body text, because every body line is prefixed.
