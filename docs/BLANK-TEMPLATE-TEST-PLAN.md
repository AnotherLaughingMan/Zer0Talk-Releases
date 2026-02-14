# Blank Template Feature - Test Plan

**Date**: October 18, 2025  
**Feature**: New from Blank Template + Save As Functionality  
**Test Status**: In Progress

---

## Test Environment

- **Build**: Debug
- **Version**: Phase 3 + Blank Template Feature
- **Test Date**: October 18, 2025

---

## Test Cases

### Test 1: Load Blank Template

**Objective**: Verify blank template loads correctly into Theme Inspector

**Steps**:
1. Launch Zer0Talk
2. Navigate to Settings > Appearance > Theme Inspector
3. Click "📄 New from Blank" button

**Expected Results**:
- ✅ Toast notification: "📄 Blank template loaded - Start customizing!"
- ✅ Theme ID shows: `template-blank`
- ✅ Display Name shows: "Blank"
- ✅ Description shows: "Neutral grey template - perfect starting point for custom themes"
- ✅ Author shows: "Zer0Talk"
- ✅ Version shows: "1.0.0"
- ✅ Read-only badge visible: "🔒 Built-in Theme (Read-Only)"
- ✅ 21 colors loaded (all neutral greys)
- ✅ 0 gradients loaded
- ✅ Undo stack cleared (CanUndo = false)
- ✅ Batch mode exited (if was active)

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 2: Verify Read-Only Indicator

**Objective**: Confirm built-in theme shows read-only badge

**Steps**:
1. Load blank template (Test 1)
2. Observe metadata section

**Expected Results**:
- ✅ Orange badge visible with text: "🔒 Built-in Theme (Read-Only)"
- ✅ Badge positioned above metadata grid
- ✅ Badge color: Background=#40FFB84D, Border=#FFB84D

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 3: Edit Colors in Blank Template

**Objective**: Verify color editing works on blank template

**Steps**:
1. Load blank template
2. Click Edit (✏️) on `App.Background` color
3. Change color from `#1C1C1C` to `#FF0000` (red)
4. Click Save (💾)
5. Repeat for `App.Accent`: Change from `#6B7B8C` to `#00FF00` (green)

**Expected Results**:
- ✅ Color editor opens for each color
- ✅ Save button updates color value
- ✅ Undo stack increments (CanUndo = true)
- ✅ Visual swatches update to show new colors
- ✅ Toast: "✅ Color updated successfully"

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 4: Undo/Redo Color Changes

**Objective**: Verify undo/redo works after editing blank template

**Steps**:
1. After Test 3 (2 colors edited)
2. Click Undo (↶) button
3. Verify last color reverts to original
4. Click Undo (↶) again
5. Click Redo (↷) button

**Expected Results**:
- ✅ First undo: `App.Accent` reverts to `#6B7B8C`
- ✅ Second undo: `App.Background` reverts to `#1C1C1C`
- ✅ Redo: `App.Background` changes back to `#FF0000`
- ✅ Undo counter updates correctly
- ✅ Toast messages for each operation

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 5: Save As - Create Custom Theme

**Objective**: Save blank template edits as new custom theme

**Steps**:
1. Load blank template
2. Edit 2-3 colors (any colors)
3. Click "💾 Save As..." button
4. In file dialog:
   - Suggested filename: "Blank.zttheme"
   - Change to: "MyCustomTheme.zttheme"
   - Choose save location (e.g., Desktop)
5. Click Save

**Expected Results**:
- ✅ File dialog opens with title: "Save Theme As"
- ✅ Suggested filename: "Blank.zttheme"
- ✅ File type filter: "Zer0Talk Theme Files (*.zttheme)"
- ✅ File saved successfully
- ✅ Toast: "💾 Theme saved as: MyCustomTheme.zttheme"
- ✅ Log entry: "[Theme SaveAs] Successfully saved theme to: <path>"

