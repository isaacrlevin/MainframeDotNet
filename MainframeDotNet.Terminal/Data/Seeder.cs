namespace MainframeDotNet.Terminal.Data;

/// <summary>Loads a deterministic book of business the first time the database is used.</summary>
public static class Seeder
{
    private static readonly string[] FirstNames =
    [
        "JAMES", "MARY", "ROBERT", "PATRICIA", "JOHN", "LINDA", "MICHAEL", "BARBARA", "WILLIAM", "ELIZABETH",
        "DAVID", "JENNIFER", "RICHARD", "SUSAN", "CHARLES", "DOROTHY", "THOMAS", "MARGARET", "DONALD", "CAROL",
        "GEORGE", "SHIRLEY", "KENNETH", "JUDITH", "RAYMOND", "DEBORAH", "WALTER", "DIANE", "HAROLD", "BEVERLY",
    ];

    private static readonly string[] LastNames =
    [
        "SMITH", "JOHNSON", "WILLIAMS", "BROWN", "JONES", "MILLER", "DAVIS", "WILSON", "ANDERSON", "TAYLOR",
        "THOMAS", "MOORE", "MARTIN", "JACKSON", "THOMPSON", "WHITE", "HARRIS", "CLARK", "LEWIS", "ROBINSON",
        "WALKER", "HALL", "ALLEN", "YOUNG", "KING", "WRIGHT", "SCOTT", "GREEN", "BAKER", "NELSON", "KOWALSKI",
    ];

    private static readonly string[] Streets =
    [
        "ELM ST", "MAPLE AVE", "OAK DR", "MAIN ST", "WASHINGTON BLVD", "LAKE SHORE DR", "PARK AVE",
        "CEDAR LN", "HICKORY RD", "SPRUCE CT", "CHESTNUT ST", "RIVER RD", "BROADWAY", "MILL RD",
    ];

    private static readonly (string City, string State, string Zip)[] Cities =
    [
        ("HARTFORD", "CT", "06103"), ("POUGHKEEPSIE", "NY", "12601"), ("ARMONK", "NY", "10504"),
        ("ENDICOTT", "NY", "13760"), ("BOCA RATON", "FL", "33431"), ("ROCHESTER", "MN", "55901"),
        ("DAYTON", "OH", "45402"), ("OMAHA", "NE", "68102"), ("DES MOINES", "IA", "50309"),
        ("COLUMBUS", "OH", "43215"), ("BLOOMINGTON", "IL", "61701"), ("CHICAGO", "IL", "60601"),
        ("DENVER", "CO", "80202"), ("SAN JOSE", "CA", "95113"), ("AUSTIN", "TX", "78701"),
    ];

    private static readonly string[] Agents = ["AG1047", "AG2210", "AG3302", "AG4415", "AG5120", "AG6008"];
    private static readonly string[] Adjusters = ["JBAKER", "MKOWALSK", "TNGUYEN", "RPATEL", "SOCONNOR", "DLEVINE", "HMUELLER"];

