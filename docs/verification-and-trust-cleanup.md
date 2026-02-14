# Verification UI Refresh Fix & Trust System Cleanup

**Date:** October 6, 2025  
**Version:** 0.0.1.58  
**Issue:** Verification badge not appearing until app restart + deprecated trust menu items

## Problems Fixed

### 1. Verification Badge Not Appearing Immediately ❌ → ✅
**Symptom:** After two users complete mutual verification (exchanging 0xC3 frames), the green verification shield badge does not appear on either user's contact list until they restart the app.

**Root Cause:** Same threading issue as the contact adding bug. When `EvaluateMutualVerification()` is called after receiving the 0xC3 frame from the network thread:
1. Network thread receives 0xC3 frame → calls `OnInboundVerifyIntent(uid)`
2. Network thread calls `EvaluateMutualVerification(uid)`
3. Network thread calls `SetPublicKeyVerified(uid, true)` → fires `Changed` event
4. Event handlers execute on network thread → potential race conditions with UI updates
5. The 180ms debounced refresh in `ScheduleContactsRefresh()` may get cancelled or not execute properly

**Solution:** Added explicit UI thread marshalling after verification completes in `EvaluateMutualVerification()`, using the same pattern as the contact adding fix:

```csharp
// Force UI refresh on main thread to ensure verification badge appears immediately
try
{
    Dispatcher.UIThread.Post(() =>
    {
        try
        {
            // Trigger changed event on UI thread to ensure UI updates
            AppServices.Contacts.NotifyChanged();
        }
        catch { }
    }, DispatcherPriority.Normal);
}
catch { }
```

### 2. Deprecated "Mark as Trusted" Menu Items Removed ✅
**Issue:** Old trust system menu items ("Mark as trusted" / "Remove trusted flag") are outdated and should be removed.

**Background:** 
- Original design had manual trust flags that users could toggle
- Now replaced by cryptographic verification system using public key verification
- Old menu items conflict with new verification-based trust model

**Solution:** Removed both menu items from contact context menu in `MainWindow.axaml`:
- ❌ Removed: "Mark as trusted" (was line 371-378)
- ❌ Removed: "Remove trusted flag" (was line 379-386)
- ✅ Added: Comments documenting deprecation reason

Trust is now managed exclusively through:
- **Cryptographic verification:** Users verify each other via 0xC3/0xC4 frame exchange
- **Persistent verification:** `IsVerified` flag persisted in contacts.p2e
- **Visual indication:** Green shield badge when `PublicKeyVerified || IsVerified`

## Code Changes

### ContactRequestsService.cs (lines ~450-480)
```csharp
private void EvaluateMutualVerification(string uid)
{
    try
    {
        if (_verifyInitiated.ContainsKey(uid) && _verifyReceived.ContainsKey(uid))
        {
            var peer = AppServices.Peers.Peers.Find(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
            if (peer?.PublicKey is { Length: > 0 })
            {
                AppServices.Peers.SetPeerVerification(uid, true);
                AppServices.Contacts.SetPublicKeyVerified(uid, true);
                try { AppServices.Contacts.SetIsVerified(uid, true, AppServices.Passphrase); } catch { }
                try { Utilities.Logger.Log($"Public key verified by mutual intent for {uid}"); } catch { }
                
                // NEW: Force UI refresh on main thread
                try
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            AppServices.Contacts.NotifyChanged();
                        }
                        catch { }
                    }, DispatcherPriority.Normal);
                }
                catch { }
                
                // User confirmation toast
                try
                {
                    var name = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? uid;
                    _ = _dialogs.ShowInfoAsync("Contact Verified", $"You verified {name}.");
                }
                catch { }
            }
        }
    }
    catch { }
}
```

### MainWindow.axaml (lines 365-390)
**Before:**
```xml
<Separator/>
<MenuItem Header="Mark as trusted"
          Command="{Binding $parent[v:MainWindow].DataContext.TrustContactCommand}"
          CommandParameter="{Binding UID}"
          IsVisible="{Binding IsTrusted, Converter={StaticResource InverseBoolConverter}}">
    <MenuItem.Icon>
        <TextBlock Classes="icon-mdl2" Text="&#xE72E;"/>
    </MenuItem.Icon>
</MenuItem>
<MenuItem Header="Remove trusted flag"
          Command="{Binding $parent[v:MainWindow].DataContext.UntrustContactCommand}"
          CommandParameter="{Binding UID}"
          IsVisible="{Binding IsTrusted}">
    <MenuItem.Icon>
        <TextBlock Classes="icon-mdl2" Text="&#xE711;"/>
    </MenuItem.Icon>
</MenuItem>
<MenuItem Header="Set simulated presence"
```

