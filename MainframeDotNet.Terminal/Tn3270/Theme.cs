namespace MainframeDotNet.Terminal.Tn3270;

/// <summary>Maps 3270 colours and highlighting onto 24-bit ANSI colours for a particular display tube.</summary>
public sealed class Theme
{
    public required string Name { get; init; }
    public required string Background { get; init; }
    public required string Bezel { get; init; }
    public required string Cursor { get; init; }
    public required Func<Color, Style, string> Foreground { get; init; }

    /// <summary>IBM 3278 with P39 green phosphor: everything is green, intensified fields glow brighter.</summary>
    public static Theme Green { get; } = Mono("GREEN", normal: "40;200;60", bright: "120;255;130", dim: "30;140;45", bg: "4;10;5");

    /// <summary>Amber phosphor, as found on many plug-compatible terminals.</summary>
    public static Theme Amber { get; } = Mono("AMBER", normal: "220;150;20", bright: "255;200;80", dim: "160;105;10", bg: "10;6;0");

    /// <summary>IBM 3279 colour display with the seven base colours.</summary>
    public static Theme Color3279 { get; } = new()
    {
        Name = "3279",
        Background = "0;0;0",
        Bezel = "70;70;70",
        Cursor = "#ffffff",
        Foreground = (color, style) => color switch
        {
            Color.Blue => "90;140;255",
            Color.Red => "255;80;80",
            Color.Pink => "255;130;255",
            Color.Turquoise => "64;224;224",
            Color.Yellow => "255;255;90",
            Color.White => "255;255;255",
            _ => style.HasFlag(Style.Bright) ? "120;255;120" : "60;220;60",
        },
    };

    public static Theme FromName(string? name) => name?.ToUpperInvariant() switch
    {
        "AMBER" => Amber,
        "COLOR" or "COLOUR" or "3279" => Color3279,
        _ => Green,
    };

    private static Theme Mono(string name, string normal, string bright, string dim, string bg) => new()
    {
        Name = name,
        Background = bg,
        Bezel = dim,
        Cursor = "#" + string.Concat(bright.Split(';').Select(c => int.Parse(c).ToString("x2"))),
        // A monochrome tube only knows "normal" and "intensified": the colours a 3279 would show
        // as white, yellow or red appear intensified, everything else at normal brightness.
        Foreground = (color, style) =>
            style.HasFlag(Style.Bright) || color is Color.White or Color.Yellow or Color.Red
                ? bright
                : color == Color.Blue ? dim : normal,
    };
}
