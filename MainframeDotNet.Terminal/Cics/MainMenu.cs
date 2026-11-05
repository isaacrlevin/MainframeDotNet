using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLM0 - claims system primary option menu.</summary>
public sealed class MainMenu(Session session, string? message = null) : Transaction(session)
{
    private static readonly (string Option, string Name, string Description)[] Options =
    [
        ("1", "CLAIM INQUIRY", "DISPLAY A CLAIM BY CLAIM NUMBER"),
        ("2", "FIRST NOTICE OF LOSS", "REGISTER A NEW CLAIM AGAINST A POLICY"),
        ("3", "CLAIM MAINTENANCE", "STATUS, RESERVES, PAYMENTS AND NOTES"),
        ("4", "CLAIM BROWSE", "LIST CLAIMS BY STATUS/TYPE/POLICY/NAME"),
        ("5", "POLICY INQUIRY", "DISPLAY POLICY AND CLAIM HISTORY"),
        ("6", "CLAIMS SUMMARY REPORT", "TOTALS BY STATUS/TYPE/LINE/ADJUSTER"),
        ("X", "EXIT", "END THE CLAIMS SESSION"),
    ];

    public override async Task<Transaction?> RunAsync()
    {
        T.Erase();
        Header("CLM0", "CLMM00", "CLAIMS INFORMATION MANAGEMENT SYSTEM");
        T.Center(3, "PRIMARY OPTION MENU", Color.Turquoise);

        Label(5, 10, "SELECT OPTION ===>");
        var option = T.Input(5, 29, 1, color: Color.White, flags: FieldFlags.Bright);

        for (var i = 0; i < Options.Length; i++)
        {
            var (opt, name, description) = Options[i];
            T.Put(7 + i, 14, opt, Color.White);
            T.Put(7 + i, 17, name, Color.Turquoise);
            T.Put(7 + i, 40, "- " + description, Color.Green);
        }

        Label(15, 10, "CLAIM NUMBER  ===>");
        var claimNo = T.Input(15, 29, 8);
        Label(15, 40, "(OPTIONS 1 AND 3)");
        Label(16, 10, "POLICY NUMBER ===>");
        var policyNo = T.Input(16, 29, 8);
        Label(16, 40, "(OPTIONS 2, 4 AND 5)");

        T.Put(18, 10, new string('*', 60), Color.Blue);
        T.Put(19, 10, "*", Color.Blue);
        T.Put(19, 69, "*", Color.Blue);
        T.Put(19, 12, Fit($"USER {Session.UserId}   TERM {T.LuName}   APPLID {Session.ApplId}", 56), Color.Green);
        T.Put(20, 10, "*", Color.Blue);
        T.Put(20, 69, "*", Color.Blue);
        T.Put(20, 12, Fit($"DATA BASE {Repo.ServerDescription}", 56), Color.Green);
        T.Put(21, 10, new string('*', 60), Color.Blue);

        Keys("ENTER=PROCESS  PF1=HELP  PF3=EXIT  PF12=CANCEL");
        Message(message);
        T.SetCursor(option);

        while (true)
        {
            var aid = Read();
            switch (aid)
            {
                case Aid.PF3:
                    return null;
                case Aid.PF1:
                    return new HelpScreen(Session, () => new MainMenu(Session));
                case Aid.PF12 or Aid.Clear:
                    return new MainMenu(Session);
                case not Aid.Enter:
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }

            var claim = ClaimKey(claimNo.Value);
            var policy = PolicyKey(policyNo.Value);
            switch (option.Value)
            {
                case "1" or "3":
                    if (claim.Length == 0) { Error(claimNo, $"CLM0002E CLAIM NUMBER IS REQUIRED FOR OPTION {option.Value}"); break; }
                    T.Busy();
                    if (await Repo.GetClaimAsync(claim) is null) { Error(claimNo, $"CLM0003E CLAIM {claim} NOT FOUND ON CLAIM_MASTER"); break; }
                    return new ClaimScreen(Session, claim, update: option.Value == "3", back: () => new MainMenu(Session));
                case "2":
                    return new FirstNoticeOfLoss(Session, policy, () => new MainMenu(Session));
                case "4":
                    return new ClaimBrowse(Session, new BrowseFilter(PolicyNo: policy));
                case "5":
                    if (policy.Length == 0) { Error(policyNo, "CLM0004E POLICY NUMBER IS REQUIRED FOR OPTION 5"); break; }
                    T.Busy();
                    if (await Repo.GetPolicyAsync(policy) is null) { Error(policyNo, $"CLM0005E POLICY {policy} NOT FOUND ON POLICY_MASTER"); break; }
                    return new PolicyInquiry(Session, policy, () => new MainMenu(Session));
                case "6":
                    return new SummaryReport(Session);
                case "X":
                    return null;
                case "":
                    Error(option, "CLM0001E ENTER AN OPTION NUMBER");
                    break;
                default:
                    Error(option, $"CLM0006E OPTION '{option.Value}' IS NOT VALID");
                    break;
            }
        }
    }
}
