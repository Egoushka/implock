using System;
using System.Text;

namespace ReportSample;

/// <summary>The model-written body: it checks for null and empty but forgot whitespace-only titles (the planted looser case).</summary>
public static class Slugs
{
    public static string Slug(string title, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Length == 0)
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
