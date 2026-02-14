# CRITICAL FIX: Shared Contact Instances

**Date**: October 6, 2025  
**Issue**: Contact list not updating, verification badges not appearing, timestamps not showing  
**Status**: ROOT CAUSE IDENTIFIED - ARCHITECTURAL FLAW

---

## PROBLEM SUMMARY

All three issues stem from the same root cause:

1. **Contact list doesn't update** after accepting request
2. **Verification badges don't appear** after mutual verification  
3. **"Last Seen" timestamps never show** for offline contacts

---

## ROOT CAUSE

### The Architectural Flaw

**MainWindowViewModel** creates **CLONED instances** of Contact objects when adding new contacts (lines 170-196). This was done to "fix" property change detection, but it created a much worse problem:

```csharp
// MainWindowViewModel.cs, lines 170-196
var newContact = new Contact
{
    UID = kv.Value.UID,
    DisplayName = kv.Value.DisplayName,
    Bio = kv.Value.Bio,
    // ... copies all properties
    PublicKeyVerified = kv.Value.PublicKeyVerified,
    IsVerified = kv.Value.IsVerified,
    Presence = kv.Value.Presence,
    LastPresenceUtc = kv.Value.LastPresenceUtc
};
Contacts.Add(newContact);  // ViewModel now has a DIFFERENT object
```

**Why This Breaks Everything**:

1. **ContactManager** stores Contact objects in `_contacts` list
2. When verification happens, `ContactManager.SetPublicKeyVerified(uid, true)` updates the **ContactManager's** Contact object:
   ```csharp
   var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, ...));
   c.PublicKeyVerified = true;  // Updates ContactManager's copy
   Changed?.Invoke();  // Triggers refresh
   ```

3. **MainWindowViewModel** has a **DIFFERENT** Contact object (the clone) in its `Contacts` collection
4. **Refresh mechanism** compares ContactManager's object with ViewModel's object:
   ```csharp
   if (existing.PublicKeyVerified != kv.Value.PublicKeyVerified)  // FALSE!
   {
       existing.PublicKeyVerified = kv.Value.PublicKeyVerified;  // Never executes
   }
   ```
5. **UI never updates** because the ViewModel's Contact object never changes

---

## WHY CLONING WAS WRONG

The original problem was that when ContactManager and ViewModel **shared the same object reference**, property changes in ContactManager weren't detected during refresh. 

**The cloning "solution" made it WORSE**:
- ✅ Solved: Property change detection during refresh  
- ❌ **BROKE**: Real-time updates (verification, timestamps, presence changes)
- ❌ **BROKE**: Any property updated by ContactManager after the contact is added

---

## THE CORRECT SOLUTION

**DO NOT CLONE**. Instead, **SHARE the same Contact objects** and rely on `INotifyPropertyChanged`:

### Key Insight

The `Contact` class **ALREADY** implements `INotifyPropertyChanged` for:
- `PublicKeyVerified`
- `IsVerified`
- `Presence`
- `DisplayName`
- etc.

When ContactManager calls:
```csharp
c.PublicKeyVerified = true;  // Triggers PropertyChanged event
```

**If the ViewModel has the SAME object**, Avalonia's binding system will automatically update the UI!

### The Fix

**MainWindowViewModel.cs** should:

1. **Add new contacts** using the **SAME** object reference from ContactManager:
   ```csharp
   // CORRECT: Share the same object
   Contacts.Add(kv.Value);
   ```

2. **Update existing contacts** by keeping the same reference:
   ```csharp
   // DON'T update properties manually
   // The object already changed via INotifyPropertyChanged
   ```

3. **Remove the refresh debounce** entirely - it's unnecessary when using shared objects

---

## IMPLEMENTATION PLAN

### Step 1: Remove Instance Cloning

**File**: `ViewModels/MainWindowViewModel.cs`  
**Lines**: 170-196

**BEFORE**:
```csharp
var newContact = new Contact
{
    UID = kv.Value.UID,
    DisplayName = kv.Value.DisplayName,
    // ... property copying
};
Contacts.Add(newContact);
```

**AFTER**:
```csharp
Contacts.Add(kv.Value);  // Use the same object!
```

### Step 2: Simplify Property Sync

**Lines**: 198-217

**BEFORE**:
```csharp
if (existing.Presence != kv.Value.Presence)
    existing.Presence = kv.Value.Presence;
if (existing.LastPresenceUtc != kv.Value.LastPresenceUtc)
    existing.LastPresenceUtc = kv.Value.LastPresenceUtc;
// ... etc
```

**AFTER**:
```csharp
// NO PROPERTY COPYING NEEDED!
// If existing == kv.Value (same reference), properties are already in sync
// INotifyPropertyChanged handles UI updates automatically
```

### Step 3: Verify Contact.cs Properties

**File**: `Models/Contact.cs`

