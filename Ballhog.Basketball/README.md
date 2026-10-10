# BallhogBasketball

**Typed async .NET client for NBA statistics.**

`BallhogBasketball` is a small, DI-friendly .NET library (part of the ballhog umbrella of sport-statistics SDKs) that queries NBA.com's stats endpoints through a typed client with built-in caching, polite source pacing, and resilient retries. Data via NBA.com/stats; not affiliated with or endorsed by the NBA.

## Install

```bash
dotnet add package BallhogBasketball
```

## Quickstart

Register the client with dependency injection, resolve `IBallhogClient`, and call the API:

```csharp
using Ballhog.Basketball;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection().AddBallhogBasketball().BuildServiceProvider();
IBallhogClient client = services.GetRequiredService<IBallhogClient>();

IReadOnlyList<Team> teams = await client.GetTeamsAsync();

foreach (Team team in teams)
{
    Console.WriteLine($"{team.TeamId} {team.Abbreviation}");
}

Console.WriteLine($"Total teams: {teams.Count}");
```

`AddBallhogBasketball()` accepts an optional `Action<BallhogOptions>` for configuration. The current release (S1) exposes the league-wide team set via `GetTeamsAsync`; more query methods are added slice by slice.

## Public behavior

- **In-process TTL cache with single-flight.** The team set is cached in-process (default TTL: 12 hours, `BallhogOptions.TeamsCacheTtl`). Cache hits return immediately with no source I/O; a miss re-fetches after expiry. Concurrent misses for the same data are coalesced (single-flight), so one source request serves all waiting callers.
- **Shared source pacing.** Consecutive outbound source requests are separated by a minimum interval (default: ~1 second, `BallhogOptions.SourcePacingInterval`) across the whole client graph. Pacing applies only on cache misses.
- **Retry/backoff honoring `Retry-After`.** Transient failures are retried with exponential backoff (default: 3 attempts, 500 ms base, 10 s cap — `BallhogOptions.Http`). Each attempt is bounded by a per-attempt `RequestTimeout` (default: 30 s); a timed-out attempt is retried like any other transient failure, and once every attempt is exhausted the request surfaces as `BallhogHttpException`. On HTTP 429, the server's `Retry-After` header is honored when present. HTTP 403 is not retried (it indicates blocked egress, not a transient fault).
- **2026 NBA request headers.** Every request carries a Chrome-like `User-Agent`, `Referer: https://www.nba.com/`, and `Accept-Encoding: gzip, deflate, br`.
- **`IStatsProvider` swappable seam.** `IStatsProvider` is the raw source; the default HTTP-backed NBA provider is registered with `TryAddTransient`, so you can pre-register or `Replace()` an alternative `IStatsProvider` (a fake in tests, or a different backing source). The client factory wraps whatever source is registered in the SDK's cache-aside/pacing decorator, so swapping the source keeps the TTL cache, single-flight, and shared pacing intact rather than displacing them.
- **Typed HTTP via `IHttpClientFactory`.** Transport comes exclusively from `IHttpClientFactory` — no static `HttpClient` anywhere.

## Egress requirement and proxy

**stats.nba.com actively drops datacenter and VPN egress.** If your application runs in a cloud VM, CI runner, container, or on a VPN, requests will fail (typically with HTTP 403) no matter how the client is configured. You need either residential egress or a proxy in front of the source.

If you have a proxy, route Ballhog through it:

```csharp
using var services = new ServiceCollection()
    .AddBallhogBasketball(o => o.Http.Proxy = new Uri("http://host:port"))
    .BuildServiceProvider();
```

## Attribution

Data via NBA.com/stats; not affiliated with or endorsed by the NBA.

## License

MIT — see [LICENSE](LICENSE).