**Actual Results**:
- [ ] Pass / [ ] Fail
- File Location: _______________
- Notes: _______________

---

### Test 6: Verify Saved Theme File

**Objective**: Confirm .zttheme file contains correct data

**Steps**:
1. After Test 5
2. Open saved file in text editor (Notepad, VS Code)
3. Verify JSON structure

**Expected Results**:
- ✅ Valid JSON format
- ✅ `id`: Starts with "custom-" + timestamp
- ✅ `displayName`: "Blank" (or edited name)
- ✅ `description`: Present
- ✅ `author`: Present (username or "Zer0Talk")
- ✅ `version`: Present
- ✅ `isReadOnly`: false
- ✅ `themeType`: 2 (Custom)
- ✅ `colorOverrides`: Contains all 21 colors with edits
- ✅ `gradients`: Empty object {}
- ✅ `tags`: ["custom", "user-created"]

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 7: Import Custom Theme

**Objective**: Verify created theme can be imported back

**Steps**:
1. After Test 6
2. Click "Import Theme" button
3. Select "MyCustomTheme.zttheme"
4. Confirm import

**Expected Results**:
- ✅ File picker opens
- ✅ Theme loads successfully
- ✅ Preview shows in Theme Inspector
- ✅ Confirmation dialog: "Do you want to register this theme?"
- ✅ After registration: Theme available in dropdown
- ✅ Colors match saved values
- ✅ Toast: Success message

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 8: Verify Built-In Theme Protection

**Objective**: Confirm built-in themes show read-only indicator

**Steps**:
1. Switch theme dropdown to "Dark"
2. Observe Theme Inspector
3. Switch to "Light"
4. Switch to "Sandy"
5. Switch to "Butter"

**Expected Results**:
For each built-in theme:
- ✅ Read-only badge visible: "🔒 Built-in Theme (Read-Only)"
- ✅ CurrentThemeIsReadOnly = true
- ✅ Theme ID shows "legacy-dark", "legacy-light", etc.
- ✅ All colors loaded from theme definition

