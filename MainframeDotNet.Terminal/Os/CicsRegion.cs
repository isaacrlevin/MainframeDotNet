using MainframeDotNet.Terminal.Cics;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Os;

/// <summary>The CICSPROD region: sign-on (CSSN), the unformatted transaction prompt, and sign-off (CSSF).</summary>
public sealed class CicsRegion(Session session)
{
    private Terminal3270 T => session.Terminal;

    /// <summary>Runs until the operator signs off and logs off back to VTAM.</summary>
    public async Task RunAsync()
    {
        if (!SignOn()) return;

        string? message = $"DFHCE3549 SIGN-ON IS COMPLETE (LANGUAGE ENU).";
        while (true)
        {
            var tran = TransactionPrompt(message);
            message = null;
            switch (tran)
            {
                case "CLMS" or "CLM0":
                    Transaction? current = new MainMenu(session, $"CLM0000I WELCOME {session.UserId} - CLAIMS SYSTEM READY");
                    while (current is not null) current = await current.RunAsync();
                    message = "CLM9999I CLMS TRANSACTION ENDED - NORMAL TERMINATION";
                    break;
                case "CESF" or "CSSF" or "CESF LOGOFF" or "CSSF LOGOFF" or "LOGOFF":
                    return;
                case "CEMT" or "CEDA" or "CECI":
                    message = $"DFHAC2002 {Now()} CICSPROD USER {session.UserId} IS NOT AUTHORIZED TO USE TRANSACTION {tran}.";
                    break;
                case "":
                    break;
                default:
                    var id = tran.Split(' ')[0];
                    message = $"DFHAC2001 {Now()} CICSPROD TRANSACTION '{(id.Length > 4 ? id[..4] : id)}' IS NOT RECOGNIZED. CHECK THAT THE TRANSACTION NAME IS CORRECT.";
                    break;
            }
        }
    }

    private bool SignOn()
    {
        string? message = null;
        while (true)
        {
            T.Erase();
            T.Put(0, 1, "CSSN", Color.White);
            T.Put(0, 25, "SIGNON TO CICS/VS", Color.White, Style.Bright);
            T.Put(0, 63, $"APPLID {session.ApplId}", Color.Blue);
            T.Put(2, 1, "ACME MUTUAL INSURANCE COMPANY - DATA PROCESSING DIVISION", Color.Yellow);

            T.Put(5, 1, "TYPE YOUR USERID AND PASSWORD, THEN PRESS ENTER:", Color.Turquoise);
            T.Put(8, 8, "USERID . . . .", Color.Turquoise);
            var user = T.Input(8, 24, 8, session.UserId.Length > 0 ? session.UserId : null, color: Color.White);
            T.Put(8, 36, "GROUPID . . .", Color.Turquoise);
            T.Input(8, 51, 8, "CLAIMS");
            T.Put(9, 8, "PASSWORD . . .", Color.Turquoise);
            var password = T.Input(9, 24, 8, flags: FieldFlags.Hidden);
            T.Put(11, 8, "NEW PASSWORD .", Color.Turquoise);
            T.Input(11, 24, 8, flags: FieldFlags.Hidden);

            T.Put(15, 1, "DEMONSTRATION SYSTEM - ANY USERID AND PASSWORD WILL BE ACCEPTED", Color.Blue);
            T.Put(22, 1, message ?? "", Color.Red, Style.Bright);
            if (message is not null) T.Alarm();
            T.Put(23, 1, "F3=EXIT", Color.Turquoise);
            T.SetCursor(user.Value.Length > 0 ? password : user);

            var aid = T.Read();
            if (aid is Aid.PF3 or Aid.Clear) return false;
            if (aid != Aid.Enter) { message = "DFHCE3532 YOU PRESSED AN UNSUPPORTED KEY."; continue; }

            if (user.Value.Length == 0) { message = "DFHCE3520 PLEASE TYPE YOUR USERID."; continue; }
            if (!user.Value.All(char.IsLetterOrDigit) || char.IsDigit(user.Value[0]))
            {
                message = $"DFHCE3530 YOUR USERID '{user.Value}' IS NOT VALID.";
                continue;
            }
            if (password.Value.Length == 0)
            {
                session.UserId = user.Value;
                message = "DFHCE3521 PLEASE TYPE YOUR PASSWORD.";
                continue;
            }

            session.UserId = user.Value;
            session.SignedOnAt = DateTime.Now;
            T.Busy();
            Thread.Sleep(400); // RACF verification
            return true;
        }
    }

    /// <summary>A cleared, unformatted screen: the operator keys a transaction id at the top left.</summary>
    private string TransactionPrompt(string? message)
    {
        T.Erase();
        var input = T.Input(0, 0, 79);
        if (message is not null)
        {
            // Long CICS messages simply wrap across the unformatted screen.
            for (var i = 0; i * 80 < message.Length; i++)
                T.Put(1 + i, 0, message.Substring(i * 80, Math.Min(80, message.Length - i * 80)), Color.White);
        }
        T.Put(20, 0, "AVAILABLE TRANSACTIONS:", Color.Blue);
        T.Put(21, 2, "CLMS  CLAIMS INFORMATION MANAGEMENT SYSTEM", Color.Blue);
        T.Put(22, 2, "CESF  SIGN OFF FROM CICS", Color.Blue);
        T.SetCursor(input);

        while (true)
        {
            var aid = T.Read();
            if (aid == Aid.Clear) return "";
            if (aid == Aid.Enter) return input.Value;
        }
    }

    private static string Now() => DateTime.Now.ToString("MM/dd/yy HH:mm:ss");
}
