using ToiletLabels.Cli;

namespace ToiletLabels.Tests.Unit;

public class SelfTestTests
{
    [Fact]
    public void SelfTest_PassesOnDevMachine()
    {
        var writer = new StringWriter();

        var exitCode = SelfTestCommand.Run(writer);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(3, lines.Count(l => l.StartsWith("OK ", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, l => l.StartsWith("FAIL", StringComparison.Ordinal));
    }
}
