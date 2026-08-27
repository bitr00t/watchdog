namespace Watchdog.Core;

/// <summary>
/// Strongly typed identifier of a monitored endpoint.
/// </summary>
/// <remarks>
/// A <c>readonly record struct</c> is a true value type. The compiler generates value
/// equality, <c>GetHashCode</c> and deconstruction, yet nothing is allocated on the heap.
/// In Java the same idea requires a wrapper class and pays for the allocation.
///
/// Pitfall: <c>default(CheckId)</c> bypasses the constructor, so <see cref="Value"/> is
/// <c>null</c> despite the non-nullable annotation. Every struct in C# has a parameterless
/// default value and that cannot be suppressed.
/// </remarks>
public readonly record struct CheckId(string Value)
{
    public override string ToString() => Value;
}
