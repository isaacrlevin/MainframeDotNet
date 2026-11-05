namespace MainframeDotNet.Terminal.Data;

public sealed record Policy(
    string PolicyNo,
    string HolderName,
    string Address,
    string City,
    string State,
    string Zip,
    string Phone,
    string LineOfBusiness,
    DateOnly EffectiveDate,
    DateOnly ExpiryDate,
    decimal CoverageLimit,
    decimal Deductible,
    decimal Premium,
    string Status,
    string Agent)
{
    public bool InForceOn(DateOnly date) => Status == "ACTV" && date >= EffectiveDate && date <= ExpiryDate;
}

public sealed record Claim(
    string ClaimNo,
    string PolicyNo,
    string Status,
    string ClaimType,
    DateOnly LossDate,
    DateOnly ReportedDate,
    string LossLocation,
    string Description1,
    string Description2,
    decimal ClaimedAmount,
    decimal ReserveAmount,
    decimal PaidAmount,
    string Adjuster,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt,
    int Version);

public sealed record ClaimRow(
    string ClaimNo,
    string PolicyNo,
    string HolderName,
    DateOnly LossDate,
    string ClaimType,
    string Status,
    decimal ClaimedAmount,
    decimal PaidAmount,
    string Adjuster);

public sealed record Activity(DateTime At, string UserId, string Action, string Detail);

public sealed record SummaryLine(string Key, int Count, decimal Claimed, decimal Reserve, decimal Paid);

public sealed record NewClaim(
    string PolicyNo,
    string ClaimType,
    DateOnly LossDate,
    string LossLocation,
    string Description1,
    string Description2,
    decimal ClaimedAmount,
    decimal ReserveAmount,
    string Adjuster);

public sealed record ClaimChanges(
    string Status,
    string ClaimType,
    string LossLocation,
    string Description1,
    string Description2,
    decimal ReserveAmount,
    string Adjuster,
    decimal Payment,
    string Note);

/// <summary>Code tables, the sort of thing that would live in a copybook.</summary>
public static class Codes
{
    public static readonly string[] Statuses = ["OPEN", "PEND", "APPR", "DENY", "CLSD"];

    public static readonly Dictionary<string, string> StatusNames = new()
    {
        ["OPEN"] = "OPEN - UNDER REVIEW",
        ["PEND"] = "PENDING INVESTIGATION",
        ["APPR"] = "APPROVED FOR PAYMENT",
        ["DENY"] = "DENIED",
        ["CLSD"] = "CLOSED",
    };

    public static readonly Dictionary<string, string> LinesOfBusiness = new()
    {
        ["AUTO"] = "PERSONAL AUTO",
        ["HOME"] = "HOMEOWNERS",
        ["LIFE"] = "ORDINARY LIFE",
        ["HLTH"] = "GROUP HEALTH",
    };

    public static readonly Dictionary<string, string[]> ClaimTypesByLine = new()
    {
        ["AUTO"] = ["COLL", "COMP", "LIAB", "GLAS"],
        ["HOME"] = ["FIRE", "WATR", "WIND", "THFT", "LIAB"],
        ["LIFE"] = ["DTH"],
        ["HLTH"] = ["MED", "HOSP"],
    };

    public static readonly Dictionary<string, string> ClaimTypeNames = new()
    {
        ["COLL"] = "COLLISION",
        ["COMP"] = "COMPREHENSIVE",
        ["LIAB"] = "LIABILITY",
        ["GLAS"] = "GLASS BREAKAGE",
        ["FIRE"] = "FIRE / SMOKE",
        ["WATR"] = "WATER DAMAGE",
        ["WIND"] = "WIND / HAIL",
        ["THFT"] = "THEFT / BURGLARY",
        ["DTH"] = "DEATH BENEFIT",
        ["MED"] = "MEDICAL EXPENSE",
        ["HOSP"] = "HOSPITALIZATION",
    };
}
