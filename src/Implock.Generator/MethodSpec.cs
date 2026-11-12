using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Implock;

/// <summary>Everything the generator and the CLI need to know about one [Implement] method.</summary>
internal sealed class MethodSpec : IEquatable<MethodSpec>
{
    public MethodSpec(
        string id,
        string specHash,
        string doc,
        ImmutableArray<string> tests,
        string declaration,
        string containingNamespace,
        ImmutableArray<string> typeHeaders,
        string containingType,
        bool nullableEnabled,
        string filePath,
        TextSpan span,
        LinePositionSpan lineSpan,
        ImmutableArray<string> usings)
    {
        Id = id;
        SpecHash = specHash;
        Doc = doc;
        Tests = tests;
        Declaration = declaration;
        Namespace = containingNamespace;
        TypeHeaders = typeHeaders;
        ContainingType = containingType;
        NullableEnabled = nullableEnabled;
        FilePath = filePath;
        Span = span;
        LineSpan = lineSpan;
        Usings = usings;
    }

    public string Id { get; }

    public string SpecHash { get; }

    /// <summary>Doc comment text, one normalized line per "\n".</summary>
    public string Doc { get; }

    public ImmutableArray<string> Tests { get; }

    /// <summary>The method header as it is emitted, without attributes, doc comment, defaults or semicolon.</summary>
    public string Declaration { get; }

    public string Namespace { get; }

    public ImmutableArray<string> TypeHeaders { get; }

    public string ContainingType { get; }

    public bool NullableEnabled { get; }

    public string FilePath { get; }

    public TextSpan Span { get; }

    public LinePositionSpan LineSpan { get; }

    /// <summary>The using directives of the file that declares the method, so a body can use the names the spec file sees.</summary>
    public ImmutableArray<string> Usings { get; }

    /// <summary>Display name of a type in the signature that did not resolve, or null. Such a spec cannot be locked from a loader that lacks the project's references.</summary>
    public string? UnresolvedType { get; set; }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public bool Equals(MethodSpec? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(SpecHash, other.SpecHash, StringComparison.Ordinal)
        && string.Equals(Declaration, other.Declaration, StringComparison.Ordinal)
        && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
        && TypeHeaders.SequenceEqual(other.TypeHeaders, StringComparer.Ordinal)
        && NullableEnabled == other.NullableEnabled
        && Usings.SequenceEqual(other.Usings, StringComparer.Ordinal)
        && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
        && Span == other.Span;

    public override bool Equals(object? obj) => Equals(obj as MethodSpec);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id) ^ StringComparer.Ordinal.GetHashCode(SpecHash);
}
