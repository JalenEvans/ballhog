using Ballhog;
using Microsoft.Extensions.DependencyInjection;

// Wire the client exactly as a consumer would: register Ballhog with its defaults and
// resolve the typed client from the container.
using var services = new ServiceCollection().AddBallhog().BuildServiceProvider();
IBallhogClient client = services.GetRequiredService<IBallhogClient>();

// Ctrl+C cancels the in-flight request rather than tearing the process down abruptly.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

try
{
    IReadOnlyList<Team> teams = await client.GetTeamsAsync(cts.Token);

    foreach (Team team in teams)
    {
        Console.WriteLine($"{team.TeamId} {team.Abbreviation}");
    }

    Console.WriteLine($"Total teams: {teams.Count}");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("The request was canceled.");
    return 1;
}

return 0;
