using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace Implock.Generator.Tests;

public class GeneratorTests
{
    private static string ValidLock() =>
        Harness.Lock((Harness.Id(Harness.Slug), Harness.Hash(Harness.Slug), Harness.SlugBody));

    [Fact]
    public void Spec_hash_is_the_same_whether_or_not_doc_comments_are_parsed()
    {
        static string HashWith(DocumentationMode mode)
        {
            var tree = CSharpSyntaxTree.ParseText(Harness.Slug, new CSharpParseOptions(LanguageVersion.Latest, mode));
            var compilation = Harness.Compile(string.Empty).AddSyntaxTrees(tree);
            return SpecScanner.Scan(compilation)[0].SpecHash;
        }

        Assert.Equal(HashWith(DocumentationMode.Parse), HashWith(DocumentationMode.None));
    }

    [Fact]
    public void Emits_the_body_from_the_lock_and_the_result_compiles()
    {
        var result = Harness.Run(Harness.Slug, ValidLock());

        Assert.Empty(result.Generator);
        Assert.Empty(result.Compiler);
        var source = Assert.Single(result.Sources.Where(s => s.Contains("Slugify", StringComparison.Ordinal)));
        Assert.Contains("title.ToLowerInvariant()", source, StringComparison.Ordinal);
        Assert.Contains("partial class Text", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_for_instance_methods_in_nested_generic_types()
    {
        const string src = """
            using Implock;

            namespace Demo
            {
                public partial class Outer
                {
                    public partial struct Inner<T>
                    {
                        [Implement]
                        public partial int Twice(int x);
                    }
                }
            }
            """;
        var lockText = Harness.Lock((Harness.Id(src), Harness.Hash(src), "return x * 2;"));

        var result = Harness.Run(src, lockText);

        Assert.Empty(result.Generator);
        Assert.Empty(result.Compiler);
    }

    [Fact]
    public void Missing_entry_reports_only_IMPL001()
    {
        var result = Harness.Run(Harness.Slug, Harness.Lock());

        Assert.Equal(["IMPL001"], result.Generator.Select(d => d.Id));
        Assert.Equal(DiagnosticSeverity.Error, result.Generator[0].Severity);
    }

    [Fact]
    public void No_lock_file_at_all_reports_IMPL001()
    {
        var result = Harness.Run(Harness.Slug, null);

        Assert.Equal(["IMPL001"], result.Generator.Select(d => d.Id));
    }

    [Fact]
    public void Changed_doc_comment_reports_only_IMPL002_and_emits_nothing()
    {
        var staleLock = ValidLock();
        var changed = Harness.Slug.Replace("joins words with dashes", "joins words with underscores", StringComparison.Ordinal);

        var result = Harness.Run(changed, staleLock);

        Assert.Equal(["IMPL002"], result.Generator.Select(d => d.Id));
        Assert.DoesNotContain(result.Sources, s => s.Contains("Slugify", StringComparison.Ordinal));
    }

    [Fact]
    public void Entry_without_a_method_reports_only_IMPL003()
    {
        var lockText = Harness.Lock(
            (Harness.Id(Harness.Slug), Harness.Hash(Harness.Slug), Harness.SlugBody),
            ("Demo.Gone.Missing(int)", "00", "return 1;"));

        var result = Harness.Run(Harness.Slug, lockText);

        Assert.Equal(["IMPL003"], result.Generator.Select(d => d.Id));
        Assert.Equal(DiagnosticSeverity.Warning, result.Generator[0].Severity);
        Assert.Empty(result.Compiler);
    }

    [Fact]
    public void Complete_lock_reports_nothing()
    {
        Assert.Empty(Harness.Run(Harness.Slug, ValidLock()).Generator);
    }

    [Theory]
    [InlineData("return 1; } public static void Evil() { ")]
    [InlineData("#pragma warning disable\nreturn 1;")]
    [InlineData("return 1;\n}\n}\nnamespace X { class Y {")]
    public void A_body_that_escapes_the_method_is_refused_with_IMPL004(string body)
    {
        var lockText = Harness.Lock((Harness.Id(Harness.Slug), Harness.Hash(Harness.Slug), body));

        var result = Harness.Run(Harness.Slug, lockText);

        Assert.Contains(result.Generator, d => d.Id == "IMPL004");
        Assert.DoesNotContain(result.Sources, s => s.Contains("Slugify", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_lock_reports_IMPL004()
    {
        var result = Harness.Run(Harness.Slug, "this is not a lock file\n");

        Assert.Contains(result.Generator, d => d.Id == "IMPL004");
    }

    [Fact]
    public void Hash_ignores_whitespace_in_doc_comments_and_changes_with_the_signature_or_tests()
    {
        var baseHash = Harness.Hash(Harness.Slug);
        var spaced = Harness.Slug.Replace("/// <summary>Lower-cases", "///    <summary>Lower-cases  ", StringComparison.Ordinal);
        var otherReturn = Harness.Slug.Replace("string Slugify(string title)", "string? Slugify(string title)", StringComparison.Ordinal);
        var otherTests = Harness.Slug.Replace("empty gives empty", "blank gives blank", StringComparison.Ordinal);
        var otherParam = Harness.Slug.Replace("string title", "string name", StringComparison.Ordinal);

        Assert.Equal(baseHash, Harness.Hash(spaced));
        Assert.NotEqual(baseHash, Harness.Hash(otherReturn));
        Assert.NotEqual(baseHash, Harness.Hash(otherTests));
        Assert.NotEqual(baseHash, Harness.Hash(otherParam));
    }

    [Fact]
    public void Method_id_is_the_qualified_signature_without_names_or_return_type()
    {
        Assert.Equal("Demo.Text.Slugify(string)", Harness.Id(Harness.Slug));
    }

    private const string Twice = """
        using Implock;

        namespace Demo;

        public static partial class Math2
        {
            /// <summary>Doubles x.</summary>
            [Implement("two: Twice(2) == 4")]
            public static partial int Twice(int x);
        }
        """;

    [Fact]
    public void The_spec_hash_matches_a_value_worked_out_by_hand_from_the_documented_fields()
    {
        Assert.Equal("Demo.Math2.Twice(int)", Harness.Id(Twice));
        Assert.Equal("c9bef845e017b43d9dafc8e773b6b2430e8f7e28ed13a2a9fa7d8c743722a641", Harness.Hash(Twice));
    }

    [Theory]
    [InlineData("public static partial int Twice(int x);", "public static partial long Twice(int x);")]
    [InlineData("public static partial int Twice(int x);", "public partial int Twice(int x);")]
    [InlineData("public static partial int Twice(int x);", "public static partial int Twice(int y);")]
    [InlineData("public static partial int Twice(int x);", "public static partial int Twice(in int x);")]
    [InlineData("public static partial int Twice(int x);", "public static partial int Twice(params int[] x);")]
    [InlineData("public static partial int Twice(int x);", "public static partial int Twice(int x, int z = 1);")]
    public void Signature_changes_change_the_spec_hash(string before, string after)
    {
        var original = Harness.Hash(Twice);
        var changed = Harness.Hash(Twice.Replace(before, after, StringComparison.Ordinal));

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void A_constraint_clause_changes_the_spec_hash()
    {
        const string generic = """
            using Implock;

            namespace Demo;

            public static partial class G
            {
                /// <summary>Echo.</summary>
                [Implement]
                public static partial T Echo<T>(T x) CONSTRAINT;
            }
            """;

        Assert.NotEqual(
            Harness.Hash(generic.Replace("CONSTRAINT", "where T : class", StringComparison.Ordinal)),
            Harness.Hash(generic.Replace("CONSTRAINT", "where T : struct", StringComparison.Ordinal)));
    }
}
