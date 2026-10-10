using System.Net;
using Ballhog;
using Ballhog.Caching;
using Ballhog.Http;
using Ballhog.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration entry points for wiring the Ballhog client into an application's service
/// container.
/// </summary>
/// <remarks>
/// This type lives in the <see cref="Microsoft.Extensions.DependencyInjection"/> namespace so
/// consumers pick it up with the same <c>using</c> directive they already use for container
/// registration.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ballhog client and the dependencies it requires.
    /// </summary>
    /// <param name="services">The service collection to add the Ballhog services to.</param>
    /// <param name="configure">
    /// An optional delegate used to configure <see cref="BallhogOptions"/>. When
    /// <see langword="null"/>, the default options are used.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, so registrations can be chained.</returns>
    /// <remarks>
    /// Registration wires the typed <see cref="IBallhogClient"/> over an HTTP-backed provider built
    /// on <c>IHttpClientFactory</c>, together with cache-aside storage and source pacing.
    /// </remarks>
    public static IServiceCollection AddBallhog(
        this IServiceCollection services,
        Action<BallhogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Options are built exactly once per container and then handed to the configure delegate,
        // so a consumer can override the documented defaults in place. TryAddSingleton keeps the
        // registration replaceable, matching standard MS.DI behavior.
        services.TryAddSingleton(sp =>
        {
            var options = new BallhogOptions();
            configure?.Invoke(options);
            return options;
        });

        // Time is a seam: the production graph uses the system clock, while tests can pre-register
        // or Replace a fake TimeProvider to drive cache expiry, pacing, and retry delays.
        services.TryAddSingleton(TimeProvider.System);

        // Per-container cache and pacing gate. Both are internal collaborators of the provider and
        // hold no process-global state.
        services.TryAddSingleton(sp => new BallhogCache(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<BallhogOptions>().TeamsCacheTtl));

        services.TryAddSingleton(sp => new SourcePacer(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<BallhogOptions>().SourcePacingInterval));

        // HTTP transport is always obtained from IHttpClientFactory, never a static HttpClient.
        //
        // Handler ordering (verified against Microsoft.Extensions.Http): the handler builder walks
        // the added handler list from last to first, assigning each as the InnerHandler of the one
        // before it, so the FIRST handler added ends up OUTERMOST. The registration order below
        // therefore yields the intended request path:
        //     HttpClient -> NbaRequestHeadersHandler -> HttpRetryHandler -> SocketsHttpHandler
        // Headers are applied once, outside the retry loop, and every retried attempt reuses the
        // same request that already carries them.
        services.AddHttpClient<StatsNbaProvider>((_, client) =>
            {
                client.BaseAddress = new Uri("https://stats.nba.com");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<BallhogOptions>();
                var handler = new SocketsHttpHandler();
                if (options.Http.Proxy is not null)
                {
                    handler.Proxy = new WebProxy(options.Http.Proxy);
                }

                return handler;
            })
            .AddHttpMessageHandler(() => new NbaRequestHeadersHandler())
            .AddHttpMessageHandler(sp => new HttpRetryHandler(
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<BallhogOptions>().Http));

        // Default provider: the HTTP-backed StatsNbaProvider wrapped in the cache-aside/pacing
        // decorator. TryAddSingleton keeps the seam swappable: a consumer may pre-register or
        // Replace(IStatsProvider) with an alternative, and this registration respects that.
        services.TryAddSingleton<IStatsProvider>(sp => new CachingStatsProvider(
            sp.GetRequiredService<StatsNbaProvider>(),
            sp.GetRequiredService<BallhogCache>(),
            sp.GetRequiredService<SourcePacer>(),
            sp.GetRequiredService<BallhogOptions>().TeamsCacheTtl));

        services.TryAddSingleton<IBallhogClient, BallhogClient>();

        return services;
    }
}