    private static readonly Dictionary<string, (string Loc, string D1, string D2)[]> Stories = new()
    {
        ["COLL"] = [("I-84 EXIT 46 HARTFORD", "INSURED VEHICLE REAR-ENDED AT TRAFFIC SIGNAL", "DAMAGE TO REAR BUMPER AND TRUNK LID"),
                    ("SUPERMARKET PARKING LOT", "BACKED INTO LIGHT POLE WHILE EXITING SPACE", "TAILGATE AND TAIL LAMP REPLACEMENT")],
        ["COMP"] = [("RESIDENCE DRIVEWAY", "HAIL STORM CAUSED HOOD AND ROOF DENTING", "ESTIMATE OBTAINED FROM BODY SHOP"),
                    ("COUNTY ROUTE 9", "STRUCK DEER AT DUSK", "FRONT GRILLE AND RADIATOR DAMAGED")],
        ["LIAB"] = [("INSURED PREMISES", "VISITOR SLIPPED ON ICY FRONT STEPS", "CLAIMANT ALLEGES WRIST FRACTURE"),
                    ("MAIN ST AND 3RD AVE", "INSURED FAILED TO YIELD - THIRD PARTY VEHICLE", "THIRD PARTY REPAIR AND RENTAL")],
        ["GLAS"] = [("INTERSTATE 95", "STONE CHIP CRACKED WINDSHIELD", "REPLACEMENT BY APPROVED GLAZIER")],
        ["FIRE"] = [("KITCHEN", "GREASE FIRE ON RANGE SPREAD TO CABINETS", "SMOKE DAMAGE THROUGHOUT FIRST FLOOR"),
                    ("DETACHED GARAGE", "ELECTRICAL FIRE IN GARAGE WIRING", "STRUCTURE AND CONTENTS LOSS")],
        ["WATR"] = [("BASEMENT", "WATER HEATER RUPTURED OVERNIGHT", "CARPET DRYWALL AND FURNACE AFFECTED"),
                    ("UPSTAIRS BATHROOM", "SUPPLY LINE FAILURE UNDER VANITY", "CEILING COLLAPSE IN DINING ROOM")],
        ["WIND"] = [("ROOF", "SEVERE THUNDERSTORM REMOVED SHINGLES", "INTERIOR LEAK IN MASTER BEDROOM")],
        ["THFT"] = [("RESIDENCE", "FORCED ENTRY THROUGH REAR DOOR", "TELEVISION STEREO AND SILVERWARE TAKEN"),
                    ("DRIVEWAY", "VEHICLE BROKEN INTO OVERNIGHT", "CB RADIO AND 8-TRACK PLAYER STOLEN")],
        ["DTH"] = [("ST FRANCIS HOSPITAL", "DEATH OF INSURED - NATURAL CAUSES", "BENEFICIARY CLAIM FORM RECEIVED")],
        ["MED"] = [("PHYSICIAN OFFICE", "OUTPATIENT TREATMENT AND DIAGNOSTICS", "ITEMIZED BILL ATTACHED")],
        ["HOSP"] = [("MERCY GENERAL HOSPITAL", "INPATIENT ADMISSION 4 DAYS", "APPENDECTOMY - SURGEON AND FACILITY")],
    };

