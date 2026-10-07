# FileConverter

Windows desktop app for converting movie files in a personal collection.  
Convert one file, many files, or everything in a folder — with a queue, live progress, and an activity log.

Powered by **ffmpeg** / **ffprobe**.

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or SDK to build)
- [ffmpeg](https://ffmpeg.org/) on `PATH`, or browsable via **Locate ffmpeg…** in the app  
  Example install: `winget install Gyan.FFmpeg`

## Run (from source)

```powershell
cd c:\FakeDesktop\00_Projects\FileConverter
dotnet run --project src/FileConverter.App/FileConverter.App.csproj -c Release
```

Built binary:

`src/FileConverter.App/bin/Release/net8.0-windows/FileConverter.exe`

## Usage

1. Start the app and confirm the status line finds ffmpeg (or use **Locate ffmpeg…**).
2. **Add files** and/or **Add folder** (optional: **Include subfolders**).  
   You can also drag-and-drop files or folders onto the window.
3. Choose **output format**:
   - **AVI (H.264 + MP3)** — default, collection-friendly
   - **MP4 (H.264 + AAC)**
   - **MKV (H.264 + AAC)**
4. Choose output location: same folder as each source, or a custom folder.
5. Optionally enable **Overwrite existing outputs** (otherwise existing targets are skipped).
6. Keep **Skip if already H.264 (smart)** on (default) so files that already use H.264 are skipped quickly — only incompatible codecs (e.g. XviD) are re-encoded. Uncheck to force re-encode everything.
7. Optionally enable **Delete original after success** to remove the source file only after a successful convert (never on skip/fail/cancel).
8. Click **Start conversion**. The app **scans all files first** (applies skips), then encodes only what remains. Overall progress is based on media duration of encode jobs; **ETA** updates from ffmpeg speed.
9. Use **Cancel** to stop the current scan/encode and mark remaining jobs cancelled.

Jobs are **skipped** when:
- smart mode is on and the video codec is already **H.264**, or
- the source is already the selected container in the same output location, or
- the output file already exists and overwrite is off.

Originals are deleted only when conversion status is **Done** and the output path is a different existing file.

### Supported inputs

`.mkv`, `.mp4`, `.avi`, `.mov`, `.m4v`, `.wmv`, `.webm`, `.ts`, `.m2ts`, `.mpg`, `.mpeg`

### Notes for movie collections

- Only the **first video** and **first audio** stream are mapped.
- Soft subtitles and Dolby Vision / HDR metadata are not preserved in AVI output.
- Large 4K encodes can take a long time; the UI stays usable and shows speed/time.

## Project docs

| Doc | Purpose |
|-----|---------|
| [PLAN.md](PLAN.md) | Product plan and phases |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Components and data flow |
| [docs/CHANGELOG.md](docs/CHANGELOG.md) | Notable changes |
| `.cursor/rules/keep-docs-updated.mdc` | Agent rule: update docs with every change |

## Solution layout

```
src/FileConverter.slnx
src/FileConverter.App/          WPF UI + conversion engine
```
