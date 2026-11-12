namespace Implock.Sample;

public static partial class Text
{
    /// <summary>Reverses the characters of the text.</summary>
    [Implock.Implement("reverses: Reverse(\"abc\") == \"cba\"", "empty: Reverse(\"\") == \"\"")]
    public static partial string Reverse(string text);

    /// <summary>Counts the words in the text. Words are separated by one or more spaces.</summary>
    [Implock.Implement("counts: WordCount(\"a  b c\") == 3", "empty: WordCount(\"\") == 0")]
    public static partial int WordCount(string text);
}
