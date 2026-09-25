using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;

namespace SampleForgeInstaller;

internal class AllInstaller
{
    MinecraftLauncher _launcher;
    NeoForgeInstaller _neoForge;

    public AllInstaller()
    {
        _launcher = new MinecraftLauncher(new MinecraftPath());
        _neoForge = new NeoForgeInstaller(_launcher);
    }

    public async Task InstallAll()
    {
        var versions = new string[]
        {
            "1.21.10", //ok
            "1.21.9", //ok
            "1.21.8", //ok
            "1.21.7", //ok
            "1.21.6", //ok
            "1.21.5", //ok
            "1.21.4", //ok
            "1.21.3", //ok
            "1.21.2", //ok
            "1.21.1", //ok
            "1.21.0", //not ok
            "1.20.6", //ok
            "1.20.5", //ok
            "1.20.4", //ok
            "1.20.3", //ok
            "1.20.2", //ok
        };

        foreach (var version in versions)
        {
            try
            {
                await InstallAndLaunch(version);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadLine();
                Console.ReadLine();
                throw;
            }
        }
    }

    public async Task InstallAndLaunch(string mcVersion)
    {
        Console.WriteLine("Minecraft: " + mcVersion);
        var versionName = await _neoForge.Install(mcVersion, new NeoForgeInstallOptions
        {
            FileProgress = new SyncProgress<InstallerProgressChangedEventArgs>(fileChanged),
            ByteProgress = new SyncProgress<ByteProgress>(progressChanged),
            InstallerOutput = new SyncProgress<string>(logOutput),
            SkipIfAlreadyInstalled = false
        });
        var process = await _launcher.CreateProcessAsync(versionName, new MLaunchOption
        {
            Session = MSession.CreateOfflineSession("tester123")
        });

        var processUtil = new ProcessWrapper(process);
        processUtil.OutputReceived += (s, e) => logOutput(e);
        processUtil.StartWithEvents();
        await Task.WhenAny(Task.Delay(30000), processUtil.WaitForExitTaskAsync());
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
