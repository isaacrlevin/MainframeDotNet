using Npgsql;

namespace MainframeDotNet.Terminal.Data;

public enum UpdateResult { Updated, NotFound, Conflict }

/// <summary>Data access for the claims system: POLICY_MASTER, CLAIM_MASTER and CLAIM_ACTIVITY.</summary>
public sealed class ClaimsRepository(NpgsqlDataSource db)
{
    private const string ClaimColumns =
        """
        claim_no, policy_no, status, claim_type, loss_date, reported_date, loss_location,
        description1, description2, claimed_amount, reserve_amount, paid_amount, adjuster,
        created_by, created_at, updated_by, updated_at, version
        """;

    private const string PolicyColumns =
        """
        policy_no, holder_name, address, city, state, zip, phone, line_of_business, effective_date,
        expiry_date, coverage_limit, deductible, premium, status, agent
        """;

    public string ServerDescription
    {
        get
        {
            var b = new NpgsqlConnectionStringBuilder(db.ConnectionString);
            return $"{b.Host}:{b.Port}/{b.Database}".ToUpperInvariant();
        }
    }

    public async Task<string> PingAsync()
    {
        await using var cmd = db.CreateCommand("select split_part(version(), ' ', 2)");
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task EnsureSchemaAsync()
    {
        await using var cmd = db.CreateCommand(
            """
            create sequence if not exists claim_no_seq start 1000001;

            create table if not exists policy_master (
                policy_no        varchar(8)    primary key,
                holder_name      varchar(30)   not null,
                address          varchar(30)   not null,
                city             varchar(20)   not null,
                state            varchar(2)    not null,
                zip              varchar(10)   not null,
                phone            varchar(14)   not null,
                line_of_business varchar(4)    not null,
                effective_date   date          not null,
                expiry_date      date          not null,
                coverage_limit   numeric(13,2) not null,
                deductible       numeric(11,2) not null,
                premium          numeric(11,2) not null,
                status           varchar(4)    not null,
                agent            varchar(8)    not null
            );

            create table if not exists claim_master (
                claim_no       varchar(8)    primary key,
                policy_no      varchar(8)    not null references policy_master,
                status         varchar(4)    not null,
                claim_type     varchar(4)    not null,
                loss_date      date          not null,
                reported_date  date          not null,
                loss_location  varchar(40)   not null default '',
                description1   varchar(60)   not null default '',
                description2   varchar(60)   not null default '',
                claimed_amount numeric(13,2) not null default 0,
                reserve_amount numeric(13,2) not null default 0,
                paid_amount    numeric(13,2) not null default 0,
                adjuster       varchar(8)    not null default '',
                created_by     varchar(8)    not null,
                created_at     timestamptz   not null default now(),
                updated_by     varchar(8)    not null,
                updated_at     timestamptz   not null default now(),
                version        integer       not null default 1
            );
            create index if not exists ix_claim_policy on claim_master (policy_no);
            create index if not exists ix_claim_status on claim_master (status);

            create table if not exists claim_activity (
                activity_id bigserial   primary key,
                claim_no    varchar(8)  not null references claim_master,
                at          timestamptz not null default now(),
                user_id     varchar(8)  not null,
                action      varchar(8)  not null,
                detail      varchar(70) not null
            );
            create index if not exists ix_activity_claim on claim_activity (claim_no, at);
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Policies, int Claims)> CountsAsync()
    {
        await using var cmd = db.CreateCommand(
            "select (select count(*) from policy_master), (select count(*) from claim_master)");
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return ((int)r.GetInt64(0), (int)r.GetInt64(1));
    }

    // ---------------------------------------------------------------- policies

    public async Task<Policy?> GetPolicyAsync(string policyNo)
    {
        await using var cmd = db.CreateCommand($"select {PolicyColumns} from policy_master where policy_no = $1");
        cmd.Parameters.AddWithValue(policyNo);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadPolicy(r) : null;
    }

    public async Task InsertPolicyAsync(Policy p, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            $"insert into policy_master ({PolicyColumns}) values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)", conn, tx);
        foreach (var v in new object[]
                 {
                     p.PolicyNo, p.HolderName, p.Address, p.City, p.State, p.Zip, p.Phone, p.LineOfBusiness,
                     p.EffectiveDate, p.ExpiryDate, p.CoverageLimit, p.Deductible, p.Premium, p.Status, p.Agent,
                 })
            cmd.Parameters.AddWithValue(v);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---------------------------------------------------------------- claims

    public async Task<Claim?> GetClaimAsync(string claimNo)
    {
        await using var cmd = db.CreateCommand($"select {ClaimColumns} from claim_master where claim_no = $1");
        cmd.Parameters.AddWithValue(claimNo);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadClaim(r) : null;
    }

    /// <summary>The equivalent of a VSAM READNEXT / READPREV from the given key.</summary>
    public async Task<string?> GetAdjacentClaimNoAsync(string claimNo, bool forward)
    {
        var sql = forward
            ? "select claim_no from claim_master where claim_no > $1 order by claim_no limit 1"
            : "select claim_no from claim_master where claim_no < $1 order by claim_no desc limit 1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(claimNo);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task<(IReadOnlyList<ClaimRow> Rows, int Total)> BrowseAsync(
        string? status, string? policyNo, string? claimType, string? namePrefix, int offset, int limit)
    {
        const string where =
            """
            where ($1 = '' or c.status = $1)
              and ($2 = '' or c.policy_no = $2)
              and ($3 = '' or c.claim_type = $3)
              and ($4 = '' or p.holder_name like $4 || '%')
            """;
        object[] args = [status ?? "", policyNo ?? "", claimType ?? "", namePrefix ?? ""];

        await using var count = db.CreateCommand(
            $"select count(*) from claim_master c join policy_master p on p.policy_no = c.policy_no {where}");
        foreach (var a in args) count.Parameters.AddWithValue(a);
        var total = (int)(long)(await count.ExecuteScalarAsync())!;

        await using var cmd = db.CreateCommand(
            $"""
             select c.claim_no, c.policy_no, p.holder_name, c.loss_date, c.claim_type, c.status,
                    c.claimed_amount, c.paid_amount, c.adjuster
             from claim_master c join policy_master p on p.policy_no = c.policy_no
             {where}
             order by c.claim_no
             offset $5 limit $6
             """);
        foreach (var a in args) cmd.Parameters.AddWithValue(a);
        cmd.Parameters.AddWithValue(offset);
        cmd.Parameters.AddWithValue(limit);

        var rows = new List<ClaimRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new ClaimRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetFieldValue<DateOnly>(3),
                r.GetString(4), r.GetString(5), r.GetDecimal(6), r.GetDecimal(7), r.GetString(8)));
        return (rows, total);
    }

    public async Task<string> AddClaimAsync(NewClaim c, string userId)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var claimNo = await InsertClaimAsync(conn, tx, c, "OPEN", DateOnly.FromDateTime(DateTime.Today), 0m, userId, DateTime.UtcNow);
        await AddActivityAsync(conn, tx, claimNo, userId, "FNOL", $"FIRST NOTICE OF LOSS RECORDED - CLAIMED {c.ClaimedAmount:N2}", DateTime.UtcNow);
        if (c.ReserveAmount > 0)
            await AddActivityAsync(conn, tx, claimNo, userId, "RESERVE", $"INITIAL RESERVE SET TO {c.ReserveAmount:N2}", DateTime.UtcNow);
        await tx.CommitAsync();
        return claimNo;
    }

    public async Task<string> InsertClaimAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, NewClaim c, string status, DateOnly reported,
        decimal paid, string userId, DateTime at)
    {
        await using var cmd = new NpgsqlCommand(
            """
            insert into claim_master (claim_no, policy_no, status, claim_type, loss_date, reported_date, loss_location,
                                      description1, description2, claimed_amount, reserve_amount, paid_amount,
                                      adjuster, created_by, created_at, updated_by, updated_at)
            values ('C' || nextval('claim_no_seq'), $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $13, $14)
            returning claim_no
            """, conn, tx);
        foreach (var v in new object[]
                 {
                     c.PolicyNo, status, c.ClaimType, c.LossDate, reported, c.LossLocation, c.Description1,
                     c.Description2, c.ClaimedAmount, c.ReserveAmount, paid, c.Adjuster, userId, at,
                 })
            cmd.Parameters.AddWithValue(v);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Workload balancing: the adjuster currently carrying the fewest open claims.</summary>
    public async Task<string> PickAdjusterAsync()
    {
        await using var cmd = db.CreateCommand(
            """
            select adjuster from claim_master where adjuster <> ''
            group by adjuster
            order by count(*) filter (where status in ('OPEN', 'PEND', 'APPR')), adjuster
            limit 1
            """);
        return (string?)await cmd.ExecuteScalarAsync() ?? "";
    }

    public async Task<UpdateResult> UpdateClaimAsync(string claimNo, int expectedVersion, ClaimChanges ch, string userId)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        Claim? current;
        await using (var sel = new NpgsqlCommand($"select {ClaimColumns} from claim_master where claim_no = $1 for update", conn, tx))
        {
            sel.Parameters.AddWithValue(claimNo);
            await using var r = await sel.ExecuteReaderAsync();
            current = await r.ReadAsync() ? ReadClaim(r) : null;
        }
        if (current is null) return UpdateResult.NotFound;
        if (current.Version != expectedVersion) return UpdateResult.Conflict;

        var now = DateTime.UtcNow;
        var log = new List<(string Action, string Detail)>();
        if (ch.Status != current.Status) log.Add(("STATUS", $"STATUS CHANGED {current.Status} -> {ch.Status}"));
        if (ch.ClaimType != current.ClaimType) log.Add(("RECODE", $"CLAIM TYPE CHANGED {current.ClaimType} -> {ch.ClaimType}"));
        if (ch.ReserveAmount != current.ReserveAmount)
            log.Add(("RESERVE", $"RESERVE CHANGED {current.ReserveAmount:N2} -> {ch.ReserveAmount:N2}"));
        if (ch.Adjuster != current.Adjuster) log.Add(("ASSIGN", $"ADJUSTER ASSIGNED {Blank(current.Adjuster)} -> {Blank(ch.Adjuster)}"));
        if (ch.LossLocation != current.LossLocation || ch.Description1 != current.Description1 || ch.Description2 != current.Description2)
            log.Add(("AMEND", "LOSS DETAILS AMENDED"));
        if (ch.Payment != 0) log.Add(("PAYMENT", $"PAYMENT ISSUED {ch.Payment:N2} - CHECK NO {Random.Shared.Next(100000, 999999)}"));
        if (ch.Note.Length > 0) log.Add(("NOTE", ch.Note));

        await using (var upd = new NpgsqlCommand(
                         """
                         update claim_master
                            set status = $2, claim_type = $3, loss_location = $4, description1 = $5, description2 = $6,
                                reserve_amount = $7, adjuster = $8, paid_amount = paid_amount + $9,
                                updated_by = $10, updated_at = $11, version = version + 1
                          where claim_no = $1
                         """, conn, tx))
        {
            foreach (var v in new object[]
                     {
                         claimNo, ch.Status, ch.ClaimType, ch.LossLocation, ch.Description1, ch.Description2,
                         ch.ReserveAmount, ch.Adjuster, ch.Payment, userId, now,
                     })
                upd.Parameters.AddWithValue(v);
            await upd.ExecuteNonQueryAsync();
        }

        foreach (var (action, detail) in log)
            await AddActivityAsync(conn, tx, claimNo, userId, action, detail, now);

        await tx.CommitAsync();
        return UpdateResult.Updated;

        static string Blank(string s) => s.Length == 0 ? "(NONE)" : s;
    }

    // ---------------------------------------------------------------- activity

    public async Task AddActivityAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string claimNo, string userId,
        string action, string detail, DateTime at)
    {
        await using var cmd = new NpgsqlCommand(
            "insert into claim_activity (claim_no, at, user_id, action, detail) values ($1, $2, $3, $4, $5)", conn, tx);
        cmd.Parameters.AddWithValue(claimNo);
        cmd.Parameters.AddWithValue(at);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(action);
        cmd.Parameters.AddWithValue(detail.Length > 70 ? detail[..70] : detail);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(IReadOnlyList<Activity> Rows, int Total)> GetActivityAsync(string claimNo, int offset, int limit)
    {
        await using var count = db.CreateCommand("select count(*) from claim_activity where claim_no = $1");
        count.Parameters.AddWithValue(claimNo);
        var total = (int)(long)(await count.ExecuteScalarAsync())!;

        await using var cmd = db.CreateCommand(
            """
            select at, user_id, action, detail from claim_activity
            where claim_no = $1 order by at desc, activity_id desc offset $2 limit $3
            """);
        cmd.Parameters.AddWithValue(claimNo);
        cmd.Parameters.AddWithValue(offset);
        cmd.Parameters.AddWithValue(limit);
        var rows = new List<Activity>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new Activity(r.GetDateTime(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return (rows, total);
    }

    // ---------------------------------------------------------------- reporting

    public async Task<IReadOnlyList<SummaryLine>> SummaryAsync(string groupBy)
    {
        var column = groupBy switch
        {
            "STATUS" => "c.status",
            "TYPE" => "c.claim_type",
            "LINE" => "p.line_of_business",
            "ADJUSTER" => "case when c.adjuster = '' then '(NONE)' else c.adjuster end",
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy)),
        };
        await using var cmd = db.CreateCommand(
            $"""
             select {column}, count(*), sum(c.claimed_amount), sum(c.reserve_amount), sum(c.paid_amount)
             from claim_master c join policy_master p on p.policy_no = c.policy_no
             group by 1 order by 1
             """);
        var rows = new List<SummaryLine>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new SummaryLine(r.GetString(0), (int)r.GetInt64(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4)));
        return rows;
    }

    public async Task<IReadOnlyList<ClaimRow>> GetPolicyClaimsAsync(string policyNo) =>
        (await BrowseAsync(null, policyNo, null, null, 0, 100)).Rows;

    public NpgsqlDataSource DataSource => db;

    // ---------------------------------------------------------------- mapping

    private static Policy ReadPolicy(NpgsqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
        r.GetString(6), r.GetString(7), r.GetFieldValue<DateOnly>(8), r.GetFieldValue<DateOnly>(9),
        r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.GetString(13), r.GetString(14));

    private static Claim ReadClaim(NpgsqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateOnly>(4),
        r.GetFieldValue<DateOnly>(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetDecimal(9),
        r.GetDecimal(10), r.GetDecimal(11), r.GetString(12), r.GetString(13), r.GetDateTime(14),
        r.GetString(15), r.GetDateTime(16), r.GetInt32(17));
}
