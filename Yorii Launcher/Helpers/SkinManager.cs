using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class SkinManager
    {
        private const string GitHubClientId = "Ov23linudy5mTRqKZQCI";
        private const string GitHubOAuthUrl = "https://github.com/login/oauth/authorize";
        private const int CallbackPort = 5190;
public const string IndexRepoOwner = "yorii-accounts";

        public const string IndexRepo = "yorii-skins";
        private const string IndexFile = "index.json";

        public const string WorkerBaseUrl = "https://yorii-worker.yoriiskin.workers.dev";

        private static readonly HttpClient http = new();

        private static readonly JsonSerializerOptions SkinJsonOptions = new() { WriteIndented = true };

        // local index snapshot, the single source of truth for the ui. seeded from
        // disk at startup, replaced only with fresh github api data and updated
        // optimistically the moment a mutation succeeds so the ui never shows stale
        // cdn state
        private static List<ProfileEntry>? _profilesCache;

        // set on every confirmed mutation, the github api is eventually consistent
        // and can lag the workers commit by up to ~30s so while a mutation is recent
        // refresh results are ignored and can never revert a confirmed
        // upload/delete (measured lag so far ~16s)
        private static DateTime _lastMutationAt = DateTime.MinValue;
        private static readonly TimeSpan MutationPropagationWindow = TimeSpan.FromSeconds(25);

        public static bool MutationPending =>
            DateTime.UtcNow - _lastMutationAt < MutationPropagationWindow;

        // the index is mirrored to disk so the skins page renders the last known
        // state instantly (offline included) and revalidates in the background
        // instead of waiting on the raw cdn
        private static readonly string LocalIndexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yorii Launcher", "skins-index.json");

        static SkinManager()
        {
            try
            {
                if (File.Exists(LocalIndexPath))
                {
                    _profilesCache = ParseProfiles(File.ReadAllText(LocalIndexPath));
                }
            }
            catch
            {
                // corrupt or missing snapshot, a normal fetch will replace it
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based JSON is intentionally enabled via JsonSerializerIsReflectionEnabledByDefault.")]
        private static void PersistIndexCache()
        {
            try
            {
                Dictionary<string, object?> players = [];
                foreach (var p in _profilesCache ?? [])
                {
                    players[p.Username] = new Dictionary<string, object?>
                    {
                        ["kind"] = p.Kind,
                        ["owner"] = p.Owner,
                        ["uuid"] = p.Uuid,
                        ["skinUrl"] = p.SkinUrl
                    };
                }
                string dir = Path.GetDirectoryName(LocalIndexPath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(LocalIndexPath, JsonSerializer.Serialize(new { players }));
            }
            catch
            {
                // best effort mirror only
            }
        }

        // downloaded skin bytes are cached for the session so hash checking and
        // the head preview share a single request per profile
        private static readonly ConcurrentDictionary<string, (byte[] Bytes, DateTime FetchedAt)> skinBytesCache = new();
        private static readonly TimeSpan SkinBytesTtl = TimeSpan.FromMinutes(10);
        private static readonly SemaphoreSlim downloadThrottle = new(6);

        // sync (local vs remote hash) results, so we don't re-download every
        // skin on each visit just to re-verify
        private static readonly ConcurrentDictionary<string, (SkinSyncInfo Info, DateTime CheckedAt)> syncCache = new();
        private static readonly TimeSpan SyncTtl = TimeSpan.FromSeconds(60);

        private static string GitHubToken => SettingsManager.Current.GitHubToken ?? "";
        private static string GitHubUsername => SettingsManager.Current.GitHubUsername ?? "";

        public static bool IsLoggedIn => SettingsManager.Current.IsGitHubLoggedIn;

        private static HttpListener? _listener;
        private static TaskCompletionSource<string>? _authTcs;

        public static string GetOAuthUrl(string state)
        {
            // repo scope is required for the worker to create the private yorii-profiles
            // repository on first private upload via POST /user/repos (installation
            // tokens cannot create user repos). The token is stored locally and only
            // sent to the worker (Authorization: Bearer) for auth/repo creation.
            return $"{GitHubOAuthUrl}?client_id={GitHubClientId}&redirect_uri=http://localhost:{CallbackPort}/callback&state={Uri.EscapeDataString(state)}&scope=read:user repo";
        }

        public static async Task AuthenticateWithGitHub()
        {
            string state = Guid.NewGuid().ToString("N");
            string url = GetOAuthUrl(state);
            _authTcs = new TaskCompletionSource<string>();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{CallbackPort}/");
            _listener.Start();

            _ = Task.Run(() => HandleCallbackAsync(_authTcs));

            OpenBrowser(url);

            string code = await _authTcs.Task;
            StopListener();

            if (string.IsNullOrEmpty(code))
                throw new Exception("Authorization failed or cancelled.");

            var (token, username) = await ExchangeCodeForToken(code);

            SettingsManager.Current.GitHubToken = token;
            SettingsManager.Current.GitHubUsername = username;
            SettingsManager.SaveSettings();
        }

        public static void Logout()
        {
            SettingsManager.Current.GitHubToken = null;
            SettingsManager.Current.GitHubUsername = null;
            SettingsManager.SaveSettings();
        }

        private static async Task HandleCallbackAsync(TaskCompletionSource<string> tcs)
        {
            try
            {
                var context = await _listener!.GetContextAsync();
                var request = context.Request;
                var code = request.QueryString["code"];
                var error = request.QueryString["error"];

                string responseHtml;
                if (!string.IsNullOrEmpty(code))
                {
                    // window.close() only works on script-opened windows; the
                    // window.open('', '_self') trick re-opens the current tab
                    // via script so the close is allowed in chrome/edge, with
                    // a meta-refresh to about:blank as a fallback
                    responseHtml = "<html><head><title>Yorii Launcher</title><meta http-equiv='refresh' content='2;url=about:blank'></head><body style='font-family:sans-serif;text-align:center;padding:40px'><h1>Success!</h1><p>You can close this window.</p><script>window.open('', '_self', '');window.close();</script></body></html>";
                    tcs.TrySetResult(code);
                }
                else
                {
                    responseHtml = $"<html><body style='font-family:sans-serif;text-align:center;padding:40px'><h1>Error</h1><p>{error}</p></body></html>";
                    tcs.TrySetResult("");
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
            catch
            {
                tcs.TrySetResult("");
            }
        }

        private static void StopListener()
        {
            try
            {
                _listener?.Stop();
                _listener?.Close();
                _listener = null;
            }
            catch { }
        }

        // called while the launcher is closing: aborts a login that is still
        // waiting on the localhost callback so the listener thread and the
        // pending continuation don't outlive the xaml dispatcher
        public static void CancelLogin()
        {
            StopListener();
            try
            {
                _authTcs?.TrySetResult("");
            }
            catch { }
        }

        // the worker exchanges the code so the oauth client secret never ships
        // in the launcher binary
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based JSON is intentionally enabled via JsonSerializerIsReflectionEnabledByDefault.")]
        private static async Task<(string token, string login)> ExchangeCodeForToken(string code)
        {
            using var web = new HttpClient();
            var content = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            var resp = await web.PostAsync($"{WorkerBaseUrl}/api/oauth/token", content);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Token exchange failed: {(int)resp.StatusCode} {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string token = root.TryGetProperty("access_token", out var tok) ? tok.GetString() ?? "" : "";
            string login = root.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(token))
                throw new Exception("Token exchange failed: no access_token in response.");
            return (token, login);
        }

        // the local snapshot is the single source of truth for the ui, it renders
        // instantly from memory/disk and is only ever replaced with fresh data
        // from the github api, never the raw cdn which lags commits by minutes and
        // would resurrect deleted profiles or hide new ones
        public static async Task<List<ProfileEntry>> GetProfilesAsync(
            CancellationToken cancellationToken = default)
        {
            if (_profilesCache is not null)
                return [.. _profilesCache];

            // first run with no snapshot yet - seed once from github
            return await RefreshProfilesAsync(cancellationToken);
        }

        // background revalidation straight from the github api (always current);
        // updates the in-memory + on-disk snapshot on success
        public static async Task<List<ProfileEntry>> RefreshProfilesAsync(
            CancellationToken cancellationToken = default)
        {
            var profiles = await FetchIndexViaApiAsync(cancellationToken);
            return ApplyServerSnapshot(profiles);
        }

        private static async Task<List<ProfileEntry>> FetchIndexViaApiAsync(
            CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{IndexRepoOwner}/{IndexRepo}/contents/{IndexFile}");
            req.Headers.Add("Accept", "application/vnd.github.raw");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("YoriiLauncher", "1.0"));
            var resp = await http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"GitHub index fetch failed: {(int)resp.StatusCode}");
            return ParseProfiles(await resp.Content.ReadAsStringAsync(cancellationToken));
        }

        private static List<ProfileEntry> ParseProfiles(string json)
        {
            List<ProfileEntry> profiles = [];
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("players", out var players))
                {
                    foreach (var prop in players.EnumerateObject())
                    {
                        string username = prop.Name;
                        string uuid = "";
                        string skinUrl = "";
                        string kind = "private";
                        string owner = "";
                        if (prop.Value.TryGetProperty("uuid", out var uuidEl)) uuid = uuidEl.GetString() ?? "";
                        if (prop.Value.TryGetProperty("skinUrl", out var urlEl)) skinUrl = urlEl.GetString() ?? "";
                        if (prop.Value.TryGetProperty("kind", out var kindEl)) kind = kindEl.GetString() ?? "private";
                        if (prop.Value.TryGetProperty("owner", out var ownerEl)) owner = ownerEl.GetString() ?? "";
                        profiles.Add(new ProfileEntry
                        {
                            Username = username,
                            Uuid = uuid,
                            SkinUrl = skinUrl,
                            Kind = kind,
                            Owner = owner
                        });
                    }
                }
            }
            catch { }
            return profiles;
        }

        // decides upload/delete auth (kind + owner) from the local snapshot when
        // possible (it's the freshest state we have); falls back to a raw
        // github fetch only when no snapshot exists yet
        private static async Task<List<ProfileEntry>> FetchRawIndexAsync(CancellationToken cancellationToken = default)
        {
            if (_profilesCache is not null)
                return [.. _profilesCache];

            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://raw.githubusercontent.com/{IndexRepoOwner}/{IndexRepo}/main/{IndexFile}");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("YoriiLauncher", "1.0"));
            var resp = await http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"GitHub index fetch failed: {(int)resp.StatusCode}");
            return ParseProfiles(await resp.Content.ReadAsStringAsync(cancellationToken));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based JSON is intentionally enabled via JsonSerializerIsReflectionEnabledByDefault.")]
        public static async Task AddOrUpdateProfile(string minecraftUsername, byte[] skinData, string kind)
        {
            if (kind == "private" && !IsLoggedIn) throw new Exception("Not logged into GitHub.");

            string base64 = Convert.ToBase64String(skinData);
            var payload = new Dictionary<string, object?>
            {
                ["username"] = minecraftUsername,
                ["skinBase64"] = base64,
                ["kind"] = kind
            };

            using var web = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{WorkerBaseUrl}/api/skins");
            if (kind == "private")
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
            }
            else
            {
                // public profiles are claimed by their token: send it when the
                // launcher holds one (update); creations mint one server-side
                if (SettingsManager.Current.ClaimTokens.TryGetValue(minecraftUsername, out var storedToken))
                    req.Headers.TryAddWithoutValidation("X-Yorii-Claim-Token", storedToken);
            }
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

            var resp = await web.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();

            // 412 -> the user hasn't installed the yorii github app yet
            if (resp.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                string? installUrl = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("installUrl", out var u)) installUrl = u.GetString();
                }
                catch { }
                if (!string.IsNullOrEmpty(installUrl)) OpenBrowser(installUrl);
                throw new Exception("Install the Yorii GitHub App to manage private profiles (browser opened).");
            }
            // 409 -> per-account private profile limit reached
            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                string? message = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var m)) message = m.GetString();
                }
                catch { }
                throw new Exception(message ?? "Profile limit reached.");
            }
            // 403 -> first-come wins: someone else owns the name
            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                string? message = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var m)) message = m.GetString();
                }
                catch { }
                throw new Exception(message ?? "This profile name is already taken.");
            }
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Upload failed: {(int)resp.StatusCode} {body}");

            // on creation the worker mints the claim token - keep it so the
            // profile stays updatable/deletable from this launcher
            if (kind == "public")
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("claimToken", out var tokenEl) &&
                        tokenEl.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(tokenEl.GetString()))
                        SettingsManager.Current.ClaimTokens[minecraftUsername] = tokenEl.GetString()!;
                    SettingsManager.SaveSettings();
                }
                catch { }
            }

            // apply the change to the local snapshot immediately (the server
            // confirmed it), so the ui shows the profile without waiting on
            // any re-fetch; everything after this point must never fail the
            // mutation or leave a stale list behind
            string entryUuid = "";
            string entrySkinUrl = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("uuid", out var uuidEl)) entryUuid = uuidEl.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("skinUrl", out var urlEl)) entrySkinUrl = urlEl.GetString() ?? "";
            }
            catch { }

            ApplyLocalProfile(new ProfileEntry
            {
                Username = minecraftUsername,
                Uuid = entryUuid,
                SkinUrl = entrySkinUrl,
                Kind = kind,
                Owner = kind == "private" ? GitHubUsername : ""
            });
            Logger.Info($"Upload confirmed for '{minecraftUsername}', snapshot count={_profilesCache?.Count ?? 0}");

            ClearSkinCache();
            InvalidateIndexCache(minecraftUsername);

            try
            {
                await RefreshIndexFromApiAsync();
            }
            catch { }
            try
            {
                await LoadProfilesIntoAccounts();
            }
            catch
            {
                // account-list sync must never fail an already-confirmed upload
            }

            // propagate the freshly uploaded skin to every instance's localskin
            await SyncSkinToAllInstancesAsync(minecraftUsername);
        }

        public static async Task RemoveProfile(string minecraftUsername)
        {
            // decide public vs private from the live index (public entries have
            // no owner and need their claim token; private ones need the github
            // account that owns them)
            var entries = await FetchRawIndexAsync();
            var entry = entries.FirstOrDefault(e => e.Username == minecraftUsername);
            if (entry is null)
            {
                // nothing on the server - nothing to remove
                ClearSkinCache();
                InvalidateIndexCache(minecraftUsername);
                RemoveLocalProfile(minecraftUsername);
                try
                {
                    await LoadProfilesIntoAccounts();
                }
                catch
                {
                    // best-effort account sync only
                }
                return;
            }

            if (entry.Kind == "private" && !IsLoggedIn)
                throw new Exception("Not logged into GitHub.");

            using var web = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{WorkerBaseUrl}/api/skins/{Uri.EscapeDataString(minecraftUsername)}");
            if (entry.Kind == "private")
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
            }
            else
            {
                if (!SettingsManager.Current.ClaimTokens.TryGetValue(minecraftUsername, out var storedToken))
                    throw new Exception("This public profile's claim token is missing; it can't be deleted from this launcher.");
                req.Headers.TryAddWithoutValidation("X-Yorii-Claim-Token", storedToken);
            }

            var resp = await web.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                string? message = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var m)) message = m.GetString();
                }
                catch { }
                throw new Exception(message ?? "You don't own this profile.");
            }
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Delete failed: {(int)resp.StatusCode} {body}");

            // server confirmed the delete - reflect it in the local snapshot
            // first, nothing after this point may leave the deleted profile
            // visible in the ui
            RemoveLocalProfile(minecraftUsername);
            Logger.Info($"Delete confirmed for '{minecraftUsername}', snapshot count={_profilesCache?.Count ?? 0}");
            ClearSkinCache();
            InvalidateIndexCache(minecraftUsername);

            try
            {
                SettingsManager.Current.ClaimTokens.Remove(minecraftUsername);
                SettingsManager.SaveSettings();
            }
            catch { }
            try
            {
                await RefreshIndexFromApiAsync();
            }
            catch { }
            try
            {
                await LoadProfilesIntoAccounts();
            }
            catch
            {
                // account-list sync must never fail an already-confirmed delete
            }
        }

        // server-verified rename for YoriiSkins: create new profile with same skin, then delete old
        // used by both Skins page and Manage Accounts edit - refuses if new name already taken
        public static async Task RenameProfileAsync(string oldUsername, string newUsername)
        {
            if (string.Equals(oldUsername, newUsername, StringComparison.Ordinal))
                return;
            if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Length > 16 || !System.Text.RegularExpressions.Regex.IsMatch(newUsername, @"^[A-Za-z0-9_]+$"))
                throw new Exception("Invalid new Minecraft username.");
            if (string.IsNullOrWhiteSpace(oldUsername))
                throw new Exception("Original profile not found.");

            var entries = await FetchRawIndexAsync();
            var oldEntry = entries.FirstOrDefault(e => e.Username == oldUsername);
            if (oldEntry == null)
                throw new Exception($"Profile '{oldUsername}' not found.");
            if (entries.Any(e => string.Equals(e.Username, newUsername, StringComparison.Ordinal)))
                throw new Exception($"'{newUsername}' is already taken.");

            // YoriiSkins rename must be verified via worker; Offline has no server entry
            if (oldEntry.Kind != "private" && oldEntry.Kind != "public")
                throw new Exception("Only YoriiSkins profiles can be renamed via server.");

            // fetch the published skin bytes for the old profile (worker proxy can read private repos)
            byte[]? skinBytes = await GetSkinBytesAsync(oldUsername, oldEntry.SkinUrl);
            if (skinBytes == null || skinBytes.Length == 0)
            {
                // fallback to local skin if remote not reachable
                string localPath = Path.Combine(GetLocalSkinsDir(), "skins", $"{oldUsername}.png");
                if (File.Exists(localPath))
                    skinBytes = await File.ReadAllBytesAsync(localPath);
            }
            if (skinBytes == null || skinBytes.Length == 0)
                throw new Exception("Could not fetch current skin for rename. Upload a skin first.");

            string kind = oldEntry.Kind;

            // create new profile with same skin - server enforces 403 if taken and 409 limit
            await AddOrUpdateProfile(newUsername, skinBytes, kind);

            // delete old - if this fails we have both names, but new is already verified
            try
            {
                await RemoveProfile(oldUsername);
            }
            catch (Exception ex)
            {
                // new succeeded, old remains - inform caller so they can retry delete
                throw new Exception($"Renamed to '{newUsername}' but could not delete old '{oldUsername}': {ex.Message}. Delete it manually on the Skins page.");
            }

            // also move local skin file
            try
            {
                string oldLocal = Path.Combine(GetLocalSkinsDir(), "skins", $"{oldUsername}.png");
                string newLocal = Path.Combine(GetLocalSkinsDir(), "skins", $"{newUsername}.png");
                if (File.Exists(oldLocal))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newLocal)!);
                    File.Move(oldLocal, newLocal, true);
                }
            }
            catch { }
        }

        // the local snapshot reflects mutations immediately; the api refresh
        // afterwards only confirms and fills in server-assigned fields
        private static void InvalidateIndexCache(string username)
        {
            skinBytesCache.TryRemove(username, out _);
            syncCache.TryRemove(username, out _);
        }

                // optimistic local updates: the ui shows the change the moment the
        // server confirms it, without waiting for any index re-fetch
        private static void ApplyLocalProfile(ProfileEntry entry)
        {
            _lastMutationAt = DateTime.UtcNow;
            _profilesCache ??= [];
            _profilesCache.RemoveAll(p => p.Username == entry.Username);
            _profilesCache.Add(entry);
            PersistIndexCache();
        }

        private static void RemoveLocalProfile(string username)
        {
            _lastMutationAt = DateTime.UtcNow;
            if (_profilesCache is null) return;
            _profilesCache.RemoveAll(p => p.Username == username);
            PersistIndexCache();
        }

        // applies a server-fetched index to the snapshot. right after a
        // confirmed mutation the github api can still serve the pre-mutation
        // index for up to ~30s (eventual consistency); during that window the
        // optimistic snapshot — built from the worker's authoritative response
        // — is the truth, and the server response is ignored entirely. merging
        // is not an option: a stale response looks identical whether it is
        // missing a new upload or still containing a deleted profile
        private static List<ProfileEntry> ApplyServerSnapshot(List<ProfileEntry> serverProfiles)
        {
            if (DateTime.UtcNow - _lastMutationAt < MutationPropagationWindow)
                return _profilesCache?.ToList() ?? serverProfiles;

            _profilesCache = serverProfiles;
            PersistIndexCache();
            return serverProfiles;
        }

        // the raw cdn can lag a fresh commit by minutes, so right after a
        // mutation the index is re-read straight from the github api (always
        // current) to confirm and complete the optimistic local update
        private static async Task RefreshIndexFromApiAsync()
        {
            try
            {
                var profiles = await FetchIndexViaApiAsync(CancellationToken.None);
                ApplyServerSnapshot(profiles);
            }
            catch
            {
                // best-effort only - the optimistic update already applied
            }
        }

        // save the published skin into the active instances customskinloader
        // localskin folder so the game loads it instantly without downloading again
        public static string? SaveLocalSkin(string minecraftUsername, byte[] skinData)
        {
            try
            {
                string dir = Path.Combine(GetLocalSkinsDir(), "skins");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"{minecraftUsername}.png");
                File.WriteAllBytes(path, skinData);
                return path;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save local skin for {minecraftUsername}: {ex.Message}");
                return null;
            }
        }

        // copy the players local csl skin into another minecraft path (used for newly created instances)
        // so the account head and the game load it instantly from the new instance
        // instead of falling back to the worker or waiting for the first launch
        // worker or waiting for the first launch
        public static void CopyLocalSkinToPath(string minecraftUsername, string targetMinecraftPath)
        {
            try
            {
                string source = Path.Combine(GetLocalSkinsDir(), "skins", $"{minecraftUsername}.png");
                if (!File.Exists(source)) return;

                string dir = Path.Combine(targetMinecraftPath, "CustomSkinLoader", "LocalSkin", "skins");
                Directory.CreateDirectory(dir);
                File.Copy(source, Path.Combine(dir, $"{minecraftUsername}.png"), true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to copy local skin for {minecraftUsername}: {ex.Message}");
            }
        }

        // csl always prefers the local skin over remote sources so a stale png in any
        // instance would keep beating the freshly uploaded github skin
        // this writes the players current skin into every instance (and the global folder)
        // runs at startup and after uploads so new and old instances all have the latest skin
        // runs at startup and after uploads so new and old instances all have the latest skin
        // all serve the latest skin
        public static async Task SyncSkinToAllInstancesAsync(string minecraftUsername)
        {
            if (string.IsNullOrWhiteSpace(minecraftUsername)) return;
            try
            {
                byte[]? data = null;
                try
                {
                    string url = $"{WorkerBaseUrl}/MinecraftSkins/{Uri.EscapeDataString(minecraftUsername)}.png";
                    data = await http.GetByteArrayAsync(url);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Skin sync fetch failed for {minecraftUsername}: {ex.Message}");
                }

                if (data == null || data.Length == 0)
                {
                    // offline / no published skin: fall back to the active local copy
                    string local = Path.Combine(GetLocalSkinsDir(), "skins", $"{minecraftUsername}.png");
                    if (File.Exists(local))
                        data = await File.ReadAllBytesAsync(local);
                }
                if (data == null || data.Length == 0) return;

                var roots = new List<string>();
                try
                {
                    foreach (var inst in InstanceManager.LoadInstances())
                        if (!string.IsNullOrEmpty(inst.MinecraftPath))
                            roots.Add(inst.MinecraftPath);
                }
                catch { }
                roots.Add(SettingsManager.Current.GetActiveMinecraftPath());

                foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        string dir = Path.Combine(root, "CustomSkinLoader", "LocalSkin", "skins");
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(Path.Combine(dir, $"{minecraftUsername}.png"), data);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Skin sync write failed for '{root}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to sync skin for {minecraftUsername}: {ex.Message}");
            }
        }
        public static void DeleteLocalSkin(string minecraftUsername)
        {
            try
            {
                string path = Path.Combine(GetLocalSkinsDir(), "skins", $"{minecraftUsername}.png");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to delete local skin for {minecraftUsername}: {ex.Message}");
            }
        }

        // preload the players current skin into the games skin caches before launching
        // launching so it is already on disk when the client starts rendering —
        // instantly at world join instead of after the clients lazy download
        // minecrafts own cache (assets/skins/<xx>/<username>.png) is keyed by profile
        // name so having the file there before the game starts means its used
        // immediately with no network round trip in game
        public static async Task PreloadSkinForLaunchAsync(string minecraftUsername, string sessionUuid, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"{WorkerBaseUrl}/MinecraftSkins/{Uri.EscapeDataString(minecraftUsername)}.png";
                byte[] data = await http.GetByteArrayAsync(url, cancellationToken);
                if (data == null || data.Length == 0)
                    return;

                string root = SettingsManager.Current.GetActiveMinecraftPath();
                string prefix = minecraftUsername.Length >= 2
                    ? minecraftUsername[..2].ToLowerInvariant()
                    : minecraftUsername.ToLowerInvariant();
                string mcDir = Path.Combine(root, "assets", "skins", prefix);
                Directory.CreateDirectory(mcDir);
                File.WriteAllBytes(Path.Combine(mcDir, $"{minecraftUsername}.png"), data);

                SaveLocalSkin(minecraftUsername, data);

                // pre-fetch the full csl profile and write it to profilecache so
                // the game reads the skin instantly from disk instead of querying
                // every source — the skin shows immediately while the cape hunt
                // (mojang/cosmetica/minecraftcapes/...) still runs in the background
                await PrefetchCslProfileAsync(minecraftUsername, sessionUuid, root);

                ConfigureCslForLaunch(root);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to preload skin for {minecraftUsername}: {ex.Message}");
            }
        }

        // rewrite the games customskinloader.json so the local players skin resolves instantly
        // instead of the 1-2s multi source profile batch
        // 1. localskin is moved to the front of the loadlist — csl checks sources
        // top-to-bottom and stops at the first skin found, and localskin is a
        // plain file read (no network round-trips)
        // 2. yoriiskins is placed just above mojang — so if localskin has no
        // preloaded skin, yoriiskins (our worker) is checked before mojang
        // 3. enablelocalprofilecache is turned on — the resolved profile is cached
        // to disk, so respawns, server joins and relaunches reuse it instead of
        // re-querying every skin source
        // 4. enablecape is turned on — capes from mojang/cosmetica/minecraftcapes
        // are preserved
        // everything else in the json is preserved
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based JSON is intentionally enabled via JsonSerializerIsReflectionEnabledByDefault.")]
        public static void ConfigureCslForLaunch(string root)
        {
            try
            {
                string cfgPath = Path.Combine(root, "CustomSkinLoader", "CustomSkinLoader.json");
                if (!File.Exists(cfgPath)) return;

                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));

                List<JsonElement> loadlist = [];
                foreach (var item in doc.RootElement.GetProperty("loadlist").EnumerateArray())
                    loadlist.Add(item);

                JsonElement? localSkin = null;
                JsonElement? yoriiSkins = null;
                int mojangIdx = -1;
                for (int i = 0; i < loadlist.Count; i++)
                {
                    string? name = loadlist[i].TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name == "LocalSkin") localSkin = loadlist[i];
                    if (name == "YoriiSkins") yoriiSkins = loadlist[i];
                    if (name == "Mojang") mojangIdx = i;
                }

                bool localSkinFirst = loadlist.Count > 0 &&
                    loadlist[0].TryGetProperty("name", out var firstName) && firstName.GetString() == "LocalSkin";
                bool profileCacheEnabled = doc.RootElement.TryGetProperty("enableLocalProfileCache", out var cacheEl) &&
                    cacheEl.ValueKind == JsonValueKind.True;
                bool capeEnabled = doc.RootElement.TryGetProperty("enableCape", out var capeEl) &&
                    capeEl.ValueKind == JsonValueKind.True;
                bool forceLoadDisabled = doc.RootElement.TryGetProperty("forceLoadAllTextures", out var fltEl) &&
                    fltEl.ValueKind == JsonValueKind.False;
                // keep other players fresh without nuking the whole cache every respawn
                bool cacheExpiryOk = doc.RootElement.TryGetProperty("cacheExpiry", out var ceEl) &&
                    ceEl.ValueKind == JsonValueKind.Number && ceEl.GetInt32() == 10;

                // check if yoriiskins is already just above mojang
                bool yoriiInPlace = false;
                if (yoriiSkins.HasValue && mojangIdx > 0)
                {
                    string? nameBeforeMojang = loadlist[mojangIdx - 1].TryGetProperty("name", out var n) ? n.GetString() : null;
                    yoriiInPlace = nameBeforeMojang == "YoriiSkins";
                }

                bool reorderNeeded = localSkin.HasValue && !localSkinFirst;
                if (!reorderNeeded && profileCacheEnabled && capeEnabled && forceLoadDisabled && cacheExpiryOk && yoriiInPlace) return;

                var kept = new List<JsonElement>(loadlist);
                if (reorderNeeded && localSkin.HasValue)
                {
                    kept.Remove(localSkin.Value);
                    kept.Insert(0, localSkin.Value);
                    // indices shift after insert
                    if (mojangIdx >= 0) mojangIdx++;
                }

                // make sure yoriiskins exists and is just above mojang
                if (yoriiSkins.HasValue)
                {
                    kept.Remove(yoriiSkins.Value);
                }
                else
                {
                    yoriiSkins = JsonSerializer.Deserialize<JsonElement>(@"{
                        ""name"": ""YoriiSkins"",
                        ""type"": ""CustomSkinAPI"",
                        ""root"": ""https://yorii-worker.yoriiskin.workers.dev/csl/""
                    }");
                }

                // find the mojang index again after the changes
                mojangIdx = -1;
                for (int i = 0; i < kept.Count; i++)
                {
                    if (kept[i].TryGetProperty("name", out var n) && n.GetString() == "Mojang")
                    {
                        mojangIdx = i;
                        break;
                    }
                }
                if (mojangIdx >= 0)
                {
                    kept.Insert(mojangIdx, yoriiSkins.Value);
                }
                else
                {
                    // yorii skins is our cloudflare auth server worker which fetches skins from github repo
                    int insertIdx = (kept.Count > 0 && kept[0].TryGetProperty("name", out var n0) && n0.GetString() == "LocalSkin") ? 1 : 0;
                    kept.Insert(insertIdx, yoriiSkins.Value);
                }

                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.NameEquals("loadlist"))
                        {
                            writer.WritePropertyName("loadlist");
                            writer.WriteStartArray();
                            foreach (var item in kept) item.WriteTo(writer);
                            writer.WriteEndArray();
                        }
                        else if (prop.NameEquals("enableLocalProfileCache"))
                        {
                            writer.WritePropertyName("enableLocalProfileCache");
                            writer.WriteBooleanValue(true);
                        }
                        else if (prop.NameEquals("enableCape"))
                        {
                            writer.WritePropertyName("enableCape");
                            writer.WriteBooleanValue(true);
                        }
                        else if (prop.NameEquals("forceLoadAllTextures"))
                        {
                            writer.WritePropertyName("forceLoadAllTextures");
                            writer.WriteBooleanValue(false);
                        }
                        else if (prop.NameEquals("cacheExpiry"))
                        {
                            writer.WritePropertyName("cacheExpiry");
                            writer.WriteNumberValue(10);
                        }
                        else
                        {
                            prop.WriteTo(writer);
                        }
                    }
                    // add cacheExpiry if it was missing entirely
                    bool hasCacheExpiry = false;
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.NameEquals("cacheExpiry")) { hasCacheExpiry = true; break; }
                    if (!hasCacheExpiry)
                    {
                        writer.WritePropertyName("cacheExpiry");
                        writer.WriteNumberValue(10);
                    }
                    writer.WriteEndObject();
                }

                File.WriteAllText(cfgPath, Encoding.UTF8.GetString(ms.ToArray()));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to configure CSL for launch: {ex.Message}");
            }
        }

        // queries the yoriiskins worker's csl endpoint and writes the resolved
        // profile directly into the game's profilecache — when the game joins a
        // server csl finds the skin on disk instantly (no network for the skin)
        // while the cape hunt (mojang/cosmetica/minecraftcapes/...) still runs
        // in the background, so the user sees their skin immediately
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based JSON is intentionally enabled via JsonSerializerIsReflectionEnabledByDefault.")]
        static async Task PrefetchCslProfileAsync(string minecraftUsername, string sessionUuid, string root)
        {
            try
            {
                string cslUrl = $"{WorkerBaseUrl}/csl/{Uri.EscapeDataString(minecraftUsername)}.json";
                using var resp = await http.GetAsync(cslUrl);
                if (!resp.IsSuccessStatusCode) return;

                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                // extract skin url from the worker's csl response format:
                // { "username": "...", "skins": { "default": "..." }, "cape": ... }
                string? skinUrl = null;
                if (doc.RootElement.TryGetProperty("skins", out var skins) &&
                    skins.TryGetProperty("default", out var defaultSkin))
                    skinUrl = defaultSkin.GetString();

                if (string.IsNullOrEmpty(skinUrl)) return;

                // write to profilecache in the format csl expects
                string cacheDir = Path.Combine(root, "CustomSkinLoader", "ProfileCache");
                Directory.CreateDirectory(cacheDir);

                string uuidNoDashes = sessionUuid.Replace("-", "");
                string fileName = $"GameProfile[id={sessionUuid}, name={minecraftUsername}, properties={{}}].json";
                string cachePath = Path.Combine(cacheDir, fileName);

                string profileJson = JsonSerializer.Serialize(new { skinUrl, model = "default" }, SkinJsonOptions);
                File.WriteAllText(cachePath, profileJson);

                Logger.Info($"Pre-fetched CSL profile for {minecraftUsername} → {cachePath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to pre-fetch CSL profile for {minecraftUsername}: {ex.Message}");
            }
        }

        // clears the game's skin caches so the freshly published skin is used on
        // the next launch instead of a stale cached copy:
        // - customskinloader's http cache (default 30-day expiry)
        // - csl resolved profile cache
        // - minecrafts own skin cache (assets/skins/<xx>/<username>.png) which keeps
        // serving the old skin after a reupload cause its keyed by username not
        // skin url
        public static void ClearSkinCache()
        {
            string root = SettingsManager.Current.GetActiveMinecraftPath();
            ClearDirectory(Path.Combine(root, "CustomSkinLoader", "caches"));
            ClearDirectory(Path.Combine(root, "CustomSkinLoader", "ProfileCache"));
            ClearDirectory(Path.Combine(root, "assets", "skins"));
        }

        private static void ClearDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to clear skin cache '{dir}': {ex.Message}");
            }
        }

        private static string GetLocalSkinsDir()
        {
            return Path.Combine(SettingsManager.Current.GetActiveMinecraftPath(), "CustomSkinLoader", "LocalSkin");
        }

        // compare the locally cached csl skin with the published one so the ui can
        // show if the skin is actually synced to github, the remote skin is always
        // fetched through the worker proxy cause it can read the private
        // yorii-profiles repo, the raw index url would 404 without a token
        public static async Task<SkinSyncInfo> GetSyncInfoAsync(
            string minecraftUsername,
            CancellationToken cancellationToken = default)
        {
            OnActiveInstanceChanged();

            if (syncCache.TryGetValue(minecraftUsername, out var cached) &&
                DateTime.UtcNow - cached.CheckedAt < SyncTtl)
                return cached.Info;

            string localPath = Path.Combine(GetLocalSkinsDir(), "skins", $"{minecraftUsername}.png");
            bool hasLocal = File.Exists(localPath);
            if (!hasLocal) return new SkinSyncInfo { HasLocal = false };

            try
            {
                byte[] localBytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
                string localHash = Convert.ToHexString(SHA256.HashData(localBytes));

                byte[]? remoteBytes = await GetSkinBytesAsync(minecraftUsername, null, cancellationToken);
                if (remoteBytes is null)
                    return new SkinSyncInfo { HasLocal = true, RemoteReachable = false };

                string remoteHash = Convert.ToHexString(SHA256.HashData(remoteBytes));

                var info = new SkinSyncInfo
                {
                    HasLocal = true,
                    RemoteReachable = true,
                    MatchesRemote = string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase)
                };
                syncCache[minecraftUsername] = (info, DateTime.UtcNow);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new SkinSyncInfo { HasLocal = true, RemoteReachable = false };
            }
        }

        // forces a fresh sync check for one profile, bypassing the ttl cache —
        // used right after an upload/delete, when the remote skin may not be
        // reachable through the worker proxy for a few seconds
        public static async Task<SkinSyncInfo> RecheckSyncAsync(
            string minecraftUsername,
            CancellationToken cancellationToken = default)
        {
            syncCache.TryRemove(minecraftUsername, out _);
            return await GetSyncInfoAsync(minecraftUsername, cancellationToken);
        }

        // local skins live inside the active instances folder so switching instances
        // instances changes what "local" means — caches keyed by username alone
        // instance a's skin for instance b so clear them on switch
        private static string? _skinsInstancePath;

        public static void OnActiveInstanceChanged()
        {
            var path = SettingsManager.Current.GetActiveMinecraftPath();
            if (_skinsInstancePath == path) return;
            _skinsInstancePath = path;
            skinBytesCache.Clear();
            syncCache.Clear();
        }

        // download the published skin for a profile or serve it from the in-session
        // cache, shared by hash checking and head previews so each profile costs one
        // request per session
        public static async Task<byte[]?> GetSkinBytesAsync(
            string minecraftUsername,
            string? skinUrl = null,
            CancellationToken cancellationToken = default)
        {
            OnActiveInstanceChanged();
            if (skinBytesCache.TryGetValue(minecraftUsername, out var cached) &&
                DateTime.UtcNow - cached.FetchedAt < SkinBytesTtl)
                return cached.Bytes;

            string url = string.IsNullOrWhiteSpace(skinUrl)
                ? $"{WorkerBaseUrl}/MinecraftSkins/{Uri.EscapeDataString(minecraftUsername)}.png"
                : skinUrl;

            try
            {
                await downloadThrottle.WaitAsync(cancellationToken);
                try
                {
                    byte[] bytes = await http.GetByteArrayAsync(url, cancellationToken);
                    if (bytes.Length > 0)
                    {
                        skinBytesCache[minecraftUsername] = (bytes, DateTime.UtcNow);
                        return bytes;
                    }
                }
                finally
                {
                    downloadThrottle.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }

            return null;
        }

        // serve the locally cached csl skin first for head previews and fall back to
        // the published skin so the ui shows the new head the moment a skin is saved
        // locally instead of waiting for the 2-3s github upload round trip
        // waiting the 2-3s github upload round-trip
        public static async Task<byte[]?> GetSkinBytesLocalFirstAsync(
            string minecraftUsername,
            string? skinUrl = null,
            CancellationToken cancellationToken = default)
        {
            OnActiveInstanceChanged();

            try
            {
                string localPath = Path.Combine(GetLocalSkinsDir(), "skins", $"{minecraftUsername}.png");
                if (File.Exists(localPath))
                {
                    byte[] local = await File.ReadAllBytesAsync(localPath, cancellationToken);
                    if (local.Length > 0) return local;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            return await GetSkinBytesAsync(minecraftUsername, skinUrl, cancellationToken);
        }

        // get a mojang accounts current skin: username -> profile id -> session
        // server textures -> skin url
        // so the account box can show real mojang skins instead of a blank head
        public static async Task<byte[]?> GetMojangSkinBytesAsync(
            string minecraftUsername,
            CancellationToken cancellationToken = default)
        {
            OnActiveInstanceChanged();
            if (skinBytesCache.TryGetValue(minecraftUsername, out var cached) &&
                DateTime.UtcNow - cached.FetchedAt < SkinBytesTtl)
                return cached.Bytes;

            try
            {
                await downloadThrottle.WaitAsync(cancellationToken);
                try
                {
                    // 1. username -> profile id
                    using var idResp = await http.GetAsync(
                        $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(minecraftUsername)}",
                        cancellationToken);
                    if (!idResp.IsSuccessStatusCode) return null;
                    using var idDoc = JsonDocument.Parse(await idResp.Content.ReadAsStringAsync(cancellationToken));
                    if (!idDoc.RootElement.TryGetProperty("id", out var idEl)) return null;
                    var uuid = idEl.GetString();
                    if (string.IsNullOrEmpty(uuid)) return null;

                    // 2. profile id -> base64 textures property
                    using var profResp = await http.GetAsync(
                        $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}",
                        cancellationToken);
                    if (!profResp.IsSuccessStatusCode) return null;
                    using var profDoc = JsonDocument.Parse(await profResp.Content.ReadAsStringAsync(cancellationToken));
                    if (!profDoc.RootElement.TryGetProperty("properties", out var propsEl)) return null;

                    string? texturesB64 = null;
                    foreach (var p in propsEl.EnumerateArray())
                    {
                        if (p.TryGetProperty("name", out var n) && n.GetString() == "textures" &&
                            p.TryGetProperty("value", out var v))
                        {
                            texturesB64 = v.GetString();
                            break;
                        }
                    }
                    if (texturesB64 == null) return null;

                    // 3. decode -> skin texture url
                    using var texDoc = JsonDocument.Parse(Convert.FromBase64String(texturesB64));
                    if (!texDoc.RootElement.TryGetProperty("textures", out var tex) ||
                        !tex.TryGetProperty("SKIN", out var skin) ||
                        !skin.TryGetProperty("url", out var urlEl))
                        return null;
                    var skinUrl = urlEl.GetString();
                    if (string.IsNullOrEmpty(skinUrl)) return null;

                    // 4. download the texture
                    byte[] bytes = await http.GetByteArrayAsync(skinUrl, cancellationToken);
                    if (bytes.Length == 0) return null;

                    skinBytesCache[minecraftUsername] = (bytes, DateTime.UtcNow);
                    return bytes;
                }
                finally
                {
                    downloadThrottle.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }

            return null;
        }

        public static async Task LoadProfilesIntoAccounts()
        {
            var profiles = await GetProfilesAsync();
            var accounts = AccountManager.LoadAccounts();

            // re sync every yoriiskins account the launcher manages: public profiles
            // claimed on this device and the logged in users private ones
            accounts.RemoveAll(a =>
                a.AccountType == Models.PlayerAccountType.YoriiSkins &&
                (string.IsNullOrEmpty(a.GitHubOwner) || a.GitHubOwner == GitHubUsername));

            foreach (var profile in profiles)
            {
                // only profiles this device/user actually owns: public ones claimed from this
                // device and private ones owned by the logged in account
                // githubusername is empty when logged out so the ownership check needs a non
                // empty username or else every public profile in the index would match
                // public profile in the index (owner == "") would match
                bool claimedHere = SettingsManager.Current.ClaimTokens.ContainsKey(profile.Username);
                bool ownedByMe = !string.IsNullOrEmpty(GitHubUsername) &&
                                 profile.Owner == GitHubUsername;
                if (!claimedHere && !ownedByMe)
                    continue;

                accounts.Add(new Models.PlayerAccount
                {
                    Id = profile.Uuid,
                    Username = profile.Username,
                    AccountType = Models.PlayerAccountType.YoriiSkins,
                    CustomUUID = profile.Uuid,
                    SkinUrl = profile.SkinUrl,
                    GitHubOwner = profile.Kind == "public" ? null : profile.Owner
                });
            }

            AccountManager.SaveAll(accounts);
        }

        public static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }
    }

    public sealed class SkinSyncInfo
    {
        public bool HasLocal { get; init; }
        public bool RemoteReachable { get; init; }
        public bool MatchesRemote { get; init; }
    }

    public sealed class ProfileEntry
    {
        public string Username { get; init; } = "";
        public string Uuid { get; init; } = "";
        public string SkinUrl { get; init; } = "";
        public string Kind { get; init; } = "private";
        public string Owner { get; init; } = "";
    }
}