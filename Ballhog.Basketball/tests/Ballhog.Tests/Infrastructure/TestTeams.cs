using Ballhog.Basketball;

namespace Ballhog.Tests.Infrastructure;

/// <summary>
/// A small, fixed team list shared by the caching tests. It is declared as an array so
/// that xUnit's sequence-equality overload is selected when compared against an
/// <see cref="IReadOnlyList{T}"/> result (rather than reference equality).
/// </summary>
internal static class TestTeams
{
    /// <summary>Three representative teams, in ascending franchise-id order.</summary>
    public static Team[] All { get; } =
    [
        new Team(1610612737, "ATL"),
        new Team(1610612738, "BOS"),
        new Team(1610612747, "LAL"),
    ];
}
