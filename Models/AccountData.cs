/*
    Account profile and keys stored in user.p2e.
    - Includes public identity and encrypted private material.
*/
using System;

namespace ZTalk.Models
{
    public class AccountData
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty; // fixed at creation (6-24 chars)
        public string UID { get; set; } = string.Empty; // derived from public key (base32 no-ambiguous)
        public byte[] PublicKey { get; set; } = Array.Empty<byte>(); // Ed25519 public key
        public byte[] PrivateKey { get; set; } = Array.Empty<byte>(); // Ed25519 private key (container-encrypted with passphrase)
        public byte[] KeyId { get; set; } = Array.Empty<byte>(); // random identifier, stored encrypted
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Profile extensions
        public System.Collections.Generic.List<DisplayNameRecord>? DisplayNameHistory { get; set; }
        public int DisplayNameChangeCount { get; set; }
        public byte[]? Avatar { get; set; } // optional small image blob (PNG/JPG), kept in encrypted container
        public bool ShareAvatar { get; set; }
        public string? Bio { get; set; } // optional short bio (<=280 chars, markdown-lite)
    }
}
