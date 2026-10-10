# ballhog

**Typed async .NET client for NBA statistics.**

`ballhog` is a small, DI-friendly library that queries NBA.com's stats endpoints through a typed client with built-in caching, polite source pacing, and resilient retries. Data via NBA.com/stats; not affiliated with or endorsed by the NBA.

## Install

```bash
dotnet add package ballhog
```

## Quickstart

Register the client with dependency injection, resolve `IBallhogClient`, and call the API:

```csharp
using Ballhog;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection().AddBallhog().BuildServiceProvider();
IBallhogClient client = services.GetRequiredService<IBallhogClient>();

IReadOnlyList<Team> teams = await client.GetTeamsAsync();

foreach (Team team in teams)
{
    Console.WriteLine($"{team.TeamId} {team.Abbreviation}");
}

Console.WriteLine($"Total teams: {teams.Count}");
```

`AddBallhog()` accepts an optional `Action<BallhogOptions>` for configuration. The current release (S1) exposes the league-wide team set via `GetTeamsAsync`; more query methods are added slice by slice.

## Public behavior

- **In-process TTL cache with single-flight.** The team set is cached in-process (default TTL: 12 hours, `BallhogOptions.TeamsCacheTtl`). Cache hits return immediately with no source I/O; a miss re-fetches after expiry. Concurrent misses for the same data are coalesced (single-flight), so one source request serves all waiting callers.
- **Shared source pacing.** Consecutive outbound source requests are separated by a minimum interval (default: ~1 second, `BallhogOptions.SourcePacingInterval`) across the whole client graph. Pacing applies only on cache misses.
- **Retry/backoff honoring `Retry-After`.** Transient failures are retried with exponential backoff (default: 3 attempts, 500 ms base, 10 s cap — `BallhogOptions.Http`). On HTTP 429, the server's `Retry-After` header is honored when present. HTTP 403 is not retried (it indicates blocked egress, not a transient fault).
- **2026 NBA request headers.** Every request carries a Chrome-like `User-Agent`, `Referer: https://www.nba.com/`, and `Accept-Encoding: gzip, deflate, br`.
- **`IStatsProvider` swappable seam.** The provider behind `IBallhogClient` is registered with `TryAddSingleton`, so you can pre-register or `Replace()` an alternative `IStatsProvider` (a fake in tests, or a different backing source) without touching transport or caching.
- **Typed HTTP via `IHttpClientFactory`.** Transport comes exclusively from `IHttpClientFactory` — no static `HttpClient` anywhere.

## Egress requirement and proxy

**stats.nba.com actively drops datacenter and VPN egress.** If your application runs in a cloud VM, CI runner, container, or on a VPN, requests will fail (typically with HTTP 403) no matter how the client is configured. You need either residential egress or a proxy in front of the source.

If you have a proxy, route Ballhog through it:

```csharp
using var services = new ServiceCollection()
    .AddBallhog(o => o.Http.Proxy = new Uri("http://host:port"))
    .BuildServiceProvider();
```

## Attribution

Data via NBA.com/stats; not affiliated with or endorsed by the NBA.

## License

MIT — see [LICENSE](LICENSE).
