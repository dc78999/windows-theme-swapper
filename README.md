# ThemeSwapper

**ThemeSwapper** is a lightweight, headless Windows 11 system tray application written in C# (.NET 6) that allows you to instantly toggle between Windows Dark Mode and Light Mode globally without opening the Settings app.

## Features

- **Global Hotkey:** Press `Alt + Shift + T` from anywhere (even when the app isn't focused or is playing a game) to invert your theme.
- **Headless Execution:** Designed around a core `ApplicationContext`. The app consumes very little memory and starts silently in the background without ever flashing a main window.
- **Instantaneous UI Feel:** Registry edits and UI notifications execute immediately, while the heavy Win32 window repaints are offloaded to an asynchronous background task. 
- **Multi-Monitor Support:** Includes specific native `WindowsThemeElement` broadcasts to ensure that secondary monitor taskbars successfully repaint alongside your primary monitor.
- **No External Resources:** Generates its own system tray icon programmatically rendering a dynamic canvas—meaning no `.ico` files to keep track of.
- **Single-File Executable:** Can be compiled down to a single binary around ~150KB for incredible portability.

## Requirements

To run the provided minimal binary release, you will need:
- **OS:** Windows 10 or Windows 11
- **Runtime:** [.NET 6.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) (x64)

## Usage

1. Launch `ThemeSwapper.exe`. It will quietly place a 🌗 icon in your system tray.
2. Hit **`Alt + Shift + T`** anywhere to switch themes.
3. Alternatively, **Double-Click** the tray icon, or **Right-Click -> Toggle Theme**.
4. Right-Click and choose **Exit** to shut down the app.

## Build Instructions

If you wish to compile or modify the source code, you need at least the **.NET 6 SDK** installed. You can build it into a slim, single-file executable using the following command in the project directory:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The resulting file will be located in: `\bin\Release\net6.0-windows\win-x64\publish\ThemeSwapper.exe`

## Technical Details

**Registry Manipulation:** Updates the `AppsUseLightTheme` and `SystemUsesLightTheme` DWORD parameters in `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`.

**Win32 APIs Leveraged:**
- `RegisterHotKey` / `UnregisterHotKey`: Global hotkey interception.
- `SendMessageTimeout`: Broadcasting `WM_SETTINGCHANGE` across `HWND_BROADCAST`. Includes both `"ImmersiveColorSet"` and `"WindowsThemeElement"` signals to forcefully reload UI shell structures across multiple displays without lagging out the context thread. 

## License
MIT License
