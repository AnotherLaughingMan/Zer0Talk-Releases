/*
    Encrypted container format reader/writer (P2E2 legacy, P2E3 current).
    - Used by AccountManager, SettingsService, and ContactManager.
*/
using System.IO;

using Zer0Talk.Services;

namespace Zer0Talk.Containers
{
    public class P2EContainer
    {
        private readonly EncryptionService _encryption = new();

        // Save raw bytes to an encrypted .p2e file
        public void SaveFile(string path, byte[] rawData, string passphrase)
        {
            var cipher = _encryption.Encrypt(rawData, passphrase);
            File.WriteAllBytes(path, cipher);
        }

        // Load and decrypt raw bytes from an encrypted .p2e file
        public byte[] LoadFile(string path, string passphrase)
        {
            var cipher = File.ReadAllBytes(path);
            return _encryption.Decrypt(cipher, passphrase);
        }
    }
}