**After:**
```xml
<Separator/>
<!-- DEPRECATED: Mark as trusted (replaced by cryptographic verification) -->
<!-- MenuItem Header="Mark as trusted" removed per design decision -->
<!-- MenuItem Header="Remove trusted flag" removed per design decision -->
<MenuItem Header="Set simulated presence"
```

## Verification Protocol Flow

### 0xC3 Frame (Verification Intent)
1. **Alice** clicks "Verify Contact" → sends 0xC3 frame to Bob
2. **Bob** receives 0xC3 → `OnInboundVerifyIntent(alice_uid)` called
3. Bob's app stores `_verifyReceived[alice_uid] = true`
4. Bob clicks "Verify Contact" → sends 0xC3 frame to Alice
5. **Alice** receives 0xC3 → `OnInboundVerifyIntent(bob_uid)` called
6. Alice's app stores `_verifyReceived[bob_uid] = true`
7. **Both** apps call `EvaluateMutualVerification()`
8. If both `_verifyInitiated` and `_verifyReceived` are true → verification complete ✅
9. **NEW:** UI thread marshalling ensures badge appears immediately

### What Gets Updated
- `peer.PublicKeyVerified = true` (transient, in-memory)
- `contact.PublicKeyVerified = true` (transient, in-memory)
- `contact.IsVerified = true` (persisted to contacts.p2e)
- `Changed` event fired → UI refreshes → green shield appears

### Why UI Thread Marshalling Is Critical
- Network frames arrive on background threads
- `OnInboundVerifyIntent` executes on network thread
- `EvaluateMutualVerification` executes on network thread
- `SetPublicKeyVerified` fires `Changed` event on network thread
- Without marshalling: Race conditions, cancelled refreshes, UI not updating
- With marshalling: Event guaranteed to fire on UI thread → immediate update

## Testing Verification Flow

### Prerequisites
- Alice and Bob must be contacts
- Both must have public keys exchanged (happens during handshake)
- Both must be online and connected

### Test Steps
1. Alice opens Bob's profile → clicks "Verify Contact"
   - Alice sends 0xC3 frame to Bob
   - Alice's `_verifyInitiated[bob_uid] = true`
   
2. Bob receives notification "Alice wants to verify"
   - Bob clicks "Accept" or opens Alice's profile → clicks "Verify Contact"
   - Bob sends 0xC3 frame to Alice
   - Bob's `_verifyInitiated[alice_uid] = true`
   
3. Alice receives Bob's 0xC3 frame
   - `OnInboundVerifyIntent(bob_uid)` called
   - `_verifyReceived[bob_uid] = true`
   - `EvaluateMutualVerification(bob_uid)` called
   - Both conditions met → verification complete
   - **UI marshalling fires** → `NotifyChanged()` on UI thread
   - ✅ Green shield appears on Alice's contact list for Bob
   
4. Bob receives Alice's 0xC3 frame (if not already processed)
   - Same flow as step 3
   - ✅ Green shield appears on Bob's contact list for Alice

### Expected Results
- ✅ Green verification shield appears **immediately** on both sides
- ✅ No app restart required
- ✅ Verification persists across app restarts (stored in contacts.p2e)
- ✅ Toast notification: "Contact Verified - You verified [Name]"

### What Was Broken Before
- ❌ Shield only appeared after restarting app
- ❌ `Changed` event fired on network thread → UI refresh unreliable
- ❌ Debounced refresh might be cancelled by rapid events

## Related Files

### Modified
- `Services/ContactRequestsService.cs`: Added UI thread marshalling in `EvaluateMutualVerification`
- `Views/MainWindow.axaml`: Removed deprecated trust menu items

### Related (Not Modified)
- `Services/ContactManager.cs`: Already has `NotifyChanged()` method (added in contact fix)
- `Models/Contact.cs`: Has `PublicKeyVerified` and `IsVerified` properties
- `Controls/VerifiedBadge.axaml`: Green shield badge component
- `Services/NetworkService.cs`: Handles 0xC3 frame parsing and routing

## Design Notes

### Why Two Verification Flags?
- **`PublicKeyVerified`** (transient): Set during runtime when crypto verification succeeds
- **`IsVerified`** (persisted): Stored in contacts.p2e, persists across app restarts
- Both are checked for showing the green shield badge

### Why Remove Trust Menu Items?
- Trust should be based on cryptographic verification, not manual flags
- Manual trust flags can be spoofed or misused
- Verification provides cryptographic proof of identity
- Simplified UX: one clear verification system instead of two overlapping concepts

### Thread Safety
- All network frame handlers execute on background threads
- UI updates must be marshalled to `Dispatcher.UIThread`
- `NotifyChanged()` call must happen on UI thread for reliable refresh
- Same pattern used for contact adding, verification, and future similar operations

## Build Info
- **Compiler:** .NET 9.0
- **Framework:** Avalonia UI 11.3.5
- **Build Result:** ✅ Succeeded with 0 errors
- **Warnings:** 92 (none related to this fix)
