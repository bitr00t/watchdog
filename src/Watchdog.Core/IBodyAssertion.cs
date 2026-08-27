namespace Watchdog.Core;

/// <summary>
/// Checks the response body of an endpoint beyond its status code.
/// </summary>
/// <remarks>
/// The interface is deliberately not generic. <see cref="EndpointConfig"/> holds a mixed
/// list of assertions over different payload types, and a generic interface would force the
/// configuration itself to carry a type argument. The generic part lives one level down, in
/// <see cref="JsonAssertion{T}"/>, which is the usual way to keep a typed implementation
/// behind an untyped boundary.
/// </remarks>
public interface IBodyAssertion
{
    /// <summary>
    /// Human readable description of what this assertion expects.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Returns the failure reason, or <c>null</c> when the body satisfies the assertion.
    /// </summary>
    string? Validate(string body);
}
