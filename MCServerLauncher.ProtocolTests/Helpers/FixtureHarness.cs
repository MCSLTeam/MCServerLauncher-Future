using System.Text.Json;

namespace MCServerLauncher.ProtocolTests.Helpers;

/// <summary>
/// Reusable fixture harness for loading JSON fixture files and asserting canonical/structural JSON equality.
/// Uses STJ JsonElement.DeepEquals for stable structural comparison.
/// </summary>
public static class FixtureHarness
{
    /// <summary>
    /// Loads a JSON fixture file and returns it as a JsonElement for structural comparison.
    /// </summary>
    /// <param name="fixturePath">Full path to the JSON fixture file.</param>
    /// <returns>The root JsonElement of the loaded fixture.</returns>
    /// <exception cref="FileNotFoundException">Thrown when fixture file does not exist.</exception>
    /// <exception cref="JsonException">Thrown when fixture file contains invalid JSON.</exception>
    public static JsonElement LoadFixture(string fixturePath)
    {
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException($"Fixture file not found: {fixturePath}", fixturePath);
        }

        var jsonContent = File.ReadAllText(fixturePath);
        using var doc = JsonDocument.Parse(jsonContent);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Loads a JSON fixture file from a directory path and filename.
    /// </summary>
    /// <param name="directory">Directory containing fixture files.</param>
    /// <param name="fileName">Name of the fixture file.</param>
    /// <returns>The root JsonElement of the loaded fixture.</returns>
    public static JsonElement LoadFixture(string directory, string fileName)
    {
        return LoadFixture(Path.Combine(directory, fileName));
    }

    /// <summary>
    /// Compares two JSON elements for structural equality using JsonElement.DeepEquals.
    /// </summary>
    /// <param name="expected">The expected JSON element (typically from a golden fixture).</param>
    /// <param name="actual">The actual JSON element (typically from serialization output).</param>
    /// <returns>True if the elements are structurally equal, false otherwise.</returns>
    public static bool StructuralEquals(JsonElement expected, JsonElement actual)
    {
        return JsonElement.DeepEquals(expected, actual);
    }

    /// <summary>
    /// Compares two JSON strings for structural equality after parsing.
    /// </summary>
    /// <param name="expectedJson">The expected JSON string.</param>
    /// <param name="actualJson">The actual JSON string.</param>
    /// <returns>True if the parsed elements are structurally equal, false otherwise.</returns>
    public static bool StructuralEquals(string expectedJson, string actualJson)
    {
        using var expectedDoc = JsonDocument.Parse(expectedJson);
        using var actualDoc = JsonDocument.Parse(actualJson);
        return JsonElement.DeepEquals(expectedDoc.RootElement, actualDoc.RootElement);
    }

    /// <summary>
    /// Serializes an object to a canonical JSON string for comparison.
    /// Uses minimal formatting to ensure deterministic output.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>A canonical JSON string.</returns>
    public static string SerializeCanonical(object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        });
    }

    /// <summary>
    /// Parses a JSON string and returns the root element for structural comparison.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>The root JsonElement.</returns>
    public static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Asserts that two JSON elements are structurally equal, throwing with a descriptive message if not.
    /// </summary>
    public static void AssertStructuralEquals(JsonElement expected, JsonElement actual, string message = "")
    {
        if (!JsonElement.DeepEquals(expected, actual))
        {
            var expectedJson = expected.GetRawText();
            var actualJson = actual.GetRawText();
            throw new AssertEqualityException(expectedJson, actualJson, message);
        }
    }
}

/// <summary>
/// Exception thrown when JSON structural equality assertion fails.
/// </summary>
public sealed class AssertEqualityException : Exception
{
    public string ExpectedJson { get; }
    public string ActualJson { get; }

    public AssertEqualityException(string expectedJson, string actualJson, string message = "")
        : base(BuildMessage(expectedJson, actualJson, message))
    {
        ExpectedJson = expectedJson;
        ActualJson = actualJson;
    }

    private static string BuildMessage(string expected, string actual, string message)
    {
        var msg = "JSON structural equality assertion failed.\n";
        if (!string.IsNullOrEmpty(message))
        {
            msg += $"Message: {message}\n";
        }
        msg += $"Expected: {expected}\nActual: {actual}";
        return msg;
    }
}