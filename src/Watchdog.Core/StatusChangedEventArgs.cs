namespace Watchdog.Core;

/// <summary>
/// Raised when an endpoint flips between healthy and failing.
/// </summary>
/// <remarks>
/// A plain class deriving from <see cref="EventArgs"/> rather than a record: event payloads
/// are passed around by reference and never compared, so the value equality a record would
/// generate has no purpose here. The convention also keeps the type usable from
/// <c>EventHandler&lt;T&gt;</c>.
/// </remarks>
public sealed class StatusChangedEventArgs(
    CheckStatus previous,
    CheckStatus current,
    CheckResult result) : EventArgs
{
    public CheckStatus Previous { get; } = previous;

    public CheckStatus Current { get; } = current;

    public CheckResult Result { get; } = result;

    public CheckId Id => Result.Id;
}
