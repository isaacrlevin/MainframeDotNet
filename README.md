# CIMS - Claims Information Management System

A 1970s IBM mainframe experience for managing insurance claims: an IBM 3278 terminal
emulation (24x80, green phosphor, block-mode fields, PF keys and an Operator Information Area)
running a CICS/VS-style claims application backed by PostgreSQL.

## Running

**With Aspire (recommended):**

```
dotnet run --project MainframeDotNet.AppHost
```

This starts PostgreSQL (persistent volume, plus PgWeb for browsing the tables). When the database
is healthy, it starts the `terminal` resource with an interactive terminal attached. Open it from
the dashboard's terminal page, or run `aspire terminal`. Restart the `terminal` resource from the
dashboard to start a fresh session.

**Standalone** (any OS, against your own PostgreSQL):

```
dotnet run --project MainframeDotNet.Terminal -- --connection "Host=localhost;Port=5432;Database=claimsdb;Username=postgres;Password=postgres"
```

The schema and sample data (80 policies, about 180 claims) are created on first start.
Options: `--theme green|amber|color` (or the `CIMS_THEME` environment variable). Use a console of
at least 84x28 for the full bezel.

## A session

1. **IPL**: MVS 3.8 brings up JES2, VTAM, CICS and connects to the claims database. Press any key to speed it up.
2. **VTAM logon**: press ENTER to accept `LOGON APPLID(CICSPROD)`.
3. **CICS sign-on**: any userid and password are accepted.
4. **Transaction prompt**: type `CLMS` and press ENTER. `CESF` signs off.
5. **Claims menu**: inquiry, first notice of loss, maintenance (status, reserves, payments, notes),
   browse with line commands (S/U/A/P), policy inquiry, and the summary report.

## Keys

| 3270        | PC              |
|-------------|-----------------|
| ENTER       | Enter           |
| PF1-PF24    | F1-F12, Shift+F1-F12 |
| PF1-PF12    | Alt+1 … Alt+9, Alt+0 (PF10), Alt+- (PF11), Alt+= (PF12) |
| PF1-PF12 (any browser) | Esc, then 1 … 0, - or = (Esc then 3 = PF3) |
| PF7 / PF8   | PgUp / PgDn     |
| CLEAR       | Ctrl+L          |
| RESET       | Esc             |
| ERASE EOF   | Ctrl+K, Ctrl+End |
| INSERT      | Ins             |
| Power off   | Ctrl+Q          |

Browsers grab several function keys before the terminal sees them (F5 reloads the page, F11 goes
full screen, F3 can open Find), and some don't pass Alt combinations either. In the dashboard's
web terminal press Esc and then the number: Esc, 3 to go back; Esc, 5 to apply an update. `PF`
lights up in the status line between the two keystrokes. When the keyboard is locked (`X -f`,
`X NUM`), Esc only resets it. If a key doesn't seem to register, start the terminal
with `--keytrace` (or `CIMS_KEYTRACE=1`): the status line then shows each key as the app receives it.

Fields behave like a real 3270: overtype by default, auto-skip to the next field when one fills,
and an input-inhibited keyboard (`X -f`, `X NUM`) when you type into a protected area or put a
letter in a numeric field. Press Esc to reset.

## Layout

- `MainframeDotNet.Terminal/Tn3270` - the display-station emulation and themes
- `MainframeDotNet.Terminal/Os` - IPL console, VTAM logon screen, CICS region (sign-on / transaction prompt)
- `MainframeDotNet.Terminal/Cics` - the CLMS transactions (one class per BMS map)
- `MainframeDotNet.Terminal/Data` - `POLICY_MASTER`, `CLAIM_MASTER`, `CLAIM_ACTIVITY` via Npgsql
