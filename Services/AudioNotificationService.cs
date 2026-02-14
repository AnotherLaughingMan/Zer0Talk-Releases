using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Vorbis;
using NAudio.MediaFoundation;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Audio notification service supporting .ogg, .mp3, and .wav formats
    /// Inspired by Signal Desktop's sound system architecture
    /// </summary>
    public class AudioNotificationService : IDisposable
    {
        private static AudioNotificationService? _instance;
        private static readonly object _lock = new();
        
        public static AudioNotificationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new AudioNotificationService();
                    }
                }
                return _instance;
            }
        }

        public enum SoundType
        {
            MessageIncoming,      // New message received
            MessageOutgoing,      // Message sent confirmation
            NotificationGeneral,  // General notifications
            NotificationAlert,    // Alert/warning notifications
            CallIncoming,         // Incoming call ringtone
            CallEnd,             // Call ended sound
            TypingIndicator,     // Typing notification (optional)
            VoiceNoteStart,      // Voice recording started
            VoiceNoteEnd,        // Voice recording ended
            Custom               // Custom sound file
        }

        // Sound file mappings - supports .ogg, .mp3, .wav
        private static readonly Dictionary<SoundType, string[]> SoundFiles = new()
        {
            { SoundType.MessageIncoming, new[] { "multi-pop-2-188167.mp3" } },
            { SoundType.MessageOutgoing, new[] { "multi-pop-1-188165.mp3" } },
            { SoundType.NotificationGeneral, new[] { "melodious-notification-sound.mp3" } },
            { SoundType.NotificationAlert, new[] { "smooth-notify-alert-toast-warn-274736.mp3" } },
            { SoundType.CallIncoming, new[] { "call-incoming.ogg", "call-incoming.mp3", "call-incoming.wav", "icq-music-on-startup.mp3" } },
            { SoundType.CallEnd, new[] { "call-end.ogg", "call-end.mp3", "call-end.wav" } },
            { SoundType.TypingIndicator, new[] { "typing.ogg", "typing.mp3", "typing.wav" } },
            { SoundType.VoiceNoteStart, new[] { "voice-start.ogg", "voice-start.mp3", "voice-start.wav" } },
            { SoundType.VoiceNoteEnd, new[] { "voice-end.ogg", "voice-end.mp3", "voice-end.wav" } }
        };

        private readonly string _soundsDirectory;
        private readonly string _optimizedSoundsDirectory;
        private bool _isEnabled = true;
        private float _mainVolume = 1.0f;
        private float _notificationVolume = 0.8f;
        private float _chatVolume = 0.7f;
        private readonly object _playerLock = new();
        private readonly List<(IWavePlayer Player, AudioFileReader Reader)> _activePlayers = new();
        private const int MaxConcurrentPlayers = 8;
        
        // Cached audio readers for immediate playback
        private readonly Dictionary<string, AudioFileReader> _cachedAudioFiles = new();
        private readonly object _cacheLock = new();

        // Runtime-optimized (decoded WAV) sound mappings for lower playback startup latency
        private readonly Dictionary<string, string> _optimizedBySourceFileName = new(StringComparer.OrdinalIgnoreCase);
        private int _audioWarmupAttempted;

        private static readonly string[] RuntimeOptimizedSourceFiles =
        {
            "multi-pop-2-188167.mp3",
            "multi-pop-1-188165.mp3",
            "melodious-notification-sound.mp3",
            "ui-10-smooth-warnnotify-sound-effect-365842.mp3",
            "smooth-notify-alert-toast-warn-274736.mp3",
            "smooth-completed-notify-starting-alert-274739.mp3"
        };

        static AudioNotificationService()
        {
            // Initialize MediaFoundation for MP3 support
            try
            {
                MediaFoundationApi.Startup();
            }
            catch (Exception ex)
            {
                SafeAudioLog($"MediaFoundation startup failed: {ex.Message}");
            }
        }

        private AudioNotificationService()
        {
            // Determine sounds directory path
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _soundsDirectory = Path.Combine(baseDir, "Assets", "Sounds");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _optimizedSoundsDirectory = Path.Combine(localAppData, "Zer0Talk", "AudioCache");
            
            SafeAudioLogVerbose($"Initialized with sounds directory: {_soundsDirectory}");
            SafeAudioLogVerbose($"Directory exists: {Directory.Exists(_soundsDirectory)}");
            
            if (Directory.Exists(_soundsDirectory))
            {
                _ = Task.Run(() => WarmUpAudioOutputDevice());
                PrepareRuntimeOptimizedSounds();

                var files = Directory.GetFiles(_soundsDirectory, "*.*", SearchOption.AllDirectories);
                SafeAudioLogVerbose($"Found {files.Length} sound files");
                foreach (var file in files)
                {
                    SafeAudioLogVerbose($"Available sound file: {Path.GetFileName(file)}");
                }
                
                // Preload frequently used sound files for instant playback
                PreloadCommonSounds();
            }
        }
        
        /// <summary>
        /// Preload commonly used sound files into memory for instant playback
        /// </summary>
        private void PreloadCommonSounds()
        {
            try
            {
                // Preload message sounds for instant playback
                var messageSounds = new[]
                {
                    SoundType.MessageIncoming,
                    SoundType.MessageOutgoing,
                    SoundType.NotificationGeneral,
                    SoundType.NotificationAlert
                };
                
                foreach (var soundType in messageSounds)
                {
                    var soundFile = FindSoundFile(soundType);
                    if (!string.IsNullOrEmpty(soundFile))
                    {
                        PreloadAudioFile(soundFile);
                    }
                }

                // Preload toast notification specific sounds
                var toastSounds = new[]
                {
                    "ui-10-smooth-warnnotify-sound-effect-365842.mp3",      // Warning
                    "smooth-notify-alert-toast-warn-274736.mp3",            // Information
                    "smooth-completed-notify-starting-alert-274739.mp3"     // Error
                };

                foreach (var soundFileName in toastSounds)
                {
                    var soundFile = ResolveSoundPath(soundFileName);
                    if (File.Exists(soundFile))
                    {
                        PreloadAudioFile(soundFile);
                        SafeAudioLogVerbose($"Preloaded toast sound: {soundFileName}");
                    }
                    else
                    {
                        SafeAudioLogVerbose($"Toast sound file not found: {soundFileName}");
                    }
                }
                
                SafeAudioLogVerbose($"Preloaded {_cachedAudioFiles.Count} sound files for instant playback");
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Error preloading sounds: {ex.Message}");
            }
        }

        private void PrepareRuntimeOptimizedSounds()
        {
            try
            {
                Directory.CreateDirectory(_optimizedSoundsDirectory);

                foreach (var sourceFileName in RuntimeOptimizedSourceFiles)
                {
                    try
                    {
                        var sourcePath = Path.Combine(_soundsDirectory, sourceFileName);
                        if (!File.Exists(sourcePath))
                        {
                            continue;
                        }

                        var optimizedFileName = Path.GetFileNameWithoutExtension(sourceFileName) + ".wav";
                        var optimizedPath = Path.Combine(_optimizedSoundsDirectory, optimizedFileName);

                        var shouldRebuild = !File.Exists(optimizedPath)
                            || File.GetLastWriteTimeUtc(optimizedPath) < File.GetLastWriteTimeUtc(sourcePath);

                        if (shouldRebuild)
                        {
                            using var reader = new AudioFileReader(sourcePath);
                            WaveFileWriter.CreateWaveFile16(optimizedPath, reader);
                            SafeAudioLogVerbose($"Built optimized WAV cache: {sourceFileName} -> {optimizedFileName}");
                        }

                        _optimizedBySourceFileName[sourceFileName] = optimizedPath;
                    }
                    catch (Exception ex)
                    {
                        SafeAudioLog($"Failed to optimize {sourceFileName}: {ex.Message}");
                    }
                }

                SafeAudioLogVerbose($"Runtime optimized sound cache entries: {_optimizedBySourceFileName.Count}");
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Runtime optimized sound cache unavailable: {ex.Message}");
            }
        }

        private string ResolveSoundPath(string candidateFileName)
        {
            try
            {
                var ext = Path.GetExtension(candidateFileName);
                if (!string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    var wavCandidate = Path.GetFileNameWithoutExtension(candidateFileName) + ".wav";
                    var wavPath = Path.Combine(_soundsDirectory, wavCandidate);
                    if (File.Exists(wavPath))
                    {
                        return wavPath;
                    }
                }
            }
            catch { }

            if (_optimizedBySourceFileName.TryGetValue(candidateFileName, out var optimizedPath) && File.Exists(optimizedPath))
            {
                return optimizedPath;
            }

            return Path.Combine(_soundsDirectory, candidateFileName);
        }

        private void WarmUpAudioOutputDevice()
        {
            if (Interlocked.Exchange(ref _audioWarmupAttempted, 1) == 1)
            {
                return;
            }

            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
                var buffer = new BufferedWaveProvider(format)
                {
                    BufferLength = format.AverageBytesPerSecond / 2,
                    DiscardOnBufferOverflow = true
                };

                using var warmupPlayer = new WaveOutEvent();
                warmupPlayer.Init(buffer);
                warmupPlayer.Play();
                Thread.Sleep(25);
                warmupPlayer.Stop();
                SafeAudioLogVerbose("Audio output warm-up complete");
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Audio output warm-up failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Preload a specific audio file into the cache
        /// </summary>
        private void PreloadAudioFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;
                
            lock (_cacheLock)
            {
                try
                {
                    // Don't reload if already cached
                    var key = filePath.ToLowerInvariant();
                    if (_cachedAudioFiles.ContainsKey(key))
                        return;
                    
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();
                    AudioFileReader? audioFile = null;
                    
                    switch (extension)
                    {
                        case ".mp3":
                        case ".wav":
                            audioFile = new AudioFileReader(filePath);
                            break;
                        case ".ogg":
                            try
                            {
                                audioFile = new AudioFileReader(filePath);
                            }
                            catch (Exception ex)
                            {
                                SafeAudioLog($"Failed to preload OGG file {Path.GetFileName(filePath)}: {ex.Message}");
                                return;
                            }
                            break;
                        default:
                            SafeAudioLog($"Skipping unsupported format for preload: {extension}");
                            return;
                    }
                    
                    if (audioFile != null)
                    {
                        _cachedAudioFiles[key] = audioFile;
                        SafeAudioLog($"Preloaded {Path.GetFileName(filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    SafeAudioLog($"Failed to preload {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Enable or disable sound notifications
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>
        /// Set main volume (0.0 to 1.0) - affects all sounds as a master volume
        /// </summary>
        public float MainVolume
        {
            get => _mainVolume;
            set => _mainVolume = Math.Clamp(value, 0.0f, 1.0f);
        }

        /// <summary>
        /// Set notification volume (0.0 to 1.0) - for general notifications
        /// </summary>
        public float NotificationVolume
        {
            get => _notificationVolume;
            set => _notificationVolume = Math.Clamp(value, 0.0f, 1.0f);
        }

        /// <summary>
        /// Set chat volume (0.0 to 1.0) - for chat-related sounds
        /// </summary>
        public float ChatVolume
        {
            get => _chatVolume;
            set => _chatVolume = Math.Clamp(value, 0.0f, 1.0f);
        }

        /// <summary>
        /// Legacy Volume property - now uses MainVolume for backward compatibility
        /// </summary>
        [Obsolete("Use MainVolume, NotificationVolume, or ChatVolume instead")]
        public float Volume
        {
            get => _mainVolume;
            set => _mainVolume = Math.Clamp(value, 0.0f, 1.0f);
        }

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
                var fullPath = ResolveSoundPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        private async Task PlayAudioFileAsync(string filePath, float volume = 1.0f, long? startTimestamp = null, DateTime? requestedAtUtc = null, string? source = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    AudioFileReader? audioFile = null;

                    // Create new audio file reader per playback so concurrent sounds can overlap safely
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();

                    switch (extension)
                    {
                        case ".mp3":
                        case ".wav":
                            audioFile = new AudioFileReader(filePath);
                            break;
                        case ".ogg":
                            try
                            {
                                var vorbisReader = new VorbisWaveReader(filePath);
                                vorbisReader.Dispose();
                                audioFile = new AudioFileReader(filePath);
                            }
                            catch (Exception ex)
                            {
                                SafeAudioLog($"VorbisWaveReader failed for {filePath}, trying AudioFileReader: {ex.Message}");
                                try
                                {
                                    audioFile = new AudioFileReader(filePath);
                                }
                                catch
                                {
                                    SafeAudioLog($"AudioFileReader also failed for OGG file: {filePath}");
                                    return;
                                }
                            }
                            break;
                        default:
                            SafeAudioLog($"Unsupported audio format: {extension}");
                            return;
                    }

                    if (audioFile == null)
                    {
                        SafeAudioLog($"Failed to create audio reader for: {filePath}");
                        return;
                    }

                    audioFile.Volume = volume;
                    SafeAudioLogVerbose($"Created new audio reader for {Path.GetFileName(filePath)}");

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
                            SafeAudioLogVerbose($"Latency: file={Path.GetFileName(filePath)}, source={source ?? "unknown"}, pipelineMs={pipelineMs:F1}, endToEndMs={endToEndMs:F1}, cached=false");
                        }
                        else
                        {
                            SafeAudioLogVerbose($"Latency: file={Path.GetFileName(filePath)}, source={source ?? "unknown"}, pipelineMs={pipelineMs:F1}, cached=false");
                        }
                    }

                    SafeAudioLogVerbose($"Started playback of {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    SafeAudioLog($"Playback error for {filePath}: {ex.Message}");
                }
            });
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
