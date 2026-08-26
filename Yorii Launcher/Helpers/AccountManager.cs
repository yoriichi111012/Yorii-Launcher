using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class AccountManager
    {
        private const string AccountsFileName = "accounts.yaml";

        private static string AccountsFilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, AccountsFileName);
        private static string LegacyAccountsFilePath => Path.ChangeExtension(AccountsFilePath, ".json");

        private static List<PlayerAccount>? _accountsCache;

        public static List<PlayerAccount> LoadAccounts()
        {
            if (_accountsCache != null)
                return _accountsCache;

            // move old playername into accounts file
            MigrateLegacyPlayerName();

            if (!File.Exists(AccountsFilePath) && !TryMigrateLegacyAccounts())
                return _accountsCache = [];

            try
            {
                string yaml = File.ReadAllText(AccountsFilePath);
                bool needsResave = false;

                // elyby got removed so old elyby accounts just become offline now
                if (yaml.Contains("ElyBy", StringComparison.Ordinal))
                {
                    yaml = yaml.Replace("account_type: ElyBy", "account_type: Offline", StringComparison.OrdinalIgnoreCase);
                    needsResave = true;
                }

                var accounts = LauncherYaml.Deserialize<List<PlayerAccount>>(yaml) ?? [];

                foreach (var account in accounts)
                {
                    if (string.IsNullOrEmpty(account.Password))
                        continue;

                    // decrypt if its still encrypted from old format
                    if (PasswordProtector.IsEncrypted(account.Password))
                    {
                        account.Password = PasswordProtector.Unprotect(account.Password);
                    }
                    else
                    {
                        // plain text from older version, will get encrypted on save
                        needsResave = true;
                    }
                }

                // resave so passwords are encrypted
                if (needsResave)
                    SaveAccounts(accounts);

                Logger.Info($"Loaded {accounts.Count} account(s)");
                return _accountsCache = accounts;
            }
            catch
            {
                Logger.Warn("Failed to load accounts");
                return _accountsCache = [];
            }
        }

        public static void SaveAccount(PlayerAccount account)
        {
            var accounts = LoadAccounts();
            var existing = accounts.FirstOrDefault(x => x.Id == account.Id);

            // update if exists else add new
            if (existing == null)
            {
                accounts.Add(account);
                Logger.Info($"Added account: {account.Username} ({account.AccountType})");
            }
            else
            {
                existing.Username = account.Username;
                existing.Password = account.Password;
                existing.AccountType = account.AccountType;
                Logger.Info($"Updated account: {account.Username} ({account.AccountType})");
            }

            SaveAccounts(accounts);
            SetSelectedAccount(account.Id);
        }

        public static void SaveAll(List<PlayerAccount> accounts)
        {
            SaveAccounts(accounts);
        }

        public static PlayerAccount? GetSelectedAccount()
        {
            var accounts = LoadAccounts();
            var selectedId = GetSelectedAccountId();

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                var selected = accounts.FirstOrDefault(x => x.Id == selectedId);

                if (selected != null)
                    return selected;
            }

            // no selected one so just use first account
            return accounts.FirstOrDefault();
        }

        public static string? GetSelectedAccountId()
        {
            var id = SettingsManager.Current.SelectedAccountId;
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public static void SetSelectedAccount(string accountId)
        {
            SettingsManager.Current.SelectedAccountId = accountId;
            SettingsManager.SaveSettings();
        }

        private static void SaveAccounts(List<PlayerAccount> accounts)
        {
            Directory.CreateDirectory(ApplicationData.Current.LocalFolder.Path);

            // encrypt passwords before saving so yaml never has plain text
            var toSave = accounts.Select(a => new PlayerAccount
            {
                Id = a.Id,
                Username = a.Username,
                Password = string.IsNullOrEmpty(a.Password) ? a.Password : PasswordProtector.Protect(a.Password),
                AccountType = a.AccountType,
                MojangIdentifier = a.MojangIdentifier,
                CustomUUID = a.CustomUUID,
                SkinUrl = a.SkinUrl,
                GitHubOwner = a.GitHubOwner
            }).ToList();

            File.WriteAllText(AccountsFilePath, LauncherYaml.Serialize(toSave));

            _accountsCache = accounts;
        }

        private static bool TryMigrateLegacyAccounts()
        {
            if (!File.Exists(LegacyAccountsFilePath))
                return false;

try
            {
                string jsonText = File.ReadAllText(LegacyAccountsFilePath);

                // elyby got removed so old elyby accounts just become offline now
                if (jsonText.Contains("ElyBy", StringComparison.Ordinal))
                    jsonText = jsonText.Replace("ElyBy", "Offline", StringComparison.Ordinal);

                var accounts = System.Text.Json.JsonSerializer.Deserialize(
                    jsonText,
                    LauncherJsonContext.Default.ListPlayerAccount);
                if (accounts == null)
                    return false;

                SaveAccounts(accounts);
                Logger.Info("Migrated accounts.json to accounts.yaml");
                return true;
            }
            catch
            {
                Logger.Warn("Failed to migrate accounts.json");
                return false;
            }
        }

        private static void MigrateLegacyPlayerName()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // see if old playername is still there
                if (!settings.Values.TryGetValue("playername", out var value))
                    return;

                var username = value?.ToString();

                if (string.IsNullOrWhiteSpace(username))
                    return;

                // already migrated so skip
                if (File.Exists(AccountsFilePath) || File.Exists(LegacyAccountsFilePath))
                    return;

                var account = new PlayerAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Username = username.Trim(),
                    AccountType = PlayerAccountType.Offline
                };

                SaveAccounts([account]);
                SetSelectedAccount(account.Id);
            }
            catch
                {
                // migration failed just ignore
            }
        }

        public static void DeleteAccount(string accountId)
        {
            var accounts = LoadAccounts();
            var deleted = accounts.Find(x => x.Id == accountId);

            accounts.RemoveAll(x => x.Id == accountId);

            SaveAccounts(accounts);

            if (deleted != null)
                Logger.Info($"Deleted account: {deleted.Username}");

            var selectedId = GetSelectedAccountId();

            // if we deleted the selected one pick another
            if (selectedId == accountId)
            {
                var replacement = accounts.FirstOrDefault();

                if (replacement != null)
                    SetSelectedAccount(replacement.Id);
                else
                {
                    SettingsManager.Current.SelectedAccountId = "";
                    SettingsManager.SaveSettings();
                }
            }
        }

        public static void UpdateAccount(PlayerAccount account)
        {
            var accounts = LoadAccounts();

            var existing = accounts.FirstOrDefault(x => x.Id == account.Id);

            if (existing == null)
                return;

            existing.Username = account.Username;
            existing.Password = account.Password;
            existing.AccountType = account.AccountType;

            SaveAccounts(accounts);
        }

    }
}
