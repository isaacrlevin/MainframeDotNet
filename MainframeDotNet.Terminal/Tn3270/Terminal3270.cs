using System.Runtime.InteropServices;
using System.Text;

namespace MainframeDotNet.Terminal.Tn3270;

/// <summary>Raised when the operator powers the terminal off (Ctrl+Q).</summary>
public sealed class PowerOffException : Exception;

/// <summary>
/// A software IBM 3278 Model 2 (24 x 80) display station. The application paints protected text
/// and defines unprotected fields, then calls <see cref="Read"/>; the operator edits fields locally
/// with no host interaction until an attention key (ENTER, CLEAR, PFnn) is pressed - exactly like
/// a block-mode terminal on a channel-attached controller.
/// </summary>
public sealed class Terminal3270 : IDisposable
{
    public const int Rows = 24;
    public const int Cols = 80;
    private const string Esc = "\u001b";

    private readonly Cell[] _cells = new Cell[Rows * Cols];
    private readonly List<Field> _fields = [];
    private int _cursor;
    private bool _alarm;
    private int _lastWidth, _lastHeight;
    private string? _lastFrame;

    public Terminal3270(Theme theme, string luName)
    {
        Theme = theme;
        LuName = luName;
        Erase();
    }

    public Theme Theme { get; set; }
    public string LuName { get; }
    public bool InsertMode { get; private set; }
    public KeyboardLock Lock { get; private set; } = KeyboardLock.System;

    /// <summary>The AID key that ended the last <see cref="Read"/>.</summary>
    public Aid LastAid { get; private set; }

    /// <summary>Cursor position when the last AID key was pressed (0-based), used for cursor-select logic.</summary>
    public (int Row, int Col) CursorPosition => (_cursor / Cols, _cursor % Cols);

    public IReadOnlyList<Field> Fields => _fields;

    /// <summary>Shows the last keystroke received in the OIA, for diagnosing terminal key mappings.</summary>
    public bool KeyTrace { get; set; }

    private string _lastKey = "";
    private bool _pfPrefix;

    /// <summary>Replaces the physical keyboard, e.g. for scripted demonstrations and tests.</summary>
    public Func<ConsoleKeyInfo>? KeySource { get; set; }

