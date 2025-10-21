using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

using Zer0Talk.Containers;
using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public class PeersStore
    {
        private readonly P2EContainer _container = new();
        private const string FileName = "peers.p2e";

        public List<Peer> Load(string passphrase)
        {
            var path = GetPath();
            try
            {
                if (!File.Exists(path)) return new List<Peer>();
                var bytes = _container.LoadFile(path, passphrase);
                var json = Encoding.UTF8.GetString(bytes);
                return JsonSerializer.Deserialize<List<Peer>>(json) ?? new List<Peer>();
            }
            catch (Exception ex)
            {
                Logger.Log($"Peers load failed: {ex.Message}");
                return new List<Peer>();
            }
        }

        public void Save(IEnumerable<Peer> peers, string passphrase)
        {
            try
            {
                var list = new List<Peer>(peers);
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Indented);
                var bytes = Encoding.UTF8.GetBytes(json);
                var path = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                _container.SaveFile(path, bytes, passphrase);
                Logger.Log($"Peers saved: {path} ({bytes.Length} bytes before encryption)");
            }
            catch (Exception ex)
            {
                Logger.Log($"Peers save failed: {ex.Message}");
            }
        }

        private static string GetPath()
        {
            return Zer0Talk.Utilities.AppDataPaths.Combine(FileName);
        }
    }
}

