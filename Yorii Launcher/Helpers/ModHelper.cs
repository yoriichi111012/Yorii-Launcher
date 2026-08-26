using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class ModHelper
    {
        public static async Task<List<ModItem>> GetInstalledMods(string minecraftPath)
        {
            var mods = new List<ModItem>();

            var modsFolder = Path.Combine(minecraftPath, "mods");

            if (!Directory.Exists(modsFolder))
                return mods;

            var files = Directory.GetFiles(modsFolder)
                .Where(f => f.EndsWith(".jar") || f.EndsWith(".jar.disabled"))
                .ToArray();

            foreach (var file in files)
            {
                var mod = await ReadMod(file);

                mods.Add(mod);
            }

            return mods;
        }

        // read mod metadata from jar
        public static async Task<ModItem> ReadMod(string file)
        {
            var mod = new ModItem
            {
                Name = Path.GetFileNameWithoutExtension(file),

                Version = "Unknown",

                FilePath = file,

                IsEnabled = !file.EndsWith(".disabled"),

                ModId = ""
            };

            try
            {
                // open the jar as a zip and look for metadata files
                using var archive = ZipFile.OpenRead(file);

                // fabric
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var stream = fabricEntry.Open();
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    using JsonDocument doc = JsonDocument.Parse(json);

                    var root = doc.RootElement;

                    // name
                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        mod.Name = nameProp.GetString() ?? mod.Name;
                    }

                    // version
                    if (root.TryGetProperty("version", out var versionProp))
                    {
                        mod.Version = versionProp.ToString();
                    }

                    // id
                    if (root.TryGetProperty("id", out var idProp))
                    {
                        mod.ModId = idProp.GetString() ?? "";
                    }

                    // icon (can be string or object)
                    if (root.TryGetProperty("icon", out var iconProp))
                    {
                        try
                        {
                            string? iconPath = null;

                            // icon could be string
                            if (iconProp.ValueKind == JsonValueKind.String)
                            {
                                iconPath = iconProp.GetString();
                            }

                            // icon could be object
                            else if (iconProp.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in iconProp.EnumerateObject())
                                {
                                    iconPath = prop.Value.GetString();

                                    break;
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(iconPath))
                                await TryLoadModIconAsync(archive, iconPath, mod);
                        }
                        catch
                        {
                        }
                    }

                    return mod;
                }

                // quilt support
                var quiltEntry = archive.GetEntry("quilt.mod.json");

                if (quiltEntry != null)
                {
                    mod.Version = "Quilt Mod";

                    return mod;
                }

                // forge / neoforge (meta-inf/mods.toml or meta-inf/neoforge.mods.toml)
                var modsTomlEntry = archive.GetEntry("META-INF/neoforge.mods.toml")
                    ?? archive.GetEntry("META-INF/mods.toml");

                if (modsTomlEntry != null)
                {
                    using var stream = modsTomlEntry.Open();
                    using var reader = new StreamReader(stream);
                    var toml = await reader.ReadToEndAsync();

                    var firstModsBlock = toml.IndexOf("[[mods]]", StringComparison.Ordinal);

                    if (firstModsBlock >= 0)
                    {
                        // read only the first [[mods]] table (stops at the next section)
                        string block = toml[firstModsBlock..];
                        int nextSection = block.IndexOf("[[", 8, StringComparison.Ordinal);

                        if (nextSection > 0)
                            block = block[..nextSection];

                        mod.Name = ReadTomlValue(block, "displayName") ?? mod.Name;
                        mod.Version = ReadTomlValue(block, "version") ?? mod.Version;
                        mod.ModId = ReadTomlValue(block, "modId") ?? mod.ModId;

                        // icon: neoforge uses iconfile, forge historically used
                        // logofile, and newer builds declare a [[icons]] table
                        string? iconPath = ReadTomlValue(block, "iconFile")
                            ?? ReadTomlValue(block, "logoFile");

                        if (string.IsNullOrWhiteSpace(iconPath))
                            iconPath = ReadTomlIconsFile(toml);

                        if (!string.IsNullOrWhiteSpace(iconPath))
                            await TryLoadModIconAsync(archive, iconPath, mod);
                    }

                    return mod;
                }
            }
            catch
            {
            }

            return mod;
        }

        // loads an icon image from inside the jar into the mod item
        private static async Task TryLoadModIconAsync(ZipArchive archive, string iconPath, ModItem mod)
        {
            try
            {
                var iconEntry = archive.GetEntry(iconPath);

                if (iconEntry == null)
                    return;

                using var iconStream = iconEntry.Open();
                using var memory = new MemoryStream();
                await iconStream.CopyToAsync(memory);
                memory.Position = 0;
                var randomAccessStream = memory.AsRandomAccessStream();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(randomAccessStream);
                mod.Icon = bitmap;
            }
            catch
            {
            }
        }

        // reads a quoted string value for the given key from a toml table
        private static string? ReadTomlValue(string toml, string key)
        {
            var match = Regex.Match(toml, $@"^\s*{Regex.Escape(key)}\s*=\s*""(?<value>[^""]*)""", RegexOptions.Multiline);
            return match.Success ? match.Groups["value"].Value : null;
        }

        // reads the first file path from a [[icons]] table (per-size icon entries)
        private static string? ReadTomlIconsFile(string toml)
        {
            var iconsStart = toml.IndexOf("[[icons]]", StringComparison.Ordinal);

            if (iconsStart < 0)
                return null;

            string block = toml[iconsStart..];
            int nextSection = block.IndexOf("[[", 8, StringComparison.Ordinal);

            if (nextSection > 0)
                block = block[..nextSection];

            return ReadTomlValue(block, "file");
        }
    }
}