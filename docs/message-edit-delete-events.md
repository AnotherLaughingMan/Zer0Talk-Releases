# Message Edit and Delete Event Channel Implementation

**Date**: January 2025  
**Status**: ✅ COMPLETED

## Overview

Implemented a centralized event channel for message edits and deletions, enabling reactive UI updates throughout the application. This provides a clean separation between message operations and UI updates, following the existing EventHub pattern.

## Changes Made

### 1. EventHub.cs - Added Two New Events

**Location**: `Services/EventHub.cs`

Added two new events with their corresponding raise methods:

```csharp
// Message edited locally or received edit from remote
public event Action<string, System.Guid, string>? MessageEdited;
public void RaiseMessageEdited(string peerUid, System.Guid messageId, string newContent)

// Message deleted locally or received delete from remote
public event Action<string, System.Guid>? MessageDeleted;
public void RaiseMessageDeleted(string peerUid, System.Guid messageId)
```

**Event Signatures**:
- `MessageEdited`: (peerUid, messageId, newContent)
- `MessageDeleted`: (peerUid, messageId)

**Safety Features**:
- Validates peerUid is not null/empty
- Validates messageId is not empty Guid
- Try/catch protection around all invocations

### 2. AppServices.cs - Remote Message Operations

**Location**: `Services/AppServices.cs`

#### MessagesUpdateFromRemote()
Updated to raise `MessageEdited` event when remote peer edits a message:

```csharp
public static void MessagesUpdateFromRemote(string peerUid, System.Guid messageId, string newContent)
{
    var mc = new MessageContainer();
    mc.UpdateMessage(peerUid, messageId, newContent, Passphrase);
    // Raise event so UI can refresh if this conversation is currently visible
    Events.RaiseMessageEdited(peerUid, messageId, newContent);
}
```

#### MessagesDeleteFromRemote()
Updated to raise `MessageDeleted` event when remote peer deletes a message:

```csharp
public static void MessagesDeleteFromRemote(string peerUid, System.Guid messageId)
{
    var mc = new MessageContainer();
    mc.DeleteMessage(peerUid, messageId, Passphrase);
    // Raise event so UI can refresh if this conversation is currently visible
    Events.RaiseMessageDeleted(peerUid, messageId);
}
```

### 3. MainWindowViewModel.cs - Local Message Operations

**Location**: `ViewModels/MainWindowViewModel.cs`

#### EditMessageCommand
Added event raising after local edit:

```csharp
msg.Content = newContent;
msg.IsEdited = true;
msg.EditedUtc = DateTime.UtcNow;
EnsureLinkPreviewProbe(msg, forceRefresh: true);
OnPropertyChanged(nameof(Messages));
_messagesStore.UpdateMessage(peerUid, id, newContent, AppServices.Passphrase);
// Raise event so other parts of the UI can react if needed
AppServices.Events.RaiseMessageEdited(peerUid, id, newContent);
```

#### DeleteMessageCommand
Added event raising after local delete:

```csharp
Messages.Remove(msg);
_messagesStore.DeleteMessage(SelectedContact.UID, id, AppServices.Passphrase);
// Raise event so other parts of the UI can react if needed
AppServices.Events.RaiseMessageDeleted(SelectedContact.UID, id);
```

### 4. MainWindowViewModel.cs - Event Subscriptions

**Location**: `ViewModels/MainWindowViewModel.cs` (Constructor)

Added two event handlers that listen for message edit/delete events and update the UI accordingly:

#### MessageEdited Handler
```csharp
Action<string, Guid, string> messageEditedHandler = (peerUid, messageId, newContent) =>
{
    if (SelectedContact?.UID != peerUid) return;
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        try
        {
            var m = Messages.FirstOrDefault(x => x.Id == messageId);
            if (m != null)
            {
                m.Content = newContent;
                m.IsEdited = true;
                m.EditedUtc = DateTime.UtcNow;
                OnPropertyChanged(nameof(Messages));
            }
        }
        catch { }
    });
};
AppServices.Events.MessageEdited += messageEditedHandler;
_teardownActions.Add(() => AppServices.Events.MessageEdited -= messageEditedHandler);
```

#### MessageDeleted Handler
```csharp
Action<string, Guid> messageDeletedHandler = (peerUid, messageId) =>
{
    if (SelectedContact?.UID != peerUid) return;
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        try
        {
            var m = Messages.FirstOrDefault(x => x.Id == messageId);
            if (m != null)
            {
                Messages.Remove(m);
            }
        }
        catch { }
    });
};
AppServices.Events.MessageDeleted += messageDeletedHandler;
_teardownActions.Add(() => AppServices.Events.MessageDeleted -= messageDeletedHandler);
```

