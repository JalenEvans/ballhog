using System.Text.Json.Serialization;

namespace Ballhog.Providers;

/// <summary>
/// The compile-time-generated <see cref="JsonSerializerContext"/> for every Ballhog
/// transport DTO.
/// </summary>
/// <remarks>
/// Deserialization goes through this context rather than the reflection-based
/// <c>JsonSerializer.Deserialize&lt;T&gt;(json)</c> overloads, so the library stays
/// trim- and AOT-safe. Add one <see cref="JsonSerializableAttribute"/> per root DTO.
/// </remarks>
[JsonSerializable(typeof(TeamYearsResponse))]
internal partial class BallhogJsonContext : JsonSerializerContext
{
}
