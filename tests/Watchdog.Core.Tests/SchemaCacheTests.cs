using Xunit;

namespace Watchdog.Core.Tests;

public sealed class SchemaCacheTests
{
    private sealed record HealthReport(string Status, int UptimeSeconds);

    private sealed record VersionReport(string Version);

    [Fact]
    public void Each_closed_generic_type_has_its_own_static_state()
    {
        // Two distinct runtime types, each with separate storage. Under type erasure both
        // would share one set of static fields and this assertion could not hold.
        Assert.Equal("HealthReport", SchemaCache<HealthReport>.TypeName);
        Assert.Equal("VersionReport", SchemaCache<VersionReport>.TypeName);
    }

    [Fact]
    public void The_metadata_is_computed_once_per_closed_type()
    {
        // Same instance on every access: the field initializer ran exactly once.
        Assert.Same(
            SchemaCache<HealthReport>.PropertyNames,
            SchemaCache<HealthReport>.PropertyNames);
    }

    [Fact]
    public void Property_names_are_read_from_the_type_argument()
    {
        Assert.Equal(
            new[] { "Status", "UptimeSeconds" },
            SchemaCache<HealthReport>.PropertyNames.ToArray());

        Assert.Equal("Status, UptimeSeconds", SchemaCache<HealthReport>.PropertySummary);
    }
}
