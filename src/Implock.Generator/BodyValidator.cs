using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implock;

internal static class BodyValidator
{
    /// <summary>Returns null when the text is exactly one well-formed method body, otherwise the reason it is not.</summary>
    public static string? Validate(string body)
    {
        var text = "{\n" + body + "\n}";
        var statement = SyntaxFactory.ParseStatement(text);

        if (statement is not BlockSyntax block)
        {
            return "the body is not a block";
        }

        var error = statement.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (error is not null)
        {
            return "the body does not parse: " + error.GetMessage(CultureInfo.InvariantCulture);
        }

        if (block.Span.End != text.Length || block.FullSpan.End != text.Length)
        {
            return "the body closes the method early or has text after it";
        }

        if (statement.DescendantTrivia().Any(t => t.IsDirective))
        {
            return "the body contains a preprocessor directive";
        }

        return null;
    }
}
