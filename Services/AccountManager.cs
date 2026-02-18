/*
    Account container (user.p2e): creation, unlock, and recovery.
    - EnsureAccountAsync creates account with user-provided username and generated Ed25519 keypair.
    - Private key is stored inside the encrypted container (passphrase-protected).
    - RecoverLostPassphraseAsync rotates passphrase and re-encrypts related containers (no email flows).
*/
using System;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
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
            SodiumCore.Init();
            var kp = PublicKeyAuth.GenerateKeyPair();
            var pub = kp.PublicKey;
            var priv = kp.PrivateKey; // 64 bytes
            // Derive UID from public key
            var uid = IdentityService.ComputeUidFromPublicKey(pub);
            var initialDisplay = string.IsNullOrWhiteSpace(displayName) ? uid : displayName.Trim();
            var initialAvatar = BundledAvatarService.TryGetRandomAvatarBytes();
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
                Avatar = initialAvatar,
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
            ValidateAccountForPersistence(account);
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
            ValidateLoadedAccount(account);
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

            // Load and validate current account using existing unlocked passphrase.
            var existingPassphrase = AppServices.Passphrase;
            if (string.IsNullOrWhiteSpace(existingPassphrase))
            {
                throw new InvalidOperationException("Recovery requires unlocking with your current passphrase first.");
            }

            AccountData current;
            try
            {
                current = LoadAccount(existingPassphrase);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unable to decrypt the current account with the active passphrase. Recovery aborted to protect key integrity.", ex);
            }

            // Create a new passphrase and re-encrypt account data with keys unchanged.
            var newPass = GenerateStrongPassphrase();
            var account = new AccountData
            {
                DisplayName = current.DisplayName,
                Username = current.Username,
                UID = current.UID,
                PublicKey = current.PublicKey?.ToArray() ?? Array.Empty<byte>(),
                PrivateKey = current.PrivateKey?.ToArray() ?? Array.Empty<byte>(),
                KeyId = current.KeyId?.ToArray() ?? Array.Empty<byte>(),
                CreatedAtUtc = current.CreatedAtUtc,
                DisplayNameHistory = current.DisplayNameHistory == null
                    ? null
                    : new System.Collections.Generic.List<DisplayNameRecord>(current.DisplayNameHistory.Select(r => new DisplayNameRecord { Name = r.Name, ChangedAtUtc = r.ChangedAtUtc })),
                DisplayNameChangeCount = current.DisplayNameChangeCount,
                Avatar = current.Avatar?.ToArray(),
                ShareAvatar = current.ShareAvatar,
                Bio = current.Bio
            };

            var backupPath = path + ".bak";
            try
            {
                File.Copy(path, backupPath, true);
            }
            catch { }

            try
            {
                SaveAccount(account, newPass);
                var reloaded = LoadAccount(newPass);
                if (!AreSameIdentity(account, reloaded))
                {
                    throw new InvalidDataException("Identity verification failed after recovery write.");
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(backupPath)) File.Copy(backupPath, path, true);
                }
                catch { }
                throw;
            }
            finally
            {
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
            }

            // Re-encrypt other containers with the new passphrase (settings, contacts)
            try
            {
                AppServices.Passphrase = newPass;
                AppServices.Settings.Save(AppServices.Passphrase);
                AppServices.Contacts.Save(AppServices.Passphrase);
                AppServices.Identity.LoadFromAccount(current);
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

        private static void ValidateLoadedAccount(AccountData account)
        {
            if (account == null) throw new InvalidDataException("Account data is missing.");
            if (account.PublicKey == null || account.PublicKey.Length != 32)
                throw new InvalidDataException("Account public key is invalid.");
            if (account.PrivateKey == null || account.PrivateKey.Length < 32)
                throw new InvalidDataException("Account private key is invalid.");
            if (string.IsNullOrWhiteSpace(account.UID))
                throw new InvalidDataException("Account UID is missing.");
        }

        private static void ValidateAccountForPersistence(AccountData account)
        {
            ValidateLoadedAccount(account);
            var derivedUid = IdentityService.ComputeUidFromPublicKey(account.PublicKey);
            if (!string.Equals((account.UID ?? string.Empty).Trim(), derivedUid, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Refusing to save account because UID does not match the stored public key.");
            }
        }

        private static bool AreSameIdentity(AccountData expected, AccountData actual)
        {
            if (expected.PublicKey == null || actual.PublicKey == null) return false;
            if (expected.PrivateKey == null || actual.PrivateKey == null) return false;
            if (!expected.PublicKey.SequenceEqual(actual.PublicKey)) return false;
            if (!expected.PrivateKey.SequenceEqual(actual.PrivateKey)) return false;
            var expectedUid = (expected.UID ?? string.Empty).Trim();
            var actualUid = (actual.UID ?? string.Empty).Trim();
            return string.Equals(expectedUid, actualUid, StringComparison.Ordinal);
        }

    }
}

