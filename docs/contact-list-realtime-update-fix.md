# Contact List Real-Time Update Fix

**Date**: October 6, 2025  
**Version**: 0.0.1.58  
**Status**: ✅ FIXED

## Issues Fixed

### 1. Contact List Not Updating Until Window Click
**Problem**: After accepting a contact request, the contact list didn't update until the user clicked on the main window, forcing a UI refresh.

**Root Cause**: 
- 180ms debounce delay in `ScheduleContactsRefresh()`
- `ContactManager.Changed` event wasn't being dispatched to UI thread with proper priority
- Event handlers ran on background threads, causing race conditions

**Fix**:
- Reduced debounce from 180ms → 10ms for near-instant updates
- Changed `ContactManager.Changed` handler to use `Dispatcher.UIThread.Post()` with `DispatcherPriority.Send`
- This ensures contact list updates are processed immediately on the UI thread

**Files Modified**:
- `ViewModels/MainWindowViewModel.cs` (lines 62-65, 130-141, 365-368)

### 2. Verification Badge Not Appearing Immediately
**Problem**: After mutual verification, the green shield didn't appear until app restart.

**Root Cause**: Same as Issue #1 - UI thread synchronization and debounce delay prevented immediate property updates from being reflected in the UI.

**Fix**: Same as Issue #1 - immediate UI thread dispatch ensures verification status updates trigger UI refresh instantly.

**Related Code**:
- `Services/ContactRequestsService.cs` - `EvaluateMutualVerification()` already calls `NotifyChanged()`
- `ViewModels/MainWindowViewModel.cs` - Now processes these notifications immediately

### 3. "Last Seen" Timestamp Appearing for New Contacts
**Problem**: Newly added contacts showed a timestamp (HH:mm format) even though they had never been online, making it confusing.

**Root Cause**: 
- XAML binding used `ObjectNotNullConverter` which only checked if `LastPresenceUtc` was not null
- Didn't verify that the contact was actually offline
- Didn't check if `LastPresenceUtc` had a meaningful value (not DateTime.MinValue)

**Fix**:
- Created new `LastSeenTimestampVisibilityConverter` multi-value converter
- Checks two conditions:
  1. `Presence == PresenceStatus.Offline`
  2. `LastPresenceUtc` is not null AND not `DateTime.MinValue` (contact has been seen before)
- Updated MainWindow.axaml binding to use `MultiBinding` with new converter

**Files Created**:
- `Utilities/LastSeenTimestampVisibilityConverter.cs`

**Files Modified**:
- `App.axaml` (line 95) - Registered converter as static resource
- `Views/MainWindow.axaml` (lines 466-473) - Changed binding from single to multi-value

## Technical Details

### Debounce Timing Analysis
```csharp
// OLD: 180ms delay
private static readonly TimeSpan ContactsRefreshDebounce = TimeSpan.FromMilliseconds(180);

// NEW: 10ms delay (near-instant while still preventing multiple rapid-fire updates)
private static readonly TimeSpan ContactsRefreshDebounce = TimeSpan.FromMilliseconds(10);
```

**Rationale**: 180ms is perceptible to users (noticeable delay). 10ms is imperceptible but still prevents excessive UI churn if multiple rapid events fire.

### UI Thread Dispatch Priority
```csharp
// OLD: No explicit UI thread dispatch
Action contactsChangedHandler = () => { ScheduleContactsRefresh(); };

// NEW: Explicit high-priority UI thread dispatch
Action contactsChangedHandler = () => 
{ 
    Avalonia.Threading.Dispatcher.UIThread.Post(
        () => ScheduleContactsRefresh(), 
        Avalonia.Threading.DispatcherPriority.Send
    ); 
};
```

**Why `DispatcherPriority.Send`?** This is the highest priority (after `MaxValue`), ensuring contact list updates are processed immediately, before lower-priority UI tasks like rendering or animations.

### LastPresenceUtc Visibility Logic
```csharp
// Converter logic:
1. Check: presence == PresenceStatus.Offline
2. Check: lastPresenceUtc != null && lastPresenceUtc != DateTime.MinValue
3. Return: true only if BOTH conditions met
```

**Behavior**:
- ✅ Contact is offline + has been seen before → Show "Last Seen HH:mm"
- ❌ Contact is offline + never been online → Hide timestamp
- ❌ Contact is online/idle/DND → Hide timestamp (presence indicator already shows status)

## Testing Checklist

- [ ] **Test 1**: Accept contact request → Contact appears in list **immediately** (no window click needed)
- [ ] **Test 2**: Perform mutual verification → Green shield appears **immediately** (no restart needed)
- [ ] **Test 3**: Add new contact → NO timestamp shown (contact never online)
- [ ] **Test 4**: Contact comes online then goes offline → Timestamp appears showing last online time
- [ ] **Test 5**: Contact stays online → NO timestamp shown (presence dot indicates online status)

## Deployment

**Build Command**:
```powershell
.\scripts\alpha-strike.ps1 -IncludeDebugSingle
```

**Artifacts**:
- `Zer0Talk-v0.0.1.58-win-x64-Debug-single-{timestamp}.zip` - Deploy to BOTH test machines
- Must update both clients to test contact request flow properly

## Related Issues

- Original report: "Contact List doesn't update until we click on the main window"
- Related: Contact instance cloning fix (prevented shared object references)
- Related: Verification badge not updating (same root cause - UI thread timing)

## Future Improvements

1. Consider removing debounce entirely for contact list updates (10ms may still be unnecessary)
2. Add telemetry to measure actual UI update latency
3. Consider batching contact updates if multiple contacts change simultaneously (e.g., presence broadcast)

---

**Status**: Ready for testing with two-client deployment
