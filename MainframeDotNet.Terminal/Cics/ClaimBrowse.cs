using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

public sealed record BrowseFilter(string Status = "", string ClaimType = "", string PolicyNo = "", string Name = "", int Offset = 0);

/// <summary>CLM4 - scrollable claim list with line selection.</summary>
public sealed class ClaimBrowse(Session session, BrowseFilter filter, string? message = null) : Transaction(session)
{
    private const int FirstRow = 6;
    private const int PageSize = 14;

    public override async Task<Transaction?> RunAsync()
    {
        T.Busy();
        var (rows, total) = await Repo.BrowseAsync(filter.Status, filter.PolicyNo, filter.ClaimType, filter.Name, filter.Offset, PageSize);

        T.Erase();
        Header("CLM4", "CLMM04", "CLAIM BROWSE");
        Label(3, 1, "STATUS:");
        var status = T.Input(3, 9, 4, filter.Status, color: Color.White);
        Label(3, 15, "TYPE:");
        var type = T.Input(3, 21, 4, filter.ClaimType, color: Color.White);
        Label(3, 27, "POLICY:");
        var policy = T.Input(3, 35, 8, filter.PolicyNo, color: Color.White);
        Label(3, 45, "NAME:");
        var name = T.Input(3, 51, 20, filter.Name, color: Color.White);

        T.Put(4, 0, "SEL CLAIM NO POLICY   INSURED            LOSS DT  TYPE STAT    CLAIMED ADJUSTER", Color.Turquoise);
        T.Put(5, 0, "--- -------- -------- ------------------ -------- ---- ---- ---------- --------", Color.Blue);

        var selections = new List<(Field Field, ClaimRow Row)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = FirstRow + i;
            selections.Add((T.Input(row, 1, 1, color: Color.White), r));
            T.Put(row, 4, r.ClaimNo, Color.Green);
            T.Put(row, 13, r.PolicyNo, Color.Green);
            T.Put(row, 22, Fit(r.HolderName, 18), Color.Green);
            T.Put(row, 41, Date(r.LossDate), Color.Green);
            T.Put(row, 50, r.ClaimType, Color.Green);
            T.Put(row, 55, r.Status, r.Status switch { "DENY" => Color.Red, "PEND" => Color.Yellow, "APPR" => Color.White, _ => Color.Green });
            T.Put(row, 60, Money(r.ClaimedAmount, 10), Color.Green);
            T.Put(row, 71, r.Adjuster, Color.Green);
        }
        if (rows.Count == 0) T.Put(FirstRow + 1, 4, "*** NO CLAIMS MATCH THE SELECTION CRITERIA ***", Color.Yellow);

        var shown = rows.Count == 0 ? "0" : $"{filter.Offset + 1} TO {filter.Offset + rows.Count}";
        T.Put(20, 1, $"ROWS {shown} OF {total}", Color.White);
        Label(20, 34, "S=INQUIRE  U=UPDATE  A=ACTIVITY  P=POLICY");
        Keys("ENTER=SELECT/FILTER  PF1=HELP  PF3=RETURN  PF5=REFRESH  PF7=BACK  PF8=FWD");
        Message(message ?? (rows.Count > 0 ? "CLM0400I TYPE A SELECTION CODE NEXT TO A CLAIM, OR CHANGE THE FILTER" : null));
        T.SetCursor(selections.Count > 0 ? selections[0].Field : status);

        while (true)
        {
            var aid = Read();
            var current = new BrowseFilter(status.Value, type.Value, PolicyKey(policy.Value), name.Value, filter.Offset);
            switch (aid)
            {
                case Aid.PF3 or Aid.Clear:
                    return Menu();
                case Aid.PF1:
                    return new HelpScreen(Session, () => new ClaimBrowse(Session, filter));
                case Aid.PF5:
                    return new ClaimBrowse(Session, current);
                case Aid.PF7:
                    if (filter.Offset == 0) { Message("CLM0405W TOP OF DATA REACHED"); continue; }
                    return new ClaimBrowse(Session, current with { Offset = Math.Max(0, filter.Offset - PageSize) });
                case Aid.PF8:
                    if (filter.Offset + PageSize >= total) { Message("CLM0406W BOTTOM OF DATA REACHED"); continue; }
                    return new ClaimBrowse(Session, current with { Offset = filter.Offset + PageSize });
                case not Aid.Enter:
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }

            Transaction Back() => new ClaimBrowse(Session, filter);

            // Line commands take priority over a change of filter.
            var selected = selections.FirstOrDefault(s => s.Field.Value.Length > 0);
            if (selected.Field is not null)
            {
                switch (selected.Field.Value)
                {
                    case "S": return new ClaimScreen(Session, selected.Row.ClaimNo, false, Back);
                    case "U": return new ClaimScreen(Session, selected.Row.ClaimNo, true, Back);
                    case "A": return new ActivityLog(Session, selected.Row.ClaimNo, Back);
                    case "P": return new PolicyInquiry(Session, selected.Row.PolicyNo, Back);
                    default:
                        Error(selected.Field, $"CLM0402E INVALID SELECTION CODE '{selected.Field.Value}' - USE S, U, A OR P");
                        continue;
                }
            }

            if (current with { Offset = 0 } != filter with { Offset = 0 })
            {
                if (current.Status.Length > 0 && !Codes.Statuses.Contains(current.Status))
                {
                    Error(status, $"CLM0403E STATUS '{current.Status}' IS NOT VALID - USE OPEN PEND APPR DENY CLSD");
                    continue;
                }
                if (current.ClaimType.Length > 0 && !Codes.ClaimTypeNames.ContainsKey(current.ClaimType))
                {
                    Error(type, $"CLM0404E CLAIM TYPE '{current.ClaimType}' IS NOT VALID");
                    continue;
                }
                return new ClaimBrowse(Session, current with { Offset = 0 });
            }

            // Cursor select: ENTER with the cursor on a list line opens that claim.
            var (cursorRow, _) = T.CursorPosition;
            var index = cursorRow - FirstRow;
            if (index >= 0 && index < rows.Count) return new ClaimScreen(Session, rows[index].ClaimNo, false, Back);

            Message("CLM0401I TYPE S, U, A OR P NEXT TO A CLAIM - OR PLACE THE CURSOR ON IT");
        }
    }
}
