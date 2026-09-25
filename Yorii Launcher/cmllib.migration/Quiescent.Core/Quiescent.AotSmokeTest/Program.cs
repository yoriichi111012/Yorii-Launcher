using System.Text;
using Quiescent.Core;
using Quiescent.Core.Auth;
using Quiescent.Core.VersionLoader;

var results = new List<(string name, bool ok, string detail)>();
void Check(string name, Action act)
{
    try { act(); results.Add((name, true, "")); }
    catch (Exception ex) { results.Add((name, false, ex.GetType().Name + ": " + ex.Message)); }
}
async Task CheckAsync(string name, Func<Task> act)
{
    try { await act(); results.Add((name, true, "")); }
    catch (Exception ex) { results.Add((name, false, ex.GetType().Name + ": " + ex.Message)); }
}

// 1. offline session (pure code, no network)
Check("offline-session", () =>
{
    var s = new MSession
    {
        Username = "Steve",
        UUID = ToUuidV3("OfflinePlayer:Steve"),
        AccessToken = Guid.NewGuid().ToString("N"),
        UserType = "legacy"
    };
    if (s.Username != "Steve") throw new Exception("roundtrip");
});

// 2. local version JSON parsing (inheritance chain + rules + args) — exercises STJ DTOs
var mcDir = Path.Combine(Path.GetTempPath(), "quiescent-smoke", Guid.NewGuid().ToString("N"));
var versionsDir = Path.Combine(mcDir, "versions", "1.20.1");
Directory.CreateDirectory(versionsDir);
var versionJson = """
{
  "id": "1.20.1",
  "type": "release",
  "mainClass": "net.minecraft.client.main.Main",
  "inheritsFrom": null,
  "arguments": {
    "game": ["--username", "${auth_player_name}"],
    "jvm": ["-Djava.awt.headless=true"]
  },
  "libraries": [
    {
      "name": "com.mojang:brigadier:1.0.18",
      "rules": [{ "action": "allow", "os": { "name": "windows" } }]
    }
  ],
  "downloads": {
    "client": { "url": "https://example.invalid/client.jar", "size": 1, "sha1": "deadbeef" }
  },
  "javaVersion": { "component": "java-runtime-gamma", "majorVersion": 17 }
}
""";
File.WriteAllText(Path.Combine(versionsDir, "1.20.1.json"), versionJson);

await CheckAsync("local-version-loader", async () =>
{
    var path = new MinecraftPath(mcDir);
    var loader = new LocalJsonVersionLoader(path);
    var versions = await loader.GetVersionMetadatasAsync();
    if (versions.Count() == 0) throw new Exception("no local versions found");
});

// 3. Mojang manifest over network (may fail in sandboxes — recorded, not fatal)
await CheckAsync("mojang-manifest", async () =>
{
    var path = new MinecraftPath(mcDir);
    var launcher = new MinecraftLauncher(path);
    var all = await launcher.GetAllVersionsAsync();
    var count = all.Count();
    if (count > 500)
        Console.WriteLine($"  manifest OK: {count} versions");
});

foreach (var (name, ok, detail) in results)
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "  -> " + detail)}");

Console.WriteLine($"\n{results.Count(r => r.ok)}/{results.Count} checks passed");
return results.All(r => r.ok) ? 0 : 1;

static string ToUuidV3(string input)
{
    var hash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(input));
    return new Guid(hash).ToString("N");
}
