# Contact Card Refresh Issue - Root Cause Analysis

**Date**: 2025-01-06  
**Version**: 0.0.1.58 (debug build)

## Problem Statement

When a contact is added to the contact list:
1. **Verification badge** doesn't appear until app restart (even after mutual verification)
2. **Presence status** doesn't update to "Online" until app restart
3. **Timestamp** (LastPresenceUtc) doesn't appear until app restart

The user who **accepts** the contact request sees these issues. The user who **sent** the request sees updates correctly.

## Architecture Overview

### Contact Flow
1. Alice sends contact request to Bob
2. Bob accepts → `OnInboundAccept()` is called
3. A new `Contact` object is created with default values:
   - `Presence = PresenceStatus.Offline`
   - `LastPresenceUtc = null`
   - `PublicKeyVerified = false`
   - `IsVerified = false`
4. Contact is added to `ContactManager._contacts`
5. `ContactManager.Changed` event fires
6. `ScheduleContactsRefresh()` is triggered in MainWindowViewModel
7. Contact is added to `Contacts` ObservableCollection

### Presence Update Flow
1. When handshake completes: `Network.HandshakeCompleted` event fires
2. `AppServices` calls `Contacts.SetPresence(uid, Online, ...)`
3. ContactManager updates the Contact object's properties:
   - `c.Presence = Online`
   - `c.LastPresenceUtc = DateTime.UtcNow`
4. `ContactManager.Changed` event fires
5. `ScheduleContactsRefresh()` is triggered again
6. **SHOULD** update the UI contact's properties

### Verification Update Flow
1. When mutual verification completes: `EvaluateMutualVerification()` is called
2. `ContactManager.SetPublicKeyVerified(uid, true)` updates the source contact
3. `ContactManager.SetIsVerified(uid, true)` updates and persists the flag
4. `ContactManager.Changed` event fires (on background thread)
5. `NotifyChanged()` is posted to UI thread
6. `ScheduleContactsRefresh()` is triggered
7. **SHOULD** update the UI contact's properties

## Refresh Logic Analysis

### For NEW Contacts (MainWindowViewModel.cs line 172):
```csharp
if (!current.TryGetValue(kv.Key, out var existing))
{
    Contacts.Add(kv.Value);  // ← Adds SOURCE object (shared reference!)
}
```

**Key Insight**: The source contact object from `ContactManager._contacts` is added **directly** to the ViewModel's `Contacts` collection. They share the **same object reference**.

