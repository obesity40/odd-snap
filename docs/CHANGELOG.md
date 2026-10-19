# OddSnap v0.8.5

Personal fork of OddSnap. Only the changes made in this fork are listed here.

## Added

- Full interface localization for 40 languages. Every user-facing string now goes
  through the localization catalogue (2165 keys) instead of being hardcoded, and
  the 40 built-in language files carry a complete translation.
- A language-aware UI font stack. Segoe UI Variable Text carries no CJK glyphs, so
  WPF used to fall back per language and resolved Traditional Chinese to
  Microsoft JhengHei UI - noticeably lighter than the YaHei used for Simplified
  Chinese, which read as faint, jagged strokes at 11-13px. The stack is now listed
  explicitly and republished whenever the interface language changes.
- Smooth wheel scrolling on the settings panels, including trackpad inertia
  passthrough. The page stays still while a ComboBox dropdown is open.

## Fixed

- Window size reset on focus. Resizing an OddSnap window was undone as soon as it
  regained focus: UiScale.ApplyToWindow runs on every Activated event to refresh
  the palette and also re-applied the recorded design-time bounds. It now records
  the scale it last applied and returns early when that scale has not changed.
- Gaps at the rounded window corners. The title bar and close button now use the
  same radius as the window itself, and the close button corner went from 11px to
  7px so it no longer leaves a sliver of desktop visible.
- The OCR language dropdown had no scrollbar. It now shows a slim thumb that
  widens on hover and follows the theme.
- Some ComboBoxes in Settings > OCR rendered no text at all, in any language. An
  editable ComboBox hides its content presenter and needs a real
  PART_EditableTextBox in the template to take its place.
- Upgrade SSH.NET to 2026.0.0 to address GHSA-mggc-4xg6-vcxf and
  GHSA-q939-rpr3-3284.
- Translate text assigned from code. TextBlock.Text is a plain CLR property, so the
  value watcher could not see runtime writes; those literals now go through
  LocalizationService.Translate.
- Translate the settings page title. It stayed English while the rest of the window
  was translated.

## Known issues

- "Show cursor in captures and recordings" draws the pointer at 32x32 regardless of
  the Windows pointer-size and colour settings. GetCursorInfo/GetIconInfo only
  expose the primary monitor DPI copy; the fix needs Desktop Duplication's
  GetFramePointerShape, which is not implemented yet.
