using System.Reflection;

namespace Watchdog.Core;

/// <summary>
/// Per type metadata, computed once for every closed generic type.
/// </summary>
/// <remarks>
/// This class is the clearest demonstration of reified generics in the whole project.
/// <c>SchemaCache&lt;HealthReport&gt;</c> and <c>SchemaCache&lt;Status&gt;</c> are two
/// distinct runtime types with entirely separate static storage, and each one runs its
/// field initializers exactly once, on first use.
///
/// Neither half of that works in Java. Type erasure removes the argument, so
/// <c>typeof(T)</c> has no counterpart and every parameterization would share one set of
/// static fields. The usual workaround there is to pass a <c>Class&lt;T&gt;</c> token
/// around by hand, which is exactly the parameter this class does not need.
/// </remarks>
public static class SchemaCache<T>
    where T : notnull
{
    /// <summary>
    /// Simple name of the type argument, for error messages.
    /// </summary>
    public static string TypeName { get; } = typeof(T).Name;

    /// <summary>
    /// Readable public instance properties, sorted, for error messages.
    /// </summary>
    public static IReadOnlyList<string> PropertyNames { get; } =
    [
        .. typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>
    /// Comma separated property list, precomputed because it never changes for a given T.
    /// </summary>
    public static string PropertySummary { get; } = string.Join(", ", PropertyNames);
}
