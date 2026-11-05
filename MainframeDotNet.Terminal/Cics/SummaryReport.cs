using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLM7 - online claims summary, laid out like the CLMR01 batch report.</summary>
public sealed class SummaryReport(Session session, string groupBy = "STATUS") : Transaction(session)
{
    private static readonly string[] Groups = ["STATUS", "TYPE", "LINE", "ADJUSTER"];
    private const int FirstRow = 8;
    private const int MaxRows = 11;
    private const int LabelWidth = 26;

    public override async Task<Transaction?> RunAsync()
    {
        T.Busy();
        var lines = await Repo.SummaryAsync(groupBy);

        T.Erase();
        Header("CLM7", "CLMM07", "CLAIMS SUMMARY REPORT");
        T.Put(3, 1, $"REPORT CLMR01   CLAIMS SUMMARY BY {groupBy}", Color.White);
        T.Put(3, 58, $"RUN {DateTime.Now.ToString("MM/dd/yy HH:mm", Us)}", Color.Green);
        Label(4, 1, "GROUP BY ===>");
        var group = T.Input(4, 15, 8, groupBy, color: Color.White);
        Label(4, 25, "(STATUS, TYPE, LINE, ADJUSTER)");

        T.Put(6, 1, Fit(groupBy, LabelWidth) + " COUNT        CLAIMED        RESERVE           PAID", Color.Turquoise);
        T.Put(7, 1, new string('-', LabelWidth) + " ----- -------------- -------------- --------------", Color.Blue);

        for (var i = 0; i < Math.Min(lines.Count, MaxRows); i++)
            T.Put(FirstRow + i, 1, Format(Describe(lines[i].Key), lines[i]), Color.Green);

        var total = new SummaryLine("", lines.Sum(l => l.Count), lines.Sum(l => l.Claimed), lines.Sum(l => l.Reserve), lines.Sum(l => l.Paid));
        T.Put(FirstRow + MaxRows, 1, new string(' ', LabelWidth) + " ===== ============== ============== ==============", Color.Blue);
        T.Put(FirstRow + MaxRows + 1, 1, Format("*** GRAND TOTAL ***", total), Color.White);

        var lossRatio = total.Claimed == 0 ? 0 : (total.Paid + total.Reserve) / total.Claimed * 100;
        T.Put(21, 1, $"INCURRED (PAID + RESERVE) AS % OF CLAIMED: {lossRatio:0.0}%", Color.Yellow);
        Keys("ENTER=REGROUP  PF1=HELP  PF3=RETURN  PF4=NEXT GROUPING  PF5=REFRESH");
        T.SetCursor(group);

        while (true)
        {
            switch (Read())
            {
                case Aid.PF3 or Aid.Clear: return Menu();
                case Aid.PF1: return new HelpScreen(Session, () => new SummaryReport(Session, groupBy));
                case Aid.PF5: return new SummaryReport(Session, groupBy);
                case Aid.PF4: return new SummaryReport(Session, Groups[(Array.IndexOf(Groups, groupBy) + 1) % Groups.Length]);
                case Aid.Enter:
                    var choice = Groups.FirstOrDefault(g => group.Value.Length > 0 && g.StartsWith(group.Value));
                    if (choice is null) { Error(group, "CLM0701E GROUP BY MUST BE STATUS, TYPE, LINE OR ADJUSTER"); continue; }
                    return new SummaryReport(Session, choice);
                default:
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }
        }
    }

    private string Describe(string key) => groupBy switch
    {
        "STATUS" => $"{key} {Codes.StatusNames.GetValueOrDefault(key, "")}",
        "TYPE" => $"{key} {Codes.ClaimTypeNames.GetValueOrDefault(key, "")}",
        "LINE" => $"{key} {Codes.LinesOfBusiness.GetValueOrDefault(key, "")}",
        _ => key,
    };

    private static string Format(string label, SummaryLine l) =>
        $"{Fit(label, LabelWidth)} {l.Count,5} {Money(l.Claimed, 14)} {Money(l.Reserve, 14)} {Money(l.Paid, 14)}";
}