## Architecture

### Event Flow

#### Message Edit Flow:
1. **Local Edit**: User edits a message
   - `MainWindowViewModel.EditMessageCommand` executes
   - Updates message in database via `MessageContainer.UpdateMessage()`
   - Raises `AppServices.Events.RaiseMessageEdited()`
   - Sends edit to remote peer via `NetworkService.SendEditMessageAsync()`

2. **Remote Edit**: Peer edits a message
   - `NetworkService` receives edit frame (0xB1)
   - Validates signature and UID
   - Calls `AppServices.MessagesUpdateFromRemote()`
   - Updates database and raises `MessageEdited` event
   - `MainWindowViewModel` handler updates UI if conversation is visible

#### Message Delete Flow:
1. **Local Delete**: User deletes a message
   - `MainWindowViewModel.DeleteMessageCommand` executes
   - Removes from UI immediately (`Messages.Remove()`)
   - Deletes from database via `MessageContainer.DeleteMessage()`
   - Raises `AppServices.Events.RaiseMessageDeleted()`
   - Sends delete to remote peer via `NetworkService.SendDeleteMessageAsync()`

2. **Remote Delete**: Peer deletes a message
   - `NetworkService` receives delete frame (0xB2)
   - Validates signature and UID
   - Calls `AppServices.MessagesDeleteFromRemote()`
   - Updates database and raises `MessageDeleted` event
   - `MainWindowViewModel` handler removes from UI if conversation is visible

### Threading Model

- **Event Raising**: Can occur on any thread (network thread, UI thread, background thread)
- **Event Handlers**: Marshal to UI thread using `Dispatcher.UIThread.Post()`
- **Safety**: All handlers wrapped in try/catch blocks
- **Cleanup**: Handlers are properly unsubscribed via `_teardownActions`

### Benefits

1. **Decoupling**: Message operations don't need direct references to UI components
2. **Extensibility**: Other parts of the app can subscribe to these events (e.g., notifications, logging)
3. **Consistency**: Follows the existing `EventHub` pattern used throughout the app
4. **Reliability**: Thread-safe with proper marshaling to UI thread
5. **Clean Shutdown**: Proper teardown via `_teardownActions` prevents memory leaks

## Relationship with Existing Events

The new events complement the existing `NetworkService` events:

### Existing NetworkService Events:
- `ChatMessageEdited`: Raised by NetworkService when edit frame is received
- `ChatMessageDeleted`: Raised by NetworkService when delete frame is received
- Used directly in MainWindowViewModel constructor

### New EventHub Events:
- `MessageEdited`: Raised for ALL edits (local + remote)
- `MessageDeleted`: Raised for ALL deletes (local + remote)
- Centralized in EventHub for app-wide access

Both sets of events coexist:
- NetworkService events: Protocol-level notifications
- EventHub events: Application-level notifications

## Testing Considerations

To test this implementation:

1. **Local Edit**:
   - Edit a message in an active conversation
   - Verify UI updates immediately
   - Check that message shows "edited" badge
   - Verify edit propagates to recipient

2. **Remote Edit**:
   - Have a peer edit a message
   - Verify UI updates when edit is received
   - Check "edited" badge appears

3. **Local Delete**:
   - Delete a message (own or received)
   - Verify message disappears from UI immediately
   - Verify it's gone after reopening conversation

4. **Remote Delete**:
   - Have a peer delete a message they sent
   - Verify message disappears from UI
   - Verify it's gone after reopening conversation

5. **Edge Cases**:
   - Edit/delete when conversation is not currently visible
   - Multiple rapid edits
   - Edit/delete while app is locked
   - Edit/delete of non-existent message

## Files Modified

1. `Services/EventHub.cs` - Added MessageEdited and MessageDeleted events
2. `Services/AppServices.cs` - Integrated event raising for remote operations
3. `ViewModels/MainWindowViewModel.cs` - Integrated event raising for local operations and added handlers
4. `checklist.md` - Marked TODOs as completed

## Build Status

✅ No compile errors  
✅ No lint warnings  
✅ All existing functionality preserved

## Future Enhancements

Possible extensions to this implementation:

1. **Notification Integration**: Show toast when message is edited/deleted in background conversation
2. **Logging**: Add structured log entries for all edit/delete operations
3. **Undo Support**: Could use events to implement undo functionality
4. **Statistics**: Track edit/delete frequency for analytics
5. **Audit Trail**: Maintain history of all edits for security/compliance
