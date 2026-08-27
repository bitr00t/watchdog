using Microsoft.EntityFrameworkCore;

namespace Watchdog.Persistence;

/// <summary>
/// Entity Framework context over the check history.
/// </summary>
/// <remarks>
/// The Java comparison people reach for is a JPA <c>EntityManager</c>, and the parallels
/// hold: identity map, change tracking, unit of work. The difference that matters is on the
/// query side. A <c>DbSet&lt;T&gt;</c> is an <c>IQueryable&lt;T&gt;</c>, so a LINQ query
/// written against it is captured as an expression tree and translated to SQL, instead of
/// being executed as code. JPA needs the Criteria API or a query string for the same thing.
/// </remarks>
public sealed class WatchdogDbContext(DbContextOptions<WatchdogDbContext> options) : DbContext(options)
{
    public DbSet<CheckRecord> Checks => Set<CheckRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var checks = modelBuilder.Entity<CheckRecord>();

        checks.HasKey(record => record.Id);
        checks.Property(record => record.EndpointId).IsRequired().HasMaxLength(100);
        checks.Property(record => record.FailureReason).HasMaxLength(500);

        // Every query in the store filters or groups by these two columns.
        checks.HasIndex(record => new { record.EndpointId, record.StartedAt });
    }
}
