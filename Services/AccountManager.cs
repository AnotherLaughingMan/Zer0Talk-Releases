/*
    Account container (user.p2e): creation, unlock, and recovery.
    - EnsureAccountAsync creates account with user-provided username and generated Ed25519 keypair.
    - Private key is stored inside the encrypted container (passphrase-protected).
    - RecoverLostPassphraseAsync rotates passphrase and re-encrypts related containers (no email flows).
*/
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

using Sodium;

namespace Zer0Talk.Services
{
    public class AccountManager
    {
        private const string FileName = "user.p2e";
        private readonly Containers.P2EContainer _container = new();
        private readonly EncryptionService _encryption = new();

        public bool HasAccount() => File.Exists(GetPath());
        public string GetPath()
        {
            return Zer0Talk.Utilities.AppDataPaths.Combine(FileName);
        }

        public async Task<string?> EnsureAccountAsync(Func<Task<(string DisplayName, string Username)>> prompt)
        {
            var path = GetPath();
            if (File.Exists(path)) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Prompt via AccountCreationWindow (supplied as callback)
            var (displayName, username) = await prompt();
            username = (username ?? string.Empty).Trim();
            if (!ValidateUsername(username, out var reason))
                throw new InvalidOperationException(reason);

            // Generate a strong passphrase to encrypt the account container; user must store it safely
            var passphrase = GenerateStrongPassphrase();
            // Generate Ed25519 keypair
            var kp = PublicKeyAuth.GenerateKeyPair();
            var pub = kp.PublicKey;
            var priv = kp.PrivateKey; // 64 bytes
            // Derive UID from public key
            var uid = IdentityService.ComputeUidFromPublicKey(pub);
            var initialDisplay = string.IsNullOrWhiteSpace(displayName) ? uid : displayName.Trim();
            var account = new AccountData
            {
                DisplayName = initialDisplay,
                Username = username,
                UID = uid,
                PublicKey = pub,
                PrivateKey = priv,
                KeyId = RandomNumberGenerator.GetBytes(16),
                CreatedAtUtc = DateTime.UtcNow,
                DisplayNameHistory = new System.Collections.Generic.List<DisplayNameRecord>
                {
                    new DisplayNameRecord { Name = initialDisplay, ChangedAtUtc = DateTime.UtcNow }
                },
                DisplayNameChangeCount = 0,
                ShareAvatar = false,
                Avatar = null,
                Bio = null
            };
            SaveAccount(account, passphrase);
            // Mirror to settings so UI shows it before account is reloaded again
            try
            {
                AppServices.Passphrase = passphrase;
                AppServices.Settings.Load(passphrase);
                AppServices.Settings.Settings.DisplayName = account.DisplayName;
                AppServices.Settings.Save(passphrase);
                // Load identity into memory
                AppServices.Identity.LoadFromAccount(account);
            }
            catch { }
            Logger.Log($"Created encrypted user account at: {path}");
            // Surface passphrase to the caller (e.g., show once in UI). For now, just log a hint without the value.
            Logger.Log("A new account passphrase was generated. Show it to the user ONCE and instruct to store securely.");
            return passphrase;
        }

        public void SaveAccount(AccountData account, string passphrase)
        {
        var json = JsonSerializer.Serialize(account, SerializationDefaults.Indented);
            var bytes = Encoding.UTF8.GetBytes(json);
            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _container.SaveFile(path, bytes, passphrase);
        }

        public AccountData LoadAccount(string passphrase)
        {
            var path = GetPath();
            var bytes = _container.LoadFile(path, passphrase);
            var json = Encoding.UTF8.GetString(bytes);
            var account = JsonSerializer.Deserialize<AccountData>(json) ?? throw new InvalidDataException("Invalid account container");
            return account;
        }

        // Lost passphrase recovery: rotate to a new passphrase without changing UID or user identity fields
        // Requires user confirmation; identity is based on stored keys (no email fallback)
        public async Task<string> RecoverLostPassphraseAsync(Func<Task<bool>> confirm)
        {
            var path = GetPath();
            if (!File.Exists(path)) throw new FileNotFoundException("No account found to recover.", path);

            // Confirm user intent with a strong warning
            if (!await confirm()) throw new InvalidOperationException("Recovery canceled.");

            // Preserve metadata as best-effort
            var displayName = AppServices.Settings.Settings.DisplayName;
            var createdAt = File.GetCreationTimeUtc(path);

            // Load current account using current in-memory passphrase if available; otherwise we cannot decrypt keys.
            AccountData? current = null;
            try { current = LoadAccount(AppServices.Passphrase); }
            catch { }

            // Create a new passphrase and re-write account data (keys unchanged)
            var newPass = GenerateStrongPassphrase();
            var account = new AccountData
            {
                DisplayName = displayName,
                Username = current?.Username ?? "user",
                UID = current?.UID ?? "",
                PublicKey = current?.PublicKey ?? Array.Empty<byte>(),
                PrivateKey = current?.PrivateKey ?? Array.Empty<byte>(),
                KeyId = current?.KeyId ?? RandomNumberGenerator.GetBytes(16),
                CreatedAtUtc = createdAt
            };
            SaveAccount(account, newPass);

            // Re-encrypt other containers with the new passphrase (settings, contacts)
            try
            {
                AppServices.Passphrase = newPass;
                AppServices.Settings.Save(AppServices.Passphrase);
                AppServices.Contacts.Save(AppServices.Passphrase);
                if (current != null) AppServices.Identity.LoadFromAccount(current);
            }
            catch { }

            return newPass;
        }

        // Recovery flow: if passphrase is lost, we delete the account and allow recreation
        public void LostPassphraseRecovery()
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static bool ValidateUsername(string username, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(username)) { reason = "Username is required."; return false; }
            if (username.Length < 6 || username.Length > 24) { reason = "Username must be 6-24 characters."; return false; }
            foreach (var ch in username)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')) { reason = "Username may contain letters, digits, '_' or '-' only."; return false; }
            }
            return true;
        }

        private static string GenerateStrongPassphrase()
        {
            // 32 bytes random, base64url-encoded (43 chars). User must store this safely.
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}

