var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgWeb();

var claimsDb = postgres.AddDatabase("claimsdb");

// The 3270 emulator needs a real interactive console. WithTerminal gives it a PTY that can be
// driven from the dashboard's terminal page (or `aspire terminal`). 84 x 28 fits the 24 x 80
// screen, the OIA row and the bezel.
builder.AddProject<Projects.MainframeDotNet_Terminal>("terminal")
    .WithReference(claimsDb)
    .WaitFor(claimsDb)
    // Temporary: shows each keystroke in the status line to diagnose PF key mappings in the browser.
    .WithEnvironment("CIMS_KEYTRACE", "1")
    .WithTerminal(options =>
    {
        options.Columns = 84;
        options.Rows = 28;
    });

builder.Build().Run();
