using EaseGit.UI.Widgets;

namespace EaseGit.Tests.UI.Widgets;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "сейчас")]
    [InlineData(59, "сейчас")]
    [InlineData(60, "1 мин")]
    [InlineData(59 * 60, "59 мин")]
    [InlineData(3 * 3600, "3 ч")]
    [InlineData(2 * 86400, "2 дн")]
    [InlineData(14 * 86400, "2 нед")]
    [InlineData(95 * 86400, "3 мес")]
    [InlineData(800 * 86400, "2 г")]
    public void Format_Elapsed_ReturnsShortText(int seconds, string expected)
    {
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-seconds), Now));
    }

    [Fact]
    public void Format_FutureDate_ReturnsNow()
    {
        Assert.Equal("сейчас", RelativeTime.Format(Now.AddHours(1), Now));
    }
}
