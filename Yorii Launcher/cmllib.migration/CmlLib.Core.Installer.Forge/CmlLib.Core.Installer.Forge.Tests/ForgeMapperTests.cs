namespace CmlLib.Core.Installer.Forge.Tests;

public class ForgeMapperTests
{
    [Theory]
    [InlineData(
        "[de.oceanlabs.mcp:mcp_config:1.16.2-20200812.004259@zip]",
        "libraries/de/oceanlabs/mcp/mcp_config/1.16.2-20200812.004259/mcp_config-1.16.2-20200812.004259.zip")]
    [InlineData(
        "[net.minecraft:client:1.16.2-20200812.004259:slim]",
        "libraries/net/minecraft/client/1.16.2-20200812.004259/client-1.16.2-20200812.004259-slim.jar")]
    public void test_ToFullPath(string input, string expected)
    {
        var actual = ForgeMapper.ToFullPath(input, "libraries", '/');
        Assert.Equal(expected, actual);
    }
}
