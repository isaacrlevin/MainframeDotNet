using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Tn3270;

namespace MainframeDotNet.Terminal.Os;

/// <summary>
/// Initial Program Load: an MVS master console bringing up JES2, VTAM, CICS and the claims database.
/// Any key speeds things up.
/// </summary>
public sealed class Ipl(Terminal3270 t, ClaimsRepository repo)
{
    private const int LogRows = 21;
    private readonly List<(string Text, Color Color)> _log = [];
    private bool _fast;

    public async Task<bool> RunAsync()
    {
        Log("IEA371I SYS1.IPLPARM ON DEVICE 0A82 SELECTED FOR IPL PARAMETERS", 300);
        Log("IEA246I LOAD   ID 00 SELECTED", 150);
        Log("IEA246I NUCLST ID 00 SELECTED", 150);
        Log("IEA101A SPECIFY SYSTEM PARAMETERS FOR RELEASE 03.8 .VS2", 400, Color.White);
        Reply("R 00,CLPA");
        Log("IEE252I MEMBER IEASYS00 FOUND IN SYS1.PARMLIB", 120);
        Log("IEA103I ALL PAGE DATA SETS ARE AVAILABLE", 250);
        Log("IEE140I SYSTEM CONSOLES", 60);
        Log("        CONSOLE/ALT COND AUTH  ID AREA     ROUTCD", 30);
        Log("        01F/010     M    ALL    01 Z,A      ALL", 30);
        Log("IEA107I VIO DATA SET(S) ON SYS1.STGINDEX", 200);
        Log("IEF165I // START JES2", 300);
        Log("$HASP426 SPECIFY OPTIONS - HASP-II, VERSION JES2 4.1", 400, Color.White);
        Reply("R 01,NOREQ");
        Log("$HASP493 JES2 COLD START IS IN PROGRESS", 400);
        Log("$HASP160 PRINTER1 INACTIVE - CLASS=A", 80);
        Log("$HASP160 PUNCH1   INACTIVE - CLASS=B", 80);
        Log("$HASP100 INIT     ON STCINRDR", 150);
        Log("IEF403I VTAM - STARTED - TIME=" + DateTime.Now.ToString("HH.mm.ss"), 300);
        Log("IST093I ISTPDCLU ACTIVE", 120);
        Log("IST093I LCL3274A ACTIVE", 120);
        Log("IST020I VTAM INITIALIZATION COMPLETE", 300);
        Log("IEF403I CICSPROD - STARTED - TIME=" + DateTime.Now.ToString("HH.mm.ss"), 300);
        Log("DFH1500 - CICS/VS STARTUP IN PROGRESS", 400);
        Log("DFH1501 - LOADING CICS NUCLEUS", 300);
        Log("DFH1503 - DFHSIP: RESOURCE DEFINITIONS LOADED FROM DFHCSD", 200);

        Log($"CLM0001I CONNECTING TO CLAIMS DATA BASE {repo.ServerDescription}", 200);
        if (!await ConnectAsync()) return false;

        Log("DFH1599 - CONTROL IS BEING GIVEN TO CICS", 400, Color.White);
        Log("DFH1700 - CICSPROD INITIALIZATION COMPLETE", 300, Color.White);
        Log("IST663I LOGON TO APPLID CICSPROD NOW PERMITTED", 600);
        return true;
    }

    private async Task<bool> ConnectAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var version = await repo.PingAsync();
                Log($"CLM0002I DATA BASE SERVER POSTGRESQL {version} ONLINE", 200);
                await repo.EnsureSchemaAsync();
                Log("CLM0003I DATA SETS POLICY_MASTER CLAIM_MASTER CLAIM_ACTIVITY OPENED", 200);
                if (await Seeder.SeedIfEmptyAsync(repo, msg => Log($"CLM0005I {msg}", 100)))
                    Log("CLM0006I INITIAL LOAD COMPLETE", 100);
                var (policies, claims) = await repo.CountsAsync();
                Log($"CLM0004I {policies} POLICIES AND {claims} CLAIMS ON FILE", 300);
                return true;
            }
            catch (Exception ex) when (ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
            {
                var reason = ex.Message.ToUpperInvariant().Replace('\n', ' ').Replace('\r', ' ');
                Log($"CLM0009E DATA BASE CONNECTION FAILED - {reason}", 0, Color.Red);
                if (attempt >= 10)
                {
                    Log("CLM0010E CLAIMS SUBSYSTEM CANNOT START - PRESS ANY KEY TO POWER OFF", 0, Color.Red);
                    t.Alarm();
                    Render();
                    t.WaitForKey(TimeSpan.FromHours(1));
                    return false;
                }
                Log($"CLM0011W RETRY {attempt} OF 10 IN 3 SECONDS", 0, Color.Yellow);
                await Task.Delay(3000);
            }
        }
    }

    private void Reply(string text)
    {
        // The operator types the reply on the command line, one character at a time.
        for (var i = 1; i <= text.Length; i++)
        {
            Render(text[..i]);
            Pause(70);
        }
        Pause(250);
        Log(text, 100, Color.White);
    }

    private void Log(string text, int delayMs, Color color = Color.Green)
    {
        _log.Add((text.Length > Terminal3270.Cols ? text[..Terminal3270.Cols] : text, color));
        Render();
        Pause(delayMs);
    }

    private void Render(string command = "")
    {
        t.Erase();
        var start = Math.Max(0, _log.Count - LogRows);
        for (var i = start; i < _log.Count; i++) t.Put(i - start, 0, _log[i].Text, _log[i].Color);
        t.Put(21, 0, new string('-', Terminal3270.Cols), Color.Blue);
        t.Put(22, 0, command, Color.White);
        t.Put(23, 0, "IEE163I MODE= RD", Color.Blue);
        t.Put(23, 54, $"CN=01  {DateTime.Now:HH.mm.ss}", Color.Blue);
        t.SetCursor(22, command.Length);
        t.Show();
    }

    private void Pause(int ms)
    {
        if (_fast || ms == 0) return;
        if (t.WaitForKey(TimeSpan.FromMilliseconds(ms))) _fast = true;
    }
}
