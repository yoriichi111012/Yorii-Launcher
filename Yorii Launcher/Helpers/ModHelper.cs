using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
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
                            {
                                var iconEntry = archive.GetEntry(iconPath);

                                if (iconEntry != null)
                                {
                                    using var iconStream = iconEntry.Open();
                                    using var memory = new MemoryStream();
                                    await iconStream.CopyToAsync(memory);
                                    memory.Position = 0;
                                    var randomAccessStream = memory.AsRandomAccessStream();
                                    var bitmap = new BitmapImage();
                                    await bitmap.SetSourceAsync(randomAccessStream);
                                    mod.Icon = bitmap;
                                }
                            }
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
            }
            catch
            {
            }

            return mod;
        }
    }
}