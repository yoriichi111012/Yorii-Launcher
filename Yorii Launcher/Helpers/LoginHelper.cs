using Quiescent.Core.Auth;
using Quiescent.Core.Auth.Microsoft;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Yorii_Launcher.Helpers
{
    public static class LoginHelper
    {
        // make fake offline session with uuid from username same as vanilla does
        public static MSession CreateOfflineSession(string username)
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
                Logger.Error($"Failed to remove Mojang account: {ex.Message}");
            }
        }

        public class LoginResult
        {
            public required MSession Session { get; set; }
            public bool IsOffline { get; set; }
        }
    }
}