**Actual Results**:
- Dark: [ ] Pass / [ ] Fail
- Light: [ ] Pass / [ ] Fail
- Sandy: [ ] Pass / [ ] Fail
- Butter: [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 9: Edit Metadata on Blank Template

**Objective**: Verify metadata editing displays but doesn't persist to built-in theme

**Steps**:
1. Load blank template
2. Click "✏️ Edit Metadata"
3. Change:
   - Display Name: "My Awesome Theme"
   - Description: "A cool custom theme"
   - Author: "Your Name"
   - Version: "2.0.0"
4. Click "💾 Save"
5. Observe updates

**Expected Results**:
- ✅ Metadata editor opens with current values
- ✅ All fields editable
- ✅ Save updates display in Theme Inspector
- ✅ Toast: "Metadata updated successfully"
- ✅ Changes visible in UI
- ⚠️ Note: Changes only affect current session, not the built-in template

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 10: Save As After Metadata Edit

**Objective**: Verify metadata edits are included in saved theme

**Steps**:
1. After Test 9 (metadata edited)
2. Edit 1-2 colors
3. Click "💾 Save As..."
4. Save as "CustomThemeWithMetadata.zttheme"
5. Open file in text editor

**Expected Results**:
- ✅ File saved successfully
- ✅ JSON contains updated metadata:
  - `displayName`: "My Awesome Theme"
  - `description`: "A cool custom theme"
  - `author`: "Your Name"
  - `version`: "2.0.0"
- ✅ Color edits included
- ✅ `id`: Unique custom ID

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 11: Multiple "New from Blank" Loads

**Objective**: Verify repeated blank template loads work correctly

**Steps**:
1. Load blank template
2. Edit 2-3 colors
3. Click "📄 New from Blank" again (without saving)
4. Observe state

**Expected Results**:
- ✅ Previous edits discarded
- ✅ Blank template reloaded with original values
- ✅ Undo stack cleared
- ✅ Toast: "📄 Blank template loaded - Start customizing!"
- ✅ No errors or crashes

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 12: Batch Edit Colors in Blank Template

**Objective**: Verify batch editing works with blank template

**Steps**:
1. Load blank template
2. Click "📦 Batch Mode"
3. Select 5 colors (checkboxes)
4. Edit one selected color to `#FF00FF`
5. Click "Paste" on other 4 selected colors
6. Exit batch mode

**Expected Results**:
- ✅ Batch mode activates
- ✅ Checkboxes visible
- ✅ Selection counter shows "Selected: 5"
- ✅ Copy/paste works across selections
- ✅ All 5 colors update to `#FF00FF`
- ✅ Undo stack tracks all changes

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 13: Gradient Editing (Should be Empty)

**Objective**: Verify blank template has no gradients

**Steps**:
1. Load blank template
2. Scroll to "Gradients" section in Theme Inspector

**Expected Results**:
- ✅ Gradients section visible
- ✅ No gradient entries displayed (empty list)
- ✅ No errors

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 14: Command State Management

**Objective**: Verify commands enable/disable correctly

**Steps**:
1. Load blank template
2. Observe button states:
   - Before any edits
   - After editing 1 color
   - During color editing
   - During metadata editing

**Expected Results**:
- ✅ NewFromBlankTemplateCommand: Enabled when not editing
- ✅ SaveAsCommand: Always enabled when not editing
- ✅ EditMetadataCommand: Enabled when not editing color/gradient
- ✅ ExportModifiedThemeCommand: Enabled when CanUndo = true
- ✅ All disabled during inline editing

**Actual Results**:
- [ ] Pass / [ ] Fail
- Notes: _______________

---

### Test 15: Logging Verification

**Objective**: Confirm comprehensive logging for audit trail

**Steps**:
1. Perform Test 1 (Load blank template)
2. Perform Test 5 (Save As)
3. Open log file: `<AppData>/Zer0Talk/Logs/theme.log`

**Expected Results**:
Log entries present for:
- ✅ "[Blank Template] Loading blank template into editor"
- ✅ "[Blank Template] Loaded 21 colors, 0 gradients"
- ✅ "[Theme SaveAs] Starting Save As operation"
- ✅ "[Theme SaveAs] Saving built-in theme 'Blank' as new custom theme"
- ✅ "[Theme SaveAs] Created theme definition with X colors, Y gradients"
- ✅ "[Theme SaveAs] Successfully saved theme to: <path>"

**Actual Results**:
- [ ] Pass / [ ] Fail
- Log File Path: _______________
- Notes: _______________

---

## Test Results Summary

**Total Tests**: 15  
**Passed**: ___ / 15  
**Failed**: ___ / 15  
**Pass Rate**: ___%

### Critical Issues

| Test # | Issue Description | Severity | Status |
|--------|-------------------|----------|--------|
| | | | |

### Non-Critical Issues

| Test # | Issue Description | Severity | Status |
|--------|-------------------|----------|--------|
| | | | |

---

## Performance Metrics

| Operation | Target | Actual | Status |
|-----------|--------|--------|--------|
| Load Blank Template | < 100ms | ___ ms | [ ] Pass / [ ] Fail |
| Edit Color | < 50ms | ___ ms | [ ] Pass / [ ] Fail |
| Save As | < 500ms | ___ ms | [ ] Pass / [ ] Fail |
| Import Theme | < 500ms | ___ ms | [ ] Pass / [ ] Fail |

---

## Sign-Off

**Tested By**: _______________  
**Date**: _______________  
**Overall Result**: [ ] PASS / [ ] FAIL  

**Production Ready**: [ ] YES / [ ] NO  

**Comments**:
_______________________________________________________________________________
_______________________________________________________________________________
_______________________________________________________________________________
