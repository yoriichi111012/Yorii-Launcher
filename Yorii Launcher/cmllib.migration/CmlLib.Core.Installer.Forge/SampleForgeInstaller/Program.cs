using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;

var path = new MinecraftPath(); // use default directory
var launcher = new MinecraftLauncher(path);
var forgeInstaller = new ForgeInstaller(launcher);

var fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
        Console.WriteLine($"[{e.EventType}][{e.ProgressedTasks}/{e.TotalTasks}] {e.Name}"));
var byteProgress = new Progress<ByteProgress>(e =>
        Console.WriteLine(e.ToRatio() * 100 + "%"));

// install forge
// show launch progress to console
var versionName = await forgeInstaller.Install("1.20.1", new ForgeInstallOptions
{
    FileProgress = fileProgress,
    ByteProgress = byteProgress,
    InstallerOutput = new Progress<string>(e =>
        Console.WriteLine(e)),
});

// ForgeInstaller does not fully install the version, you still need to call InstallAsync
await launcher.InstallAsync(versionName, fileProgress, byteProgress);
var process = await launcher.BuildProcessAsync(versionName, new MLaunchOption
{
    MaximumRamMb = 1024,
    Session = MSession.CreateOfflineSession("Gamer123"),
});

// print game logs
var processUtil = new ProcessWrapper(process);
processUtil.OutputReceived += (s, e) => Console.WriteLine(e);
processUtil.StartWithEvents();
await processUtil.WaitForExitTaskAsync();