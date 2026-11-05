using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLM2 - first notice of loss: registers a new claim.</summary>
public sealed class FirstNoticeOfLoss(Session session, string policyNo, Func<Transaction> back) : Transaction(session)
{
    private const int ValueCol = 19;

    public override async Task<Transaction?> RunAsync()
    {
        T.Erase();
        Header("CLM2", "CLMM02", "FIRST NOTICE OF LOSS");

        Label(3, 1, "POLICY NUMBER ..:");
        var policyField = T.Input(3, ValueCol, 8, policyNo, color: Color.White);
        Label(3, 29, "(PRESS ENTER TO RETRIEVE POLICY)");

        Rule(6, "LOSS INFORMATION");
        Label(7, 1, "DATE OF LOSS ...:");
        var lossDate = T.Input(7, ValueCol, 8, flags: FieldFlags.Numeric);
        Label(7, 28, "(MM/DD/YY)");
        Label(7, 40, "DATE REPORTED ..:");
        Data(7, 58, Date(DateOnly.FromDateTime(DateTime.Today)) + " (TODAY)");
        Label(8, 1, "CLAIM TYPE .....:");
        var type = T.Input(8, ValueCol, 4);
        Label(9, 1, "LOSS LOCATION ..:");
        var location = T.Input(9, ValueCol, 40);
        Label(10, 1, "DESCRIPTION ....:");
        var desc1 = T.Input(10, ValueCol, 60);
        var desc2 = T.Input(11, ValueCol, 60);

        Rule(12, "FINANCIALS");
        Label(13, 1, "AMOUNT CLAIMED .:");
        var claimed = T.Input(13, ValueCol, 12, flags: FieldFlags.Numeric);
        Label(14, 1, "INITIAL RESERVE :");
        var reserve = T.Input(14, ValueCol, 12, flags: FieldFlags.Numeric);
        Label(14, 33, "(BLANK = AMOUNT CLAIMED LESS DEDUCTIBLE)");
        Label(15, 1, "ADJUSTER .......:");
        var adjuster = T.Input(15, ValueCol, 8);
        Label(15, 33, "(BLANK = ASSIGN BY WORKLOAD)");

        T.Put(18, 1, "COMPLETE THE FORM AND PRESS ENTER TO EDIT.  PRESS PF5 TO RECORD THE CLAIM.", Color.Turquoise);
        Keys("ENTER=EDIT  PF1=HELP  PF3=RETURN  PF5=ADD CLAIM  PF12=CLEAR FORM");

        Policy? policy = null;
        if (policyNo.Length > 0)
        {
            T.Busy();
            policy = await Repo.GetPolicyAsync(policyNo);
            PaintPolicy(policy);
            T.SetCursor(policy is null ? policyField : lossDate);
            Message(policy is null ? $"CLM0005E POLICY {policyNo} NOT FOUND ON POLICY_MASTER" : "CLM0200I POLICY RETRIEVED - ENTER LOSS DETAILS");
        }
        else
        {
            T.SetCursor(policyField);
            Message("CLM0199I ENTER THE POLICY NUMBER OF THE INSURED");
        }

        while (true)
        {
            var aid = Read();
            switch (aid)
            {
                case Aid.PF3 or Aid.Clear: return back();
                case Aid.PF1: return new HelpScreen(Session, () => new FirstNoticeOfLoss(Session, policyNo, back));
                case Aid.PF12: return new FirstNoticeOfLoss(Session, "", back);
                case not (Aid.Enter or Aid.PF5):
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }

            // Re-read the policy whenever the number on the screen changes.
            var key = PolicyKey(policyField.Value);
            if (key.Length == 0) { PaintPolicy(null); Error(policyField, "CLM0004E POLICY NUMBER IS REQUIRED"); continue; }
            if (policy?.PolicyNo != key)
            {
                T.Busy();
                policy = await Repo.GetPolicyAsync(key);
                PaintPolicy(policy);
                if (policy is null) { Error(policyField, $"CLM0005E POLICY {key} NOT FOUND ON POLICY_MASTER"); continue; }
                if (aid == Aid.Enter && lossDate.Value.Length == 0)
                {
                    Message("CLM0200I POLICY RETRIEVED - ENTER LOSS DETAILS");
                    T.SetCursor(lossDate);
                    continue;
                }
            }

            if (policy.Status == "CANC") { Error(policyField, "CLM0210E POLICY IS CANCELLED - NO COVERAGE IN FORCE"); continue; }

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!TryDate(lossDate.Value, out var loss)) { Error(lossDate, "CLM0211E DATE OF LOSS IS INVALID - USE MM/DD/YY"); continue; }
            if (loss > today) { Error(lossDate, "CLM0212E DATE OF LOSS CANNOT BE IN THE FUTURE"); continue; }
            if (loss < policy.EffectiveDate || loss > policy.ExpiryDate)
            {
                Error(lossDate, $"CLM0213E LOSS DATE OUTSIDE POLICY PERIOD {Date(policy.EffectiveDate)} - {Date(policy.ExpiryDate)}");
                continue;
            }

            var valid = Codes.ClaimTypesByLine[policy.LineOfBusiness];
            if (!valid.Contains(type.Value))
            {
                Error(type, $"CLM0214E CLAIM TYPE MUST BE ONE OF: {string.Join(' ', valid)}");
                continue;
            }
            if (location.Value.Length == 0) { Error(location, "CLM0215E LOSS LOCATION IS REQUIRED"); continue; }
            if (desc1.Value.Length == 0) { Error(desc1, "CLM0216E DESCRIPTION OF LOSS IS REQUIRED"); continue; }

            if (!TryAmount(claimed.Value, out var amount) || amount <= 0)
            {
                Error(claimed, "CLM0217E AMOUNT CLAIMED MUST BE GREATER THAN ZERO, E.G. 1250.00");
                continue;
            }
            if (amount > policy.CoverageLimit)
            {
                Error(claimed, $"CLM0218E AMOUNT CLAIMED EXCEEDS COVERAGE LIMIT OF {Money(policy.CoverageLimit)}");
                continue;
            }

            if (!TryAmount(reserve.Value, out var initialReserve) || initialReserve < 0)
            {
                Error(reserve, "CLM0219E INITIAL RESERVE MUST BE A POSITIVE AMOUNT");
                continue;
            }
            if (reserve.Value.Length == 0) initialReserve = Math.Max(0, amount - policy.Deductible);

            if (aid == Aid.Enter)
            {
                Message($"CLM0220I CLAIM EDITED OK - RESERVE {Money(initialReserve)} - PRESS PF5 TO ADD");
                continue;
            }

            T.Busy();
            var assigned = adjuster.Value.Length > 0 ? adjuster.Value : await Repo.PickAdjusterAsync();
            var claimNo = await Repo.AddClaimAsync(
                new NewClaim(policy.PolicyNo, type.Value, loss, location.Value, desc1.Value, desc2.Value, amount, initialReserve, assigned),
                Session.UserId);
            return new ClaimScreen(Session, claimNo, false, back,
                $"CLM0201I CLAIM {claimNo} ADDED - ASSIGNED TO ADJUSTER {(assigned.Length > 0 ? assigned : "(NONE)")}");
        }
    }

    private void PaintPolicy(Policy? p)
    {
        T.Put(4, 0, new string(' ', Terminal3270.Cols));
        T.Put(5, 0, new string(' ', Terminal3270.Cols));
        T.Put(8, 25, new string(' ', 55));
        if (p is null) return;

        Label(4, 1, "INSURED ........:");
        Data(4, ValueCol, p.HolderName, Color.White);
        Label(4, 51, "LINE:");
        Data(4, 57, Fit($"{p.LineOfBusiness} {Codes.LinesOfBusiness[p.LineOfBusiness]}", 22).TrimEnd());
        Label(5, 1, "POLICY PERIOD ..:");
        Data(5, ValueCol, $"{Date(p.EffectiveDate)} - {Date(p.ExpiryDate)}  {p.Status}");
        Label(5, 46, "LIMIT:");
        Data(5, 53, Money(p.CoverageLimit));
        Label(5, 65, "DED:");
        Data(5, 70, Money(p.Deductible));
        Label(8, 25, $"VALID: {string.Join(' ', Codes.ClaimTypesByLine[p.LineOfBusiness])}");
    }
}
