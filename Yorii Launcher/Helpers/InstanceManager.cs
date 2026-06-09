using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
	public static class InstanceManager
    {
        private const string InstanceFileName = "instance.json";

        public static string InstancesRoot => Path.Combine(GetBaseMinecraftPath(), "instances");

        public static string GetBaseMinecraftPath()
        {
            return SettingsManager.Current.MinecraftPath;
        }

        public static void SetBaseMinecraftPath(string path)
        {
            SettingsManager.Current.MinecraftPath = path;
            SettingsManager.SaveSettings();
        }

        public static string? GetSelectedInstanceId()
        {
            var id = SettingsManager.Current.SelectedInstanceId;
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public static void SetSelectedInstance(string instanceId)
        {
            SettingsManager.Current.SelectedInstanceId = instanceId;
            SettingsManager.SaveSettings();
        }

        public static void ClearSelectedInstance()
        {
            SettingsManager.Current.SelectedInstanceId = "";
            SettingsManager.SaveSettings();
        }

        public static LauncherInstance? GetSelectedInstance()
        {
            var selectedId = GetSelectedInstanceId();

            if (string.IsNullOrWhiteSpace(selectedId))
                return null;

            // match id to loaded instances
            return LoadInstances().FirstOrDefault(i => i.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        }

        public static string? GetSelectedInstanceVersion()
        {
            return GetSelectedInstance()?.MinecraftVersion;
        }

        public static void SetSelectedInstanceVersion(string version)
        {
            var selectedId = GetSelectedInstanceId();

            if (string.IsNullOrWhiteSpace(selectedId))
                return;

            var instancePath = Path.Combine(InstancesRoot, selectedId);
            var metadata = LoadMetadata(instancePath);

            if (metadata == null)
                return;

            metadata.MinecraftVersion = version;
            SaveMetadata(instancePath, metadata);
        }

        public static List<LauncherInstance> LoadInstances(double scale = 1.0)
        {
            Directory.CreateDirectory(InstancesRoot);

            var instances = new List<LauncherInstance>();

            foreach (var directory in Directory.GetDirectories(InstancesRoot))
            {
                var metadataPath = Path.Combine(directory, InstanceFileName);

                if (!File.Exists(metadataPath))
                    continue;

                try
                {
                    var metadata = LoadMetadata(directory);

                    if (metadata == null || string.IsNullOrWhiteSpace(metadata.Id))
                        continue;

                    var minecraftPath = Path.Combine(directory, "minecraft");
                    var iconPath = ResolveIconPath(directory, metadata.IconPath);

                    instances.Add(new LauncherInstance
                    {
                        Id = metadata.Id,
                        Name = string.IsNullOrWhiteSpace(metadata.Name) ? metadata.Id : metadata.Name,
                        IconPath = iconPath,
                        MinecraftPath = minecraftPath,
                        InstancePath = directory,
                        MinecraftVersion = metadata.MinecraftVersion,
                        CreatedAt = metadata.CreatedAt,
                        LastPlayedAt = metadata.LastPlayedAt,
                        Icon = CreateIcon(iconPath, scale)
                    });
                }
                catch
                {
                }
            }

            // newest played first, then alphabetical
            return [.. instances.OrderByDescending(i => i.LastPlayedAt).ThenBy(i => i.Name)];
        }

        public static LauncherInstance CreateInstance(string name, string? sourceIconPath, double scale = 1.0)
        {
            var id = CreateUniqueId(name);
            var instancePath = Path.Combine(InstancesRoot, id);
            var minecraftPath = Path.Combine(instancePath, "minecraft");

            // create standard minecraft folders
            Directory.CreateDirectory(Path.Combine(minecraftPath, "versions"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "mods"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "resourcepacks"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "shaderpacks"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "config"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "saves"));

            string? iconFileName = null;

            // copy icon if provided
            if (!string.IsNullOrWhiteSpace(sourceIconPath) && File.Exists(sourceIconPath))
            {
                var extension = Path.GetExtension(sourceIconPath);

                if (string.IsNullOrWhiteSpace(extension))
                    extension = ".png";

                iconFileName = "icon" + extension;
                File.Copy(sourceIconPath, Path.Combine(instancePath, iconFileName), true);
            }

            var metadata = new InstanceMetadata
            {
                Id = id,
                Name = name,
                IconPath = iconFileName,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            SaveMetadata(instancePath, metadata);

            var iconPath = ResolveIconPath(instancePath, iconFileName);

            return new LauncherInstance
            {
                Id = id,
                Name = name,
                IconPath = iconPath,
                MinecraftPath = minecraftPath,
                InstancePath = instancePath,
                CreatedAt = metadata.CreatedAt,
                Icon = CreateIcon(iconPath, scale)
            };
        }

        public static void DeleteInstance(LauncherInstance instance)
        {
            var fullInstancePath = Path.GetFullPath(instance.InstancePath);
            var fullRootPath = Path.GetFullPath(InstancesRoot);

            // path traversal check
            if (!fullInstancePath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(fullInstancePath))
                Directory.Delete(fullInstancePath, true);

            if (GetSelectedInstanceId() == instance.Id)
                ClearSelectedInstance();
        }

        public static void MarkPlayed(string instanceId)
        {
            var instancePath = Path.Combine(InstancesRoot, instanceId);
            var metadataPath = Path.Combine(instancePath, InstanceFileName);

            if (!File.Exists(metadataPath))
                return;

            var metadata = LoadMetadata(instancePath);

            if (metadata == null)
                return;

            metadata.LastPlayedAt = DateTimeOffset.UtcNow.ToString("O");
            SaveMetadata(instancePath, metadata);
        }

        private static void SaveMetadata(string instancePath, InstanceMetadata metadata)
        {
            var json = JsonSerializer.Serialize(metadata, LauncherJsonContext.Default.InstanceMetadata);
            File.WriteAllText(Path.Combine(instancePath, InstanceFileName), json);
        }

        private static InstanceMetadata? LoadMetadata(string instancePath)
        {
            var metadataPath = Path.Combine(instancePath, InstanceFileName);

            if (!File.Exists(metadataPath))
                return null;

            return JsonSerializer.Deserialize(File.ReadAllText(metadataPath), LauncherJsonContext.Default.InstanceMetadata);
        }

        private static string CreateUniqueId(string name)
        {
            Directory.CreateDirectory(InstancesRoot);

            // sanitize name to folder-safe string
            var safeName = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            safeName = string.Join("-", safeName.Split('-', StringSplitOptions.RemoveEmptyEntries));

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "instance";

            var id = safeName;
            var counter = 2;

            // append number if name already exists
            while (Directory.Exists(Path.Combine(InstancesRoot, id)))
            {
                id = $"{safeName}-{counter}";
                counter++;
            }

            return id;
        }

        private static string? ResolveIconPath(string instancePath, string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
                return null;

            // absolute path, use as is
            if (Path.IsPathRooted(iconPath))
                return iconPath;

            // relative, combine with instance folder
            return Path.Combine(instancePath, iconPath);
        }

        private static BitmapImage? CreateIcon(string? iconPath, double scale = 1.0)
        {
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
                return null;

            // scale to 62px base
            var decodeSize = (int)Math.Round(62 * scale);
            return new BitmapImage(new Uri(iconPath)) { DecodePixelWidth = decodeSize, DecodePixelHeight = decodeSize };
        }

        internal sealed class InstanceMetadata
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("iconPath")]
            public string? IconPath { get; set; }

            [JsonPropertyName("minecraftVersion")]
            public string? MinecraftVersion { get; set; }

            [JsonPropertyName("createdAt")]
            public string? CreatedAt { get; set; }

            [JsonPropertyName("lastPlayedAt")]
            public string? LastPlayedAt { get; set; }
        }
    }
}
