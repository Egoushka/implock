using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implock.Cli;

/// <summary>Signature-only outline of the types a model needs to see to write one body. Never contains bodies.</summary>
internal static class TypeContext
{
    private static readonly SymbolDisplayFormat TypeHeaderFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
        kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat MemberFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeExtensionThis,
        kindOptions: SymbolDisplayKindOptions.IncludeMemberKeyword,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The outline for <paramref name="spec"/>, or an empty string when there is nothing to show.</summary>
    public static string Build(Compilation compilation, MethodSpec spec)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault(t => string.Equals(t.FilePath, spec.FilePath, StringComparison.Ordinal));
        if (tree is null)
        {
            return string.Empty;
        }

        var method = tree.GetRoot().FindNode(spec.Span).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null || compilation.GetSemanticModel(tree).GetDeclaredSymbol(method) is not IMethodSymbol symbol)
        {
            return string.Empty;
        }

        var outline = new StringBuilder();
        var containing = symbol.ContainingType;
        var siblings = Members(containing, publicAndInternalOnly: false)
            .Where(m => !SymbolEqualityComparer.Default.Equals(m, symbol))
            .ToList();
        if (siblings.Count > 0)
        {
            AppendType(outline, containing, siblings);
        }

        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { containing.OriginalDefinition };
        var found = new List<INamedTypeSymbol>();
        Collect(symbol.ReturnType, seen, found);
        foreach (var parameter in symbol.Parameters)
        {
            Collect(parameter.Type, seen, found);
        }

        var blocks = found.Select(t => (Type: t, Members: Members(t, publicAndInternalOnly: true))).ToList();

        // One level deeper: the project types the listed members name. Their own members are not followed.
        var deeper = new List<INamedTypeSymbol>();
        foreach (var member in siblings.Concat(blocks.SelectMany(b => b.Members)))
        {
            foreach (var used in SignatureTypes(member))
            {
                Collect(used, seen, deeper);
            }
        }

        foreach (var (type, members) in blocks.Concat(deeper.Select(t => (Type: t, Members: Members(t, publicAndInternalOnly: true)))))
        {
            AppendType(outline, type, members);
        }

        return outline.ToString();
    }

    private static IEnumerable<ITypeSymbol> SignatureTypes(ISymbol member) => member switch
    {
        IFieldSymbol field => [field.Type],
        IPropertySymbol property => property.Parameters.Select(p => p.Type).Prepend(property.Type),
        IEventSymbol @event => [@event.Type],
        IMethodSymbol method => method.Parameters.Select(p => p.Type).Prepend(method.ReturnType),
        _ => [],
    };

    private static void Collect(ITypeSymbol type, HashSet<INamedTypeSymbol> seen, List<INamedTypeSymbol> found)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                Collect(array.ElementType, seen, found);
                break;
            case IPointerTypeSymbol pointer:
                Collect(pointer.PointedAtType, seen, found);
                break;
            case INamedTypeSymbol named:
                var definition = named.OriginalDefinition;
                if (definition.SpecialType == SpecialType.None
                    && definition.Locations.Any(l => l.IsInSource)
                    && seen.Add(definition))
                {
                    found.Add(definition);
                }

                foreach (var argument in named.TypeArguments)
                {
                    Collect(argument, seen, found);
                }

                break;
        }
    }

    private static void AppendType(StringBuilder outline, INamedTypeSymbol type, List<ISymbol> members)
    {
        outline.Append(Header(type)).Append('\n');
        if (type.TypeKind == TypeKind.Enum)
        {
            foreach (var member in members.OfType<IFieldSymbol>())
            {
                outline.Append("  ").Append(member.Name).Append('\n');
            }

            return;
        }

        foreach (var member in members)
        {
            var line = member is INamedTypeSymbol nested
                ? Header(nested)
                : member.ToDisplayString(MemberFormat);
            outline.Append("  ").Append(line).Append('\n');
        }
    }

    private static string Header(INamedTypeSymbol type)
    {
        var text = new StringBuilder();
        text.Append(type.DeclaredAccessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Internal => "internal ",
            Accessibility.Private => "private ",
            Accessibility.Protected => "protected ",
            Accessibility.ProtectedOrInternal => "protected internal ",
            Accessibility.ProtectedAndInternal => "private protected ",
            _ => string.Empty,
        });
        if (type.TypeKind == TypeKind.Class)
        {
            text.Append(type.IsStatic ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : string.Empty);
        }
        else if (type.TypeKind == TypeKind.Struct)
        {
            text.Append(type.IsReadOnly ? "readonly " : string.Empty).Append(type.IsRefLikeType ? "ref " : string.Empty);
        }

        return text.Append(type.ToDisplayString(TypeHeaderFormat)).ToString();
    }

    private static List<ISymbol> Members(INamedTypeSymbol type, bool publicAndInternalOnly) =>
        type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && m.DeclaringSyntaxReferences.Length > 0 && IsListed(m))
            .Where(m => !publicAndInternalOnly || m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .OrderBy(m => m.DeclaringSyntaxReferences[0].SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(m => m.DeclaringSyntaxReferences[0].Span.Start)
            .ToList();

    private static bool IsListed(ISymbol member) => member switch
    {
        IFieldSymbol or IPropertySymbol or IEventSymbol or INamedTypeSymbol => true,
        IMethodSymbol method => method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
            or MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.Destructor,
        _ => false,
    };
}
