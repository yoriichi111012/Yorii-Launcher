using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Optifine.Installer
{
    public class OptifineInstaller
    {
        private static FileInfo JarFile;

        private static ZipResourceProvider JarFileZrp;

        private readonly HttpClient _httpClient;

        public OptifineInstaller(HttpClient client)
        {
            _httpClient = client;
        }

        public async Task<string> GetDirectDownloadUrlAsync(OptifineVersion version)
        {
            HttpClient client = new HttpClient();
            var res = await client.GetAsync(version.DownloadUrl);
            var html = await res.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var node = doc.DocumentNode.SelectSingleNode("//a[@onclick='onDownload()']");
            var href = node.GetAttributeValue("href", null);

            return $"https://optifine.net/{href}";
        }

        public async Task<IEnumerable<OptifineVersion>> GetOptifineVersionsAsync()
        {
            var response = await _httpClient.GetAsync("https://optifine.net/downloads");
            string html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var node = doc.DocumentNode.SelectSingleNode("//td[@class='content']").SelectSingleNode(".//span[@class='downloads']");

            List<OptifineVersion> versions = new List<OptifineVersion>();

            var versionTrList = node.SelectNodes(".//tr[contains(@class, 'downloadLine')]");

            foreach (var versionTr in versionTrList)
            {
                var mcVer = Regex.Replace(node.SelectNodes(".//h2")?.
                    Where(h2 => h2.StreamPosition < versionTr.StreamPosition).Last().InnerText.Split()[1], @"\.0$", "");

                var forgeVersion = versionTr.SelectSingleNode(".//td[@class='colForge']").InnerText.Replace("Forge ", "").Replace("#", "");

                var version = new OptifineVersion
                (
                    mcVer,
                    versionTr.SelectSingleNode(".//td[@class='colFile']").InnerText.Split(new[] { ' ' }, 2)[1].Replace(" ", "_"),
                    forgeVersion != "Forge N/A" ? forgeVersion : null,
                    versionTr.GetAttributeValue("class", "").Contains("downloadLinePreview"),
                    DateTime.ParseExact(versionTr.SelectSingleNode(".//td[@class='colDate']").InnerText, "dd.MM.yyyy", CultureInfo.InvariantCulture)
                );
                versions.Add(version);
            }

            return versions;
        }

        public async Task<string> InstallOptifineAsync(string minecraftpath, OptifineVersion version)
        {
            var url = await GetDirectDownloadUrlAsync(version);

            using (HttpResponseMessage response = await _httpClient.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();
                string path = Path.Combine(Path.GetTempPath(), version.Version + ".jar");
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                DoInstall(new DirectoryInfo(minecraftpath), new FileInfo(path), version);
                return $"{version.MinecraftVersion}-OptiFine_{version.OptifineEdition}";
            }
        }

        public async Task<string> InstallOptifineAsync(string minecraftpath, string optifineVersion)
        {
            var versions = await GetOptifineVersionsAsync();
            var version = versions.FirstOrDefault(x => x.Version == optifineVersion);
            return await InstallOptifineAsync(minecraftpath, version);
        }

        private static void DoInstall(DirectoryInfo dirMc, FileInfo jarFile, OptifineVersion version)
        {
            JarFile = jarFile;
            var diffStream = JarFile.OpenRead();
            var diffZip = new ZipArchive(diffStream, ZipArchiveMode.Read);
            JarFileZrp = new ZipResourceProvider(diffZip);

            string ofVer = version.Version;
            Debug.WriteLine($"OptiFine Version: {ofVer}");

            string mcVer = version.MinecraftVersion;
            Debug.WriteLine($"Minecraft Version: {mcVer}");

            string ofEd = version.OptifineEdition;
            Debug.WriteLine($"OptiFine Edition: {ofEd}");

            Debug.WriteLine($"Dir minecraft: {dirMc}");

            var dirMcLib = new DirectoryInfo(Path.Combine(dirMc.FullName, "libraries"));
            Debug.WriteLine($"Dir libraries: {dirMcLib}");

            var dirMcVers = new DirectoryInfo(Path.Combine(dirMc.FullName, "versions"));
            Debug.WriteLine($"Dir versions: {dirMcVers}");

            string mcVerOf = $"{mcVer}-OptiFine_{ofEd}";
            Debug.WriteLine($"Minecraft_OptiFine Version: {mcVerOf}");

            CopyMinecraftVersion(mcVer, mcVerOf, dirMcVers);
            InstallOptiFineLibrary(mcVer, ofEd, dirMcLib, Utils.IsNewVersion(version));
            if (Utils.IsLegacyVersion(version))
                UpdateJsonLegacy(dirMcVers, mcVer, ofEd, Utils.GetLaunchwrapperVersionLegacy(version));
            else
            {
                InstallLaunchwrapperLibrary(dirMcLib);
                UpdateJson(dirMcVers, mcVer, ofEd);
            }
            JarFile = null; JarFileZrp = null;
            diffStream.Dispose();
            jarFile.Delete();
        }

        private static void UpdateJson(DirectoryInfo versionsDir, string mcVer, string ofEd)
        {
            string mcVerOf = $"{mcVer}-OptiFine_{ofEd}";
            var jsonPath = Path.Combine(versionsDir.FullName, mcVerOf, $"{mcVerOf}.json");
            string rawJson = File.ReadAllText(jsonPath, Encoding.UTF8);

            using (JsonDocument originalDoc = JsonDocument.Parse(rawJson))
            {
                JsonElement root = originalDoc.RootElement;

                var updated = new JsonObject
                {
                    ["id"] = mcVerOf,
                    ["inheritsFrom"] = mcVer,
                    ["time"] = FormatDate(DateTime.Now),
                    ["releaseTime"] = FormatDate(DateTime.Now),
                    ["type"] = "release",
                    ["libraries"] = new JsonArray()
                };

                JsonArray libraries = (JsonArray)updated["libraries"];

                string mainClass = null;
                if (root.TryGetProperty("mainClass", out JsonElement mcElement) && mcElement.ValueKind == JsonValueKind.String)
                {
                    mainClass = mcElement.GetString();
                }

                if (string.IsNullOrEmpty(mainClass) || !mainClass.StartsWith("net.minecraft.launchwrapper.", StringComparison.Ordinal))
                {
                    updated["mainClass"] = "net.minecraft.launchwrapper.Launch";

                    if (root.TryGetProperty("minecraftArguments", out JsonElement mcArgsToken) && mcArgsToken.ValueKind == JsonValueKind.String)
                    {
                        string mcArgs = mcArgsToken.GetString() + " --tweakClass optifine.OptiFineTweaker";
                        updated["minecraftArguments"] = mcArgs;
                    }
                    else
                    {
                        updated["minimumLauncherVersion"] = "21";

                        var arguments = new JsonObject
                        {
                            ["game"] = new JsonArray("--tweakClass", "optifine.OptiFineTweaker")
                        };
                        updated["arguments"] = arguments;
                    }

                    libraries.Add(new JsonObject
                    {
                        ["name"] = "optifine:launchwrapper-of:" + GetLaunchwrapperVersion()
                    });
                }

                libraries.Insert(0, new JsonObject
                {
                    ["name"] = $"optifine:OptiFine:{mcVer}_{ofEd}"
                });

                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));

                var utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string resultJson = updated.ToJsonString(options);
                resultJson = resultJson.Replace("\r\n", "\n");

                File.WriteAllText(jsonPath, resultJson, utf8WithoutBom);
            }
        }

        private static void UpdateJsonLegacy(DirectoryInfo versionsDir, string mcVer, string ofEd, string launchwrapper)
        {
            string mcVerOf = $"{mcVer}-OptiFine_{ofEd}";
            var dirMcVersOf = new DirectoryInfo(Path.Combine(versionsDir.FullName, mcVerOf));
            var fileJson = new FileInfo(Path.Combine(dirMcVersOf.FullName, mcVerOf + ".json"));
            string json;
            using (var reader = new StreamReader(fileJson.FullName, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }
            using (JsonDocument originalDoc = JsonDocument.Parse(json))
            {
                JsonElement root = originalDoc.RootElement;
                string originalMainClass = "";
                if (root.TryGetProperty("mainClass", out JsonElement mcElement) && mcElement.ValueKind == JsonValueKind.String)
                {
                    originalMainClass = mcElement.GetString();
                }
                bool needsOverride = !originalMainClass.StartsWith("net.minecraft.launchwrapper.", StringComparison.Ordinal);
                string originalMcArgs = "";
                if (root.TryGetProperty("minecraftArguments", out JsonElement maElement) && maElement.ValueKind == JsonValueKind.String)
                {
                    originalMcArgs = maElement.GetString();
                }
                var options = new JsonWriterOptions { Indented = true };
                using (var stream = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream, options))
                    {
                        writer.WriteStartObject();
                        foreach (JsonProperty prop in root.EnumerateObject())
                        {
                            string name = prop.Name;
                            if (name == "id" || name == "inheritsFrom" || name == "libraries")
                            {
                                continue;
                            }
                            if (name == "mainClass")
                            {
                                if (needsOverride) continue;
                                writer.WritePropertyName("mainClass");
                                writer.WriteStringValue(prop.Value.GetString());
                                continue;
                            }
                            if (name == "minecraftArguments")
                            {
                                if (needsOverride) continue;
                                writer.WritePropertyName("minecraftArguments");
                                writer.WriteStringValue(prop.Value.GetString());
                                continue;
                            }
                            writer.WritePropertyName(name);
                            prop.Value.WriteTo(writer);
                        }
                        writer.WriteString("id", mcVerOf);
                        writer.WriteString("inheritsFrom", mcVer);
                        writer.WritePropertyName("libraries");
                        writer.WriteStartArray();
                        writer.WriteStartObject();
                        writer.WriteString("name", $"optifine:OptiFine:{mcVer}_{ofEd}");
                        writer.WriteEndObject();
                        if (needsOverride)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("name", $"net.minecraft:launchwrapper:{launchwrapper}");
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        if (needsOverride)
                        {
                            writer.WriteString("mainClass", "net.minecraft.launchwrapper.Launch");
                            string args = originalMcArgs + " --tweakClass optifine.OptiFineTweaker";
                            writer.WriteString("minecraftArguments", args);
                        }
                        writer.WriteEndObject();
                        writer.Flush();
                    }
                    string resultJson = Encoding.UTF8.GetString(stream.ToArray());
                    resultJson = resultJson.Replace("\r\n", "\n");
                    var utf8WithoutBom = new UTF8Encoding(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(fileJson.FullName));
                    using (var writerFile = new StreamWriter(fileJson.FullName, false, utf8WithoutBom))
                    {
                        writerFile.Write(resultJson);
                    }
                }
            }
        }

        private static string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd'T'HH:mm:ssK");
        }

        private static bool InstallOptiFineLibrary(string mcVer, string ofEd, DirectoryInfo dirMcLib, bool isOver117)
        {
            var dirDest = new DirectoryInfo(Path.Combine(dirMcLib.FullName, $"optifine/OptiFine/{mcVer}_{ofEd}"));
            var fileDest = new FileInfo(Path.Combine(dirDest.FullName, $"OptiFine-{mcVer}_{ofEd}.jar"));

            if (fileDest.FullName == JarFile.FullName)
            {
                Debug.WriteLine("Source and target file are the same. : Save");
                return false;
            }

            Debug.WriteLine($"Source: {JarFile}");
            Debug.WriteLine($"Dest: {fileDest}");

            var versionDir = new DirectoryInfo(Path.Combine(dirMcLib.Parent.FullName, $"versions/{mcVer}"));
            var baseJar = new FileInfo(Path.Combine(versionDir.FullName, $"{mcVer}.jar"));

            dirDest.Create();
            Patcher.Process(baseJar, JarFile, fileDest, isOver117);
            return true;
        }

        private static void InstallLaunchwrapperLibrary(DirectoryInfo dirMcLib)
        {
            string ver = GetLaunchwrapperVersion();
            string fileName = $"launchwrapper-of-{ver}.jar";
            var dirDest = new DirectoryInfo(Path.Combine(dirMcLib.FullName, $"optifine/launchwrapper-of/{ver}"));
            var fileDest = new FileInfo(Path.Combine(dirDest.FullName, fileName));

            Debug.WriteLine($"Source: {fileName}");
            Debug.WriteLine($"Dest: {fileDest}");

            using (var stream = JarFileZrp.GetResourceStream(fileName))
            {
                dirDest.Create();
                using (var outStream = File.Create(fileDest.FullName))
                {
                    stream.CopyTo(outStream);
                }
            }
        }

        private static void CopyMinecraftVersion(string mcVer, string mcVerOf, DirectoryInfo dirMcVer)
        {
            var dirVerMc = new DirectoryInfo(Path.Combine(dirMcVer.FullName, mcVer));
            if (!dirVerMc.Exists)
            {
                ShowMessageVersionNotFound(mcVer);
                throw new DirectoryNotFoundException($"Please Install {mcVer} Vanilla Version Minecraft First");
            }

            var dirVerMcOf = new DirectoryInfo(Path.Combine(dirMcVer.FullName, mcVerOf));
            dirVerMcOf.Create();

            var fileJarMc = new FileInfo(Path.Combine(dirVerMc.FullName, $"{mcVer}.jar"));
            var fileJarMcOf = new FileInfo(Path.Combine(dirVerMcOf.FullName, $"{mcVerOf}.jar"));
            if (!fileJarMc.Exists)
            {
                ShowMessageVersionNotFound(mcVer);
                throw new FileNotFoundException($"{mcVer}.jar file not found");
            }
            File.Copy(fileJarMc.FullName, fileJarMcOf.FullName);

            var fileJsonMc = new FileInfo(Path.Combine(dirVerMc.FullName, $"{mcVer}.json"));
            var fileJsonMcOf = new FileInfo(Path.Combine(dirVerMcOf.FullName, $"{mcVerOf}.json"));
            if (!fileJsonMc.Exists)
            {
                ShowMessageVersionNotFound(mcVer);
                throw new FileNotFoundException($"{mcVer}.json file not found");
            }
            File.Copy(fileJsonMc.FullName, fileJsonMcOf.FullName);
        }

        private static void ShowMessageVersionNotFound(string mcVer)
        {
            Console.WriteLine($"Cannot find Minecraft {mcVer}.\nYou must download and start Minecraft {mcVer} once in the official launcher.");
        }

        private static string GetLaunchwrapperVersion()
        {
            using (var stream = JarFileZrp.GetResourceStream("launchwrapper-of.txt")
                          ?? throw new FileNotFoundException("Resource not found: launchwrapper-of.txt"))
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            {
                var str = reader.ReadToEnd().Trim();

                if (!Regex.IsMatch(str, @"^[0-9\.]+$"))
                    throw new FormatException($"Invalid launchwrapper version format: '{str}'");

                return str;
            }
        }
    }
}