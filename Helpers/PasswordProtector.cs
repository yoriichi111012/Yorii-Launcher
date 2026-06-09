using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Yorii_Launcher.Helpers
{
    // uses windows DPAPI to encrypt/decrypt passwords tied to the current user
    public static class PasswordProtector
    {
        private const string Entropy = "YoriiLauncher";

        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return plaintext;

            try
            {
                // encrypt with dpapi, tied to current windows user
                byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                byte[] entropyBytes = Encoding.UTF8.GetBytes(Entropy);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, entropyBytes, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (CryptographicException ex)
            {
                Debug.WriteLine($"Password protection failed: {ex.Message}");
                return plaintext;
            }
        }

        public static string Unprotect(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                // decrypt, return original if it fails
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] entropyBytes = Encoding.UTF8.GetBytes(Entropy);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, entropyBytes, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                return encryptedText;
            }
            catch (FormatException)
            {
                return encryptedText;
            }
        }

        public static bool IsEncrypted(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                return bytes.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
