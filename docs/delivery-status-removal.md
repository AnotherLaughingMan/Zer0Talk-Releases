# Delivery Status Tracking Removal

**Date:** October 17, 2025  
**Reason:** Session instability preventing reliable read receipt delivery; user opted to remove entire tracking system

## Overview

Completely removed the message delivery status tracking system from Zer0Talk. Messages no longer display "Pending", "Sending", "Sent", or "Read" indicators. The application now operates on a "trust-based" model where users assume messages are delivered successfully.

## Root Cause

Investigation revealed that read receipts (0xB6 frames) were not reaching the sender due to:
- Frequent session disconnects immediately after sending read receipts
- `Error decrypting message` and `EndOfStreamException` errors
- Connection forcibly closed by remote host
- AEAD transport counter desynchronization

Rather than fixing the underlying session stability issues, the decision was made to remove the entire delivery tracking feature.

## Files Modified

### Models
- **Message.cs**
  - Removed `DeliveryStatus` (string) property
  - Removed `DeliveredUtc` (DateTime?) property  
  - Removed `ReadUtc` (DateTime?) property

### Containers
- **MessageContainer.cs**
  - Removed `UpdateDelivery()` method (previously used to persist delivery status changes)

### Services
- **NetworkService.cs**
  - Removed `SendReadReceiptAsync()` method
  - Removed 0xB6 read receipt frame handling (now silently ignored)
  - Removed `ChatMessageReadAcked` event declaration
  - Removed `ChatMessageReceivedAcked` event declaration
  - Changed 0xB5 received-ack handling to no-op with comment

- **AppServices.cs**
  - Removed `ChatMessageReadAcked` event handler subscription
  - Removed `ChatMessageReceivedAcked` event handler subscription

- **OutboxService.cs**
  - Removed delivery status updates on successful send

- **EventHub.cs**
  - Removed `OutboundDeliveryUpdated` event declaration
  - Removed `RaiseOutboundDeliveryUpdated()` method

### ViewModels
- **MainWindowViewModel.cs**
  - Removed `ChatMessageReceivedAcked` event handler (lines 859-888)
  - Removed `OutboundDeliveryUpdated` event handler (lines 893-928)
  - Removed `MarkOutgoingMessagesReadForContact()` method
  - Simplified `MarkAllUnreadMessagesAsRead()` to only clear notification badges
  - Simplified `MarkMessagesAsRead()` to only clear notification badges
  - Simplified `HandleSimulatedSend()` to skip delivery status simulation
  - Simplified `IsWithinEditWindow()` to use only `Timestamp` instead of `DeliveredUtc`
  - Simplified `CanEditMessage()` to remove `DeliveryStatus == "Pending"` special case
  - Simplified `DrainSimulatedPending()` to use `OutboxService.DrainAsync()`
  - Simplified `GetEditRemaining()` to use only `Timestamp`
  - Simplified `ShowEditRemaining()` to not check `DeliveryStatus`
  - Removed `DeliveryStatus` initialization from new message creation
  - Removed delivery status tracking from send callback logic
  - Removed "isPending" special handling in `EditMessageCommand`
  - Removed `DeliveryStatus`, `DeliveredUtc` from incoming message creation

### Views
- **MainWindow.axaml**
  - Removed entire delivery status indicator UI section (lines 745-798)
    - Clock icon for "Pending" status
    - Spinner for "Sending" status
    - Dot for "Sent" status
    - Checkmark for "Read" status

- **MainWindow.axaml.cs**
  - Removed `DeliveryStatus` initialization from test message injection (line 1331)

## Protocol Changes

### Frames Still Handled
- **0xB0** - Chat message (send/receive)
- **0xB1** - Edit message
- **0xB2** - Delete message
- **0xB3** - Edit ack
- **0xB4** - Delete ack

### Frames Now Ignored
- **0xB5** - Received acknowledgment (no-op, formerly updated status to "Sent")
- **0xB6** - Read receipt (silently ignored, frame type removed from protocol)

## User Experience Changes

### Before
- Messages showed progression: `Pending` → `Sending` → `Sent` → `Read`
- Users could see when recipient read their message
- Clock icon for queued messages
- Spinner for in-flight messages
- Dot for delivered messages
- Checkmark for read messages

### After
- Messages are sent and displayed immediately
- No visual indicators for delivery or read status
- Users trust that messages are delivered successfully
- Edit window still enforced (25 minutes from `Timestamp`)
- Notification badges still cleared when viewing conversation

## Behavioral Preservation

- **Message editing** - Still works; edit window based on `Timestamp` instead of `DeliveredUtc`
- **Notification system** - Still works; clears badges when viewing conversation
- **Message queueing** - Still works; offline messages queued in `OutboxService`
- **Session management** - Unchanged; still uses AEAD encrypted transport
- **Signature validation** - Unchanged; still validates all incoming messages

## Testing Recommendations

1. Send messages between two clients - verify they appear without errors
2. Edit messages within 25-minute window - verify edits work
3. Queue messages while contact offline - verify they drain when contact comes online
4. Check notification badges - verify they clear when viewing conversation
5. Test simulated contacts - verify echo responses work
6. Verify no UI remnants of delivery status indicators

## Future Considerations

If delivery tracking needs to be restored:
1. Fix underlying session stability issues first
2. Investigate AEAD counter desynchronization
3. Add session recovery/reconnection logic
4. Consider connection pooling or keep-alive mechanism
5. Review `EndOfStreamException` and "connection forcibly closed" errors

## Build Status

✅ **Build successful** - All compilation errors resolved  
⚠️ 6 pre-existing warnings (unrelated to this change):
- NetworkWindow drag/drop obsolete API warnings (5)
- MainWindow unused field warning (1)

## Completion Checklist

- [x] Remove properties from `Message` model
- [x] Remove `UpdateDelivery()` from `MessageContainer`
- [x] Remove read receipt protocol handling
- [x] Remove delivery status event subscriptions
- [x] Remove UI indicators from XAML
- [x] Remove ViewModel delivery tracking logic
- [x] Remove EventHub delivery events
- [x] Verify no remaining references
- [x] Build succeeds without errors
- [x] Document changes

## Summary

The delivery status tracking system has been completely removed from Zer0Talk. The application now operates without delivery or read indicators, trusting that messages are successfully delivered. Core messaging functionality remains intact, including sending, receiving, editing, deleting, and queuing messages.
