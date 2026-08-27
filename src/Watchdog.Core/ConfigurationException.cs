namespace Watchdog.Core;

/// <summary>
/// Thrown when a configuration file cannot be read or does not validate.
/// </summary>
/// <remarks>
/// Carries every problem found rather than only the first one. Fixing a config file one
/// error per run is the kind of small friction that makes a tool feel unfinished.
/// </remarks>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message)
        : base(message) => Errors = [message];

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException) => Errors = [message];

    public ConfigurationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors)) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count switch
        {
            0 => "The configuration is invalid.",
            1 => errors[0],
            _ => $"The configuration has {errors.Count} problems:{Environment.NewLine}"
                + string.Join(Environment.NewLine, errors.Select(error => $"  - {error}")),
        };
    }
}
