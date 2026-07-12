# Winnow 🌾

> **Swipe through your files. Keep what matters. Delete the rest.**

Winnow is a Windows desktop app that makes cleaning up your PC genuinely fun. Inspired by the swipe-left / swipe-right mechanic of dating apps, it turns the tedious task of file management into a satisfying, fast-paced experience.

---

## ✨ Features

| Feature | Details |
|---|---|
| **Swipe to decide** | Drag right to keep, drag left to delete — or use arrow keys |
| **Smart file stack** | Files sorted largest-first so you reclaim space fast |
| **Filters** | Filter by file type (images, video, docs, audio, archives) and date |
| **Subfolder scanning** | Optionally include files from nested folders with streaming so you start swiping immediately |
| **Session persistence** | Picks up right where you left off when you reopen a folder |
| **File preview** | In-app preview for images, video, audio, and PDFs; opens in default app for everything else |
| **Open in default app** | One-click / keyboard shortcut (`O`) to open any file in its native app |
| **Open file location** | Reveals the file in Windows Explorer with it selected (`L`) |
| **Review buckets** | Group files you're unsure about into named buckets for side-by-side comparison |
| **Duplicate detection** | Finds likely duplicates by content hash + metadata in the background |
| **Space statistics** | Live counter of space freed; filtered total size that updates as you swipe |
| **Sound effects** | Subtle audio feedback (muted by default — toggle with the 🔇 button) |
| **Micro-animations** | Smooth card transitions, swipe overlays, and rotation feedback |
| **Recycle Bin safe** | Deleted files go to the Recycle Bin, not permanently erased |

---

## 🖥 Screenshots

> *(Add screenshots here once you've captured them)*

---

## 🚀 Installation

### Option A — Download the installer *(recommended)*

1. Go to the [Releases](../../releases) page
2. Download `WinnowSetup-x.x.x.exe`
3. Run it — no admin required, no .NET runtime needed

### Option B — Portable EXE

Download `Winnow.exe` from Releases and run it directly. Nothing to install.

---

## 🎮 Keyboard Shortcuts

| Key | Action |
|---|---|
| `→` or `D` | Keep file |
| `←` or `A` | Delete file |
| `O` | Open in default app |
| `L` | Open file location in Explorer |
| `B` | Add to a new bucket |

---

## 🏗 Building from Source

**Requirements:** .NET 10 SDK, Windows 10/11

```powershell
git clone https://github.com/KayodeAjayi200/winnow-pc-file-cleaner.git
cd winnow-pc-file-cleaner
dotnet run
```

**Build installer:**
```powershell
# 1. Publish self-contained EXE
dotnet publish /p:PublishProfile=win-x64-singlefile

# 2. Compile installer (requires Inno Setup 6)
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer.iss
# → installer\WinnowSetup-1.0.0.exe
```

---

## 📦 Tech Stack

- **WPF** (.NET 10, C#) — UI framework
- **Inno Setup 6** — installer packaging
- Synthesised PCM audio (no external audio files)
- No third-party NuGet dependencies

---

## 🗺 Roadmap

- [ ] Dark / light theme toggle
- [ ] Cloud sync for session state
- [ ] Batch undo (restore multiple deleted files at once)
- [ ] Plugin system for custom swipe rules

---

## 📄 License

MIT © 2025
