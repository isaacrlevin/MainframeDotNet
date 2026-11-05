using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLM5 - policy master display with the policy's claim history.</summary>
public sealed class PolicyInquiry(Session session, string policyNo, Func<Transaction> back, int offset = 0) : Transaction(session)
{
    private const int FirstRow = 13;
    private const int PageSize = 7;

    public override async Task<Transaction?> RunAsync()
    {
        T.Busy();
        var policy = await Repo.GetPolicyAsync(policyNo);
        if (policy is null) return Menu($"CLM0005E POLICY {policyNo} NOT FOUND ON POLICY_MASTER");
        var claims = await Repo.GetPolicyClaimsAsync(policyNo);
        var page = claims.Skip(offset).Take(PageSize).ToList();

        T.Erase();
        Header("CLM5", "CLMM05", "POLICY INQUIRY");

        Label(3, 1, "POLICY NUMBER ..:");
        var policyField = T.Input(3, 19, 8, policy.PolicyNo, color: Color.White);
        Label(3, 40, "POLICY STATUS ..:");
        var inForce = policy.InForceOn(DateOnly.FromDateTime(DateTime.Today));
        Data(3, 58, policy.Status switch
        {
            "ACTV" when inForce => "ACTV - IN FORCE",
            "ACTV" => "ACTV - NOT YET IN FORCE",
            "EXPD" => "EXPD - EXPIRED",
            "CANC" => "CANC - CANCELLED",
            var s => s,
        }, policy.Status == "CANC" ? Color.Red : policy.Status == "EXPD" ? Color.Yellow : Color.White);

        Label(4, 1, "NAMED INSURED ..:");
        Data(4, 19, policy.HolderName, Color.White);
        Label(5, 1, "MAILING ADDRESS :");
        Data(5, 19, policy.Address);
        Data(6, 19, $"{policy.City}, {policy.State} {policy.Zip}");
        Label(6, 40, "TELEPHONE ......:");
        Data(6, 58, policy.Phone);
        Label(7, 1, "LINE OF BUSINESS:");
        Data(7, 19, $"{policy.LineOfBusiness} {Codes.LinesOfBusiness[policy.LineOfBusiness]}");
        Label(7, 40, "PRODUCING AGENT :");
        Data(7, 58, policy.Agent);
        Label(8, 1, "POLICY PERIOD ..:");
        Data(8, 19, $"{Date(policy.EffectiveDate)} - {Date(policy.ExpiryDate)}");
        Label(8, 40, "ANNUAL PREMIUM .:");
        Data(8, 58, Money(policy.Premium, 12));
        Label(9, 1, "COVERAGE LIMIT .:");
        Data(9, 19, Money(policy.CoverageLimit, 12));
        Label(9, 40, "DEDUCTIBLE .....:");
        Data(9, 58, Money(policy.Deductible, 12));

        Rule(10, "CLAIM HISTORY");
        T.Put(11, 0, "SEL CLAIM NO LOSS DT  TYPE STAT        CLAIMED          PAID ADJUSTER", Color.Turquoise);
        T.Put(12, 0, "--- -------- -------- ---- ---- -------------- ------------- --------", Color.Blue);

        var selections = new List<(Field Field, ClaimRow Row)>();
        for (var i = 0; i < page.Count; i++)
        {
            var c = page[i];
            var row = FirstRow + i;
            selections.Add((T.Input(row, 1, 1, color: Color.White), c));
            T.Put(row, 4, c.ClaimNo);
            T.Put(row, 13, Date(c.LossDate));
            T.Put(row, 22, c.ClaimType);
            T.Put(row, 27, c.Status, c.Status == "DENY" ? Color.Red : c.Status == "PEND" ? Color.Yellow : Color.Green);
            T.Put(row, 32, Money(c.ClaimedAmount, 14));
            T.Put(row, 47, Money(c.PaidAmount, 13));
            T.Put(row, 61, c.Adjuster);
        }
        if (claims.Count == 0) T.Put(FirstRow, 4, "*** NO CLAIMS ON FILE FOR THIS POLICY ***", Color.Yellow);

        T.Put(20, 1, $"{claims.Count} CLAIM(S)", Color.White);
        Label(20, 16, "TOTAL CLAIMED");
        Data(20, 30, Money(claims.Sum(c => c.ClaimedAmount), 14));
        Label(20, 46, "TOTAL PAID");
        Data(20, 57, Money(claims.Sum(c => c.PaidAmount), 13));
        if (claims.Count > PageSize)
            Label(21, 1, $"SHOWING {offset + 1}-{offset + page.Count} - PF7/PF8 TO SCROLL");

        Keys("ENTER=SELECT  PF1=HELP  PF2=NEW CLAIM  PF3=RETURN  PF7=BACK  PF8=FWD");
        Message(page.Count > 0 ? "CLM0500I S=INQUIRE  U=UPDATE  A=ACTIVITY  - OR TYPE A NEW POLICY NUMBER" : null);
        T.SetCursor(selections.Count > 0 ? selections[0].Field : policyField);

        Transaction Self() => new PolicyInquiry(Session, policyNo, back, offset);

        while (true)
        {
            var aid = Read();
            switch (aid)
            {
                case Aid.PF3 or Aid.Clear: return back();
                case Aid.PF1: return new HelpScreen(Session, Self);
                case Aid.PF2:
                    if (policy.Status == "CANC") { Message("CLM0210E POLICY IS CANCELLED - NO COVERAGE IN FORCE"); continue; }
                    return new FirstNoticeOfLoss(Session, policyNo, Self);
                case Aid.PF7:
                    if (offset == 0) { Message("CLM0405W TOP OF DATA REACHED"); continue; }
                    return new PolicyInquiry(Session, policyNo, back, Math.Max(0, offset - PageSize));
                case Aid.PF8:
                    if (offset + PageSize >= claims.Count) { Message("CLM0406W BOTTOM OF DATA REACHED"); continue; }
                    return new PolicyInquiry(Session, policyNo, back, offset + PageSize);
                case not Aid.Enter:
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }

            var key = PolicyKey(policyField.Value);
            if (key != policyNo)
            {
                T.Busy();
                if (await Repo.GetPolicyAsync(key) is null) { Error(policyField, $"CLM0005E POLICY {key} NOT FOUND ON POLICY_MASTER"); continue; }
                return new PolicyInquiry(Session, key, back);
            }

            var selected = selections.FirstOrDefault(s => s.Field.Value.Length > 0);
            if (selected.Field is null)
            {
                var index = T.CursorPosition.Row - FirstRow;
                if (index >= 0 && index < page.Count) return new ClaimScreen(Session, page[index].ClaimNo, false, Self);
                Message("CLM0500I S=INQUIRE  U=UPDATE  A=ACTIVITY  - OR TYPE A NEW POLICY NUMBER");
                continue;
            }
            switch (selected.Field.Value)
            {
                case "S": return new ClaimScreen(Session, selected.Row.ClaimNo, false, Self);
                case "U": return new ClaimScreen(Session, selected.Row.ClaimNo, true, Self);
                case "A": return new ActivityLog(Session, selected.Row.ClaimNo, Self);
                default:
                    Error(selected.Field, $"CLM0402E INVALID SELECTION CODE '{selected.Field.Value}' - USE S, U OR A");
                    continue;
            }
        }
    }
}
