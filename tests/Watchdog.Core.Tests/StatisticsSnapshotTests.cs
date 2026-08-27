using Xunit;

using static Watchdog.Core.Tests.TestData;

namespace Watchdog.Core.Tests;

public sealed class StatisticsSnapshotTests
{
    [Fact]
    public void Current_is_empty_before_the_first_update()
    {
        Assert.Empty(new StatisticsSnapshot().Current);
    }

    [Fact]
    public void Update_replaces_the_published_list()
    {
        var snapshot = new StatisticsSnapshot();

        var first = new[] { new[] { Result("a", success: true) }.Summarize(new CheckId("a")) };
        var second = new[] { new[] { Result("b", success: false) }.Summarize(new CheckId("b")) };

        snapshot.Update(first);
        Assert.Same(first, snapshot.Current);

        snapshot.Update(second);
        Assert.Same(second, snapshot.Current);
    }
}
