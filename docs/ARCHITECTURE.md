# Architecture

## Overview

```
MainWindow (WPF)
    └── MainViewModel
            ├── ConversionQueueService   (job list + sequential runner)
            │       └── ConversionService (ffmpeg process + progress)
            │               └── MediaProbeService (ffprobe duration)
            ├── FfmpegLocator
            └── MediaFileEnumerator
```

UI stays responsive: conversion runs on async tasks; progress is marshaled via `IProgress<T>` / property change notifications on `ConversionJob`.

## Projects

| Path | Role |
|------|------|
| `src/FileConverter.App` | WPF application project |
| `src/FileConverter.slnx` | Solution entry |
| `FileConverter.exe` (repo root) | Self-contained win-x64 single-file publish for GitHub download |

## Key types

### Models

- `ConversionJob` — one source file; status, %, detail, speed, errors
- `AppSettings` — format, output folder policy, overwrite, subfolders, skip-compatible-codec (smart), delete-original-after-success, ffmpeg dir
- `OutputFormat` — `Avi`, `Mp4`, `Mkv` (+ display/extension helpers)
- `JobStatus` — `Pending`, `Running`, `Done`, `Failed`, `Cancelled`, `Skipped`

### Services

- **FfmpegLocator** — finds `ffmpeg.exe` / `ffprobe.exe` via preferred folder, `PATH`, common install dirs, `where.exe`
- **MediaProbeService** — ffprobe duration + first real video codec; `HasCompatibleVideoCodec` is true for `h264`
- **ConversionService** — `PrepareAsync` (probe + skip rules), then encode with `-progress pipe:1`; parses `out_time_*` / `speed`; cancel kills process tree; deletes partial outputs on failure/cancel
- **ConversionQueueService** — `RunAsync` pre-scans every pending job (marks skips), then encodes only work items; optional delete of source after `JobStatus.Done`
- **MainViewModel** — overall progress weighted by encode-job duration (skips excluded after scan); ETA = remaining media time / ffmpeg speed factor
- **MediaFileEnumerator** — expands files/folders to supported video paths

### ViewModels / UI

- **MainViewModel** — commands, settings bindings, log text, overall progress
- **MainWindow** — dark cinema-styled layout; drag-and-drop hosts `AddDroppedPaths`
- **App.xaml** — dark control templates for ComboBox/TextBox/ProgressBar/GridView headers + system brush overrides so chrome stays dark
- Converters: `OutputFormatDisplayConverter`, `InverseBoolConverter`

## Conversion defaults (v1)

| Setting | Value |
|---------|--------|
| Video | `libx264`, CRF 20, preset `medium`, `yuv420p` |
| Audio (AVI) | MP3 192 kb/s |
| Audio (MP4/MKV) | AAC 192 kb/s |
| Streams | `-map 0:v:0 -map 0:a:0?` |

## Threading & cancel

1. `Start conversion` → `ConversionQueueService.RunAsync`
2. For each pending job → `ConversionService.ConvertAsync` with linked `CancellationToken`
3. Cancel → CTS cancel → `Process.Kill(entireProcessTree: true)` → partial output removed → remaining pending jobs marked `Cancelled`

## Documentation rule

`.cursor/rules/keep-docs-updated.mdc` requires README / ARCHITECTURE / CHANGELOG (and PLAN when scope changes) to be updated in the same turn as code changes.
