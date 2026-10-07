# FileConverter — Project Plan

## Goal

A native Windows desktop app for converting movie files in a personal collection.
Primary use: MKV (and other common containers) → chosen output format, one file,
many files, or an entire folder.

## Stack

| Layer | Choice | Why |
|-------|--------|-----|
| UI | WPF (.NET 8) | Native Windows, solid for long-running desktop tools, good progress/list UX |
| Conversion | ffmpeg / ffprobe (external) | Already proven on this collection (HEVC/EAC3 → H.264/MP3 AVI) |
| Architecture | MVVM-lite | Keep UI and conversion engine separable and testable |

## Features (v1)

1. **Add sources**
   - Add one or more files (multi-select)
   - Add all supported videos from a folder (non-recursive by default, with recursive option)
   - Drag-and-drop files/folders onto the window
2. **Queue**
   - List of jobs with status: Pending / Running / Done / Failed / Cancelled
   - Remove selected / clear finished / clear all
3. **Output settings**
   - Target format: AVI (default), MP4, MKV
   - Output folder: same as source, or a chosen folder
   - Overwrite policy: skip if exists (default) / overwrite
4. **Conversion presets**
   - **Collection AVI** (default): H.264 + MP3, `yuv420p`, CRF 20 — matches prior manual convert
   - **Fast MP4**: H.264 + AAC
   - Copy streams when container allows (optional later)
5. **Progress & messages**
   - Per-file progress (% / time / speed from ffmpeg)
   - Overall queue progress (N of M)
   - Scrollable activity log with timestamps
   - Cancel current job / cancel remaining queue
6. **ffmpeg discovery**
   - Use PATH, then common install locations
   - Settings path override if not found
   - Clear error if missing

## Non-goals (v1)

- Editing / trimming / subtitles burn-in
- Hardware encoder auto-tuning beyond optional future NVENC toggle
- Cloud sync / library database
- ~~Deleting source files after convert~~ ✅ (optional checkbox; success only)

## UX outline

```
┌─────────────────────────────────────────────────────────────┐
│  FileConverter                         [Settings]           │
├─────────────────────────────────────────────────────────────┤
│  [Add files] [Add folder] [Clear finished] [Start] [Cancel] │
│  Format: [AVI ▼]   Output: (•) Same folder  ( ) Choose…     │
├─────────────────────────────────────────────────────────────┤
│  Queue                                                      │
│  ☐ Movie1.mkv   Pending                                     │
│  ☐ Movie2.mkv   Running  ████████░░  62%  0.9x              │
│  ☐ Movie3.mkv   Done                                        │
├─────────────────────────────────────────────────────────────┤
│  Overall ████████████░░░░░░░░  1 / 3                        │
├─────────────────────────────────────────────────────────────┤
│  Log                                                        │
│  18:01 Added 3 files from D:\Movies                         │
│  18:02 Converting Movie2.mkv → Movie2.avi                   │
│  18:15 Done Movie2.avi (7.6 GB)                             │
└─────────────────────────────────────────────────────────────┘
```

Visual direction: dark cinema-friendly UI (deep charcoal, warm amber accent),
not generic purple dashboard. Clear hierarchy: actions → queue → progress → log.

## Project layout

```
FileConverter/
  PLAN.md                 # This plan
  README.md               # User-facing setup & usage
  docs/
    ARCHITECTURE.md       # Components & data flow
    CHANGELOG.md          # Notable changes
  .cursor/rules/
    keep-docs-updated.mdc # Agent must update docs with code changes
  src/
    FileConverter.App/    # WPF application
      Services/           # FfmpegLocator, ConversionService, QueueRunner
      Models/             # ConversionJob, AppSettings, Presets
      ViewModels/
      Views/
      Styles/
  .gitignore
```

## Implementation phases

1. **Scaffold** — solution, WPF project, docs, gitignore, docs rule ✅
2. **Engine** — ffprobe probe, ffmpeg convert with progress parse, cancel ✅
3. **Queue** — sequential batch runner, folder import, status updates ✅
4. **UI** — wire controls, progress bars, log, drag-drop ✅
5. **Polish** — settings for ffmpeg path, presets, error UX, README ✅ (v1)

### Possible next

- Persist settings between runs
- Optional NVENC / hardware encode toggle
- Faster x264 preset selector (`veryfast` / `ultrafast`)
- Parallel encodes (careful with disk/CPU)
- Disk-space warning before large AVI outputs

### Done recently

- Smart skip for files already using H.264 video
- Pre-scan + duration-weighted overall progress + ETA


## Risks

- **Very large 4K files**: encodes take hours; UI must stay responsive; progress must update
- **Unicode filenames**: Czech titles — always pass paths carefully to ffmpeg
- **AVI limitations**: no softsubs / DV / EAC3 — document clearly in UI/help
- **Disk space**: AVI can be larger than source; warn when free space looks tight (nice-to-have)

## Success criteria

- User can convert multiple MKVs or a whole folder to AVI without CLI
- Progress and log remain usable during multi-hour jobs
- Docs stay accurate after each change (enforced by Cursor rule)
