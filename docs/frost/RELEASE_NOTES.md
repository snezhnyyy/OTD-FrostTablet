<img src="https://raw.githubusercontent.com/snezhnyyy/OTD-FrostTablet/0.6.x/docs/frost/icon.png" width="64" align="right">

**FrostTablet** — a faster, cleaner OpenTabletDriver for osu! and everyday pen work. *by tgk: snezhnyyy · and claude ^^*

### Which file do I need?
| System | File |
| --- | --- |
| **Windows (most PCs)** | `FrostTablet-…-win-x64.zip` |
| Windows on ARM | `FrostTablet-…-win-arm64.zip` |
| Linux | `FrostTablet-…-linux-x64.tar.gz` / `linux-arm64` *(experimental)* |
| macOS | `FrostTablet-…-osx-arm64.tar.gz` (Apple Silicon) / `osx-x64` (Intel) *(experimental)* |

Unzip and run `FrostTablet` — one file, nothing else to install. Close other tablet drivers first. Full guide: [README](https://github.com/snezhnyyy/OTD-FrostTablet#quick-start) · [по-русски](https://github.com/snezhnyyy/OTD-FrostTablet#русский)

### Highlights
- Clicks no longer fire before the cursor moves (pen tip → Z/X bindings)
- Pixel-exact cursor, sub-pixel precision kept for raw input
- High-resolution timer and MMCSS input thread, no EcoQoS / E-core throttling
- Real scan codes for key bindings, live resolution changes, no stuck keys on unplug
- Live report rate and latency stats in the Console
- New interface: dark/light themes, 9 languages, "Disable pen buttons", Reset / Apply / Save bar, new tablet debugger
- Based on OpenTabletDriver 0.6.x (all tablets and 0.6 plugins work; your OTD settings carry over)

`SHA256SUMS.txt` lists checksums for every file. This fork includes AI-assisted code; see the README.
