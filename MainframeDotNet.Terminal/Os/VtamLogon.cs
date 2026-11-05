using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Os;

/// <summary>The VTAM USS "message 10" logon screen every terminal shows when it is not in session.</summary>
public sealed class VtamLogon(Terminal3270 t)
{
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = [" ### ", "#   #", "#####", "#   #", "#   #"],
        ['C'] = [" ####", "#    ", "#    ", "#    ", " ####"],
        ['E'] = ["#####", "#    ", "#### ", "#    ", "#####"],
        ['L'] = ["#    ", "#    ", "#    ", "#    ", "#####"],
        ['M'] = ["#   #", "## ##", "# # #", "#   #", "#   #"],
        ['T'] = ["#####", "  #  ", "  #  ", "  #  ", "  #  "],
        ['U'] = ["#   #", "#   #", "#   #", "#   #", " ### "],
        [' '] = ["     ", "     ", "     ", "     ", "     "],
    };

    /// <summary>Returns true once the operator has asked for a session with CICS, false on LOGOFF.</summary>
    public bool Run(string? message = null)
    {
        while (true)
        {
            t.Erase();
            var now = DateTime.Now;
            t.Put(0, 1, "VTAM USSTAB ISTINCDT", Color.Blue);
            t.Put(0, 54, $"TERMINAL {t.LuName}", Color.Blue);
            t.Put(1, 54, $"DATE {now:MM/dd/yy} {now:HH:mm}", Color.Blue);

            Banner(3, "ACME MUTUAL");
            t.Center(9, "I N S U R A N C E      C O M P A N Y", Color.Yellow, Style.Bright);
            t.Center(10, "HARTFORD, CONNECTICUT", Color.Turquoise);
            t.Center(12, "CLAIMS INFORMATION MANAGEMENT SYSTEM  (CIMS)", Color.White);

            t.Put(14, 4, "*  THIS SYSTEM IS FOR THE USE OF AUTHORIZED ACME MUTUAL PERSONNEL ONLY.  *", Color.Red);
            t.Put(15, 4, "*  ALL ACTIVITY IS MONITORED AND RECORDED ON THE SYSTEM LOG (SMF).      *", Color.Red);

            t.Put(17, 4, "AVAILABLE APPLICATIONS:", Color.Turquoise);
            t.Put(17, 28, "CICSPROD - CLAIMS PROCESSING (CICS/VS)", Color.Green);
            t.Put(18, 28, "TSO      - TIME SHARING OPTION", Color.Green);

            t.Put(20, 4, "ENTER LOGON COMMAND ===>", Color.White);
            var command = t.Input(20, 29, 46, "LOGON APPLID(CICSPROD)");
            t.Put(23, 1, "ENTER=SEND  PF1-12=F1-F12, ALT+1..0 OR ESC THEN 1..0,-,=  ESC=RESET  CTRL+Q=OFF", Color.Blue);
            if (message is not null)
            {
                t.Put(22, 1, message, Color.Red, Style.Bright);
                t.Alarm();
            }
            t.SetCursor(command);

            var aid = t.Read();
            if (aid != Aid.Enter)
            {
                message = null;
                continue;
            }

            var text = command.Value.Replace(" ", "");
            switch (text)
            {
                case "LOGONAPPLID(CICSPROD)" or "LOGONAPPLID(CICS)" or "CICS" or "CICSPROD" or "LOGONCICS" or "LCICS":
                    return true;
                case "LOGOFF" or "OFF":
                    return false;
                case "TSO" or "LOGONAPPLID(TSO)":
                    message = "IST453I LOGON REJECTED - TSO IS NOT ACTIVE ON THIS SYSTEM";
                    break;
                default:
                    message = $"IST457I POSITIVE SESSION SETUP ERROR - COMMAND '{command.Value}' INVALID";
                    break;
            }
        }
    }

    private void Banner(int row, string text)
    {
        var width = text.Length * 6 - 1;
        var col = (Terminal3270.Cols - width) / 2;
        for (var line = 0; line < 5; line++)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var glyph = Font[text[i]][line];
                for (var x = 0; x < glyph.Length; x++)
                    if (glyph[x] == '#') t.Put(row + line, col + i * 6 + x, text[i].ToString(), Color.Green, Style.Bright);
            }
        }
    }
}
