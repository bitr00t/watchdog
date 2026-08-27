namespace Watchdog.Core;

/// <summary>
/// Raised once per completed round, before the round is handed to the consumer.
/// </summary>
public sealed class RoundCompletedEventArgs(CheckRound round) : EventArgs
{
    public CheckRound Round { get; } = round;
}
