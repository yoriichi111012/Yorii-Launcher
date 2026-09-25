using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;

namespace SampleForgeInstaller;

internal class AllInstaller
{
    MinecraftLauncher _launcher;
    ForgeInstaller _forge;

    public AllInstaller()
    {
        _launcher = new MinecraftLauncher(new MinecraftPath());
        _forge = new ForgeInstaller(_launcher);
    }

    public async Task InstallAll()
    {
        var versions = new string[]
        {
            "1.21.4",
            //"1.20.6", //ok
            //"1.20.1", // ok
            //"1.20", // ok
            //"1.19.4", // ok
            //"1.19.3", // ok
            //"1.19.2", // ok
            //"1.19.1", // ok
            //"1.19", // ok
            //"1.18.2", // ok
            //"1.18.1", // ok
            //"1.18", // ok
            //"1.17.1", // ok
            //"1.16.5", // ok
            //"1.16.4", // ok
            //"1.16.3", // ok
            //"1.16.2", // ok
            //"1.16.1", // ok
            //"1.15.2", // ok
            //"1.15.1", // ok
            //"1.15", // ok
            //"1.14.4", // ok
            //"1.14.3", // ok
            //"1.14.2", // ok
            //"1.13.2", // ok
            //"1.12.2", // ok
            //"1.12.1", // ok
            //"1.12", // ok
            //"1.11.2", // ok
            //"1.11", // ok
            //"1.10.2", // ok
            //"1.10", // ok
            //"1.9.4", // ok
            //"1.9", // ok
            //"1.8.9", // ok
            //"1.8.8", // ok
            //"1.7.10", // ok
            //"1.7.10_pre4", // cannot find 1.7.10_pre4
            //"1.7.2", // crash java.util.ConcurrentModificationException 1.7.2-Forge10.12.2.1161-mc172
            //"1.6.4", // ok, 1.6.4-Forge9.11.1.1345
            //"1.6.3", // ok, 1.6.3-Forge9.11.0.878
            //"1.6.2", // ok, 1.6.2-Forge9.10.1.871
            //"1.6.1", // ok, Forge8.9.0.775
            //"1.5.2", // ok, 1.5.2-Forge7.8.1.738, need https://web.archive.org/web/20140626042316/http://files.minecraftforge.net/fmllibs/deobfuscation_data_1.5.2.zip
            //"1.5.1", // ok, 1.5.1-Forge7.7.2.682
            //"1.5", // ok, 1.5-Forge7.7.0.598
            //"1.4.7", // ok, 1.4.7-Forge6.6.2.534
            //"1.4.6", // ok, 1.4.6-Forge6.5.0.489
            //"1.4.5", // ok, 1.4.5-Forge6.4.2.448
            //"1.4.4", // ok, 1.4.4-Forge6.3.0.378
            //"1.4.3", // ok, 1.4.3-Forge6.2.1.358
            //"1.4.2", // ok, 1.4.2-Forge6.0.1.355
            //"1.4.1", // ok, 1.4.1-Forge6.0.0.329
            //"1.4.0", // cannot find 1.4.0
            //"1.3.2", // ok, 1.3.2-Forge4.3.5.318
            //"1.2.5", // ok, 1.2.5-Forge3.4.9.171
            //"1.2.4", // ok, 1.2.4-Forge2.0.0.68
            //"1.2.3", // ok, 1.2.3-Forge1.4.1.64
            //"1.1" // ok, 1.1-Forge1.3.4.29
        };

        foreach (var version in versions.Reverse())
        {
            try
            {
                await InstallAndLaunch(version);
            }
            catch(Exception ex)
            {
                throw;
                Console.WriteLine(ex.ToString());
                Console.ReadLine();
                Console.ReadLine();
            }
        }
    }

    public async Task InstallAndLaunch(string mcVersion)
    {
        Console.WriteLine("Minecraft: " + mcVersion);
        var versionName = await _forge.Install(mcVersion, new ForgeInstallOptions
        {
            FileProgress = new SyncProgress<InstallerProgressChangedEventArgs>(fileChanged),
            ByteProgress = new SyncProgress<ByteProgress>(progressChanged),
            InstallerOutput = new SyncProgress<string>(logOutput),
            SkipIfAlreadyInstalled = false
        });
        var process = await _launcher.InstallAndBuildProcessAsync(versionName, new MLaunchOption
        {
            Session = MSession.CreateOfflineSession("tester123"),
            ExtraJvmArguments = new[]
            {
                new MArgument("-Dfml.ignoreInvalidMinecraftCertificates=true"),
            }
        });

        var processUtil = new ProcessWrapper(process);
        processUtil.OutputReceived += (s, e) => logOutput(e);
        processUtil.StartWithEvents();
        await Task.WhenAny(Task.Delay(10000), processUtil.WaitForExitTaskAsync());
        if (processUtil.Process.HasExited)
            throw new Exception("Process was dead!");
        else
            processUtil.Process.Kill();
    }

    private void logOutput(string e)
    {
        Console.WriteLine(e);
    }

    private void fileChanged(InstallerProgressChangedEventArgs e)
    {
        Console.WriteLine($"[{e.EventType}][{e.ProgressedTasks}/{e.TotalTasks}] {e.Name}");
    }

    private void progressChanged(ByteProgress e)
    {
        Console.WriteLine(e.ToRatio() * 100 + "%");
    }
}
