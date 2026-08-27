using System.Diagnostics;

namespace Watchdog.Core;

/// <summary>
/// Measured response time of a single check.
/// </summary>
/// <remarks>
/// Shows two C# specifics: value semantics without heap allocation, and overloaded
/// comparison operators. Java has no operator overloading, leaving only <c>compareTo</c>.
/// </remarks>
public readonly record struct Latency(double Milliseconds) : IComparable<Latency>
{
    public static Latency Zero => new(0);

    public static Latency FromTimeSpan(TimeSpan elapsed) => new(elapsed.TotalMilliseconds);

    public static Latency FromStopwatch(Stopwatch stopwatch) => FromTimeSpan(stopwatch.Elapsed);

    public int CompareTo(Latency other) => Milliseconds.CompareTo(other.Milliseconds);

    public static bool operator <(Latency left, Latency right) => left.CompareTo(right) < 0;

    public static bool operator >(Latency left, Latency right) => left.CompareTo(right) > 0;

    public static bool operator <=(Latency left, Latency right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Latency left, Latency right) => left.CompareTo(right) >= 0;

    // String interpolation with a format specifier: F1 means one decimal place.
    public override string ToString() => $"{Milliseconds:F1} ms";
}
