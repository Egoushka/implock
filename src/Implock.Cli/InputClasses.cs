using System.Globalization;

namespace Implock.Cli;

/// <summary>One named slice of a parameter's input space, with a way to draw a value from it.</summary>
internal sealed record InputClass(string Name, Func<Random, object?> Draw);

internal static class InputClasses
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Punctuation = "!@#$%^&*()-_=+[]{};:,.<>/?~";
    private const string Unicode = "éßñøüçλжあ中😀";

    /// <summary>Returns the classes for a parameter type, or null when the type is not supported.</summary>
    public static IReadOnlyList<InputClass>? For(Type type)
    {
        if (type == typeof(string))
        {
            return Strings;
        }

        if (type == typeof(int))
        {
            return Integers(int.MinValue, int.MaxValue, v => (int)v);
        }

        if (type == typeof(long))
        {
            return Integers(long.MinValue, long.MaxValue, v => v);
        }

        if (type == typeof(double))
        {
            return Doubles;
        }

        if (type == typeof(bool))
        {
            return [new("false", _ => false), new("true", _ => true)];
        }

        if (type == typeof(char))
        {
            return Chars;
        }

        return null;
    }

    private static readonly InputClass[] Strings =
    [
        new("null", _ => null),
        new("empty", _ => string.Empty),
        new("whitespace", r => new string(' ', r.Next(1, 6))),
        new("word", r => Make(r, Letters, r.Next(1, 12))),
        new("punctuated", r => Make(r, Letters, r.Next(2, 6)) + Make(r, Punctuation, r.Next(1, 4)) + " " + Make(r, Letters, r.Next(1, 6))),
        new("unicode", r => Make(r, Unicode, r.Next(1, 8))),
        new("long", r => Make(r, Letters, r.Next(300, 600))),
        new("digits", r => Make(r, "0123456789", r.Next(1, 10))),
    ];

    private static readonly InputClass[] Doubles =
    [
        new("negative", r => -(r.NextDouble() * 1000) - 0.001),
        new("zero", _ => 0.0),
        new("small", r => r.NextDouble() * 10),
        new("large", r => 1e6 + (r.NextDouble() * 1e9)),
        new("NaN", _ => double.NaN),
        new("+inf", _ => double.PositiveInfinity),
    ];

    private static readonly InputClass[] Chars =
    [
        new("letter", r => Letters[r.Next(Letters.Length)]),
        new("digit", r => (char)('0' + r.Next(10))),
        new("space", _ => ' '),
        new("control", r => (char)r.Next(1, 32)),
    ];

    private static InputClass[] Integers(long min, long max, Func<long, object> convert) =>
    [
        new("min", _ => convert(min)),
        new("negative", r => convert(-r.NextInt64(1, 1_000_000))),
        new("zero", _ => convert(0)),
        new("small", r => convert(r.NextInt64(1, 21))),
        new("large", r => convert(r.NextInt64(21, 1_000_000_000))),
        new("max", _ => convert(max)),
    ];

    private static string Make(Random random, string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[random.Next(alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Renders a returned value so two runs can be compared as text.</summary>
    public static string Render(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