    /// <summary>The screen as plain text, the way a local-copy printer would produce it.</summary>
    public string Snapshot()
    {
        var sb = new StringBuilder();
        for (var r = 0; r < Rows; r++)
        {
            var line = new char[Cols];
            for (var c = 0; c < Cols; c++) line[c] = _cells[r * Cols + c].Ch;
            foreach (var f in _fields.Where(f => f.Row == r))
                for (var i = 0; i < f.Length; i++)
                    line[f.Col + i] = f.Hidden || f.Data[i] == '\0' ? '_' : f.Data[i];
            sb.AppendLine(new string(line).TrimEnd());
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- session control

    public void Open()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.TreatControlCAsInput = true;
        EnableVirtualTerminal();
        TrySizeWindow();
        // Alternate screen buffer, phosphor cursor colour, blinking block cursor.
        Write($"{Esc}[?1049h{Esc}]12;{Theme.Cursor}\a{Esc}[1 q{Esc}[?7l");
        _lastWidth = 0;
    }

    public void Dispose()
    {
        Write($"{Esc}[0m{Esc}]112\a{Esc}[0 q{Esc}[?7h{Esc}[?25h{Esc}[?1049l");
        Console.TreatControlCAsInput = false;
    }

    // ---------------------------------------------------------------- host-side (application) API

    /// <summary>ERASE/WRITE: clears the buffer and all field definitions.</summary>
    public void Erase()
    {
        Array.Fill(_cells, new Cell { Ch = ' ', Color = Color.Default });
        _fields.Clear();
        _cursor = 0;
    }

    /// <summary>Paints protected text at the given position, clipped at the end of the row.</summary>
    public void Put(int row, int col, string text, Color color = Color.Green, Style style = Style.Normal)
    {
        if (row is < 0 or >= Rows) return;
        for (var i = 0; i < text.Length && col + i < Cols; i++)
            if (col + i >= 0) _cells[row * Cols + col + i] = new Cell { Ch = text[i], Color = color, Style = style };
    }

    public void Center(int row, string text, Color color = Color.Green, Style style = Style.Normal) =>
        Put(row, Math.Max(0, (Cols - text.Length) / 2), text, color, style);

    /// <summary>Defines an unprotected input field. The column is the first data position.</summary>
    public Field Input(int row, int col, int length, string? value = null, FieldFlags flags = FieldFlags.None, Color color = Color.Green)
    {
        var field = new Field(length, value) { Row = row, Col = col, Length = length, Flags = flags, Color = color };
        // Blank the area the field occupies so stale protected text never shows through.
        for (var i = field.Start; i < field.End; i++) _cells[i] = new Cell { Ch = ' ', Color = color };
        _fields.Add(field);
        _fields.Sort((a, b) => a.Start.CompareTo(b.Start));
        if (_fields[0] == field && _cursor == 0) _cursor = field.Start;
        return field;
    }

    public void SetCursor(Field field) => _cursor = field.Start;
    public void SetCursor(int row, int col) => _cursor = row * Cols + col;

    /// <summary>Sounds the terminal alarm on the next write.</summary>
    public void Alarm() => _alarm = true;

    /// <summary>Shows "X SYSTEM" in the OIA while the host is busy.</summary>
    public void Busy()
    {
        Lock = KeyboardLock.System;
        Render();
    }

    /// <summary>Displays the buffer without unlocking the keyboard (used for unsolicited output).</summary>
    public void Show() => Render();

    // ---------------------------------------------------------------- operator-side (keyboard)

    /// <summary>Unlocks the keyboard and processes keystrokes locally until an AID key is pressed.</summary>
    public Aid Read()
    {
        Lock = KeyboardLock.None;
        _pfPrefix = false;
        foreach (var f in _fields) f.Modified = false;
        Render();
        while (true)
        {
            var key = NextKey();
            var aid = HandleKey(key);
            if (aid is { } a)
            {
                LastAid = a;
                Lock = KeyboardLock.System;
                Render();
                // Channel and controller latency: long enough to see "X SYSTEM" flash in the OIA.
                Thread.Sleep(Random.Shared.Next(60, 180));
                return a;
            }
            Render();
        }
    }

    /// <summary>Waits up to the given time for any keystroke; returns true if one was pressed.</summary>
    public bool WaitForKey(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control)) throw new PowerOffException();
                return true;
            }
            if (WindowChanged()) Render();
            Thread.Sleep(15);
        }
        return false;
    }

    private ConsoleKeyInfo NextKey()
    {
        if (KeySource is not null) return KeySource();
        while (!Console.KeyAvailable)
        {
            if (WindowChanged()) Render();
            Thread.Sleep(15);
        }
        return Console.ReadKey(true);
    }

    private Aid? HandleKey(ConsoleKeyInfo k)
    {
        var ctrl = k.Modifiers.HasFlag(ConsoleModifiers.Control);
        var alt = k.Modifiers.HasFlag(ConsoleModifiers.Alt);
        var shift = k.Modifiers.HasFlag(ConsoleModifiers.Shift);

        if (ctrl && k.Key == ConsoleKey.Q) throw new PowerOffException();

        if (KeyTrace)
        {
            var mods = k.Modifiers == 0 ? "" : $" {k.Modifiers.ToString().Replace(", ", "+").ToUpperInvariant()}";
            _lastKey = $"KEY {k.Key.ToString().ToUpperInvariant()}{mods} X'{(int)k.KeyChar:X2}'";
        }

        if (k.Key is >= ConsoleKey.F1 and <= ConsoleKey.F12)
            return Aid.PF1 + (k.Key - ConsoleKey.F1) + (shift ? 12 : 0);

        // Browser-hosted terminals swallow F1, F3, F5 and F11, so Alt+1..0, Alt+- and Alt+= double as PF1-PF12.
        if (alt && !ctrl && BrowserSafePfKey(k) is { } pf) return pf;

        // Some browser terminals pass neither F-keys nor Alt, so Esc followed by 1..0, - or = is a two-stroke PF key.
        if (_pfPrefix)
        {
            _pfPrefix = false;
            if (!ctrl && !alt && BrowserSafePfKey(k) is { } prefixed) return prefixed;
            if (k.Key == ConsoleKey.Escape) return null; // a second Esc cancels
        }

        switch (k.Key)
        {
            case ConsoleKey.Enter: return Aid.Enter;
            case ConsoleKey.PageUp: return Aid.PF7;
            case ConsoleKey.PageDown: return Aid.PF8;
            case ConsoleKey.Pause: return Aid.Clear;
            case ConsoleKey.L when ctrl: return Aid.Clear;
            case ConsoleKey.Escape:
                // RESET. On a keyboard that isn't inhibited there is nothing to reset, so it arms the PF prefix instead.
                _pfPrefix = Lock == KeyboardLock.None;
                Lock = KeyboardLock.None;
                InsertMode = false;
                return null;
        }

        var printable = k.KeyChar >= ' ' && !char.IsControl(k.KeyChar) && (!ctrl || alt);

        // An inhibited keyboard rejects data keys until RESET; cursor and edit keys implicitly reset it.
        if (Lock != KeyboardLock.None)
        {
            if (printable)
            {
                _alarm = true;
                return null;
            }
            Lock = KeyboardLock.None;
        }

        var field = FieldAt(_cursor);
        switch (k.Key)
        {
            case ConsoleKey.Tab when shift: BackTab(); return null;
            case ConsoleKey.Tab: Tab(); return null;
            case ConsoleKey.LeftArrow: Move(-1); return null;
            case ConsoleKey.RightArrow: Move(1); return null;
            case ConsoleKey.UpArrow: Move(-Cols); return null;
            case ConsoleKey.DownArrow: Move(Cols); return null;
            case ConsoleKey.Home:
                if (_fields.Count > 0) _cursor = _fields[0].Start;
                return null;
            case ConsoleKey.Insert:
                InsertMode = !InsertMode;
                return null;
            case ConsoleKey.End when ctrl:
            case ConsoleKey.K when ctrl:
                if (field is null) { Lock = KeyboardLock.Protected; _alarm = true; }
                else EraseEof(field);
                return null;
            case ConsoleKey.End:
                if (field is not null)
                {
                    var last = Array.FindLastIndex(field.Data, c => c is not '\0' and not ' ');
                    _cursor = field.Start + Math.Min(last + 1, field.Length - 1);
                }
                return null;
            case ConsoleKey.Backspace:
                if (field is null) { Move(-1); return null; }
                if (_cursor > field.Start)
                {
                    _cursor--;
                    DeleteAt(field, _cursor - field.Start);
                }
                return null;
            case ConsoleKey.Delete:
                if (field is null) { Lock = KeyboardLock.Protected; _alarm = true; }
                else DeleteAt(field, _cursor - field.Start);
                return null;
        }

        if (printable) Type(field, k.KeyChar);
        return null;
    }

    private static Aid? BrowserSafePfKey(ConsoleKeyInfo k)
    {
        // A PTY delivers Alt+key as ESC followed by the character, so match on the character as well as the key.
        var digit = k.KeyChar is >= '0' and <= '9' ? k.KeyChar - '0'
            : k.Key is >= ConsoleKey.D0 and <= ConsoleKey.D9 ? k.Key - ConsoleKey.D0
            : -1;
        if (digit >= 0) return Aid.PF1 + (digit == 0 ? 9 : digit - 1);
        if (k.KeyChar == '-' || k.Key == ConsoleKey.OemMinus) return Aid.PF11;
        if (k.KeyChar == '=' || k.Key == ConsoleKey.OemPlus) return Aid.PF12;
        return null;
    }

    private void Type(Field? field, char ch)
    {
        if (field is null)
        {
            Lock = KeyboardLock.Protected;
            _alarm = true;
            return;
        }
        if (field.Numeric && !(char.IsDigit(ch) || ch is '.' or ',' or '-' or '+' or '/' or ' '))
        {
            Lock = KeyboardLock.Numeric;
            _alarm = true;
            return;
        }
        if (!field.Flags.HasFlag(FieldFlags.MixedCase)) ch = char.ToUpperInvariant(ch);

        var offset = _cursor - field.Start;
        if (InsertMode)
        {
            if (field.Data[^1] is not '\0' and not ' ')
            {
                Lock = KeyboardLock.Overflow;
                _alarm = true;
                return;
            }
            Array.Copy(field.Data, offset, field.Data, offset + 1, field.Length - offset - 1);
        }
        // Typing past a null leaves earlier nulls as blanks, as the terminal would transmit them.
        for (var i = 0; i < offset; i++) if (field.Data[i] == '\0') field.Data[i] = ' ';
        field.Data[offset] = ch;
        field.Modified = true;

        if (offset + 1 < field.Length) _cursor++;
        else Tab(); // auto-skip to the next unprotected field
    }

    private static void DeleteAt(Field field, int offset)
    {
        Array.Copy(field.Data, offset + 1, field.Data, offset, field.Length - offset - 1);
        field.Data[^1] = '\0';
        field.Modified = true;
    }

    private void EraseEof(Field field)
    {
        for (var i = _cursor - field.Start; i < field.Length; i++) field.Data[i] = '\0';
        field.Modified = true;
    }

    private void Tab()
    {
        if (_fields.Count == 0) { _cursor = 0; return; }
        var next = _fields.FirstOrDefault(f => f.Start > _cursor) ?? _fields[0];
        _cursor = next.Start;
    }

    private void BackTab()
    {
        if (_fields.Count == 0) { _cursor = 0; return; }
        var current = FieldAt(_cursor);
        if (current is not null && _cursor > current.Start) { _cursor = current.Start; return; }
        var prev = _fields.LastOrDefault(f => f.Start < _cursor && f != current) ?? _fields[^1];
        _cursor = prev.Start;
    }

    private void Move(int delta) => _cursor = ((_cursor + delta) % _cells.Length + _cells.Length) % _cells.Length;

    private Field? FieldAt(int pos) => _fields.FirstOrDefault(f => pos >= f.Start && pos < f.End);

    // ---------------------------------------------------------------- rendering

    private bool WindowChanged()
    {
        try { return Console.WindowWidth != _lastWidth || Console.WindowHeight != _lastHeight; }
        catch (IOException) { return false; }
    }

    private void Render()
    {
        int width, height;
        try { (width, height) = (Console.WindowWidth, Console.WindowHeight); }
        catch (IOException) { (width, height) = (Cols, Rows + 2); }

        var resized = width != _lastWidth || height != _lastHeight;
        _lastWidth = width;
        _lastHeight = height;

        var showRule = height >= Rows + 2;
        var totalRows = Rows + (showRule ? 2 : 1);
        var top = Math.Max(0, (height - totalRows) / 2) + 1;
        var left = Math.Max(0, (width - Cols) / 2) + 1;

        var sb = new StringBuilder(16 * 1024);
        var bg = $"{Esc}[48;2;{Theme.Background}m";
        sb.Append($"{Esc}[?25l{Esc}[0m{bg}");
        if (resized) sb.Append($"{Esc}[2J");

        if (width >= Cols + 4 && height >= totalRows + 2)
            DrawBezel(sb, top, left, totalRows);

        // The 1920-position display buffer.
        var buffer = new Cell[_cells.Length];
        Array.Copy(_cells, buffer, _cells.Length);
        foreach (var f in _fields)
            for (var i = 0; i < f.Length; i++)
            {
                var ch = f.Data[i];
                buffer[f.Start + i] = new Cell
                {
                    Ch = f.Hidden || ch == '\0' ? ' ' : ch,
                    Color = f.Color,
                    Style = Style.Underline | (f.Flags.HasFlag(FieldFlags.Bright) ? Style.Bright : Style.Normal),
                };
            }

        for (var r = 0; r < Rows; r++)
        {
            sb.Append($"{Esc}[{top + r};{left}H");
            string? currentSgr = null;
            for (var c = 0; c < Cols; c++)
            {
                var cell = buffer[r * Cols + c];
                var sgr = Sgr(cell.Color, cell.Style);
                if (sgr != currentSgr)
                {
                    sb.Append(sgr);
                    currentSgr = sgr;
                }
                sb.Append(cell.Ch);
            }
        }

        var oiaRow = top + Rows;
        if (showRule)
        {
            sb.Append($"{Esc}[{oiaRow};{left}H{Esc}[0m{bg}{Esc}[38;2;{Theme.Foreground(Color.Blue, Style.Normal)}m");
            sb.Append(new string('─', Cols));
            oiaRow++;
        }
        sb.Append($"{Esc}[{oiaRow};{left}H");
        AppendOia(sb, bg);

        var (cr, cc) = (_cursor / Cols, _cursor % Cols);
        sb.Append($"{Esc}[0m{Esc}[{top + cr};{left + cc}H");
        sb.Append(InsertMode ? $"{Esc}[3 q" : $"{Esc}[1 q");
        sb.Append($"{Esc}[?25h");
        if (_alarm)
        {
            sb.Append('\a');
            _alarm = false;
        }

        var frame = sb.ToString();
        if (frame != _lastFrame || resized)
        {
            Write(frame);
            _lastFrame = frame;
        }
    }

    private void DrawBezel(StringBuilder sb, int top, int left, int totalRows)
    {
        var color = $"{Esc}[0m{Esc}[38;2;{Theme.Bezel}m";
        sb.Append(color);
        sb.Append($"{Esc}[{top - 1};{left - 2}H╭{new string('─', Cols + 2)}╮");
        for (var r = 0; r < totalRows; r++)
        {
            sb.Append($"{Esc}[{top + r};{left - 2}H│");
            sb.Append($"{Esc}[{top + r};{left + Cols + 1}H│");
        }
        const string badge = " 3278-2 ";
        sb.Append($"{Esc}[{top + totalRows};{left - 2}H╰{new string('─', Cols + 2 - badge.Length - 2)}{badge}──╯");
    }

    private void AppendOia(StringBuilder sb, string bg)
    {
        var oia = new Cell[Cols];
        var normal = Color.Blue;
        for (var i = 0; i < Cols; i++) oia[i] = new Cell { Ch = ' ', Color = normal };

        void At(int col, string text, Style style = Style.Normal, Color color = Color.Blue)
        {
            for (var i = 0; i < text.Length && col + i < Cols; i++)
                oia[col + i] = new Cell { Ch = text[i], Color = color, Style = style };
        }

        At(0, "4", Style.Reverse);   // ready / online to controller
        At(1, "A", Style.Reverse);   // connected to the host application
        At(3, " ", Style.Normal);
        At(4, LuName[..Math.Min(8, LuName.Length)]);

        var lockText = Lock switch
        {
            KeyboardLock.System => "X SYSTEM",
            KeyboardLock.Protected => "X -f    ",   // "go elsewhere": cursor in protected area
            KeyboardLock.Numeric => "X NUM   ",
            KeyboardLock.Overflow => "X >     ",   // more than the field will hold
            _ => "",
        };
        if (lockText.Length > 0) At(14, lockText, Style.Bright, Lock == KeyboardLock.System ? Color.White : Color.Red);
        if (KeyTrace) At(24, _lastKey.Length > 26 ? _lastKey[..26] : _lastKey, Style.Normal, Color.Yellow);
        if (_pfPrefix) At(46, "PF", Style.Reverse, Color.White);
        if (InsertMode) At(51, "^", Style.Bright, Color.White);
        At(57, Theme.Name);
        var (r, c) = (_cursor / Cols + 1, _cursor % Cols + 1);
        At(Cols - 7, $"{r:000}/{c:000}");

        string? current = null;
        foreach (var cell in oia)
        {
            var sgr = Sgr(cell.Color, cell.Style);
            if (sgr != current) { sb.Append(sgr); current = sgr; }
            sb.Append(cell.Ch);
        }
        sb.Append($"{Esc}[0m{bg}");
    }

    private string Sgr(Color color, Style style)
    {
        var fg = Theme.Foreground(color, style);
        var sgr = new StringBuilder($"{Esc}[0;38;2;{fg};48;2;{Theme.Background}");
        if (style.HasFlag(Style.Bright)) sgr.Append(";1");
        if (style.HasFlag(Style.Underline)) sgr.Append(";4");
        if (style.HasFlag(Style.Reverse)) sgr.Append(";7");
        return sgr.Append('m').ToString();
    }

    private static void Write(string s)
    {
        Console.Out.Write(s);
        Console.Out.Flush();
    }

    // ---------------------------------------------------------------- platform plumbing

    private static void TrySizeWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (Console.WindowWidth < Cols + 4 || Console.WindowHeight < Rows + 4)
            {
                Console.SetWindowSize(Math.Max(Console.WindowWidth, Cols + 4), Math.Max(Console.WindowHeight, Rows + 4));
            }
        }
        catch
        {
            // Windows Terminal does not allow applications to resize the window.
        }
    }

    private static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows()) return;
        const int stdOutputHandle = -11;
        const uint enableVirtualTerminalProcessing = 0x0004;
        var handle = GetStdHandle(stdOutputHandle);
        if (GetConsoleMode(handle, out var mode))
            SetConsoleMode(handle, mode | enableVirtualTerminalProcessing);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