Ensure all properties that need UI updates implement `INotifyPropertyChanged`:

- ✅ `PublicKeyVerified` - **HAS** INotifyPropertyChanged  
- ✅ `IsVerified` - **HAS** INotifyPropertyChanged  
- ✅ `Presence` - **HAS** INotifyPropertyChanged  
- ❌ `LastPresenceUtc` - **MISSING** INotifyPropertyChanged (auto-property)
- ❌ `PresenceExpiresUtc` - **MISSING** INotifyPropertyChanged (auto-property)
- ❌ `PresenceSource` - **MISSING** INotifyPropertyChanged (auto-property)

**FIX**: Add INotifyPropertyChanged to timestamp properties:

```csharp
[JsonIgnore]
private DateTime? _lastPresenceUtc;
public DateTime? LastPresenceUtc
{
    get => _lastPresenceUtc;
    set
    {
        if (_lastPresenceUtc != value)
        {
            _lastPresenceUtc = value;
            OnPropertyChanged(nameof(LastPresenceUtc));
        }
    }
}
```

### Step 4: Remove/Simplify Refresh Mechanism

**Lines**: 130-220

The refresh mechanism becomes much simpler:

```csharp
private void ScheduleContactsRefresh()
{
    // Just reload the list from ContactManager
    // The objects themselves are already up-to-date via INotifyPropertyChanged
    
    var prevUid = SelectedContact?.UID;
    var contactsDict = AppServices.Contacts.Contacts.ToDictionary(c => c.UID, StringComparer.OrdinalIgnoreCase);
    
    // Add new contacts
    foreach (var kv in contactsDict)
    {
        if (!Contacts.Any(c => string.Equals(c.UID, kv.Key, StringComparison.OrdinalIgnoreCase)))
        {
            Contacts.Add(kv.Value);  // Same reference!
        }
    }
    
    // Remove deleted contacts
    var toRemove = Contacts.Where(c => !contactsDict.ContainsKey(c.UID)).ToList();
    foreach (var c in toRemove)
        Contacts.Remove(c);
    
    // Restore selection
    if (!string.IsNullOrWhiteSpace(prevUid))
    {
        SelectedContact = Contacts.FirstOrDefault(x => string.Equals(x.UID, prevUid, StringComparison.OrdinalIgnoreCase));
    }
}
```

---

## TESTING

After implementing this fix:

### ✅ Test 1: Contact Request
1. Alice sends contact request to Bob
2. Bob accepts
3. **VERIFY**: Bob's list updates **IMMEDIATELY** (no click needed)
4. **VERIFY**: Alice's list updates **IMMEDIATELY** (no click needed)

### ✅ Test 2: Verification Badge
1. Perform mutual verification
2. **VERIFY**: Green shield appears **IMMEDIATELY** on both sides (no restart needed)

### ✅ Test 3: Timestamp Visibility
1. Add new contact (never seen)
2. **VERIFY**: NO timestamp appears
3. Contact comes online
4. **VERIFY**: NO timestamp while online
5. Contact goes offline
6. **VERIFY**: Timestamp appears showing "HH:mm"

### ✅ Test 4: Timestamp Persistence
1. Contact offline with timestamp showing
2. Restart app
3. **VERIFY**: Timestamp still shows correctly

---

## WHY THIS WORKS

1. **ContactManager** is the **single source of truth** for Contact data
2. **ViewModel** uses the **same object references** from ContactManager  
3. When ContactManager updates properties:
   - `c.PublicKeyVerified = true` triggers `PropertyChanged` event
   - Avalonia bindings detect the change
   - UI updates **AUTOMATICALLY**
4. **No refresh delay**, **no manual property copying**, **no cloning**

---

## FILES TO MODIFY

1. **ViewModels/MainWindowViewModel.cs**:
   - Remove instance cloning (lines 170-196)
   - Simplify refresh to only add/remove contacts, not copy properties
   - Remove debounce timer (optional - can keep minimal debounce for coalescing)

2. **Models/Contact.cs**:
   - Add INotifyPropertyChanged to `LastPresenceUtc`
   - Add INotifyPropertyChanged to `PresenceExpiresUtc`
   - Add INotifyPropertyChanged to `PresenceSource`

3. **Utilities/LastSeenTimestampVisibilityConverter.cs**:
   - No changes needed (already correct)

4. **Services/ContactManager.cs**:
   - No changes needed (already correct)

5. **Services/ContactRequestsService.cs**:
   - No changes needed (already correct)

---

## CONCLUSION

The root cause was **object reference mismatch** caused by cloning. The correct MVVM pattern is to:

1. Have a **single source of truth** (ContactManager)
2. **Share object references** between service and ViewModel
3. Use **INotifyPropertyChanged** for all mutable properties
4. Let **Avalonia's binding system** handle UI updates automatically

This is the standard MVVM pattern. The cloning workaround broke it.
