using System.Text;

namespace Implock.Cli;

internal static class ModelOutput
{
    /// <summary>Turns raw model stdout into a method body, or null when nothing usable is there.</summary>
    public static string? ToBody(string raw)
    {
        var text = raw.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal);
        var fenced = FirstFence(text);
        var lines = (fenced ?? text).Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static string? FirstFence(string text)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        if (start < 0)
        {
            return null;
        }

        var body = new StringBuilder();
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                return body.ToString();
            }

            body.Append(lines[i]).Append('\n');
        }

        return body.ToString();
    }
}
