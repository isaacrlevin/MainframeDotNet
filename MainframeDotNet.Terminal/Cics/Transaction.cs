using System.Globalization;
using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

public sealed class Session(Terminal3270 terminal, ClaimsRepository repo)
{
    public Terminal3270 Terminal { get; } = terminal;
    public ClaimsRepository Repo { get; } = repo;
    public string ApplId { get; } = "CICSPROD";
    public string UserId { get; set; } = "";
    public DateTime SignedOnAt { get; set; }
}

/// <summary>
/// One CICS transaction / BMS map. Each <see cref="RunAsync"/> call paints a map, converses with the
/// operator and returns the next transaction to run (null to end the CLMS session).
/// </summary>
public abstract class Transaction(Session session)
{
    // Standard map layout rows.
    protected const int MessageRow = 22;
    protected const int KeysRow = 23;

    protected static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    protected Session Session { get; } = session;
    protected Terminal3270 T => Session.Terminal;
    protected ClaimsRepository Repo => Session.Repo;

    public abstract Task<Transaction?> RunAsync();

    protected Transaction Menu(string? message = null) => new MainMenu(Session, message);

    // ---------------------------------------------------------------- map painting helpers

    protected void Header(string tranId, string mapName, string title)
    {
        var now = DateTime.Now;
        T.Put(0, 1, tranId, Color.White);
        T.Center(0, "ACME MUTUAL INSURANCE COMPANY", Color.Yellow);
        T.Put(0, 71, now.ToString("MM/dd/yy", Us), Color.Turquoise);
        T.Put(1, 1, mapName, Color.Blue);
        T.Center(1, title, Color.White, Style.Bright);
        T.Put(1, 71, now.ToString("HH:mm:ss", Us), Color.Turquoise);
        T.Put(2, 0, new string('-', Terminal3270.Cols), Color.Blue);
    }

    protected void Rule(int row, string title)
    {
        var text = $" {title} ";
        var left = (Terminal3270.Cols - text.Length) / 2;
        T.Put(row, 0, new string('-', Terminal3270.Cols), Color.Blue);
        T.Put(row, left, text, Color.Turquoise);
    }

    protected void Label(int row, int col, string text) => T.Put(row, col, text, Color.Blue);

    protected void Data(int row, int col, string text, Color color = Color.Green) => T.Put(row, col, text, color);

    /// <summary>
    /// Writes a message to the message line. The severity is taken from the last character of the
    /// message id (I, W, E) in the IBM convention; errors sound the alarm.
    /// </summary>
    protected void Message(string? text)
    {
        T.Put(MessageRow, 0, new string(' ', Terminal3270.Cols));
        if (string.IsNullOrEmpty(text)) return;
        var id = text.Split(' ')[0];
        var color = id.EndsWith('E') ? Color.Red : id.EndsWith('W') ? Color.Yellow : Color.White;
        T.Put(MessageRow, 1, text.Length > 78 ? text[..78] : text, color, Style.Bright);
        if (id.EndsWith('E')) T.Alarm();
    }

    protected void Keys(string legend) => T.Put(KeysRow, 1, legend, Color.Turquoise);

    /// <summary>Shows the message, positions the cursor on the offending field and sounds the alarm.</summary>
    protected void Error(Field field, string message)
    {
        Message(message);
        T.SetCursor(field);
    }

    protected Aid Read() => T.Read();

    // ---------------------------------------------------------------- formatting and editing

    protected static string Money(decimal value) => value.ToString("#,##0.00", Us);
    protected static string Money(decimal value, int width) => Money(value).PadLeft(width);
    protected static string Date(DateOnly date) => date.ToString("MM/dd/yy", Us);
    protected static string Stamp(DateTime utc) => utc.ToLocalTime().ToString("MM/dd/yy HH:mm", Us);
    protected static string Fit(string s, int width) => s.Length > width ? s[..width] : s.PadRight(width);

    protected static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, ["MM/dd/yy", "MMddyy", "MM/dd/yyyy", "M/d/yy", "MM-dd-yy"], Us, DateTimeStyles.None, out date);

    protected static bool TryAmount(string text, out decimal amount)
    {
        if (text.Length == 0)
        {
            amount = 0;
            return true;
        }
        return decimal.TryParse(text, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, Us, out amount)
               && decimal.Round(amount, 2) == amount;
    }

    /// <summary>Accepts "1000001" as well as "C1000001".</summary>
    protected static string ClaimKey(string text) => text.Length > 0 && text.All(char.IsDigit) ? "C" + text : text;

    protected static string PolicyKey(string text) => text.Length > 0 && text.All(char.IsDigit) ? "P" + text : text;
}
