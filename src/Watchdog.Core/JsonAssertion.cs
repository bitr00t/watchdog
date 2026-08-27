using System.Text.Json;

namespace Watchdog.Core;

/// <summary>
/// Deserializes the response body into <typeparamref name="T"/> and applies a predicate.
/// </summary>
/// <remarks>
/// <c>JsonSerializer.Deserialize&lt;T&gt;</c> works without a class token because the
/// runtime knows the type argument. The constraint <c>where T : notnull</c> keeps
/// <c>T</c> away from nullable value types, which would make the null check below
/// ambiguous.
/// </remarks>
public sealed class JsonAssertion<T> : IBodyAssertion
    where T : notnull
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Func<T, bool> _predicate;

    public JsonAssertion(Func<T, bool> predicate, string description)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        _predicate = predicate;
        Description = description;
    }

    public string Description { get; }

    public string? Validate(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Body is empty, expected {SchemaCache<T>.TypeName}";
        }

        T? value;

        try
        {
            value = JsonSerializer.Deserialize<T>(body, Options);
        }
        catch (JsonException exception)
        {
            // The property list comes from the per type cache, so it is built once per
            // closed generic type rather than on every failed check.
            return $"Body is not valid {SchemaCache<T>.TypeName} "
                + $"({SchemaCache<T>.PropertySummary}): {exception.Message}";
        }

        if (value is null)
        {
            return $"Body deserialized to null for {SchemaCache<T>.TypeName}";
        }

        return _predicate(value) ? null : $"Body did not satisfy '{Description}'";
    }
}

/// <summary>
/// Factory helpers for assertions.
/// </summary>
public static class BodyAssertion
{
    /// <summary>
    /// Asserts a predicate over the body deserialized as <typeparamref name="T"/>.
    /// </summary>
    public static IBodyAssertion Json<T>(Func<T, bool> predicate, string description)
        where T : notnull => new JsonAssertion<T>(predicate, description);
}
