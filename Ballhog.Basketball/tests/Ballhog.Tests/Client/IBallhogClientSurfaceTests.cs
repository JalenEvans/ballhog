using System.Reflection;
using Ballhog.Basketball;
using Xunit;

namespace Ballhog.Tests.Client;

/// <summary>
/// Surface-shape tests for the public client abstraction <see cref="IBallhogClient"/>
/// (requirement R2, Jira BALLHOG-9, chunk C-003; test T-005). The typed surface is the
/// consumer-facing contract, so its shape is pinned by reflection rather than by a
/// behavioral call: the interface must be public and <c>GetTeamsAsync</c> must return
/// <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Team"/>
/// while accepting an optional <see cref="CancellationToken"/>.
/// </summary>
public sealed class IBallhogClientSurfaceTests
{
    [Fact]
    public void IBallhogClient_is_a_public_interface()
    {
        Type surface = typeof(IBallhogClient);

        Assert.True(surface.IsInterface, "IBallhogClient must be an interface so consumers can swap implementations.");
        Assert.True(surface.IsPublic, "IBallhogClient must be public so consumers can depend on it.");
    }

    [Fact]
    public void GetTeamsAsync_exposes_the_typed_task_and_a_cancellation_token()
    {
        MethodInfo? method = typeof(IBallhogClient).GetMethod(
            nameof(IBallhogClient.GetTeamsAsync),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IReadOnlyList<Team>>), method!.ReturnType);

        ParameterInfo cancellation = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(CancellationToken), cancellation.ParameterType);
        Assert.True(cancellation.IsOptional, "The cancellation token must be optional (defaulted).");
        Assert.True(cancellation.HasDefaultValue, "The cancellation token must carry a default value.");
    }
}
