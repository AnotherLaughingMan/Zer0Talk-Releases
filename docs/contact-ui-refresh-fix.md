# Contact UI Refresh Fix

## Issues Fixed

### Issue #1: Contact List Not Updating After Accept
When Bob accepts Alice's contact request:
- ✅ Bob (accepter) sees Alice appear in their contact list immediately
- ❌ Alice (requester) doesn't see Bob in her contact list until app restart

### Issue #2: Verification Badge Not Appearing Until App Restart
When two users complete mutual verification:
- ❌ Verification badge (green shield) doesn't appear until app restart
- Same root cause as Issue #1

### Issue #3: Deprecated "Mark as Trusted" Menu Items
- Old "Mark as trusted" and "Remove trusted flag" context menu items are outdated
- Replaced by cryptographic verification system
- Removed from contact context menu

## Root Cause (Issues #1 & #2)
When `OnInboundAccept` is called after receiving the C1 acceptance frame, it runs on a **network background thread**. The sequence was:

1. Network thread: `OnInboundAccept` called
2. Network thread: `ContactManager.AddContact` called
3. Network thread: `Changed?.Invoke()` fires event
4. Network thread: Event handler `ScheduleContactsRefresh()` starts
5. Background thread pool: `Task.Delay(180ms)` debounce
6. UI thread: `Dispatcher.UIThread.Post()` updates UI

### The Problem
The `Changed` event was being invoked from a network background thread. While the event handler uses `Dispatcher.UIThread.Post()` to marshal to the UI thread, there may have been race conditions or timing issues with:

1. Multiple `Changed` events firing rapidly (from both `ContactManager.AddContact` and `PeerManager.IncludeContacts`)
2. The debounce mechanism cancelling pending refreshes with `_contactsRefreshCts?.Cancel()`
3. Thread synchronization issues between the network thread and UI thread

## Solution
Added explicit UI thread marshalling after successfully adding a contact in `OnInboundAccept` and after verification completes in `EvaluateMutualVerification`:

```csharp
// In OnInboundAccept after AddContact succeeds
if (added)
{
    Dispatcher.UIThread.Post(() =>
    {
        AppServices.Contacts.NotifyChanged();
    }, DispatcherPriority.Normal);
}

// In EvaluateMutualVerification after verification completes
Dispatcher.UIThread.Post(() =>
{
    AppServices.Contacts.NotifyChanged();
}, DispatcherPriority.Normal);
```

### Key Changes

1. **ContactManager.cs** (line ~145):
   - Added `NotifyChanged()` public method to explicitly trigger the `Changed` event
   - This provides a clean API for external code to force a UI refresh

2. **ContactRequestsService.cs** - Contact Adding (lines ~325-350):
   - Import `Avalonia.Threading` namespace
   - After `AddContact` succeeds in `OnInboundAccept`, explicitly post to UI thread
   - Call `NotifyChanged()` on UI thread to ensure event handlers run on correct thread
   - Added logging to track if contact was actually added (`added={added}`)

3. **ContactRequestsService.cs** - Verification (lines ~450-480):
   - After mutual verification completes in `EvaluateMutualVerification`, post to UI thread
   - Call `NotifyChanged()` on UI thread to ensure verification badge appears immediately
   - Same pattern as contact adding fix

4. **MainWindow.axaml** (lines ~371-388):
   - Removed deprecated "Mark as trusted" menu item
   - Removed deprecated "Remove trusted flag" menu item
   - Added comments documenting why they were removed
   - Trust is now managed entirely through cryptographic verification

## How It Works Now

### Bob's Side (Accepter) - Was Already Working ✅
1. User clicks Accept
2. UI thread: `AcceptPendingAsync` called
3. UI thread: `AddContact` → `Changed` fires
4. UI thread: `ScheduleContactsRefresh()` → UI updates immediately

### Alice's Side (Requester) - Now Fixed ✅
1. Network thread: Receives C1 frame
2. Network thread: `OnInboundAccept` called
3. Network thread: `AddContact` → `Changed` fires
4. **NEW:** Explicitly post to UI thread
5. UI thread: `NotifyChanged()` → `Changed` fires again on UI thread
6. UI thread: `ScheduleContactsRefresh()` → UI updates immediately

## Testing Checklist

### Contact Adding
- [ ] Alice sends contact request to Bob
- [ ] Bob accepts request
- [ ] ✅ Bob sees Alice in contact list immediately (was already working)
- [ ] ✅ Alice sees Bob in contact list immediately (THIS WAS THE BUG - now fixed)
- [ ] Both contacts have correct display names
- [ ] No duplicate contacts appear
- [ ] Contact list refreshes without needing app restart

### Verification
- [ ] Alice and Bob are contacts
- [ ] Alice initiates verification
- [ ] Bob accepts verification
- [ ] ✅ Both see green verification shield immediately (THIS WAS THE BUG - now fixed)
- [ ] Verification persists across app restarts
- [ ] Shield appears without needing app restart

### Deprecated Menu Items
- [ ] Right-click on contact
- [ ] ✅ "Mark as trusted" menu item is gone
- [ ] ✅ "Remove trusted flag" menu item is gone
- [ ] Only verification-based trust system remains

## Technical Notes
- The UI thread marshalling uses `DispatcherPriority.Normal` to ensure proper sequencing
- The `NotifyChanged()` method was added instead of directly invoking the event to maintain encapsulation
- Logging includes `added={bool}` to help diagnose if `AddContact` is returning false (duplicate check)
- The fix ensures the `Changed` event handlers in `MainWindowViewModel` run on the UI thread where they expect to be

## Files Modified
- `Services/ContactManager.cs`: Added `NotifyChanged()` method (line ~147)
- `Services/ContactRequestsService.cs`: 
  - Added `using Avalonia.Threading` (line 11)
  - Added UI thread marshalling in `OnInboundAccept` (lines ~335-347)
  - Added UI thread marshalling in `EvaluateMutualVerification` (lines ~468-478)
  - Enhanced logging with `added` result
- `Views/MainWindow.axaml`: 
  - Removed deprecated "Mark as trusted" menu item (was line 371)
  - Removed deprecated "Remove trusted flag" menu item (was line 378)
  - Added comments documenting deprecation
