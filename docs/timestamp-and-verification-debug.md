# Contact Card Timestamp & Verification Debug Session

**Date**: 2025-01-06  
**Version**: 0.0.1.58 (debug logging added)

## Issue 1: Timestamp Appearing on Contact Cards

**Problem**: Remote contact sees a timestamp (e.g., "15:40") on our contact card, but we don't see one on theirs after they accept our contact request.

**Root Cause Identified**:  
The timestamp is `LastPresenceUtc` displayed in "HH:mm" format (see `MainWindow.axaml` lines 467-468):

```xml
<TextBlock Text="{Binding LastPresenceUtc, StringFormat='{}{0:HH:mm}'}" FontSize="11" Opacity="0.5"
           IsVisible="{Binding LastPresenceUtc, Converter={StaticResource ObjectNotNullConverter}}"/>
```

**Why the asymmetry?**:
1. **Alice sends contact request to Bob**
2. **Bob accepts** → Bob's client auto-adds Alice to his contacts
3. At this point:
   - **Alice**: Has observed Bob online (has LastPresenceUtc) → Shows timestamp
   - **Bob**: Just added Alice, hasn't observed her presence yet (no LastPresenceUtc) → No timestamp

This is **working as designed** - the timestamp shows when you last saw that contact online.

**Options**:
1. **Keep it** - It's informative (shows last-seen time)
2. **Remove it** - If it's confusing or unnecessary
3. **Hide for contacts added < X minutes ago** - Avoid showing stale timestamps

**Decision**: Waiting for user preference

## Issue 2: Verification Badge Still Not Appearing Until Restart

**Problem**: After mutual verification completes, the green shield badge doesn't appear on the contact card until the app is restarted.

**Investigation Progress**:

### Code Added (Lines 180-186 in MainWindowViewModel.cs):
```csharp
// Update verification status (transient and persisted)
if (existing.PublicKeyVerified != kv.Value.PublicKeyVerified)
{
    existing.PublicKeyVerified = kv.Value.PublicKeyVerified;
    try { Utilities.Logger.Log($"[VERIFY-REFRESH] Updated PublicKeyVerified={kv.Value.PublicKeyVerified} for {existing.DisplayName}"); } catch { }
}
if (existing.IsVerified != kv.Value.IsVerified)
{
    existing.IsVerified = kv.Value.IsVerified;
    try { Utilities.Logger.Log($"[VERIFY-REFRESH] Updated IsVerified={kv.Value.IsVerified} for {existing.DisplayName}"); } catch { }
}
```

### Debugging Logs Added:

1. **ScheduleContactsRefresh()** (line 137):
   ```csharp
   try { Utilities.Logger.Log($"[CONTACTS-REFRESH] Scheduled"); } catch { }
   ```

2. **Before UI thread post** (line 143):
   ```csharp
   try { Utilities.Logger.Log($"[CONTACTS-REFRESH] Executing refresh"); } catch { }
   ```

3. **Property updates** (lines 183-186, 188-191):
   - Logs when `PublicKeyVerified` changes
   - Logs when `IsVerified` changes

### Expected Log Flow for Successful Verification:

```
[Alice verifies Bob's key]
Public key verified by mutual intent for <bob-uid>
[CONTACTS-REFRESH] Scheduled
[CONTACTS-REFRESH] Executing refresh
[VERIFY-REFRESH] Updated PublicKeyVerified=True for Bob
[VERIFY-REFRESH] Updated IsVerified=True for Bob
```

### Verification Flow Trace:

1. **EvaluateMutualVerification()** (ContactRequestsService.cs:461):
   ```csharp
   AppServices.Peers.SetPeerVerification(uid, true);
   AppServices.Contacts.SetPublicKeyVerified(uid, true);
   AppServices.Contacts.SetIsVerified(uid, true, AppServices.Passphrase);
   ```

2. **SetPublicKeyVerified()** (ContactManager.cs:223-234):
   ```csharp
   var c = _contacts.FirstOrDefault(...);
   c.PublicKeyVerified = verified;  // Sets on SOURCE contact
   Changed?.Invoke();                // Fires event
   ```

3. **SetIsVerified()** (ContactManager.cs:237-249):
   ```csharp
   var c = _contacts.FirstOrDefault(...);
   c.IsVerified = isVerified;        // Sets on SOURCE contact
   Save(passphrase);                 // Persists to disk
   Changed?.Invoke();                // Fires event
   ```

4. **NotifyChanged()** posted to UI thread (ContactRequestsService.cs:469):
   ```csharp
   Dispatcher.UIThread.Post(() => {
       AppServices.Contacts.NotifyChanged();
   }, DispatcherPriority.Normal);
   ```

5. **ScheduleContactsRefresh()** triggered by Changed event

6. **Property sync** in refresh (should copy from source to ViewModel contact)

### Potential Issues to Check:

1. **Is the refresh being triggered?**
   - Check logs for `[CONTACTS-REFRESH] Scheduled`

2. **Is the refresh executing?**
   - Check logs for `[CONTACTS-REFRESH] Executing refresh`

3. **Are the properties being updated?**
   - Check logs for `[VERIFY-REFRESH] Updated PublicKeyVerified=True`
   - Check logs for `[VERIFY-REFRESH] Updated IsVerified=True`

4. **Is INotifyPropertyChanged working?**
   - Contact class implements it correctly (verified)
   - UI should respond to property changes automatically

5. **Object reference issue?**
   - First time contact added: Uses SOURCE object (shared reference)
   - Subsequent refreshes: Copies properties to EXISTING ViewModel object
   - The code looks correct, but may have subtle timing/threading issue

### Next Steps:

1. **Test with debug build** and check logs
2. **Verify the logs show all expected messages**
3. **If logs show properties updated but UI doesn't reflect it**: UI binding issue
4. **If logs don't show property updates**: Refresh not running or object mismatch
5. **If logs don't show refresh triggered**: Event not firing or subscription issue

## Related Files Modified:

- `ViewModels/MainWindowViewModel.cs` (lines 137, 143, 180-191) - Added debug logging
- `Views/MainWindow.axaml` (lines 467-468) - Timestamp display identified

## Testing Instructions:

1. Launch two Zer0Talk debug instances (Alice and Bob)
2. Alice adds Bob (Bob accepts)
3. Verify timestamp behavior:
   - Check if Alice shows timestamp for Bob (should appear)
   - Check if Bob shows timestamp for Alice (may not appear initially)
4. Perform mutual verification:
   - Alice verifies Bob
   - Bob verifies Alice
5. Check logs for verification flow:
   ```
   [CONTACTS-REFRESH] Scheduled
   [CONTACTS-REFRESH] Executing refresh
   [VERIFY-REFRESH] Updated PublicKeyVerified=True for <contact>
   [VERIFY-REFRESH] Updated IsVerified=True for <contact>
   ```
6. Check if green shield appears immediately (without restart)

## Build Information:

- **Configuration**: Debug
- **Build Status**: ✅ Success
- **Logging**: Enabled
- **Log Location**: Check Zer0Talk logs directory
