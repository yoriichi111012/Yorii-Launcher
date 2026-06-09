using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Windows.Storage;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class AccountManager
    {
        private const string AccountsFileName = "accounts.json";

        private static string AccountsFilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, AccountsFileName);

        public static List<PlayerAccount> LoadAccounts()
        {
            // move old playername to accounts file
            MigrateLegacyPlayerName();

            if (!File.Exists(AccountsFilePath))
                return [];

            try
            {
                var json = File.ReadAllText(AccountsFilePath);
                var accounts = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.ListPlayerAccount) ?? [];
                bool needsResave = false;

                foreach (var account in accounts)
                {
                    if (string.IsNullOrEmpty(account.Password))
                        continue;

                    // decrypt password if still encrypted from old format
                    if (PasswordProtector.IsEncrypted(account.Password))
                    {
                        account.Password = PasswordProtector.Unprotect(account.Password);
                    }
                    else
                    {
                        needsResave = true;
                    }
                }

                // resave with encrypted passwords
                if (needsResave)
                    SaveAccounts(accounts);

                return accounts;
            }
            catch
            {
                return [];
            }
        }

        public static void SaveAccount(PlayerAccount account)
        {
            var accounts = LoadAccounts();
            var existing = accounts.FirstOrDefault(x => x.Id == account.Id);

            // update existing or add new
            if (existing == null)
            {
                accounts.Add(account);
            }
            else
            {
                existing.Username = account.Username;
                existing.Password = account.Password;
                existing.AccountType = account.AccountType;
            }

            SaveAccounts(accounts);
            SetSelectedAccount(account.Id);
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

            // fallback to first account
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

            // encrypt passwords before writing
            var toSave = accounts.Select(a => new PlayerAccount
            {
                Id = a.Id,
                Username = a.Username,
                Password = string.IsNullOrEmpty(a.Password) ? a.Password : PasswordProtector.Protect(a.Password),
                AccountType = a.AccountType,
                MojangIdentifier = a.MojangIdentifier
            }).ToList();

            var json = JsonSerializer.Serialize(toSave, LauncherJsonContext.Default.ListPlayerAccount);
            File.WriteAllText(AccountsFilePath, json);
        }

        private static void MigrateLegacyPlayerName()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // check if old playername exists
                if (!settings.Values.TryGetValue("playername", out var value))
                    return;

                var username = value?.ToString();

                if (string.IsNullOrWhiteSpace(username))
                    return;

                // already migrated
                if (File.Exists(AccountsFilePath))
                    return;

                var account = new PlayerAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Username = username.Trim(),
                    AccountType = PlayerAccountType.ElyBy
                };

                SaveAccounts([account]);
                SetSelectedAccount(account.Id);
            }
            catch
            {
                // migration failed
            }
        }

        public static void DeleteAccount(string accountId)
        {
            var accounts = LoadAccounts();

            accounts.RemoveAll(x => x.Id == accountId);

            SaveAccounts(accounts);

            var selectedId = GetSelectedAccountId();

            // if deleted account was selected, switch to another
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