**Implication**: When properties are updated on the source contact, they ARE updated on the UI contact (because it's the same object). The Contact class implements `INotifyPropertyChanged` correctly for most properties.

### For EXISTING Contacts (MainWindowViewModel.cs lines 174-201):
```csharp
else
{
    // Update presence and related transient properties
    if (existing.Presence != kv.Value.Presence)
        existing.Presence = kv.Value.Presence;
    if (existing.LastPresenceUtc != kv.Value.LastPresenceUtc)
        existing.LastPresenceUtc = kv.Value.LastPresenceUtc;
    // ... more property copies
}
```

Properties are **copied** from source to existing ViewModel contact. This is necessary for contacts that were loaded at startup and are separate objects.

## The Critical Bug Discovery

### Contact.cs Properties Analysis

Most properties implement INotifyPropertyChanged correctly:
```csharp
public PresenceStatus Presence { 
    get => _presence; 
    set { 
        if (_presence != value) { 
            _presence = value; 
            OnPropertyChanged(nameof(Presence)); // ✅ Notifies UI
        } 
    } 
}

public bool PublicKeyVerified { 
    get => _publicKeyVerified; 
    set { 
        if (_publicKeyVerified != value) { 
            _publicKeyVerified = value; 
            OnPropertyChanged(nameof(PublicKeyVerified)); // ✅ Notifies UI
        } 
    } 
}
```

**BUT** the timestamp properties do NOT:
```csharp
public System.DateTime? LastPresenceUtc { get; set; }  // ❌ No OnPropertyChanged!
public System.DateTime? PresenceExpiresUtc { get; set; }  // ❌ No OnPropertyChanged!
public PresenceSource PresenceSource { get; set; }  // ❌ No OnPropertyChanged!
```

These are simple auto-properties that don't notify the UI when changed!

## Hypothesis: Why Verification Doesn't Update

Even though `PublicKeyVerified` and `IsVerified` DO implement INotifyPropertyChanged correctly, they still don't update. This suggests:

1. **For newly added contacts**: The source object IS in the ObservableCollection, but Avalonia's ListBox might not be listening to individual item's PropertyChanged events
2. **Timing issue**: Verification completes before the contact is added to the UI, so the UI shows the old values
3. **Race condition**: Multiple threads updating properties while UI is rendering

## The Real Issue: ObservableCollection vs INotifyPropertyChanged

**Key Problem**: `ObservableCollection<Contact>` notifies when:
- Items are added/removed
- The collection itself changes

**But it does NOT automatically** notify when:
- Individual item properties change (even with INotifyPropertyChanged)

Avalonia's ListBox **should** subscribe to each item's PropertyChanged event, but there may be edge cases or timing issues where this doesn't work reliably.

## Fix Applied (Partial)

Added missing property synchronization in `ScheduleContactsRefresh()`:

```csharp
// Update presence and related transient properties  
if (existing.LastPresenceUtc != kv.Value.LastPresenceUtc)
    existing.LastPresenceUtc = kv.Value.LastPresenceUtc;
if (existing.PresenceExpiresUtc != kv.Value.PresenceExpiresUtc)
    existing.PresenceExpiresUtc = kv.Value.PresenceExpiresUtc;
if (existing.PresenceSource != kv.Value.PresenceSource)
    existing.PresenceSource = kv.Value.PresenceSource;
```

This ensures that **existing** contacts get their timestamp properties updated. But for **newly added** contacts, we're still relying on shared object references.

## Potential Root Causes

### Theory 1: Avalonia ListBox Not Subscribing to Item PropertyChanged
The ListBox might not be properly subscribing to individual Contact's PropertyChanged events when items are added to the ObservableCollection.

**Test**: Add logging in Contact.PropertyChanged to see if events are firing

### Theory 2: Race Condition
The contact is added with default values, then immediately (before UI renders) properties are updated. The ListBox might be caching the initial values.

**Test**: Add delay between contact creation and property updates

### Theory 3: Wrong Contact Instance
Perhaps `ScheduleContactsRefresh()` is being called but operating on stale data or the wrong dictionary.

**Test**: Check logs to see if refresh is being triggered and what values it sees

### Theory 4: Refresh Debounce Delay
The 180ms debounce might mean that property updates happen BEFORE the refresh runs, so the refresh sees old source values.

**Test**: Reduce debounce delay or remove it

## Next Steps

1. **Add PropertyChanged logging** to Contact class to verify events are firing
2. **Check debug logs** to see if refresh is running after verification
3. **Test with debug build** to see log messages
4. **Consider alternative approaches**:
   - Force a manual `OnPropertyChanged()` call in ViewModel after refresh
   - Use weak event subscriptions to Contact.PropertyChanged
   - Replace ObservableCollection with a custom collection that propagates child changes

## Files Modified

- `ViewModels/MainWindowViewModel.cs` (lines 174-201) - Added presence timestamp sync
- `ViewModels/MainWindowViewModel.cs` (lines 137, 143) - Added debug logging

## Testing Instructions

1. Launch two Zer0Talk instances (Alice and Bob)
2. Alice sends contact request to Bob
3. Bob accepts
4. Check Bob's contact list:
   - Does Alice appear? (Should: YES)
   - Is Alice shown as "Online"? (Should: YES, currently: NO)
   - Does Alice have a timestamp? (Should: YES, currently: NO)
5. Perform mutual verification
6. Check if green shield appears immediately (currently: NO)
7. Check logs for:
   ```
   [CONTACTS-REFRESH] Scheduled
   [CONTACTS-REFRESH] Executing refresh
   [VERIFY-REFRESH] Updated PublicKeyVerified=True
   [VERIFY-REFRESH] Updated IsVerified=True
   ```

## Critical Questions to Answer

1. Are PropertyChanged events firing on Contact objects? → Add logging
2. Is the refresh running after property updates? → Check logs
3. Is Avalonia listening to item PropertyChanged? → Verify framework behavior
4. Are we operating on the correct object instances? → Add identity logging

## Possible Solutions

### Solution A: Force Collection Notification
After updating properties, force the ObservableCollection to notify:
```csharp
var idx = Contacts.IndexOf(contact);
if (idx >= 0)
{
    Contacts.RemoveAt(idx);
    Contacts.Insert(idx, contact);
}
```

### Solution B: Replace with ReactiveUI
Use ReactiveUI's `ReactiveList` which better handles item property changes

### Solution C: Manual Property Notification
After refresh, explicitly notify all relevant properties:
```csharp
OnPropertyChanged(nameof(Contacts));
foreach (var contact in Contacts)
{
    // Force bindings to re-evaluate
}
```

### Solution D: Different Data Structure
Don't share object references - always copy to new ViewModel-specific Contact objects

## Conclusion

The refresh mechanism is **partially working** but has issues with property updates not propagating to the UI. The addition of timestamp property synchronization should help, but the core issue of INotifyPropertyChanged not triggering UI updates for shared objects needs further investigation.

The debug build with logging will help us determine exactly where the breakdown is occurring.
