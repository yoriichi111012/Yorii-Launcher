using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher.Helpers
{
    public static class LoginHelper
    {
        public static async Task<LoginResult> LoginOrUseCachedSession(string username, string password)
        {
            var s = SettingsManager.Current;
            bool hasCachedUsername = !string.IsNullOrEmpty(s.CachedUsername);

            // offline modes
            if (string.IsNullOrWhiteSpace(password))
            {
                Debug.WriteLine("Password empty.");

                // same username as Ely.by cache
                if (hasCachedUsername && string.Equals(s.CachedUsername, username, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("Using cached Ely.by session.");

                    return new LoginResult
                    {
                        Session = LoadOfflineSession() ?? CreateOfflineSession(username),
                        IsOffline = true
                    };
                }

                // different username = new offline sesison
                Debug.WriteLine("Creating offline session.");

                return new LoginResult
                {
                    Session = CreateOfflineSession(username),
                    IsOffline = true
                };
            }

            // online
            try
            {
                var session = await LoginWithElyBy(username, password);

                SaveSession(session);

                return new LoginResult
                {
                    Session = session,
                    IsOffline = false
                };
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Online auth failed: {ex}");

                // fallback ONLY if same username
                if (hasCachedUsername && string.Equals(s.CachedUsername, username, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("Using cached Ely.by session.");

                    return new LoginResult
                    {
                        Session = LoadOfflineSession() ?? CreateOfflineSession(username),
                        IsOffline = true
                    };
                }

                Debug.WriteLine("Creating offline fallback session.");

                return new LoginResult
                {
                    Session = CreateOfflineSession(username),
                    IsOffline = true
                };
            }
            catch (Exception ex)
            {
                if (ex.Message == "INVALID_CREDENTIALS")
                {
                    Debug.WriteLine("Wrong password.");
                    NotificationHelper.Show("Login failed", "Please verify your Ely.by account credentials.");
                    throw;
                }

                throw;
            }
        }
        // create a fake offline session with a uuid based on the username
        private static MSession CreateOfflineSession(string username)
        {
            string input = $"OfflinePlayer:{username}";

            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));

            string offlineUuid = new Guid(hash).ToString("N");

            return new MSession
            {
                Username = username,
                UUID = offlineUuid,

                // fake token
                AccessToken = Guid.NewGuid().ToString("N"),

                UserType = "legacy"
            };
        }
        private static string GetOrCreateClientToken()
        {
            var s = SettingsManager.Current;

            if (!string.IsNullOrEmpty(s.ClientToken))
                return s.ClientToken;

            s.ClientToken = Guid.NewGuid().ToString();
            SettingsManager.SaveSettings();
            return s.ClientToken;
        }

        private static void SaveSession(MSession session)
        {
            var s = SettingsManager.Current;
            s.CachedUsername = session.Username;
            s.CachedUUID = session.UUID;
            s.CachedAccessToken = session.AccessToken;
            s.ClientToken = GetOrCreateClientToken();
            SettingsManager.SaveSettings();
        }

        public static MSession? LoadOfflineSession()
        {
            var s = SettingsManager.Current;

            if (!string.IsNullOrEmpty(s.CachedUsername) && !string.IsNullOrEmpty(s.CachedUUID)
                && !string.IsNullOrEmpty(s.CachedAccessToken) && !string.IsNullOrEmpty(s.ClientToken))
            {
                return new MSession
                {
                    Username = s.CachedUsername,
                    UUID = s.CachedUUID,
                    AccessToken = s.CachedAccessToken,
                    ClientToken = s.ClientToken,
                    UserType = "legacy"
                };
            }

            return null;
        }
        public static async Task<MSession> LoginWithElyBy(string Username, string Password)
        {
            var requestData = new ElyAuthRequest
            {
                username = Username,
                password = Password,
                clientToken = GetOrCreateClientToken(),
                requestUser = true
            };

            string json = JsonSerializer.Serialize(requestData, ElyJsonContext.Default.ElyAuthRequest);

            // post to ely.by auth server
            var response = await HttpService.Client.PostAsync("https://authserver.ely.by/auth/authenticate", new StringContent(json, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                string errorBody =
                    await response.Content.ReadAsStringAsync();

                Debug.WriteLine(errorBody);

                // 401/403 = wrong password
                if (response.StatusCode ==
                    System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode ==
                    System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new Exception(
                        "INVALID_CREDENTIALS"
                    );
                }

                throw new HttpRequestException(
                    $"Auth server error: {response.StatusCode}"
                );
            }

            string responseBody = await response.Content.ReadAsStringAsync();

            var authResult = JsonSerializer.Deserialize(responseBody, ElyJsonContext.Default.ElyAuthResponse);

            ArgumentNullException.ThrowIfNull(authResult);


            Debug.WriteLine($"Logged in as: {authResult.selectedProfile.name}");

            return new MSession
            {
                Username = authResult.selectedProfile.name,
                UUID = authResult.selectedProfile.id,
                AccessToken = authResult.accessToken
            };
        }

        public static async Task<(MSession session, string identifier)> LoginWithMojangInteractive()
        {
            var loginHandler = JELoginHandlerBuilder.BuildDefault();
            var session = await loginHandler.AuthenticateInteractively();

            var accounts = loginHandler.AccountManager.GetAccounts();
            var jeAccount = accounts.LastOrDefault();

            return (session, jeAccount?.Identifier ?? "");
        }

        public static async Task<MSession> LoginWithMojangSilently(string identifier)
        {
            var loginHandler = JELoginHandlerBuilder.BuildDefault();
            var accounts = loginHandler.AccountManager.GetAccounts();
            var account = accounts.GetAccount(identifier);

            if (account == null)
                throw new InvalidOperationException("Microsoft account not found. Please re-add the account.");

            return await loginHandler.Authenticate(account);
        }

        public static async Task RemoveMojangAccount(string identifier)
        {
            try
            {
                var loginHandler = JELoginHandlerBuilder.BuildDefault();
                var accounts = loginHandler.AccountManager.GetAccounts();
                var account = accounts.GetAccount(identifier);

                if (account != null)
                    await loginHandler.Signout(account);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove Mojang account: {ex.Message}");
            }
        }

        public class ElyAuthRequest
        {
            public string? username { get; set; }
            public string? password { get; set; }
            public string? clientToken { get; set; }
            public bool requestUser { get; set; }
        }

        public class ElyProfile
        {
            public string? id { get; set; }
            public string? name { get; set; }
        }

        public class ElyAuthResponse
        {
            public string? accessToken { get; set; }
            public string? clientToken { get; set; }
            public ElyProfile? selectedProfile { get; set; }
        }

        public class LoginResult
        {
            public required MSession Session { get; set; }
            public bool IsOffline { get; set; }
        }
    }
}
