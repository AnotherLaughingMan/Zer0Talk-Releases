using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Vorbis;
using NAudio.MediaFoundation;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class AudioNotificationService
    {
        /// <summary>
        /// Play a notification sound asynchronously with appropriate volume channel
        /// </summary>
        public async Task PlaySoundAsync(SoundType soundType, DateTime? requestedAtUtc = null, string? source = null)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            var effectiveVolume = GetEffectiveVolume(soundType);
            SafeAudioLogVerbose($"PlaySoundAsync called: soundType={soundType}, source={source ?? "unknown"}, _isEnabled={_isEnabled}, effectiveVolume={effectiveVolume}, _mainVolume={_mainVolume}, _chatVolume={_chatVolume}");
            
            if (!_isEnabled || effectiveVolume <= 0.0f)
            {
                SafeAudioLogVerbose($"Sound disabled or volume zero - skipping {soundType} (isEnabled={_isEnabled}, effectiveVolume={effectiveVolume})");
                return;
            }

            // Note: DND suppression is now handled by NotificationService before calling this method
            // This ensures consistent behavior across the notification system

            try
            {
                var soundFile = FindSoundFile(soundType);
                if (string.IsNullOrEmpty(soundFile))
                {
                    SafeAudioLog($"No sound file found for {soundType}");
                    return;
                }

                SafeAudioLogVerbose($"Playing {soundType}: {Path.GetFileName(soundFile)} at {effectiveVolume:F2} volume");
                await PlayAudioFileAsync(soundFile, effectiveVolume, startTimestamp, requestedAtUtc, source);
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Failed to play {soundType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Play a custom sound file using main volume
        /// </summary>
        public async Task PlayCustomSoundAsync(string fileName, DateTime? requestedAtUtc = null, string? source = null)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            if (!_isEnabled || _mainVolume <= 0.0f)
                return;

            try
            {
                var soundFile = ResolveSoundPath(fileName);
                if (!File.Exists(soundFile))
                {
                    SafeAudioLog($"Custom sound file not found: {fileName}");
                    return;
                }

                SafeAudioLogVerbose($"Playing custom sound: {fileName}");
                await PlayAudioFileAsync(soundFile, _mainVolume, startTimestamp, requestedAtUtc, source);
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Failed to play custom sound {fileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop any currently playing sound
        /// </summary>
        public void StopSound()
        {
            lock (_playerLock)
            {
                try
                {
                    var players = _activePlayers.ToArray();
                    _activePlayers.Clear();
                    foreach (var entry in players)
                    {
                        try { entry.Player.Stop(); } catch { }
                        try { entry.Player.Dispose(); } catch { }
                        try { entry.Reader.Dispose(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    SafeAudioLog($"Error stopping sound: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get the effective volume for a sound type (Main * Channel volume)
        /// </summary>
        private float GetEffectiveVolume(SoundType soundType)
        {
            var channelVolume = soundType switch
            {
                SoundType.MessageIncoming => _chatVolume,
                SoundType.MessageOutgoing => _chatVolume,
                SoundType.CallIncoming => _chatVolume,
                SoundType.CallEnd => _chatVolume,
                SoundType.TypingIndicator => _chatVolume,
                SoundType.VoiceNoteStart => _chatVolume,
                SoundType.VoiceNoteEnd => _chatVolume,
                SoundType.NotificationGeneral => _notificationVolume,
                SoundType.NotificationAlert => _notificationVolume,
                SoundType.Custom => _mainVolume,
                _ => _mainVolume
            };
            
            return _mainVolume * channelVolume;
        }

        private string? FindSoundFile(SoundType soundType)
        {
            if (!SoundFiles.TryGetValue(soundType, out var candidates))
                return null;

            foreach (var candidate in candidates)
            {
                var resolvedPaths = ResolveSoundPaths(candidate);
                foreach (var resolvedPath in resolvedPaths)
                {
                    if (File.Exists(resolvedPath))
                    {
                        return resolvedPath;
                    }
                }
            }

            SafeAudioLog($"No existing file found for {soundType}. Candidates: {string.Join(", ", candidates)}");
            return null;
        }

        private async Task PlayAudioFileAsync(string filePath, float volume = 1.0f, long? startTimestamp = null, DateTime? requestedAtUtc = null, string? source = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    var candidatePaths = new List<string>();
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        candidatePaths.Add(filePath);
                    }
                    var fileName = Path.GetFileName(filePath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        candidatePaths.AddRange(ResolveSoundPaths(fileName));
                    }
                    candidatePaths = candidatePaths
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    AudioFileReader? audioFile = null;
                    string? selectedPath = null;
                    string? lastReaderError = null;

                    foreach (var candidatePath in candidatePaths)
                    {
                        if (!File.Exists(candidatePath))
                        {
                            continue;
                        }

                        if (TryCreateAudioReader(candidatePath, out audioFile, out var readerError))
                        {
                            selectedPath = candidatePath;
                            break;
                        }

                        lastReaderError = readerError;
                    }

                    if (audioFile == null || string.IsNullOrWhiteSpace(selectedPath))
                    {
                        SafeAudioLog($"Failed to create audio reader for requested path '{filePath}'. Last error: {lastReaderError ?? "n/a"}");
                        return;
                    }

                    audioFile.Volume = volume;
                    SafeAudioLogVerbose($"Created new audio reader for {Path.GetFileName(selectedPath)}");

                    var player = new WaveOutEvent
                    {
                        DesiredLatency = 100
                    };
                    player.Init(audioFile);

                    lock (_playerLock)
                    {
                        while (_activePlayers.Count >= MaxConcurrentPlayers)
                        {
                            var oldest = _activePlayers[0];
                            _activePlayers.RemoveAt(0);
                            try { oldest.Player.Stop(); } catch { }
                            try { oldest.Player.Dispose(); } catch { }
                            try { oldest.Reader.Dispose(); } catch { }
                        }
                        _activePlayers.Add((player, audioFile));
                    }

                    player.PlaybackStopped += (_, __) =>
                    {
                        lock (_playerLock)
                        {
                            for (int i = _activePlayers.Count - 1; i >= 0; i--)
                            {
                                if (ReferenceEquals(_activePlayers[i].Player, player))
                                {
                                    _activePlayers.RemoveAt(i);
                                    break;
                                }
                            }
                        }
                        try { player.Dispose(); } catch { }
                        try { audioFile.Dispose(); } catch { }
                    };

                    player.Play();

                    if (startTimestamp.HasValue)
                    {
                        var pipelineMs = Stopwatch.GetElapsedTime(startTimestamp.Value).TotalMilliseconds;
                        if (requestedAtUtc.HasValue)
                        {
                            var endToEndMs = (DateTime.UtcNow - requestedAtUtc.Value).TotalMilliseconds;
                            SafeAudioLogVerbose($"Latency: file={Path.GetFileName(selectedPath)}, source={source ?? "unknown"}, pipelineMs={pipelineMs:F1}, endToEndMs={endToEndMs:F1}, cached=false");
                        }
                        else
                        {
                            SafeAudioLogVerbose($"Latency: file={Path.GetFileName(selectedPath)}, source={source ?? "unknown"}, pipelineMs={pipelineMs:F1}, cached=false");
                        }
                    }

                    SafeAudioLogVerbose($"Started playback of {Path.GetFileName(selectedPath)}");
                }
                catch (Exception ex)
                {
                    SafeAudioLog($"Playback error for {filePath}: {ex.Message}");
                }
            });
        }

        private bool TryCreateAudioReader(string filePath, out AudioFileReader? audioFile, out string? error)
        {
            audioFile = null;
            error = null;

            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                switch (extension)
                {
                    case ".mp3":
                    case ".wav":
                        audioFile = new AudioFileReader(filePath);
                        return true;
                    case ".ogg":
                        try
                        {
                            using var vorbisReader = new VorbisWaveReader(filePath);
                        }
                        catch (Exception ex)
                        {
                            SafeAudioLog($"VorbisWaveReader failed for {filePath}, trying AudioFileReader: {ex.Message}");
                        }

                        audioFile = new AudioFileReader(filePath);
                        return true;
                    default:
                        error = $"Unsupported audio format: {extension}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { audioFile?.Dispose(); } catch { }
                audioFile = null;
                return false;
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            StopSound();
            
            // Dispose cached audio files
            lock (_cacheLock)
            {
                foreach (var audioFile in _cachedAudioFiles.Values)
                {
                    try
                    {
                        audioFile?.Dispose();
                    }
                    catch { }
                }
                _cachedAudioFiles.Clear();
            }

            lock (_playerLock)
            {
                foreach (var entry in _activePlayers)
                {
                    try { entry.Player.Dispose(); } catch { }
                    try { entry.Reader.Dispose(); } catch { }
                }
                _activePlayers.Clear();
            }
            
            try
            {
                MediaFoundationApi.Shutdown();
            }
            catch { }
        }

        /// <summary>
        /// Safe audio logging to audio.log with timestamping
        /// </summary>
        private static void SafeAudioLog(string message)
        {
            try
            {
                var line = $"[AUDIO] {DateTime.Now:O}: {message}";
                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                    System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Audio, line + Environment.NewLine);
            }
            catch { }
        }

        private static void SafeAudioLogVerbose(string message)
        {
            if (!IsVerboseAudioLoggingEnabled()) return;
            SafeAudioLog(message);
        }

        private static bool IsVerboseAudioLoggingEnabled()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        ~AudioNotificationService()
        {
            Dispose();
        }
    }
}
