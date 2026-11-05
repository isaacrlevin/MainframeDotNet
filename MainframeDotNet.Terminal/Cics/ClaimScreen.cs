using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Cics;

/// <summary>CLM1 (inquiry) and CLM3 (maintenance) share map CLMM01.</summary>
public sealed class ClaimScreen(Session session, string claimNo, bool update, Func<Transaction> back, string? message = null)
    : Transaction(session)
{
    private const int ValueCol = 19;

    private Field? _status, _type, _location, _desc1, _desc2, _reserve, _payment, _adjuster, _note;

    public override async Task<Transaction?> RunAsync()
    {
        T.Busy();
        var claim = await Repo.GetClaimAsync(claimNo);
        if (claim is null) return Menu($"CLM0003E CLAIM {claimNo} NOT FOUND ON CLAIM_MASTER");
        var policy = (await Repo.GetPolicyAsync(claim.PolicyNo))!;

        Paint(claim, policy);
        Message(message ?? (update ? "CLM0300I OVERTYPE FIELDS TO BE CHANGED, PRESS ENTER TO EDIT" : null));

        while (true)
        {
            var aid = Read();
            switch (aid)
            {
                case Aid.PF3 or Aid.Clear:
                    return back();
                case Aid.PF1:
                    return new HelpScreen(Session, () => new ClaimScreen(Session, claimNo, update, back));
                case Aid.PF4:
                    return new ActivityLog(Session, claimNo, () => new ClaimScreen(Session, claimNo, update, back));
                case Aid.PF11:
                    return new PolicyInquiry(Session, claim.PolicyNo, () => new ClaimScreen(Session, claimNo, update, back));
            }

            if (!update)
            {
                switch (aid)
                {
                    case Aid.Enter or Aid.PF5:
                        return new ClaimScreen(Session, claimNo, false, back);
                    case Aid.PF10:
                        return new ClaimScreen(Session, claimNo, true, back);
                    case Aid.PF7 or Aid.PF8:
                        T.Busy();
                        var next = await Repo.GetAdjacentClaimNoAsync(claimNo, forward: aid == Aid.PF8);
                        if (next is null)
                        {
                            Message(aid == Aid.PF8 ? "CLM0106W END OF FILE - NO MORE CLAIMS" : "CLM0105W START OF FILE - NO PRIOR CLAIMS");
                            continue;
                        }
                        return new ClaimScreen(Session, next, false, back);
                    default:
                        Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                        continue;
                }
            }

            switch (aid)
            {
                case Aid.PF12:
                    return new ClaimScreen(Session, claimNo, false, back, "CLM0309I UPDATE CANCELLED - NO CHANGES APPLIED");
                case Aid.Enter or Aid.PF5:
                    var changes = Validate(claim, policy);
                    if (changes is null) continue;
                    if (aid == Aid.Enter)
                    {
                        Message("CLM0302I CHANGES ARE VALID - PRESS PF5 TO APPLY THE UPDATE");
                        continue;
                    }
                    T.Busy();
                    switch (await Repo.UpdateClaimAsync(claimNo, claim.Version, changes, Session.UserId))
                    {
                        case UpdateResult.Updated:
                            return new ClaimScreen(Session, claimNo, false, back, $"CLM0310I CLAIM {claimNo} UPDATED SUCCESSFULLY");
                        case UpdateResult.Conflict:
                            Message("CLM0399E CLAIM WAS CHANGED BY ANOTHER USER - PF12 THEN RETRY");
                            continue;
                        default:
                            return Menu($"CLM0003E CLAIM {claimNo} NOT FOUND ON CLAIM_MASTER");
                    }
                default:
                    Message("CLM0009E PF KEY NOT DEFINED FOR THIS SCREEN");
                    continue;
            }
        }
    }

    private void Paint(Claim c, Policy p)
    {
        T.Erase();
        Header(update ? "CLM3" : "CLM1", "CLMM01", update ? "CLAIM MAINTENANCE" : "CLAIM INQUIRY");

        Label(3, 1, "CLAIM NO:");
        Data(3, 11, c.ClaimNo, Color.White);
        Label(3, 21, "POLICY:");
        Data(3, 29, p.PolicyNo);
        Label(3, 39, "LINE:");
        Data(3, 45, p.LineOfBusiness);
        Label(3, 51, "STATUS:");
        Data(3, 59, Codes.StatusNames.GetValueOrDefault(c.Status, c.Status), StatusColor(c.Status));

        Label(4, 1, "INSURED :");
        Data(4, 11, p.HolderName);
        Label(4, 51, "PHONE:");
        Data(4, 58, p.Phone);
        Label(5, 1, "PERIOD  :");
        Data(5, 11, $"{Date(p.EffectiveDate)} - {Date(p.ExpiryDate)}");
        Label(5, 32, "POL STATUS:");
        Data(5, 44, p.Status);
        Label(5, 51, "AGENT:");
        Data(5, 58, p.Agent);

        Rule(6, "LOSS INFORMATION");
        Label(7, 1, "DATE OF LOSS ...:");
        Data(7, ValueCol, Date(c.LossDate));
        Label(7, 40, "DATE REPORTED ..:");
        Data(7, 58, Date(c.ReportedDate));

        Label(8, 1, "CLAIM TYPE .....:");
        _type = Value(8, ValueCol, 4, c.ClaimType);
        Data(8, 25, Fit(Codes.ClaimTypeNames.GetValueOrDefault(c.ClaimType, ""), 18));
        Label(8, 46, $"VALID: {string.Join(' ', Codes.ClaimTypesByLine[p.LineOfBusiness])}");

        Label(9, 1, "LOSS LOCATION ..:");
        _location = Value(9, ValueCol, 40, c.LossLocation);
        Label(10, 1, "DESCRIPTION ....:");
        _desc1 = Value(10, ValueCol, 60, c.Description1);
        _desc2 = Value(11, ValueCol, 60, c.Description2);

        Rule(12, "FINANCIALS");
        var payable = Payable(c, p);
        Label(13, 1, "AMOUNT CLAIMED .:");
        Data(13, ValueCol, Money(c.ClaimedAmount, 12));
        Label(13, 40, "COVERAGE LIMIT .:");
        Data(13, 58, Money(p.CoverageLimit, 12));
        Label(14, 1, "RESERVE ........:");
        _reserve = Value(14, ValueCol, 12, update ? Money(c.ReserveAmount) : Money(c.ReserveAmount, 12), FieldFlags.Numeric);
        Label(14, 40, "DEDUCTIBLE .....:");
        Data(14, 58, Money(p.Deductible, 12));
        Label(15, 1, "PAID TO DATE ...:");
        Data(15, ValueCol, Money(c.PaidAmount, 12));
        Label(15, 40, "REMAINING PAYABLE:");
        Data(15, 58, Money(payable - c.PaidAmount, 12), payable - c.PaidAmount > 0 ? Color.Green : Color.Yellow);
        if (update)
        {
            Label(16, 1, "ISSUE PAYMENT ..:");
            _payment = T.Input(16, ValueCol, 12, flags: FieldFlags.Numeric, color: Color.White);
            Label(16, 40, "(CLAIM MUST BE IN APPR STATUS)");
        }

        Rule(17, "HANDLING");
        Label(18, 1, "STATUS .........:");
        _status = Value(18, ValueCol, 4, c.Status);
        Label(18, 25, "(OPEN PEND APPR DENY CLSD)");
        Label(18, 55, "ADJUSTER:");
        _adjuster = Value(18, 65, 8, c.Adjuster.Length == 0 && !update ? "(NONE)" : c.Adjuster);
        if (update)
        {
            Label(19, 1, "ADD NOTE .......:");
            _note = T.Input(19, ValueCol, 60, color: Color.White);
        }
        Label(20, 1, "CREATED");
        Data(20, 9, $"{Stamp(c.CreatedAt)} BY {c.CreatedBy}");
        Label(20, 40, "LAST UPDATE");
        Data(20, 52, $"{Stamp(c.UpdatedAt)} BY {c.UpdatedBy}");

        Keys(update
            ? "ENTER=EDIT PF1=HELP PF3=RETURN PF4=ACTIVITY PF5=APPLY PF11=POLICY PF12=CANCEL"
            : "PF1=HELP PF3=RETURN PF4=ACTIVITY PF7=PREV PF8=NEXT PF10=UPDATE PF11=POLICY");

        if (update) T.SetCursor(_status!);
        else T.SetCursor(KeysRow, 0);
    }

    /// <summary>Inquiry mode paints protected data; maintenance mode makes the same positions input fields.</summary>
    private Field? Value(int row, int col, int length, string value, FieldFlags flags = FieldFlags.None)
    {
        if (update) return T.Input(row, col, length, value, flags);
        Data(row, col, value);
        return null;
    }

    private ClaimChanges? Validate(Claim c, Policy p)
    {
        var status = _status!.Value;
        if (!Codes.Statuses.Contains(status))
            return Fail(_status, $"CLM0320E STATUS '{status}' IS NOT VALID - USE OPEN PEND APPR DENY CLSD");

        var type = _type!.Value;
        if (!Codes.ClaimTypesByLine[p.LineOfBusiness].Contains(type))
            return Fail(_type, $"CLM0321E CLAIM TYPE '{type}' NOT VALID FOR {p.LineOfBusiness} POLICY");

        if (_desc1!.Value.Length == 0)
            return Fail(_desc1, "CLM0323E DESCRIPTION OF LOSS IS REQUIRED");

        if (!TryAmount(_reserve!.Value, out var reserve) || reserve < 0)
            return Fail(_reserve, "CLM0324E RESERVE MUST BE A POSITIVE AMOUNT, E.G. 1500.00");
        if (reserve > p.CoverageLimit)
            return Fail(_reserve, $"CLM0325E RESERVE EXCEEDS COVERAGE LIMIT OF {Money(p.CoverageLimit)}");

        if (!TryAmount(_payment!.Value, out var payment) || payment < 0)
            return Fail(_payment, "CLM0326E PAYMENT MUST BE A POSITIVE AMOUNT, E.G. 250.00");
        if (payment > 0 && status != "APPR")
            return Fail(_payment, "CLM0327E PAYMENTS REQUIRE STATUS APPR - APPROVE THE CLAIM FIRST");
        var remaining = Payable(c, p) - c.PaidAmount;
        if (payment > remaining)
            return Fail(_payment, $"CLM0328E PAYMENT EXCEEDS REMAINING PAYABLE OF {Money(Math.Max(0, remaining))}");

        var adjuster = _adjuster!.Value;
        var note = _note!.Value;

        // Closed and denied claims are frozen until reopened.
        var detailsChanged = type != c.ClaimType || reserve != c.ReserveAmount || adjuster != c.Adjuster
                             || _location!.Value != c.LossLocation || _desc1.Value != c.Description1
                             || _desc2!.Value != c.Description2 || payment > 0;
        if (c.Status is "CLSD" or "DENY" && status == c.Status && detailsChanged)
            return Fail(_status, $"CLM0330E CLAIM IS {c.Status} - SET STATUS TO OPEN TO REOPEN IT");

        // Payments erode the reserve; closing or denying a claim releases it.
        reserve = status is "CLSD" or "DENY" ? 0 : Math.Max(0, reserve - payment);

        if (!detailsChanged && status == c.Status && note.Length == 0)
            return Fail(_status, "CLM0301W NO CHANGES ENTERED");

        return new ClaimChanges(status, type, _location!.Value, _desc1.Value, _desc2!.Value, reserve, adjuster, payment, note);
    }

    private ClaimChanges? Fail(Field field, string message)
    {
        Error(field, message);
        return null;
    }

    private static decimal Payable(Claim c, Policy p) => Math.Min(p.CoverageLimit, Math.Max(0, c.ClaimedAmount - p.Deductible));

    private static Color StatusColor(string status) => status switch
    {
        "DENY" => Color.Red,
        "PEND" => Color.Yellow,
        "APPR" => Color.White,
        "CLSD" => Color.Blue,
        _ => Color.Green,
    };
}
