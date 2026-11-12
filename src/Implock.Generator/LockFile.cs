using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Implock;

internal sealed class LockEntry
{
    public LockEntry(string specHash, string body)
    {
        SpecHash = specHash;
        Body = body;
    }

    public string SpecHash { get; }

    public string Body { get; }
}

/// <summary>
/// The committed implement.lock: a header, then one section per method, sorted by id.
/// Body lines carry a "| " prefix so blank lines and any C# text need no escaping.
/// </summary>
internal sealed class LockFile
{
    private static readonly char[] TokenBreaks = { '\n', '\r', ' ' };

    public const string FileName = "implement.lock";

    private const string Header = "implock 1";
    private const string MethodPrefix = "@@ method ";
    private const string SpecPrefix = "@@ spec ";

    private readonly SortedDictionary<string, LockEntry> _entries = new SortedDictionary<string, LockEntry>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, LockEntry> Entries => _entries;

    public void Set(string id, string specHash, string body)
    {
        if (string.IsNullOrEmpty(id) || id.IndexOf('\n') >= 0 || id.IndexOf('\r') >= 0)
        {
            throw new ArgumentException("A method id must be a single non-empty line.", nameof(id));
        }

        if (string.IsNullOrEmpty(specHash) || specHash.IndexOfAny(TokenBreaks) >= 0)
        {
            throw new ArgumentException("A spec hash must be a single token.", nameof(specHash));
        }

        _entries[id] = new LockEntry(specHash, NormalizeBody(body));
    }

    public bool Remove(string id) => _entries.Remove(id);

    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var pair in _entries)
        {
            sb.Append('\n').Append(MethodPrefix).Append(pair.Key).Append('\n');
            sb.Append(SpecPrefix).Append(pair.Value.SpecHash).Append('\n');
            if (pair.Value.Body.Length > 0)
            {
                foreach (var line in pair.Value.Body.Split('\n'))
                {
                    sb.Append(line.Length == 0 ? "|" : "| " + line).Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    public static LockFile? Parse(string text, out string? error)
    {
        var file = new LockFile();
        error = ParseInto(text, file);
        return error is null ? file : null;
    }

    private static string NormalizeBody(string body) =>
        body.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');

    private static string? ParseInto(string text, LockFile file)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var count = lines.Length;
        if (count > 0 && lines[count - 1].Length == 0)
        {
            count--;
        }

        if (count == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
        {
            return count > 0 && lines[0].StartsWith("implock ", StringComparison.Ordinal)
                ? "line 1: unsupported lock version, expected 'implock 1'"
                : "line 1: expected the header 'implock 1'";
        }

        string? id = null;
        string? hash = null;
        var body = new List<string>();

        for (var i = 1; i < count; i++)
        {
            var line = lines[i];
            var number = i + 1;

            if (line.StartsWith(MethodPrefix, StringComparison.Ordinal))
            {
                var flushed = Flush(file, ref id, ref hash, body, number);
                if (flushed is not null)
                {
                    return flushed;
                }

                id = line.Substring(MethodPrefix.Length);
                if (id.Length == 0)
                {
                    return string.Format(CultureInfo.InvariantCulture, "line {0}: empty method id", number);
                }
            }
            else if (line.StartsWith(SpecPrefix, StringComparison.Ordinal))
            {
                if (id is null || hash is not null || body.Count > 0)
                {
                    return string.Format(CultureInfo.InvariantCulture, "line {0}: unexpected spec line", number);
                }

                hash = line.Substring(SpecPrefix.Length);
                if (hash.Length == 0 || hash.IndexOf(' ') >= 0)
                {
                    return string.Format(CultureInfo.InvariantCulture, "line {0}: malformed spec hash", number);
                }
            }
            else if (string.Equals(line, "|", StringComparison.Ordinal) || line.StartsWith("| ", StringComparison.Ordinal))
            {
                if (hash is null)
                {
                    return string.Format(CultureInfo.InvariantCulture, "line {0}: body line before the spec line", number);
                }

                body.Add(line.Length > 2 ? line.Substring(2) : string.Empty);
            }
            else if (line.Length == 0)
            {
                var flushed = Flush(file, ref id, ref hash, body, number);
                if (flushed is not null)
                {
                    return flushed;
                }
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "line {0}: unrecognized line", number);
            }
        }

        return Flush(file, ref id, ref hash, body, count + 1);
    }

    private static string? Flush(LockFile file, ref string? id, ref string? hash, List<string> body, int number)
    {
        if (id is null)
        {
            return null;
        }

        if (hash is null)
        {
            return string.Format(CultureInfo.InvariantCulture, "line {0}: entry '{1}' has no spec line", number, id);
        }

        if (file._entries.ContainsKey(id))
        {
            return string.Format(CultureInfo.InvariantCulture, "line {0}: duplicate entry '{1}'", number, id);
        }

        file._entries[id] = new LockEntry(hash, string.Join("\n", body));
        id = null;
        hash = null;
        body.Clear();
        return null;
    }
}
