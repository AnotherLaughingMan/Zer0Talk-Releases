# ZTalk Audio Notification System

## Overview
The ZTalk audio notification system supports multiple audio formats for flexible notification sounds:
- **OGG Vorbis** (.ogg) - Open source, high quality, small file size
- **MP3** (.mp3) - Widely supported, good compression
- **WAV** (.wav) - Uncompressed, highest quality, larger file size

## Sound Types
The system defines several sound types for different events:

### Core Message Sounds
- `MessageIncoming` - New message received
- `MessageOutgoing` - Message sent confirmation

### Call Sounds  
- `CallIncoming` - Incoming call ringtone
- `CallEnd` - Call ended sound

### Voice Note Sounds
- `VoiceNoteStart` - Voice recording started
- `VoiceNoteEnd` - Voice recording ended

### General
- `NotificationGeneral` - General notifications
- `TypingIndicator` - Typing notification (optional)
- `Custom` - Custom sound file

## File Structure
Place your sound files in the `Assets/Sounds/` directory:

```
Assets/Sounds/
├── message-incoming.ogg     # Primary incoming message sound
├── message-incoming.mp3     # MP3 fallback
├── message-incoming.wav     # WAV fallback
├── message-outgoing.ogg     # Primary outgoing message sound  
├── message-outgoing.mp3     # MP3 fallback
├── message-outgoing.wav     # WAV fallback
├── notification.ogg         # General notification sound
├── call-incoming.ogg        # Incoming call ringtone
├── call-end.ogg            # Call end sound
├── voice-start.ogg         # Voice recording start
├── voice-end.ogg           # Voice recording end
└── typing.ogg              # Typing indicator (optional)
```

## File Format Priority
The system will search for sounds in this order:
1. `.ogg` files (preferred for quality/size balance)
2. `.mp3` files (fallback for compatibility)
3. `.wav` files (fallback for maximum compatibility)
4. Existing files (like your current `melodious-notification-sound.mp3`)

## Usage Examples

### In Code
```csharp
// Play specific sound types
await AudioHelper.PlayIncomingMessageAsync();
await AudioHelper.PlayOutgoingMessageAsync();
await AudioHelper.PlayIncomingCallAsync();

// Play custom sound
await AudioHelper.PlayCustomSoundAsync("my-custom-sound.ogg");

// Control playback
AudioHelper.StopSound();
AudioHelper.IsEnabled = true;
AudioHelper.Volume = 0.8f; // 80% volume
```

### Direct Service Access
```csharp
// Direct service access for more control
var audio = AppServices.AudioNotifications;
await audio.PlaySoundAsync(AudioNotificationService.SoundType.MessageIncoming);
audio.Volume = 0.5f;
audio.IsEnabled = false;
```

## Current Sound Files
Your existing sound files will be used as fallbacks:
- `melodious-notification-sound.mp3` - Used for incoming messages and general notifications
- `icq-music-on-startup.mp3` - Used for outgoing messages and call sounds

## Recommended Sound Sources
For creating your own notification sounds:

### Free Resources
- **Freesound.org** - Creative Commons licensed sounds
- **Zapsplat** - Professional sound effects (requires free account)
- **BBC Sound Effects** - High quality, royalty-free

### Sound Characteristics
- **Duration**: 0.5-3 seconds for notifications, longer for ringtones
- **Volume**: Normalize to prevent jarring volume differences
- **Format**: OGG Vorbis recommended for best quality/size ratio
- **Sample Rate**: 44.1kHz or 48kHz
- **Bit Depth**: 16-bit minimum

## Integration Points
The audio system is automatically integrated with:
- `NotificationService` - Plays sounds for all notifications
- Message notifications - Incoming/outgoing message sounds
- Toast notifications - General notification sounds

## Configuration
Audio settings can be controlled through:
- `AudioHelper.IsEnabled` - Enable/disable all sounds
- `AudioHelper.Volume` - Set volume level (0.0 to 1.0)
- Per-sound-type customization (future enhancement)

## Troubleshooting

### No Sound Playing
1. Check if `AudioHelper.IsEnabled` is true
2. Verify sound files exist in `Assets/Sounds/`
3. Check volume level with `AudioHelper.Volume`
4. Look for error messages in logs

### Unsupported Format
The system supports OGG, MP3, and WAV. Other formats will be rejected with a log message.

### Missing Files
The system will try fallback files and existing files. Check logs for which files are being used.

## Future Enhancements
- Per-contact custom sounds
- Sound themes/packs
- User-configurable sound mappings via Settings
- Volume per sound type
- Do Not Disturb integration