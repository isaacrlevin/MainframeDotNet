using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLMH - terminal operator help.</summary>
public sealed class HelpScreen(Session session, Func<Transaction> back) : Transaction(session)
{
    private static readonly (string Key, string Pc, string Meaning)[] KeyMap =
    [
        ("ENTER", "ENTER", "SEND THE SCREEN TO THE HOST"),
        ("PF1-PF12", "F1-F12", "PROGRAM FUNCTION KEYS (SHIFT+F1-F12 = PF13-PF24)"),
        ("PF1-PF12", "ALT+1-0,-,=", "SAME (ALT+3=PF3  ALT+0=PF10  ALT+-=PF11)"),
        ("PF1-PF12", "ESC THEN 1-0", "TWO KEYSTROKES, WORKS IN ANY BROWSER (ESC,3=PF3)"),
        ("PF7/PF8", "PGUP/PGDN", "SCROLL BACKWARD / FORWARD"),
        ("CLEAR", "CTRL+L", "CLEAR THE SCREEN AND RETURN"),
        ("RESET", "ESC", "UNLOCK THE KEYBOARD AFTER AN INPUT INHIBIT (X)"),
        ("TAB/BACKTAB", "TAB/SHIFT+TAB", "NEXT / PREVIOUS INPUT FIELD"),
        ("ERASE EOF", "CTRL+K/CTRL+END", "ERASE TO END OF FIELD"),
        ("INSERT", "INS", "TOGGLE INSERT MODE (^ IN THE OIA)"),
        ("POWER", "CTRL+Q", "SWITCH THE TERMINAL OFF"),
    ];

    public override Task<Transaction?> RunAsync()
    {
        T.Erase();
        Header("CLMH", "CLMM99", "OPERATOR HELP");

        T.Put(3, 1, "3270 KEY      PC KEY           FUNCTION", Color.Turquoise);
        T.Put(4, 1, "------------  ---------------  ------------------------------------------------", Color.Blue);
        for (var i = 0; i < KeyMap.Length; i++)
        {
            var (key, pc, meaning) = KeyMap[i];
            T.Put(5 + i, 1, Fit(key, 12), Color.White);
            T.Put(5 + i, 15, Fit(pc, 15), Color.Green);
            T.Put(5 + i, 32, meaning, Color.Green);
        }

        T.Put(16, 1, "STATUS CODES", Color.Turquoise);
        T.Put(17, 3, "OPEN=UNDER REVIEW  PEND=INVESTIGATION  APPR=APPROVED  DENY=DENIED  CLSD=CLOSED", Color.Green);
        T.Put(18, 1, "OIA INDICATORS (BOTTOM LINE)", Color.Turquoise);
        T.Put(19, 3, "X SYSTEM  HOST IS PROCESSING        X -f   CURSOR IN A PROTECTED FIELD", Color.Green);
        T.Put(20, 3, "X NUM     NUMERIC FIELD             X >    FIELD IS FULL (INSERT MODE)", Color.Green);
        T.Put(21, 3, "CLAIM AND POLICY NUMBERS MAY BE ENTERED WITHOUT THE C / P PREFIX", Color.Green);

        Keys("ENTER OR PF3=RETURN");
        T.SetCursor(KeysRow, 0);

        while (true)
        {
            if (Read() is Aid.Enter or Aid.PF3 or Aid.Clear) return Task.FromResult<Transaction?>(back());
            Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
        }
    }
}
