using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installer.NeoForge.Versions;
using Newtonsoft.Json;

namespace CmlLib.Core.Installer.Forge.Tests;

public class ForgeInstallerVersionMapperTest
{
    [Theory]
    [InlineData(typeof(NeoFNewest), "26.1.2", "26.1.2.43", "neoforge-26.1.2.43")]
    [InlineData(typeof(NeoFNewest), "1.20.5", "20.5.21-beta", "neoforge-20.5.21-beta")]
    public void Test(Type installerType, string mcVersion, string forgeVersion, string versionName)
    {
        var mapper = new NeoForgeInstallerVersionMapper();
        var installer = mapper.CreateInstaller(new NeoForgeVersion(mcVersion, forgeVersion));
        Assert.IsType(installerType, installer);
        Assert.Equal(versionName, installer.VersionName);
    }

    [Theory]
    [InlineData("26.1.2")]
    [InlineData("26.1.1")]
    [InlineData("26.1")]
    [InlineData("1.21.11")]
    [InlineData("1.21.10")]
    [InlineData("1.21.9")]
    [InlineData("1.21.8")]
    [InlineData("1.21.7")]
    [InlineData("1.21.6")]
    [InlineData("1.21.5")]
    [InlineData("1.21.4")]
    [InlineData("1.21.3")]
    [InlineData("1.21.2")]
    [InlineData("1.21.1")]
    [InlineData("1.21.0")]
    [InlineData("1.20.6")]
    [InlineData("1.20.5")]
    [InlineData("1.20.4")]
    [InlineData("1.20.3")]
    [InlineData("1.20.2")]
    public async Task TestVersions(string minecraftVersion)
    {
        using var client = new HttpClient();
        var loader = new NeoForgeVersionLoader(client);

        var versions = await loader.GetNeoForgeVersions(minecraftVersion);

        Assert.NotNull(versions);
        Assert.NotEmpty(versions);
    }

    [Theory]
    [InlineData("26.1.2")]
    [InlineData("26.1.1")]
    [InlineData("26.1")]
    [InlineData("1.21.11")]
    [InlineData("1.21.10")]
    [InlineData("1.21.9")]
    [InlineData("1.21.8")]
    [InlineData("1.21.7")]
    [InlineData("1.21.6")]
    [InlineData("1.21.5")]
    [InlineData("1.21.4")]
    [InlineData("1.21.3")]
    [InlineData("1.21.2")]
    [InlineData("1.21.1")]
    [InlineData("1.21.0")]
    [InlineData("1.20.6")]
    [InlineData("1.20.5")]
    [InlineData("1.20.4")]
    [InlineData("1.20.3")]
    [InlineData("1.20.2")]
    public async Task TestAllNeoForgeVersions(string minecraftVersion)
    {
        using var client = new HttpClient();
        var loader = new NeoForgeVersionLoader(client);

        var versions = await loader.GetAllNeoForgeVersions(minecraftVersion);

        Assert.NotNull(versions);
        Assert.NotEmpty(versions);
    }
}