    public static async Task<bool> SeedIfEmptyAsync(ClaimsRepository repo, Action<string> progress)
    {
        var (policies, _) = await repo.CountsAsync();
        if (policies > 0) return false;

        var rnd = new Random(1976);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await using var conn = await repo.DataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        progress("LOADING POLICY_MASTER");
        var book = new List<Policy>();
        for (var i = 0; i < 80; i++)
        {
            var lob = rnd.Next(100) switch { < 45 => "AUTO", < 80 => "HOME", < 90 => "LIFE", _ => "HLTH" };
            var (city, state, zip) = Cities[rnd.Next(Cities.Length)];
            var effective = today.AddDays(-rnd.Next(30, 900));
            var term = lob is "LIFE" ? 20 : 1;
            var expiry = effective.AddYears(term).AddDays(-1);
            var (limit, deductible, premium) = lob switch
            {
                "AUTO" => (Pick(rnd, 25_000m, 50_000m, 100_000m), Pick(rnd, 100m, 250m, 500m), 300m + rnd.Next(0, 600)),
                "HOME" => (Pick(rnd, 60_000m, 85_000m, 120_000m, 250_000m), Pick(rnd, 250m, 500m, 1_000m), 180m + rnd.Next(0, 400)),
                "LIFE" => (Pick(rnd, 10_000m, 25_000m, 50_000m, 100_000m), 0m, 120m + rnd.Next(0, 500)),
                _ => (Pick(rnd, 50_000m, 250_000m), Pick(rnd, 100m, 200m), 400m + rnd.Next(0, 800)),
            };
            var status = expiry < today ? "EXPD" : rnd.Next(30) == 0 ? "CANC" : "ACTV";
            var p = new Policy(
                $"P{4_100_000 + i * 37:0000000}",
                $"{LastNames[rnd.Next(LastNames.Length)]}, {FirstNames[rnd.Next(FirstNames.Length)]} {(char)('A' + rnd.Next(26))}",
                $"{rnd.Next(10, 9999)} {Streets[rnd.Next(Streets.Length)]}",
                city, state, zip,
                $"({rnd.Next(201, 989)}) {rnd.Next(200, 999)}-{rnd.Next(1000, 9999)}",
                lob, effective, expiry, limit, deductible, premium, status, Agents[rnd.Next(Agents.Length)]);
            await repo.InsertPolicyAsync(p, conn, tx);
            book.Add(p);
        }

        progress("LOADING CLAIM_MASTER AND CLAIM_ACTIVITY");
        var claims = 0;
        foreach (var p in book)
        {
            var count = p.LineOfBusiness == "LIFE" ? rnd.Next(0, 2) : rnd.Next(0, 6);
            for (var j = 0; j < count; j++)
            {
                var lastDay = p.ExpiryDate < today ? p.ExpiryDate : today;
                var span = lastDay.DayNumber - p.EffectiveDate.DayNumber;
                if (span <= 0) continue;
                var loss = p.EffectiveDate.AddDays(rnd.Next(span));
                var reported = loss.AddDays(rnd.Next(0, 10));
                if (reported > today) reported = today;

                var types = Codes.ClaimTypesByLine[p.LineOfBusiness];
                var type = types[rnd.Next(types.Length)];
                var story = Stories[type][rnd.Next(Stories[type].Length)];
                var claimed = type switch
                {
                    "DTH" => p.CoverageLimit,
                    "GLAS" => rnd.Next(80, 400),
                    "HOSP" => rnd.Next(2_000, 15_000),
                    "FIRE" => rnd.Next(5_000, 60_000),
                    _ => rnd.Next(300, 12_000),
                } + rnd.Next(0, 100) / 100m;
                claimed = Math.Min(claimed, p.CoverageLimit);

                var age = today.DayNumber - reported.DayNumber;
                var status = age switch
                {
                    < 14 => rnd.Next(3) == 0 ? "PEND" : "OPEN",
                    < 60 => Pick(rnd, "OPEN", "PEND", "APPR", "APPR"),
                    _ => Pick(rnd, "APPR", "CLSD", "CLSD", "CLSD", "DENY"),
                };
                var payable = Math.Max(0, claimed - p.Deductible);
                var paid = status switch
                {
                    "CLSD" => payable,
                    "APPR" => Math.Round(payable * rnd.Next(0, 60) / 100m, 2),
                    _ => 0m,
                };
                var reserve = status is "CLSD" or "DENY" ? 0m : Math.Round(payable - paid, 2);
                var adjuster = status == "OPEN" && rnd.Next(3) == 0 ? "" : Adjusters[rnd.Next(Adjusters.Length)];

                var at = reported.ToDateTime(new TimeOnly(8 + rnd.Next(9), rnd.Next(60)), DateTimeKind.Local).ToUniversalTime();
                var claimNo = await repo.InsertClaimAsync(conn, tx,
                    new NewClaim(p.PolicyNo, type, loss, story.Loc, story.D1, story.D2, claimed, reserve, adjuster),
                    status, reported, paid, "CLMBATCH", at);
                claims++;

                await repo.AddActivityAsync(conn, tx, claimNo, "CLMBATCH", "FNOL", $"FIRST NOTICE OF LOSS RECORDED - CLAIMED {claimed:N2}", at);
                if (adjuster.Length > 0)
                    await repo.AddActivityAsync(conn, tx, claimNo, "CLMBATCH", "ASSIGN", $"ADJUSTER ASSIGNED (NONE) -> {adjuster}", at.AddHours(2));
                if (status is "APPR" or "CLSD")
                    await repo.AddActivityAsync(conn, tx, claimNo, adjuster, "STATUS", "STATUS CHANGED OPEN -> APPR", at.AddDays(rnd.Next(3, 20)));
                if (paid > 0)
                    await repo.AddActivityAsync(conn, tx, claimNo, adjuster, "PAYMENT", $"PAYMENT ISSUED {paid:N2} - CHECK NO {rnd.Next(100000, 999999)}", at.AddDays(rnd.Next(20, 40)));
                if (status == "DENY")
                    await repo.AddActivityAsync(conn, tx, claimNo, adjuster, "STATUS", "STATUS CHANGED PEND -> DENY", at.AddDays(rnd.Next(20, 45)));
                if (status == "CLSD")
                    await repo.AddActivityAsync(conn, tx, claimNo, adjuster, "STATUS", "STATUS CHANGED APPR -> CLSD", at.AddDays(rnd.Next(41, 60)));
            }
        }

        await tx.CommitAsync();
        progress($"{book.Count} POLICIES AND {claims} CLAIMS LOADED");
        return true;
    }

    private static T Pick<T>(Random rnd, params T[] values) => values[rnd.Next(values.Length)];
}
