// ────────────────────────────────────────────────────────────────────────────
//  ThemeSwapper — A lightweight Windows 11 system-tray app
//  Toggles Dark ↔ Light theme via Alt+Shift+T
// ────────────────────────────────────────────────────────────────────────────

using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ThemeSwapper;

// ═══════════════════════════════════════════════════════════════════════════
//  Entry Point
// ═══════════════════════════════════════════════════════════════════════════
static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main()
    {
        // ── Single-instance guard ──────────────────────────────────────
        const string mutexName = "Local\\ThemeSwapper_B7F3A1D0";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running — exit silently
            MessageBox.Show(
                "Theme Swapper is already running in the system tray.",
                "Theme Swapper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ThemeSwapperContext());
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Application Context — runs headless with a system-tray icon
// ═══════════════════════════════════════════════════════════════════════════
sealed class ThemeSwapperContext : ApplicationContext
{
    // ── Win32 Interop ──────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    // ── Win32 Constants ────────────────────────────────────────────────
    private const int    HOTKEY_ID           = 0x7001;
    private const uint   MOD_ALT             = 0x0001;
    private const uint   MOD_SHIFT           = 0x0004;
    private const uint   MOD_NOREPEAT        = 0x4000;
    private const uint   VK_T                = 0x54;

    private const uint   WM_HOTKEY           = 0x0312;
    private const uint   WM_SETTINGCHANGE    = 0x001A;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint   SMTO_ABORTIFHUNG    = 0x0002;

    // ── Registry ───────────────────────────────────────────────────────
    private const string ThemeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // ── Fields ─────────────────────────────────────────────────────────
    private readonly NotifyIcon  _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;

    // ═══════════════════════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════════════════════
    public ThemeSwapperContext()
    {
        // ── Build context menu ─────────────────────────────────────────
        var menu = new ContextMenuStrip();

        var headerItem = new ToolStripLabel("🌗  Theme Swapper")
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        menu.Items.Add(headerItem);
        menu.Items.Add(new ToolStripSeparator());

        var toggleItem = new ToolStripMenuItem("Toggle Theme  (Alt+Shift+T)");
        toggleItem.Click += OnToggleThemeClick;
        menu.Items.Add(toggleItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += OnExitClick;
        menu.Items.Add(exitItem);

        // ── Create tray icon ───────────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            Icon             = CreateTrayIcon(),
            Text             = "Theme Swapper — Alt+Shift+T",
            ContextMenuStrip = menu,
            Visible          = true,
        };
        _trayIcon.DoubleClick += OnToggleThemeClick;

        // ── Register global hotkey ─────────────────────────────────────
        _hotkeyWindow = new HotkeyWindow(OnHotkeyPressed);

        bool registered = RegisterHotKey(
            _hotkeyWindow.Handle,
            HOTKEY_ID,
            MOD_ALT | MOD_SHIFT | MOD_NOREPEAT,
            VK_T);

        if (!registered)
        {
            int err = Marshal.GetLastWin32Error();
            MessageBox.Show(
                $"Could not register hotkey Alt+Shift+T.\n\n" +
                $"Another application may already be using this combo.\n" +
                $"Win32 error code: {err}",
                "Theme Swapper — Hotkey Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Theme Toggle Logic
    // ═══════════════════════════════════════════════════════════════════
    private async void ToggleTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ThemeRegistryPath, writable: true);
            if (key is null)
            {
                ShowBalloon("Error", "Could not open theme registry key.", ToolTipIcon.Error);
                return;
            }

            // Read current state (default to Dark = 0 if missing)
            int current  = (int)(key.GetValue("AppsUseLightTheme") ?? 0);
            int newState = current == 0 ? 1 : 0;

            key.SetValue("AppsUseLightTheme",     newState, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme",   newState, RegistryValueKind.DWord);

            // Show the notification immediately so the UI feels snappy
            string themeName = newState == 1 ? "☀️ Light" : "🌙 Dark";
            ShowBalloon("Theme Swapper", $"Switched to {themeName} mode.", ToolTipIcon.Info);

            // ── Force the shell & apps to refresh (offloaded to avoid lag) ──────
            await Task.Run(() => BroadcastSettingChange());
        }
        catch (Exception ex)
        {
            ShowBalloon("Error", $"Failed to toggle theme:\n{ex.Message}", ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Broadcasts WM_SETTINGCHANGE so that the Windows shell, taskbar,
    /// and running apps refresh their theme without needing a log-off.
    /// </summary>
    private static void BroadcastSettingChange()
    {
        // 1. Notify that the color set (Light/Dark mode) changed.
        SendMessageTimeout(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            UIntPtr.Zero,
            "ImmersiveColorSet",
            SMTO_ABORTIFHUNG,
            3000,   
            out _);

        // 2. Windows 11 secondary taskbars often need this second specific 
        // theme element broadcast to realize they need to repaint themselves.
        SendMessageTimeout(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            UIntPtr.Zero,
            "WindowsThemeElement",
            SMTO_ABORTIFHUNG,
            3000,   
            out _);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UI Helpers
    // ═══════════════════════════════════════════════════════════════════
    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText  = text;
        _trayIcon.BalloonTipIcon  = icon;
        _trayIcon.ShowBalloonTip(2000);
    }

    /// <summary>
    /// Creates a simple 16×16 tray icon programmatically so we don't
    /// need an external .ico resource to compile.
    /// A half-moon / sun glyph rendered onto a bitmap.
    /// </summary>
    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Dark half (left)
        using var darkBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        g.FillPie(darkBrush, 0, 0, 15, 15, 90, 180);

        // Light half (right)
        using var lightBrush = new SolidBrush(Color.FromArgb(255, 220, 80));
        g.FillPie(lightBrush, 0, 0, 15, 15, 270, 180);

        // Border
        using var pen = new Pen(Color.FromArgb(100, 100, 100), 1f);
        g.DrawEllipse(pen, 0, 0, 15, 15);

        return Icon.FromHandle(bmp.GetHicon());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════════════════
    private void OnToggleThemeClick(object? sender, EventArgs e) => ToggleTheme();

    private void OnHotkeyPressed() => ToggleTheme();

    private void OnExitClick(object? sender, EventArgs e)
    {
        // Cleanup
        UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID);
        _hotkeyWindow.Dispose();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Application.Exit();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Dispose
    // ═══════════════════════════════════════════════════════════════════
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID);
            _hotkeyWindow.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Hidden Window — receives WM_HOTKEY messages from the OS
// ═══════════════════════════════════════════════════════════════════════════
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Action _onHotkey;

    public HotkeyWindow(Action onHotkey)
    {
        _onHotkey = onHotkey;

        // Create an invisible message-only window
        CreateHandle(new CreateParams
        {
            Caption = "ThemeSwapperHotkeyReceiver",
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            _onHotkey();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
