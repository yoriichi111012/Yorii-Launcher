using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Yorii_Launcher.Helpers
{
    internal enum LoaderKind
    {
        Vanilla,
        Fabric,
        Forge,
        NeoForge
    }

    // one parsed installed version folder, e.g. fabric-loader-0.19.5-26.2.
    // DisplayBase is the plain "Fabric 26.2" form; DisplayName gains a
    // " (loader x)" suffix when several loader builds share one MC version.
    internal sealed class InstalledVersionInfo
    {
        public string FolderName { get; init; } = "";
        public LoaderKind Loader { get; init; } = LoaderKind.Vanilla;
        public string McVersion { get; init; } = "";
        public string LoaderVersion { get; init; } = "";
        public string DisplayBase { get; init; } = "";
        public string DisplayName { get; set; } = "";
    }

    // Single place that translates between on-disk loader ids
    // (fabric-loader-0.19.5-26.2, 1.21.1-forge1.21.1-52.0.0, neoforge-21.1.73)
    // and the display names shown in the UI (Fabric 26.2, ...).
    // Display-only: nothing on disk is ever renamed. Anything unrecognized
    // maps to itself, so unknown formats keep today's raw behavior.
    // All parsing is plain string/regex work: NativeAOT-safe, no reflection.
    internal static class VersionDisplay
    {
        private const string FabricFolderPrefix = "fabric-loader-";
        private const string NeoForgeFolderPrefix = "neoforge-";
        private const string LoaderSuffixPrefix = " (loader ";
        private const char LoaderSuffixSuffix = ')';

        public static string FabricPrefix => "Fabric ";
        public static string ForgePrefix => "Forge ";
        public static string NeoForgePrefix => "NeoForge ";

        // Parse one installed versions/<folder> name. versionsDir is used only
        // to read <folder>/<folder>.json for its authoritative inheritsFrom
        // (the MC version) — never required, name parsing is the fallback.
        // Returns null for vanilla folders and anything unrecognized.
        public static InstalledVersionInfo? ParseInstalledFolder(string folderName, string versionsDir)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return null;

            string folder = folderName.Trim();

            if (folder.StartsWith(FabricFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = folder[FabricFolderPrefix.Length..];
                var match = Regex.Match(rest, @"^(\d[\d.]*)-(\d.*)$");
                if (!match.Success)
                    return null;
                string mc = ReadInheritsFrom(versionsDir, folder) ?? match.Groups[2].Value;
                return new InstalledVersionInfo
                {
                    FolderName = folder,
                    Loader = LoaderKind.Fabric,
                    McVersion = mc,
                    LoaderVersion = match.Groups[1].Value,
                    DisplayBase = FabricPrefix + mc,
                };
            }

            if (folder.StartsWith(NeoForgeFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string loader = folder[NeoForgeFolderPrefix.Length..].Trim();
                if (loader.Length == 0)
                    return null;
                // a neoforge folder id carries no MC version — the json is the
                // only source. without it, leave the row raw.
                string? mc = ReadInheritsFrom(versionsDir, folder);
                if (string.IsNullOrWhiteSpace(mc))
                    return null;
                return new InstalledVersionInfo
                {
                    FolderName = folder,
                    Loader = LoaderKind.NeoForge,
                    McVersion = mc,
                    LoaderVersion = loader,
                    DisplayBase = NeoForgePrefix + mc,
                };
            }

            // forge ids look like 1.21.1-forge1.21.1-52.0.0 (MC up front).
            int forgeSep = folder.IndexOf("-forge", StringComparison.OrdinalIgnoreCase);
            if (forgeSep > 0)
            {
                string mc = ReadInheritsFrom(versionsDir, folder) ?? folder[..forgeSep].Trim();
                if (mc.Length == 0 || !char.IsDigit(mc[0]))
                    return null;
                string tail = folder[(forgeSep + "-forge".Length)..].TrimStart('-', ' ');
                // trailing segment is the forge build (52.0.0); tolerate old
                // single-segment shapes by taking the whole tail.
                int lastDash = tail.LastIndexOf('-');
                string loader = lastDash >= 0 ? tail[(lastDash + 1)..].Trim() : tail;
                if (loader.Length == 0)
                    loader = tail;
                return new InstalledVersionInfo
                {
                    FolderName = folder,
                    Loader = LoaderKind.Forge,
                    McVersion = mc,
                    LoaderVersion = loader,
                    DisplayBase = ForgePrefix + mc,
                };
            }

            return null;
        }

        // Build folder -> info for every loader folder, resolving collisions:
        // the newest loader build per (loader, mc) keeps the plain display
        // name, older ones gain " (loader x)". Vanilla/unknown folders are
        // skipped (callers keep them raw).
        public static Dictionary<string, InstalledVersionInfo> BuildDisplayMap(
            IEnumerable<string> folderNames, string versionsDir)
        {
            var parsed = new List<InstalledVersionInfo>();
            foreach (var folder in folderNames)
            {
                var info = ParseInstalledFolder(folder, versionsDir);
                if (info is not null)
                    parsed.Add(info);
            }

            var groups = new Dictionary<string, List<InstalledVersionInfo>>(StringComparer.Ordinal);
            foreach (var info in parsed)
            {
                string key = info.Loader + "\0" + info.McVersion;
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = [];
                list.Add(info);
            }

            var map = new Dictionary<string, InstalledVersionInfo>(StringComparer.Ordinal);
            foreach (var group in groups.Values)
            {
                // newest loader first (unparseable sorts last, folder-name tiebreak).
                group.Sort(static (a, b) =>
                {
                    bool pa = Version.TryParse(NormalizeLoaderVersion(a.LoaderVersion), out var va);
                    bool pb = Version.TryParse(NormalizeLoaderVersion(b.LoaderVersion), out var vb);
                    if (pa && pb)
                    {
                        int cmp = vb.CompareTo(va);
                        if (cmp != 0) return cmp;
                    }
                    else if (pa) return -1;
                    else if (pb) return 1;
                    return string.Compare(a.FolderName, b.FolderName, StringComparison.Ordinal);
                });

                for (int i = 0; i < group.Count; i++)
                {
                    var info = group[i];
                    info.DisplayName = i == 0
                        ? info.DisplayBase
                        : info.DisplayBase + LoaderSuffixPrefix + info.LoaderVersion + LoaderSuffixSuffix;
                    map[info.FolderName] = info;
                }
            }

            return map;
        }

        // Invert a display map for launch: every display variant (plain and
        // suffixed) points at its real on-disk folder id.
        public static Dictionary<string, string> BuildReverseMap(
            Dictionary<string, InstalledVersionInfo> displayMap)
        {
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var info in displayMap.Values)
                reverse[info.DisplayName] = info.FolderName;
            return reverse;
        }

        // Split a DISPLAY name ("Fabric 26.2", "Forge 26.2 (loader 52.0.0)",
        // "26.2") into loader kind + base MC version. Unknown -> Vanilla/raw.
        public static (LoaderKind Loader, string McVersion) SplitDisplay(string? display)
        {
            string name = StripLoaderSuffix(display ?? "").Trim();
            if (name.StartsWith(FabricPrefix, StringComparison.OrdinalIgnoreCase))
                return (LoaderKind.Fabric, name[FabricPrefix.Length..].Trim());
            if (name.StartsWith(NeoForgePrefix, StringComparison.OrdinalIgnoreCase))
                return (LoaderKind.NeoForge, name[NeoForgePrefix.Length..].Trim());
            if (name.StartsWith(ForgePrefix, StringComparison.OrdinalIgnoreCase))
                return (LoaderKind.Forge, name[ForgePrefix.Length..].Trim());
            return (LoaderKind.Vanilla, name);
        }

        public static bool IsLoaderDisplay(string? display)
            => SplitDisplay(display).Loader != LoaderKind.Vanilla;

        // Remove a trailing " (loader x)" collision suffix, if present.
        public static string StripLoaderSuffix(string display)
        {
            if (string.IsNullOrEmpty(display))
                return display;
            int idx = display.LastIndexOf(LoaderSuffixPrefix, StringComparison.Ordinal);
            if (idx >= 0 && display.EndsWith(LoaderSuffixSuffix))
                return display[..idx].TrimEnd();
            return display;
        }

        // Normalize a STORED version string (settings, instance metadata) to
        // its display form. Idempotent: display names (plain or suffixed) and
        // vanilla ids pass through untouched; unrecognized raws pass through
        // too. Needs the target versions dir for collision suffixes.
        public static string? NormalizeStored(string? stored, string versionsDir, IEnumerable<string>? folderNames = null)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return stored;
            string value = stored.Trim();
            if (IsLoaderDisplay(value))
                return value;
            try
            {
                folderNames ??= Directory.GetDirectories(versionsDir);
                var names = new List<string>();
                foreach (var dir in folderNames)
                    names.Add(Path.GetFileName(dir));
                var map = BuildDisplayMap(names, versionsDir);
                foreach (var info in map.Values)
                {
                    if (string.Equals(info.FolderName, value, StringComparison.OrdinalIgnoreCase))
                        return info.DisplayName;
                }
            }
            catch
            {
            }
            return value;
        }

        // Authoritative MC version for an installed folder, from its json.
        // Null when missing/unreadable (forge full-json copies have none).
        private static string? ReadInheritsFrom(string versionsDir, string folder)
        {
            try
            {
                string jsonPath = Path.Combine(versionsDir, folder, folder + ".json");
                string text = File.ReadAllText(jsonPath);
                var match = Regex.Match(text, "\"inheritsFrom\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            catch
            {
            }
            return null;
        }

        // Loader builds are plain dotted numerics; trim junk so Version can
        // compare them (52.0.0, 0.19.5, 21.1.73 all parse as-is).
        private static string NormalizeLoaderVersion(string loaderVersion)
            => loaderVersion.Trim().TrimEnd('.');
    }
}
