namespace MainframeDotNet.Terminal.Tn3270;

/// <summary>Attention identifiers - the keys that send the screen to the host.</summary>
public enum Aid
{
    Enter, Clear, PA1, PA2,
    PF1, PF2, PF3, PF4, PF5, PF6, PF7, PF8, PF9, PF10, PF11, PF12,
    PF13, PF14, PF15, PF16, PF17, PF18, PF19, PF20, PF21, PF22, PF23, PF24,
}

/// <summary>The seven 3279 base colours. On a monochrome 3277/3278 they collapse to normal or intensified.</summary>
public enum Color { Default, Blue, Red, Pink, Green, Turquoise, Yellow, White }

[Flags]
public enum Style { Normal = 0, Bright = 1, Reverse = 2, Underline = 4 }

[Flags]
public enum FieldFlags { None = 0, Numeric = 1, Hidden = 2, Bright = 4, MixedCase = 8 }

/// <summary>Operator Information Area keyboard-inhibit states.</summary>
public enum KeyboardLock { None, System, Protected, Numeric, Overflow }

public struct Cell
{
    public char Ch;
    public Color Color;
    public Style Style;
}

/// <summary>An unprotected (input) field. Protected text is just painted into the buffer.</summary>
public sealed class Field
{
    public required int Row { get; init; }
    public required int Col { get; init; }
    public required int Length { get; init; }
    public FieldFlags Flags { get; init; }
    public Color Color { get; init; } = Color.Green;
    public bool Modified { get; set; }
    public char[] Data { get; }

    public Field(int length, string? value)
    {
        Data = new char[length];
        Set(value);
    }

    public int Start => Row * Terminal3270.Cols + Col;
    public int End => Start + Length; // exclusive

    public bool Numeric => Flags.HasFlag(FieldFlags.Numeric);
    public bool Hidden => Flags.HasFlag(FieldFlags.Hidden);

    /// <summary>Field contents with nulls turned to blanks and trailing blanks removed, as a COBOL program would see them.</summary>
    public string Value => new string(Data).Replace('\0', ' ').Trim();

    public void Set(string? value)
    {
        Array.Fill(Data, '\0');
        if (value is null) return;
        if (!Flags.HasFlag(FieldFlags.MixedCase)) value = value.ToUpperInvariant();
        for (var i = 0; i < Math.Min(value.Length, Data.Length); i++) Data[i] = value[i];
    }
}
