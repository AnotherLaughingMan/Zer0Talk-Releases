using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Vorbis;
using NAudio.MediaFoundation;
using ZTalk.Utilities;

namespace ZTalk.Services
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
        private bool _isEnabled = true;
        private float _mainVolume = 1.0f;
        private float _notificationVolume = 0.8f;
        private float _chatVolume = 0.7f;
        private IWavePlayer? _currentPlayer;
        private readonly object _playerLock = new();
        
        // Cached audio readers for immediate playback
        private readonly Dictionary<string, AudioFileReader> _cachedAudioFiles = new();
        private readonly object _cacheLock = new();

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
            
            SafeAudioLog($"Initialized with sounds directory: {_soundsDirectory}");
            SafeAudioLog($"Directory exists: {Directory.Exists(_soundsDirectory)}");
            
            if (Directory.Exists(_soundsDirectory))
            {
                var files = Directory.GetFiles(_soundsDirectory, "*.*", SearchOption.AllDirectories);
                SafeAudioLog($"Found {files.Length} sound files");
                foreach (var file in files)
                {
                    SafeAudioLog($"Available sound file: {Path.GetFileName(file)}");
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
                    var soundFile = Path.Combine(_soundsDirectory, soundFileName);
                    if (File.Exists(soundFile))
                    {
                        PreloadAudioFile(soundFile);
                        SafeAudioLog($"Preloaded toast sound: {soundFileName}");
                    }
                    else
                    {
                        SafeAudioLog($"Toast sound file not found: {soundFileName}");
                    }
                }
                
                SafeAudioLog($"Preloaded {_cachedAudioFiles.Count} sound files for instant playback");
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Error preloading sounds: {ex.Message}");
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
        public async Task PlaySoundAsync(SoundType soundType)
        {
            var effectiveVolume = GetEffectiveVolume(soundType);
            if (!_isEnabled || effectiveVolume <= 0.0f)
            {
                SafeAudioLog($"Sound disabled or volume zero - skipping {soundType}");
                return;
            }

            // Check if audio should be suppressed in Do Not Disturb mode
            try
            {
                var settings = AppServices.Settings.Settings;
                if (settings.SuppressNotificationsInDnd && settings.Status == Models.PresenceStatus.DoNotDisturb)
                {
                    SafeAudioLog($"Sound suppressed due to Do Not Disturb mode - skipping {soundType}");
                    return;
                }
            }
            catch { }

            try
            {
                var soundFile = FindSoundFile(soundType);
                if (string.IsNullOrEmpty(soundFile))
                {
                    SafeAudioLog($"No sound file found for {soundType}");
                    return;
                }

                SafeAudioLog($"Playing {soundType}: {Path.GetFileName(soundFile)} at {effectiveVolume:F2} volume");
                await PlayAudioFileAsync(soundFile, effectiveVolume);
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Failed to play {soundType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Play a custom sound file using main volume
        /// </summary>
        public async Task PlayCustomSoundAsync(string fileName)
        {
            if (!_isEnabled || _mainVolume <= 0.0f)
                return;

            try
            {
                var soundFile = Path.Combine(_soundsDirectory, fileName);
                if (!File.Exists(soundFile))
                {
                    SafeAudioLog($"Custom sound file not found: {fileName}");
                    return;
                }

                SafeAudioLog($"Playing custom sound: {fileName}");
                await PlayAudioFileAsync(soundFile, _mainVolume);
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
                    _currentPlayer?.Stop();
                    _currentPlayer?.Dispose();
                    _currentPlayer = null;
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
                var fullPath = Path.Combine(_soundsDirectory, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        private async Task PlayAudioFileAsync(string filePath, float volume = 1.0f)
        {
            await Task.Run(() =>
            {
                lock (_playerLock)
                {
                    try
                    {
                        // Stop any currently playing sound
                        _currentPlayer?.Stop();
                        _currentPlayer?.Dispose();

                        AudioFileReader? audioFile = null;
                        var key = filePath.ToLowerInvariant();
                        
                        // Try to use cached audio file first for instant playback
                        lock (_cacheLock)
                        {
                            if (_cachedAudioFiles.TryGetValue(key, out var cachedFile))
                            {
                                // Reset position to start for reuse
                                cachedFile.Position = 0;
                                cachedFile.Volume = volume;
                                audioFile = cachedFile;
                                SafeAudioLog($"Using cached audio for {Path.GetFileName(filePath)}");
                            }
                        }
                        
                        // If not cached, create new audio file reader
                        if (audioFile == null)
                        {
                            var extension = Path.GetExtension(filePath).ToLowerInvariant();

                            switch (extension)
                            {
                                case ".mp3":
                                case ".wav":
                                    audioFile = new AudioFileReader(filePath);
                                    break;
                                case ".ogg":
                                    // For OGG files, try VorbisWaveReader first, then fallback to AudioFileReader
                                    try
                                    {
                                        var vorbisReader = new VorbisWaveReader(filePath);
                                        // Wrap VorbisWaveReader to make it compatible with AudioFileReader
                                        audioFile = new AudioFileReader(filePath);
                                    }
                                    catch (Exception ex)
                                    {
                                        SafeAudioLog($"VorbisWaveReader failed for {filePath}, trying AudioFileReader: {ex.Message}");
                                        try
                                        {
                                            // Fallback to AudioFileReader which might handle some OGG files
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

                            // Set volume for non-cached files
                            audioFile.Volume = volume;
                            SafeAudioLog($"Created new audio reader for {Path.GetFileName(filePath)}");
                        }

                        // Create output device
                        _currentPlayer = new WaveOutEvent();
                        _currentPlayer.Init(audioFile);

                        // Handle playback completion
                        var isCached = false;
                        lock (_cacheLock)
                        {
                            isCached = _cachedAudioFiles.ContainsKey(key);
                        }
                        
                        _currentPlayer.PlaybackStopped += (sender, args) =>
                        {
                            lock (_playerLock)
                            {
                                try
                                {
                                    // Only dispose non-cached audio files
                                    if (!isCached)
                                    {
                                        audioFile?.Dispose();
                                    }
                                    _currentPlayer?.Dispose();
                                    _currentPlayer = null;
                                }
                                catch { }
                            }
                        };

                        // Start playback
                        _currentPlayer.Play();

                        SafeAudioLog($"Started playback of {Path.GetFileName(filePath)}");
                    }
                    catch (Exception ex)
                    {
                        SafeAudioLog($"Playback error for {filePath}: {ex.Message}");
                    }
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
                if (ZTalk.Utilities.LoggingPaths.Enabled)
                    System.IO.File.AppendAllText(ZTalk.Utilities.LoggingPaths.Audio, line + Environment.NewLine);
            }
            catch { }
        }

        ~AudioNotificationService()
        {
            Dispose();
        }
    }
}