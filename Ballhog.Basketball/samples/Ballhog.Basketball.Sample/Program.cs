using Ballhog.Basketball;
using Ballhog.Basketball.Http;
using Microsoft.Extensions.DependencyInjection;

// Wire the client exactly as a consumer would: register Ballhog with its defaults and
// resolve the typed client from the container.
using var services = new ServiceCollection().AddBallhogBasketball().BuildServiceProvider();
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
catch (BallhogHttpException exception)
{
    Console.Error.WriteLine($"The request failed: {exception.Message}");

    // Mirror the README's egress note: this source frequently rejects datacenter/VPN egress,
    // so a failure here is most often an environment problem, not a coding one.
    Console.Error.WriteLine(
        "stats.nba.com requires residential egress or a proxy. See the README's " +
        "'Egress requirement and proxy' section.");
    return 1;
}

return 0;
