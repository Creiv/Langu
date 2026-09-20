# Langu

Offline on-screen translator for Windows. Hold a key, read text on screen (Latin and CJK), and click to translate.

## Download

Ready-to-run release: [Langu 1.1.0](https://github.com/Creiv/Langu/releases/tag/v1.1.0)

1. Download [Langu-1.1.0-win-x64.zip](https://github.com/Creiv/Langu/releases/download/v1.1.0/Langu-1.1.0-win-x64.zip)
2. Extract the zip and start `Langu.exe`
3. In Langu, click **Start**. For offline translation, click **Download models** once

You do not need to install .NET.

## How to use

1. Click **Start** so Langu is running (tray icon + overlay armed).
2. Hold the probe key (default **Alt**). Detected text is highlighted.
3. Move the mouse over a highlight to see that you can click it.
4. **Left-click** a highlight to translate that line. The translation covers the original text.
5. **Right-click** to translate every visible highlight.
6. **Left-click** a translation (while still holding the probe key) to dismiss it.
7. Release the probe key. Translations stay pinned until you clear them.

### Extra controls while holding the probe key

| Action | What it does |
| --- | --- |
| Mouse-wheel button drag | Select a temporary area. After that, right-click translates only text inside that area |
| Double-tap the probe key | Hide the overlay and cancel in-progress OCR / translations |
| Hover | Stronger highlight on the line under the cursor |

### Settings window

- **Key** — hold this key to scan. **Press a key…** captures any key you tap next
- **Translate into** — Italian or English
- **What to read** — whole **Monitor**, a **Window**, or a fixed **Region**
- **On-screen language** — auto-detect, or force Japanese / Chinese / Korean / others
- **Engine**
  - **Auto** — Windows OCR for layout; Rapid only fills gaps (CJK / empty areas)
  - **Windows** — fast Latin
  - **RapidOCR** — slower, better for Japanese / Chinese / Korean
- **Responsiveness** — how often the hold-key scan refreshes
- **Show boxes and translations** — turn the overlay on or off
- **Clear translations** — drop every pinned translation
- **Download models** — one-time download of OCR extras and the offline NLLB translator

### Global shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+Shift+P | Start / pause |
| Ctrl+Shift+W | Pick a window |
| Ctrl+Shift+R | Pick a screen region |
| Ctrl+Shift+L | Toggle Italian / English output |

Tray menu: start/pause, settings, exit.

Games work best in **borderless** or **windowed** mode. Exclusive fullscreen can block capture.

## Build from source

Requires Windows 10/11 64-bit and the .NET 8 SDK.

```bat
dotnet publish src\Langu.App\Langu.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\Langu
```

Run `dist\Langu\Langu.exe`.

## Tests

```bat
dotnet test src\Langu.Tests\Langu.Tests.csproj -c Debug
dotnet run --project src\Langu.App\Langu.App.csproj -- --smoke
```

Sample page: `tests/langu-test.html`

## License

Use and modify at your own risk. OCR and NLLB models remain under their own licenses.
