using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
	public static class InstanceManager
    {
        private const string InstanceFileName = "instance.yaml";
        private const string LegacyInstanceFileName = "instance.json";

        private const string YoriiSkinsLoaderFabricJar = "yoriiSkinsLoader-fabric.jar";
        private const string YoriiSkinsLoaderForgeJar = "yoriiSkinsLoader-forge.jar";
        private const string YoriiSkinsLoaderNeoForgeJar = "yoriiSkinsLoader-neoforge.jar";

        private static readonly Version MinYoriiSkinsLoaderFabricVersion = new(1, 8, 9);
        private static readonly Version MinYoriiSkinsLoaderForgeVersion = new(1, 8, 0);
        private static readonly Version MinYoriiSkinsLoaderNeoForgeVersion = new(1, 20, 2);

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

            // find the instance that matches this id
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

                if (!File.Exists(metadataPath) && !File.Exists(Path.Combine(directory, LegacyInstanceFileName)))
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

            // sort newest played first then alphabetical
            return [.. instances.OrderByDescending(i => i.LastPlayedAt).ThenBy(i => i.Name)];
        }

        public static LauncherInstance CreateInstance(string name, string? sourceIconPath, double scale = 1.0)
        {
            var id = CreateUniqueId(name);
            Logger.Info($"Creating instance: {name} (id: {id})");
            var instancePath = Path.Combine(InstancesRoot, id);
            var minecraftPath = Path.Combine(instancePath, "minecraft");

            // make the usual minecraft folders
            Directory.CreateDirectory(Path.Combine(minecraftPath, "versions"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "mods"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "modpacks"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "resourcepacks"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "shaderpacks"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "config"));
            Directory.CreateDirectory(Path.Combine(minecraftPath, "saves"));

            string? iconFileName = null;

            // copy icon if we got one
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
            Logger.Info($"Deleting instance: {instance.Name} (id: {instance.Id})");
            var fullInstancePath = Path.GetFullPath(instance.InstancePath);
            var fullRootPath = Path.GetFullPath(InstancesRoot);

            // make sure we dont delete outside instances folder
            if (!fullInstancePath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(fullInstancePath))
                Directory.Delete(fullInstancePath, true);

            if (GetSelectedInstanceId() == instance.Id)
                ClearSelectedInstance();
        }

        public static void MarkPlayed(string instanceId)
        {
            Logger.Info($"Marking instance played: {instanceId}");
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

        public static void RenameInstance(LauncherInstance instance, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var instancePath = Path.Combine(InstancesRoot, instance.Id);
            var metadata = LoadMetadata(instancePath);

            if (metadata == null)
                return;

            metadata.Name = name.Trim();
            SaveMetadata(instancePath, metadata);
        }

        // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
        public static void EnsureYoriiSkinsLoaderInstalled()
        {
            try
            {
                // no instances so just install into global minecraft folder
                if (!SettingsManager.Current.InstancesEnabled)
                {
                    string activePath = SettingsManager.Current.GetActiveMinecraftPath();
                    if (!string.IsNullOrEmpty(activePath))
                        InstallYoriiSkinsLoader(activePath, SettingsManager.Current.SelectedVersion);

                    return;
                }

                foreach (var instance in LoadInstances())
                {
                    try
                    {
                        InstallYoriiSkinsLoader(instance.MinecraftPath, instance.MinecraftVersion);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to install yoriiSkinsLoader into instance '{instance.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to auto-install yoriiSkinsLoader: {ex.Message}");
            }
        }

        // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
        public static bool IsYoriiSkinsLoaderSupported(string? version)
        {
            if (!TryGetLoaderInfo(version, out var loader, out var baseVersion))
                return false;

            return baseVersion >= GetMinLoaderVersion(loader);
        }

        private static void InstallYoriiSkinsLoader(string minecraftPath, string? versionString)
        {
            if (!TryGetLoaderInfo(versionString, out var loader, out var baseVersion))
                return;

            string jarName = loader switch
            {
                ModLoaderKind.Fabric => YoriiSkinsLoaderFabricJar,
                ModLoaderKind.Forge => YoriiSkinsLoaderForgeJar,
                ModLoaderKind.NeoForge => YoriiSkinsLoaderNeoForgeJar,
                _ => ""
            };

            if (string.IsNullOrEmpty(jarName) || baseVersion < GetMinLoaderVersion(loader))
                return;

            string bundledJar = Path.Combine(AppContext.BaseDirectory, jarName);
            if (!File.Exists(bundledJar))
            {
                Logger.Warn($"{jarName} not found next to the launcher, skipping auto-install");
                return;
            }

            string modsDir = Path.Combine(minecraftPath, "mods");
            Directory.CreateDirectory(modsDir);

            string destJar = Path.Combine(modsDir, jarName);

            // already up to date so skip
            if (File.Exists(destJar) && new FileInfo(bundledJar).Length == new FileInfo(destJar).Length)
                return;

            // clean out old jars so only the right loader one stays
            RemoveOtherYoriiSkinsJars(modsDir, jarName);

            File.Copy(bundledJar, destJar, true);
            Logger.Info($"Installed {jarName} into '{minecraftPath}' ({versionString})");
        }

        // remove wrong loader jars from mods folder
        private static void RemoveOtherYoriiSkinsJars(string modsDir, string keepJar)
        {
            string[] candidates =
            [
                YoriiSkinsLoaderFabricJar,
                YoriiSkinsLoaderForgeJar,
                YoriiSkinsLoaderNeoForgeJar,
                "yoriiSkinsLoader.jar"
            ];

            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate, keepJar, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var suffix in new[] { "", ".disabled" })
                {
                    string stale = Path.Combine(modsDir, candidate + suffix);
                    if (File.Exists(stale))
                    {
                        try
                        {
                            File.Delete(stale);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to remove stale yoriiSkinsLoader jar {stale}: {ex.Message}");
                        }
                    }
                }
            }

            // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
            try
            {
                foreach (var file in Directory.EnumerateFiles(modsDir, "*.jar*"))
                {
                    string name = Path.GetFileName(file);
                    if (name.StartsWith("customskinloader", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(file);
                            Logger.Info($"Removed conflicting CustomSkinLoader jar '{name}'");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to remove conflicting CustomSkinLoader jar {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to scan mods dir for CustomSkinLoader jars: {ex.Message}");
            }
        }

        // split loader version string into loader type and base mc version, false for vanilla
        private static bool TryGetLoaderInfo(string? version, out ModLoaderKind loader, out Version baseVersion)
        {
            loader = ModLoaderKind.Vanilla;
            baseVersion = new Version(0, 0, 0);

            if (string.IsNullOrWhiteSpace(version))
                return false;

            string trimmed = version.Trim();

            if (trimmed.StartsWith("Fabric ", StringComparison.OrdinalIgnoreCase))
                loader = ModLoaderKind.Fabric;
            else if (trimmed.StartsWith("Forge ", StringComparison.OrdinalIgnoreCase))
                loader = ModLoaderKind.Forge;
            else if (trimmed.StartsWith("NeoForge ", StringComparison.OrdinalIgnoreCase))
                loader = ModLoaderKind.NeoForge;
            else
                return false;

            string prefix = loader switch
            {
                ModLoaderKind.Fabric => "Fabric ",
                ModLoaderKind.Forge => "Forge ",
                ModLoaderKind.NeoForge => "NeoForge ",
                _ => ""
            };

            string baseVersionString = trimmed[prefix.Length..].Trim();
            return Version.TryParse(baseVersionString, out baseVersion);
        }

        private static Version GetMinLoaderVersion(ModLoaderKind loader)
        {
            return loader switch
            {
                ModLoaderKind.Fabric => MinYoriiSkinsLoaderFabricVersion,
                ModLoaderKind.Forge => MinYoriiSkinsLoaderForgeVersion,
                ModLoaderKind.NeoForge => MinYoriiSkinsLoaderNeoForgeVersion,
                _ => new Version(int.MaxValue, 0, 0)
            };
        }

        private enum ModLoaderKind
        {
            Vanilla,
            Fabric,
            Forge,
            NeoForge
        }

        private static void SaveMetadata(string instancePath, InstanceMetadata metadata)
        {
            File.WriteAllText(Path.Combine(instancePath, InstanceFileName), LauncherYaml.Serialize(metadata));
        }

        private static InstanceMetadata? LoadMetadata(string instancePath)
        {
            var metadataPath = Path.Combine(instancePath, InstanceFileName);

            if (File.Exists(metadataPath))
                return LauncherYaml.Deserialize<InstanceMetadata>(File.ReadAllText(metadataPath));

            var legacyMetadataPath = Path.Combine(instancePath, LegacyInstanceFileName);
            if (!File.Exists(legacyMetadataPath))
                return null;

            var legacy = JsonSerializer.Deserialize(
                File.ReadAllText(legacyMetadataPath), LauncherJsonContext.Default.InstanceMetadata);
            if (legacy != null)
                SaveMetadata(instancePath, legacy);
            return legacy;
        }

        private static string CreateUniqueId(string name)
        {
            Directory.CreateDirectory(InstancesRoot);

            // make name safe for folder
            var safeName = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            safeName = string.Join("-", safeName.Split('-', StringSplitOptions.RemoveEmptyEntries));

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "instance";

            var id = safeName;
            var counter = 2;

            // add number if name already taken
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

            // absolute path just use it
            if (Path.IsPathRooted(iconPath))
                return iconPath;

            // relative so combine with instance folder
            return Path.Combine(instancePath, iconPath);
        }

        private static BitmapImage? CreateIcon(string? iconPath, double scale = 1.0)
        {
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
                return null;

            // scale to 62px, bigger for high dpi
            var decodeSize = (int)Math.Round(62 * scale);
            return new BitmapImage(new Uri(iconPath)) { DecodePixelWidth = decodeSize, DecodePixelHeight = decodeSize };
        }

    }
}