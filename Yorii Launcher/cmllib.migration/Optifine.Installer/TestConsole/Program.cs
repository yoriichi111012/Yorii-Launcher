using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using Optifine.Installer;
using System;
using System.Linq;
using System.Net.Http;

var loader = new OptifineInstaller(new HttpClient());
var versions = await loader.GetOptifineVersionsAsync();

Console.WriteLine($"{"Version",-40} {"Forge Ver",-10} {"Preview",-8} {"Uploaded",-12}");
Console.WriteLine(new string('-', 61));

foreach (var v in versions)
{
    Console.WriteLine($"{v.Version,-40} {v.ForgeVersion,-10} {(v.IsPreviewVersion ? "Yes" : "No"),-8} {v.UploadedDate:yyyy-MM-dd}");
}
Console.WriteLine(new string('-', 61));

Console.Write("Version: ");
var version = Console.ReadLine();

Console.WriteLine("selected version: " + version);

var selectedVersion = versions.FirstOrDefault(x => x.Version == version);

if (selectedVersion is null)
{
    Console.WriteLine("version not found");
    return;
}
var minecraftPath = new MinecraftPath();
var launcher = new MinecraftLauncher(minecraftPath);

await launcher.InstallAsync(selectedVersion.MinecraftVersion);
Console.WriteLine($"done installing vanilla version: {selectedVersion.MinecraftVersion}");

var versionName = await loader.InstallOptifineAsync(minecraftPath.BasePath, selectedVersion);
Console.WriteLine($"done installing optifine: {versionName}");

var process = await launcher.InstallAndBuildProcessAsync(versionName, new MLaunchOption
{
    Session = MSession.CreateOfflineSession("test123"),
    MaximumRamMb = 2048
});
process.Start();
await process.WaitForExitAsync();