using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ballhog.Basketball.Providers;

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
    /// Gets the raw <c>resultSets</c> member as a <see cref="JsonElement"/>. It is kept
    /// untyped because the endpoint is inconsistent about the member's shape: the v3
    /// documented form is a single object, while some responses emit a v2-style array with a
    /// single element. The provider normalizes both shapes before mapping.
    /// </summary>
    [JsonPropertyName("resultSets")]
    public JsonElement? ResultSets { get; init; }
}
