namespace Ballhog;

/// <summary>
/// Identifies an NBA team by its stable franchise identifier and canonical three-letter code.
/// </summary>
/// <param name="TeamId">
/// The stable NBA franchise identifier, for example <c>1610612737</c> for the Atlanta Hawks.
/// </param>
/// <param name="Abbreviation">
/// The canonical three-letter team code, for example <c>ATL</c> for the Atlanta Hawks.
/// </param>
/// <remarks>
/// <see cref="TeamId"/> is the stable numeric franchise identifier published by the NBA data
/// source; it does not change when a franchise relocates or is renamed. <see cref="Abbreviation"/>
/// is the canonical three-letter code published by the league.
/// </remarks>
public sealed record Team(int TeamId, string Abbreviation);
