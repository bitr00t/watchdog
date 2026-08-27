using System.Text.Json;

namespace Watchdog.Core;

/// <summary>
/// Navigates to a value in the JSON body and compares it against an expected string.
/// </summary>
/// <remarks>
/// The runtime configured counterpart to <see cref="JsonAssertion{T}"/>. A configuration
/// file cannot supply a type argument, so this variant walks the document with
/// <see cref="JsonDocument"/> instead of deserializing into a known shape. The trade is the
/// usual one: this works for anything a config file can express, while the generic variant
/// gets compile time checking and refactoring support in exchange for living in code.
///
/// The path is a slash separated sequence of property names, with integers addressing array
/// elements, so <c>/status/indicator</c> and <c>/components/0/name</c> both work.
/// </remarks>
public sealed class JsonValueAssertion : IBodyAssertion
{
    private readonly string[] _segments;
    private readonly string _path;
    private readonly string _expected;

    public JsonValueAssertion(string path, string expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expected);

        _path = path;
        _expected = expected;
        _segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (_segments.Length == 0)
        {
            throw new ArgumentException("The path must name at least one segment.", nameof(path));
        }
    }

    public string Description => $"{_path} equals '{_expected}'";

    public string? Validate(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            return $"Body is not valid JSON: {exception.Message}";
        }

        using (document)
        {
            var element = document.RootElement;

            foreach (var segment in _segments)
            {
                if (!TryDescend(element, segment, out element))
                {
                    return $"Body has no value at {_path}";
                }
            }

            var actual = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Null => "null",
                _ => element.ToString(),
            };

            return string.Equals(actual, _expected, StringComparison.Ordinal)
                ? null
                : $"{_path} is '{actual}', expected '{_expected}'";
        }
    }

    private static bool TryDescend(JsonElement current, string segment, out JsonElement next)
    {
        if (current.ValueKind == JsonValueKind.Array
            && int.TryParse(segment, out var index)
            && index >= 0
            && index < current.GetArrayLength())
        {
            next = current[index];
            return true;
        }

        if (current.ValueKind == JsonValueKind.Object)
        {
            return current.TryGetProperty(segment, out next);
        }

        next = default;
        return false;
    }
}
