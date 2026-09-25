namespace CmlLib.Core.Installer.Forge.Tests;

public class ForgePackageNameTests
{
    [Theory]
    [InlineData(
        "de.oceanlabs.mcp:mcp_config:1.16.2-20200812.004259", 
        "de/oceanlabs/mcp/mcp_config/1.16.2-20200812.004259/mcp_config-1.16.2-20200812.004259.jar")]
    public void test_GetPath(string input, string expected)
    {
        var actual = ForgePackageName.GetPath(input, '/');
        Assert.Equal(expected, actual);
    }
}
