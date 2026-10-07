# Changelog

## 2026-10-04

- Initial WPF app (`FileConverter`) with queue, folder import, drag-and-drop, progress, and activity log
- Formats: AVI (H.264+MP3), MP4 / MKV (H.264+AAC)
- ffmpeg/ffprobe auto-discovery plus manual folder locate
- Project plan, architecture doc, README, and always-on Cursor rule to keep docs in sync
- Fix: progress bars bind `OneWay` so the window can open (read-only VM properties)
- Dark ComboBox/TextBox/ListView header templates + system brush overrides (no white chrome)
- Skip jobs already in the selected format (same path / same-folder match) so the source is never overwritten
- Option: **Delete original after success** (only after `Done`, never on skip/fail/cancel; refuses if output missing or same path)
- Smart mode checkbox **Skip if already H.264** (default on): probe codec and skip compatible files instead of re-encoding
- Pre-scan all queued files before encoding so skips are applied first; overall progress is duration-weighted for encode jobs only
- Show estimated time remaining (ETA) from remaining media duration and ffmpeg `speed`

