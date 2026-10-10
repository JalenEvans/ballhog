using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ballhog.Tests.Infrastructure;

/// <summary>
/// Resolves JSON fixtures that are copied next to the test assembly at build
/// time. Fully offline: fixtures are plain files under the project's
/// <c>Fixtures/</c> folder and never require a network or external service.
/// </summary>
public static class FixtureLoader
{
    /// <summary>The folder (relative to the test output directory) that holds fixtures.</summary>
    public const string FixturesDirectoryName = "Fixtures";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Returns the absolute path of a fixture by name, appending <c>.json</c> if needed.</summary>
    public static string GetPath(string fixtureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureName);

        return Path.Combine(
            AppContext.BaseDirectory,
            FixturesDirectoryName,
            fixtureName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? fixtureName
                : fixtureName + ".json");
    }

    /// <summary>
    /// Loads a fixture as a raw <see cref="JsonDocument"/>. Callers own the document
    /// and should dispose it. Throws <see cref="FileNotFoundException"/> when the
    /// fixture is not present in the output directory.
    /// </summary>
    public static JsonDocument LoadDocument(string fixtureName)
    {
        string path = GetPath(fixtureName);
        EnsureFixtureExists(path, fixtureName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Loads a fixture as a JSON object (<see cref="JsonObject"/>). Throws
    /// <see cref="FileNotFoundException"/> when the fixture is missing, or
    /// <see cref="InvalidDataException"/> when the fixture is not an object.
    /// </summary>
    public static JsonObject LoadObject(string fixtureName)
    {
        string path = GetPath(fixtureName);
        EnsureFixtureExists(path, fixtureName);

        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException($"Fixture '{fixtureName}' is not a JSON object.");
    }

    /// <summary>
    /// Loads a fixture and deserializes it into <typeparamref name="T"/>. Throws
    /// <see cref="FileNotFoundException"/> when the fixture is missing, or
    /// <see cref="InvalidDataException"/> when deserialization yields null.
    /// </summary>
    public static T Load<T>(string fixtureName)
    {
        string path = GetPath(fixtureName);
        EnsureFixtureExists(path, fixtureName);

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidDataException($"Fixture '{fixtureName}' deserialized to null.");
    }

    private static void EnsureFixtureExists(string path, string fixtureName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Fixture '{fixtureName}' was not found at '{path}'. " +
                $"Place a JSON file in the test project's '{FixturesDirectoryName}' folder (it is copied to the output directory).",
                path);
        }
    }
}