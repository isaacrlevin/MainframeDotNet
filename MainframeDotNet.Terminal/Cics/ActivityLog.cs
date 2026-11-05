using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLM6 - claim activity journal, newest first.</summary>
public sealed class ActivityLog(Session session, string claimNo, Func<Transaction> back, int offset = 0) : Transaction(session)
{
    private const int FirstRow = 7;
    private const int PageSize = 14;
    private const int DetailCol = 33;
    private const int DetailWidth = Terminal3270.Cols - DetailCol - 1;

    public override async Task<Transaction?> RunAsync()
    {
        T.Busy();
        var claim = await Repo.GetClaimAsync(claimNo);
        if (claim is null) return Menu($"CLM0003E CLAIM {claimNo} NOT FOUND ON CLAIM_MASTER");
        var policy = (await Repo.GetPolicyAsync(claim.PolicyNo))!;
        var (activity, _) = await Repo.GetActivityAsync(claimNo, 0, 1000);

        // Long details wrap onto continuation lines, so page by display line rather than by entry.
        var lines = new List<(string Left, string Detail, string Action)>();
        foreach (var a in activity)
        {
            var left = $"{Stamp(a.At)} {Fit(a.UserId, 8)} {Fit(a.Action, 7)}";
            var detail = a.Detail;
            var first = true;
            while (first || detail.Length > 0)
            {
                var take = Math.Min(DetailWidth, detail.Length);
                if (take < detail.Length)
                {
                    var space = detail.LastIndexOf(' ', take);
                    if (space > 0) take = space;
                }
                lines.Add((first ? left : "", detail[..take].Trim(), a.Action));
                detail = detail[take..].TrimStart();
                first = false;
            }
        }

        T.Erase();
        Header("CLM6", "CLMM06", "CLAIM ACTIVITY JOURNAL");
        Label(3, 1, "CLAIM NO:");
        Data(3, 11, claim.ClaimNo, Color.White);
        Label(3, 21, "POLICY:");
        Data(3, 29, claim.PolicyNo);
        Label(3, 39, "INSURED:");
        Data(3, 48, Fit(policy.HolderName, 20).TrimEnd());
        Label(3, 70, "STAT:");
        Data(3, 76, claim.Status, Color.White);

        T.Put(5, 1, "DATE     TIME  USERID   ACTION", Color.Turquoise);
        T.Put(5, DetailCol, "DETAIL", Color.Turquoise);
        T.Put(6, 1, "-------- ----- -------- -------", Color.Blue);
        T.Put(6, DetailCol, new string('-', DetailWidth), Color.Blue);

        var page = lines.Skip(offset).Take(PageSize).ToList();
        for (var i = 0; i < page.Count; i++)
        {
            var (left, detail, action) = page[i];
            T.Put(FirstRow + i, 1, left, Color.Green);
            T.Put(FirstRow + i, DetailCol, detail, action switch
            {
                "PAYMENT" => Color.White,
                "STATUS" => Color.Yellow,
                "NOTE" => Color.Turquoise,
                _ => Color.Green,
            });
        }
        if (lines.Count == 0) T.Put(FirstRow, 1, "*** NO ACTIVITY RECORDED ***", Color.Yellow);

        T.Put(21, 1, $"{activity.Count} ENTRIES  -  LINES {Math.Min(offset + 1, lines.Count)}-{offset + page.Count} OF {lines.Count}", Color.White);
        Keys("PF1=HELP  PF3=RETURN  PF5=REFRESH  PF7=BACK  PF8=FWD");
        T.SetCursor(KeysRow, 0);

        while (true)
        {
            switch (Read())
            {
                case Aid.PF3 or Aid.Clear: return back();
                case Aid.PF1: return new HelpScreen(Session, () => new ActivityLog(Session, claimNo, back, offset));
                case Aid.PF5 or Aid.Enter: return new ActivityLog(Session, claimNo, back, offset);
                case Aid.PF7:
                    if (offset == 0) { Message("CLM0405W TOP OF DATA REACHED"); continue; }
                    return new ActivityLog(Session, claimNo, back, Math.Max(0, offset - PageSize));
                case Aid.PF8:
                    if (offset + PageSize >= lines.Count) { Message("CLM0406W BOTTOM OF DATA REACHED"); continue; }
                    return new ActivityLog(Session, claimNo, back, offset + PageSize);
                default:
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }
        }
    }
}
