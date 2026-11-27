using OpenClicky.Core.Launcher;
using Xunit;

namespace OpenClicky.Core.Tests.Launcher;

public class LauncherTests
{
    [Theory]
    [InlineData("2 * (3,5 + 4)", "15")]
    [InlineData("-2+3*4", "10")]
    [InlineData("1/0", null)]
    [InlineData("1; start", null)]
    [InlineData("(1+2", null)]
    public void Arithmetic(string input, string? expected) => Assert.Equal(expected, Calculator.Evaluate(input));

    [Theory]
    [InlineData("notpead")]
    [InlineData("noxepad")]
    [InlineData("ntpd")]
    public void ApproximateMatches(string query)
    {
        Assert.InRange(LauncherSearch.Score("Notepad", query), 1, LauncherSearch.Score("Notepad", "Note") - 1);
    }

    [Fact]
    public void RegisteredActionRunsOnlyOnExecution()
    {
        var calls = 0;
        var registry = new LocalToolRegistry();
        registry.Register(new("test", "Test", () => calls++));
        Assert.Single(registry.Tools);
        Assert.Equal(0, calls);
        registry.Execute("test");
        Assert.Equal(1, calls);
    }
}
