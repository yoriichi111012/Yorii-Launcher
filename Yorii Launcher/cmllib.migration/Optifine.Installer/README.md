# Optifine.Installer
Minecraft OptiFine Installer.

[![Nuget Badge](https://img.shields.io/nuget/v/Optifine.Installer)](https://www.nuget.org/packages/Optifine.Installer)
[![GitHub license](https://img.shields.io/github/license/Naereen/StrapDown.js.svg)](https://github.com/mzggr0914/Optifine.Installer/blob/master/LICENSE)

## Features

* Fetch available OptiFine versions from the official source.
* Display version details including Forge compatibility, preview status, and upload date.
* Install Nearly All OptiFine Versions

## Install

Install the [Optifine.Installer Nuget package](https://www.nuget.org/packages/Optifine.Installer)

## Sample Code With [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core)

```csharp
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
```

---

## Supported OptiFine Versions

### Confirmed Working Versions

* OptiFine\_1.21.4\_HD\_U\_J4\_pre2
* OptiFine\_1.21.3\_HD\_U\_J2
* OptiFine\_1.21.1\_HD\_U\_J1
* OptiFine\_1.21\_HD\_U\_J1\_pre9
* OptiFine\_1.20.6\_HD\_U\_J1\_pre18
* OptiFine\_1.20.4\_HD\_U\_I8\_pre4
* OptiFine\_1.20.2\_HD\_U\_I7\_pre1
* OptiFine\_1.20.1\_HD\_U\_I6
* OptiFine\_1.20\_HD\_U\_I5\_pre5
* OptiFine\_1.19.4\_HD\_U\_I4
* OptiFine\_1.19.3\_HD\_U\_I3
* OptiFine\_1.19.2\_HD\_U\_I2
* OptiFine\_1.19.1\_HD\_U\_H9
* OptiFine\_1.19\_HD\_U\_H9
* OptiFine\_1.18.2\_HD\_U\_H9
* OptiFine\_1.18.1\_HD\_U\_H6
* OptiFine\_1.18\_HD\_U\_H3
* OptiFine\_1.17.1\_HD\_U\_H2\_pre1
* OptiFine\_1.17\_HD\_U\_G9\_pre26
* OptiFine\_1.16.5\_HD\_U\_G8
* OptiFine\_1.16.4\_HD\_U\_G7
* OptiFine\_1.16.3\_HD\_U\_G5
* OptiFine\_1.14.4\_HD\_U\_G5
* OptiFine\_1.14.3\_HD\_U\_F2
* OptiFine\_1.12.2\_HD\_U\_G6\_pre1
* OptiFine\_1.8.9\_HD\_U\_M6\_pre2
* OptiFine\_1.8.9\_HD\_U\_M5
* OptiFine\_1.8.8\_HD\_U\_I7
* OptiFine\_1.7.2\_HD\_U\_E3

### Known Non-Working Versions

* (None identified yet.)
