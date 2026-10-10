using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ballhog.Providers;

/// <summary>
/// The root of a Stats NBA v3 <c>commonteamyears</c> response body.
/// </summary>
/// <remarks>
/// This is an internal transport DTO. It exists only so the provider can deserialize the
/// raw payload with the source-generated <see cref="BallhogJsonContext"/>; no DTO type is
/// ever exposed through the public contract.
/// </remarks>
internal sealed class TeamYearsResponse
{
    /// <summary>
    /// Gets the single v3 result set carried by the response. The JSON property is the
    /// camel-cased <c>resultSets</c> and, for this endpoint, is an object rather than an array.
    /// </summary>
    [JsonPropertyName("resultSets")]
    public TeamYearsResultSets? ResultSets { get; init; }
}

/// <summary>
/// A Stats NBA v3 result set: its column names and its heterogeneous row data.
/// </summary>
internal sealed class TeamYearsResultSets
{
    /// <summary>
    /// Gets the ordered column names. Used to locate <c>ABBREVIATION</c>, <c>TEAM_ID</c>,
    /// and <c>MAX_YEAR</c> by name so mapping does not depend on a fixed column order.
    /// </summary>
    [JsonPropertyName("headers")]
    public string[]? Headers { get; init; }

    /// <summary>
    /// Gets the rows. Each row is an array of <see cref="JsonElement"/> values because a
    /// single row mixes kinds (strings and, for <c>TEAM_ID</c>, either a JSON number or a
    /// numeric string). Deserializing per cell as <see cref="JsonElement"/> preserves each
    /// cell's original kind so the provider can read it robustly without narrowing every
    /// value to a single CLR type.
    /// </summary>
    [JsonPropertyName("rowSet")]
    public JsonElement[][]? RowSet { get; init; }
}
