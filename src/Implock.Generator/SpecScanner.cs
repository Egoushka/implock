using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implock;

internal static class SpecScanner
{
    private static readonly SymbolDisplayFormat IdFormat = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat HashFormat =
        IdFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ImmutableArray<MethodSpec> Scan(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<MethodSpec>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            SemanticModel? model = null;
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!IsCandidate(method))
                {
                    continue;
                }

                model ??= compilation.GetSemanticModel(tree);
                var spec = TryCreate(method, model, CancellationToken.None);
                if (spec is not null)
                {
                    builder.Add(spec);
                }
            }
        }

        builder.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return builder.ToImmutable();
    }

    public static bool IsCandidate(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.PartialKeyword)
        && method.Body is null
        && method.ExpressionBody is null
        && method.AttributeLists.SelectMany(l => l.Attributes).Any(IsImplementName);

    public static MethodSpec? TryCreate(MethodDeclarationSyntax method, SemanticModel model, CancellationToken cancellationToken)
    {
        var attribute = FindAttribute(method, model);
        if (attribute is null || model.GetDeclaredSymbol(method, cancellationToken) is not IMethodSymbol symbol)
        {
            return null;
        }

        var tests = ImmutableArray.CreateBuilder<string>();
        foreach (var argument in attribute.ArgumentList?.Arguments ?? default)
        {
            var constant = model.GetConstantValue(argument.Expression, cancellationToken);
            tests.Add(constant.HasValue && constant.Value is string text ? text : argument.Expression.ToString());
        }

        var doc = ReadDoc(method);
        var id = BuildId(symbol);
        var hash = ComputeHash(method, symbol, id, doc, tests);
        var location = method.Identifier.GetLocation();

        var spec = new MethodSpec(
            id,
            hash,
            string.Join("\n", doc),
            tests.ToImmutable(),
            BuildDeclaration(method),
            symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty,
            BuildTypeHeaders(symbol),
            symbol.ContainingType.ToDisplayString(HashFormat),
            (model.GetNullableContext(method.SpanStart) & NullableContext.AnnotationsEnabled) != 0,
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span,
            CollectUsings(method))
        {
            UnresolvedType = FindUnresolved(symbol),
        };
        return spec;
    }

    private static string? FindUnresolved(IMethodSymbol symbol)
    {
        var types = new List<ITypeSymbol> { symbol.ReturnType };
        types.AddRange(symbol.Parameters.Select(p => p.Type));
        types.AddRange(symbol.TypeParameters.SelectMany(t => t.ConstraintTypes));
        return types.Select(FindError).FirstOrDefault(e => e is not null);
    }

    private static string? FindError(ITypeSymbol type)
    {
        switch (type)
        {
            case IErrorTypeSymbol:
                return type.ToDisplayString(HashFormat);
            case IArrayTypeSymbol array:
                return FindError(array.ElementType);
            case IPointerTypeSymbol pointer:
                return FindError(pointer.PointedAtType);
            case INamedTypeSymbol named:
                return named.TypeArguments.Select(FindError).FirstOrDefault(e => e is not null);
            default:
                return null;
        }
    }

    private static ImmutableArray<string> CollectUsings(MethodDeclarationSyntax method)
    {
        var usings = ImmutableArray.CreateBuilder<string>();
        foreach (var node in method.Ancestors().Reverse())
        {
            var list = node switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax ns => ns.Usings,
                _ => default,
            };
            foreach (var directive in list)
            {
                var text = directive.ToString().Trim();
                if (!usings.Contains(text))
                {
                    usings.Add(text);
                }
            }
        }

        return usings.ToImmutable();
    }

    private static bool IsImplementName(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => string.Empty,
        };
        return name is "Implement" or "ImplementAttribute";
    }

    private static AttributeSyntax? FindAttribute(MethodDeclarationSyntax method, SemanticModel model)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(l => l.Attributes))
        {
            if (!IsImplementName(attribute))
            {
                continue;
            }

            var owner = model.GetSymbolInfo(attribute).Symbol?.ContainingType;
            if (owner is null || string.Equals(owner.ToDisplayString(), "Implock.ImplementAttribute", StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    private static List<string> ReadDoc(MethodDeclarationSyntax method)
    {
        var lines = new List<string>();
        foreach (var trivia in method.GetLeadingTrivia())
        {
            if (!IsDocComment(trivia))
            {
                continue;
            }

            foreach (var raw in trivia.ToFullString().Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("///", StringComparison.Ordinal))
                {
                    line = line.Substring(3);
                }
                else if (line.StartsWith("/**", StringComparison.Ordinal))
                {
                    line = line.Substring(3);
                }
                else if (line.StartsWith("*", StringComparison.Ordinal) && !line.StartsWith("*/", StringComparison.Ordinal))
                {
                    line = line.Substring(1);
                }

                if (line.EndsWith("*/", StringComparison.Ordinal))
                {
                    line = line.Substring(0, line.Length - 2);
                }

                line = Collapse(line);
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        return lines;
    }

    // Without a documentation file the compiler parses "///" as a plain comment, so both shapes count.
    private static bool IsDocComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
        || (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.ToString().StartsWith("///", StringComparison.Ordinal))
        || (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) && trivia.ToString().StartsWith("/**", StringComparison.Ordinal));

    private static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string BuildId(IMethodSymbol symbol)
    {
        var sb = new StringBuilder();
        if (symbol.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            sb.Append(ns.ToDisplayString()).Append('.');
        }

        var chain = new Stack<INamedTypeSymbol>();
        for (var t = symbol.ContainingType; t is not null; t = t.ContainingType)
        {
            chain.Push(t);
        }

        foreach (var type in chain)
        {
            sb.Append(NameWithTypeParameters(type.Name, type.TypeParameters)).Append('.');
        }

        sb.Append(NameWithTypeParameters(symbol.Name, symbol.TypeParameters)).Append('(');
        sb.Append(string.Join(", ", symbol.Parameters.Select(p => RefPrefix(p.RefKind) + p.Type.ToDisplayString(IdFormat))));
        return sb.Append(')').ToString();
    }

    private static string NameWithTypeParameters(string name, ImmutableArray<ITypeParameterSymbol> parameters) =>
        parameters.IsDefaultOrEmpty ? name : name + "<" + string.Join(", ", parameters.Select(p => p.Name)) + ">";

    private static string RefPrefix(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        _ => string.Empty,
    };

    private static ImmutableArray<string> BuildTypeHeaders(IMethodSymbol symbol)
    {
        var headers = new Stack<string>();
        for (var t = symbol.ContainingType; t is not null; t = t.ContainingType)
        {
            var keyword = t.TypeKind switch
            {
                TypeKind.Interface => "interface",
                TypeKind.Struct => t.IsRecord ? "record struct" : "struct",
                _ => t.IsRecord ? "record" : "class",
            };
            headers.Push((t.IsRefLikeType ? "ref " : string.Empty) + "partial " + keyword + " " + NameWithTypeParameters(t.Name, t.TypeParameters));
        }

        return headers.ToImmutableArray();
    }

    private static string BuildDeclaration(MethodDeclarationSyntax method)
    {
        var parameters = method.ParameterList.Parameters.Select(p => p.WithDefault(null));
        var declaration = method
            .WithAttributeLists(default)
            .WithParameterList(method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(parameters)))
            .WithSemicolonToken(default)
            .WithoutTrivia();
        return declaration.NormalizeWhitespace().ToFullString();
    }

    private static string ComputeHash(MethodDeclarationSyntax method, IMethodSymbol symbol, string id, List<string> doc, IEnumerable<string> tests)
    {
        var sb = new StringBuilder();
        Field(sb, "id", id);
        Field(sb, "return", RefPrefix(symbol.RefKind) + symbol.ReturnType.ToDisplayString(HashFormat));
        Field(sb, "modifiers", string.Join(" ", method.Modifiers.Select(m => m.ValueText).OrderBy(m => m, StringComparer.Ordinal)));
        Field(sb, "constraints", Collapse(string.Concat(method.ConstraintClauses.Select(c => c.ToString() + " "))));
        foreach (var parameter in symbol.Parameters)
        {
            Field(sb, "param", RefPrefix(parameter.RefKind) + (parameter.IsParams ? "params " : string.Empty) + parameter.Type.ToDisplayString(HashFormat) + " " + parameter.Name);
        }

        foreach (var line in doc)
        {
            Field(sb, "doc", line);
        }

        foreach (var test in tests)
        {
            Field(sb, "test", test);
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static void Field(StringBuilder sb, string name, string value) =>
        sb.Append(name).Append('\t').Append(value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r")).Append('\n');
}
