using System;
using System.Threading.Tasks;

namespace ZTalk.Services
{
    /// <summary>
    /// Static helper class for easy audio notification access throughout the application
    /// </summary>
    public static class AudioHelper
    {
        /// <summary>
        /// Play an incoming message sound
        /// </summary>
        public static async Task PlayIncomingMessageAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.MessageIncoming);
            }
            catch { }
        }

        /// <summary>
        /// Play an outgoing message sound
        /// </summary>
        public static async Task PlayOutgoingMessageAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.MessageOutgoing);
            }
            catch { }
        }

        /// <summary>
        /// Play an incoming call sound
        /// </summary>
        public static async Task PlayIncomingCallAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.CallIncoming);
            }
            catch { }
        }

        /// <summary>
        /// Play a call end sound
        /// </summary>
        public static async Task PlayCallEndAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.CallEnd);
            }
            catch { }
        }

        /// <summary>
        /// Play a voice note recording start sound
        /// </summary>
        public static async Task PlayVoiceNoteStartAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.VoiceNoteStart);
            }
            catch { }
        }

        /// <summary>
        /// Play a voice note recording end sound
        /// </summary>
        public static async Task PlayVoiceNoteEndAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.VoiceNoteEnd);
            }
            catch { }
        }

        /// <summary>
        /// Play a general notification sound
        /// </summary>
        public static async Task PlayNotificationAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.NotificationGeneral);
            }
            catch { }
        }

        /// <summary>
        /// Play an alert/warning notification sound
        /// </summary>
        public static async Task PlayAlertNotificationAsync()
        {
            try
            {
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.NotificationAlert);
            }
            catch { }
        }

        /// <summary>
        /// Play a custom sound file
        /// </summary>
        public static async Task PlayCustomSoundAsync(string fileName)
        {
            try
            {
                await AppServices.AudioNotifications.PlayCustomSoundAsync(fileName);
            }
            catch { }
        }

        /// <summary>
        /// Stop any currently playing sound
        /// </summary>
        public static void StopSound()
        {
            try
            {
                AppServices.AudioNotifications.StopSound();
            }
            catch { }
        }

        /// <summary>
        /// Enable or disable sound notifications
        /// </summary>
        public static bool IsEnabled
        {
            get => AppServices.AudioNotifications.IsEnabled;
            set => AppServices.AudioNotifications.IsEnabled = value;
        }

        /// <summary>
        /// Main volume control (0.0 to 1.0) - affects all audio
        /// </summary>
        public static float MainVolume
        {
            get => AppServices.AudioNotifications.MainVolume;
            set => AppServices.AudioNotifications.MainVolume = value;
        }

        /// <summary>
        /// Notification volume control (0.0 to 1.0) - combined with MainVolume
        /// </summary>
        public static float NotificationVolume
        {
            get => AppServices.AudioNotifications.NotificationVolume;
            set => AppServices.AudioNotifications.NotificationVolume = value;
        }

        /// <summary>
        /// Chat volume control (0.0 to 1.0) - combined with MainVolume
        /// </summary>
        public static float ChatVolume
        {
            get => AppServices.AudioNotifications.ChatVolume;
            set => AppServices.AudioNotifications.ChatVolume = value;
        }

        /// <summary>
        /// Legacy volume property - returns notification volume for backward compatibility
        /// </summary>
        [Obsolete("Use MainVolume, NotificationVolume, or ChatVolume instead")]
        public static float Volume
        {
            get => NotificationVolume;
            set => NotificationVolume = value;
        }

        /// <summary>
        /// Preload common sounds for instant playback (call during app startup)
        /// </summary>
        public static void PreloadSounds()
        {
            try
            {
                // Trigger preloading by accessing the service
                _ = AppServices.AudioNotifications;
                SafeAudioLog("Sound preloading initiated");
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Sound preloading failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Test the audio system by playing the first available sound
        /// </summary>
        public static async Task TestAudioSystemAsync()
        {
            try
            {
                var originalMainVolume = MainVolume;
                var originalNotificationVolume = NotificationVolume;
                
                MainVolume = 0.8f; // Set to moderate volume for testing
                NotificationVolume = 0.7f; // Set notification channel volume
                
                // Try to play a test sound - start with existing files
                await PlayCustomSoundAsync("melodious-notification-sound.mp3");
                
                // Wait a bit, then try message incoming
                await Task.Delay(1000);
                await PlayIncomingMessageAsync();
                
                MainVolume = originalMainVolume; // Restore original volumes
                NotificationVolume = originalNotificationVolume;
                
                SafeAudioLog("Audio system test completed");
            }
            catch (Exception ex)
            {
                SafeAudioLog($"Audio system test failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Safe audio logging to audio.log with timestamping
        /// </summary>
        private static void SafeAudioLog(string message)
        {
            try
            {
                var line = $"[AUDIO_HELPER] {DateTime.Now:O}: {message}";
                if (ZTalk.Utilities.LoggingPaths.Enabled)
                    System.IO.File.AppendAllText(ZTalk.Utilities.LoggingPaths.Audio, line + Environment.NewLine);
            }
            catch { }
        }
    }
}