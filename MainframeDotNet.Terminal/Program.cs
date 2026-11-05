using MainframeDotNet.Terminal.Cics;
using MainframeDotNet.Terminal.Data;
using MainframeDotNet.Terminal.Os;
using MainframeDotNet.Terminal.Tn3270;
using Npgsql;

// Usage: MainframeDotNet.Terminal [--theme green|amber|color] [--connection "<npgsql connection string>"] [--keytrace]
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var connectionString = Arg("--connection")
                       ?? Environment.GetEnvironmentVariable("ConnectionStrings__claimsdb")
                       ?? "Host=localhost;Port=5432;Database=claimsdb;Username=postgres;Password=postgres";
var theme = Theme.FromName(Arg("--theme") ?? Environment.GetEnvironmentVariable("CIMS_THEME"));

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("CIMS requires an interactive console (a 3270 cannot be driven through a pipe).");
    return 1;
}

await using var dataSource = NpgsqlDataSource.Create(connectionString);
var repo = new ClaimsRepository(dataSource);

using var terminal = new Terminal3270(theme, $"T3278A{Random.Shared.Next(1, 99):00}")
{
    KeyTrace = args.Contains("--keytrace") || Environment.GetEnvironmentVariable("CIMS_KEYTRACE") == "1",
};
terminal.Open();
Console.Title = $"CIMS - {terminal.LuName}";

try
{
    if (!await new Ipl(terminal, repo).RunAsync()) return 2;

    var vtam = new VtamLogon(terminal);
    var session = new Session(terminal, repo);
    while (vtam.Run())
        await new CicsRegion(session).RunAsync();
}
catch (PowerOffException)
{
    // Operator switched the terminal off.
}

return 0;
