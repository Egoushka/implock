using System;
using System.Text;

namespace ReportSample;

/// <summary>The hand-written reference: it rejects empty and whitespace-only titles.</summary>
public static class Slugs
{
    public static string Slug(string title, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A title needs at least one visible character.", nameof(title));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var sb = new StringBuilder();
        foreach (var c in title)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().TrimEnd('-');
        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
    }
}
