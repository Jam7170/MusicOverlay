using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MusicOverlay;

/// <summary>
/// Window placement. Positions are stored in physical screen pixels (DPI-independent across mixed-scaling
/// monitors) and always validated against the real monitor layout, so the overlay can never end up stranded
/// on a monitor that is no longer connected.
///
/// The position is anchored to whichever corner of its monitor the overlay is nearest: X/Y is that corner of the
/// window (e.g. bottom-right), so changing the size or DPI scaling keeps it glued there instead of growing
/// out from the top-left.
///
/// The saved position is the user's *intended* spot: it only changes when the user drags or resets.
/// If that monitor disappears the overlay is moved to the nearest screen without overwriting the saved spot,
/// so when the monitor comes back the overlay returns to it.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _displayDebounce;
    private bool _placed;

    private nint Hwnd => new WindowInteropHelper(this).Handle;

    private void InitPosition()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Migrate settings from the old DIP-based Left/Top.
        if (_settings.X == null && _settings.Left is double l && _settings.Top is double t)
        {
            Left = l;
            Top = t;
        }

        // Size/scale changed (Ctrl+wheel, Size menu, DPI): re-pin to the anchored corner once the window has resized.
        SizeChanged += (_, _) =>
        {
            if (_placed) Dispatcher.BeginInvoke(ApplySavedPosition, DispatcherPriority.Background);
        };
    }

    private void AttachPositionHandling(nint hwnd)
    {
        PlaceAtAnchor();
        // Validate once layout has sized the window.
        Dispatcher.BeginInvoke(InitialPlacement, DispatcherPriority.Background);

        _displayDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(750) };
        _displayDebounce.Tick += (_, _) =>
        {
            _displayDebounce.Stop();
            ApplySavedPosition();
        };

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private void InitialPlacement()
    {
        if (_settings.X == null && _settings.Left != null)
        {
            // Migrated from the legacy DIP position: validate and store as pixels.
            _settings.Left = _settings.Top = null;
            EnsureOnScreen();
            SavePosition();
        }
        else if (_settings.X == null)
        {
            ResetPosition();
        }
        else
        {
            ApplySavedPosition();
        }
        _placed = true;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Monitor added/removed/resolution changed, or the taskbar/work area changed.
        if (msg == WM_DISPLAYCHANGE || (msg == WM_SETTINGCHANGE && wParam == SPI_SETWORKAREA))
        {
            _displayDebounce?.Stop();
            _displayDebounce?.Start();
        }
        return 0;
    }

    /// <summary>Go back to where the user put it (if that screen exists), otherwise the nearest screen.</summary>
    private void ApplySavedPosition()
    {
        PlaceAtAnchor();
        EnsureOnScreen();
    }

    /// <summary>Move so the anchored corner of the window sits on the saved point.</summary>
    private void PlaceAtAnchor()
    {
        if (_settings.X is not int x || _settings.Y is not int y) return;
        if (!TryGetWindowRect(out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int left = _settings.AnchorRight ? x - w : x;
        int top = _settings.AnchorBottom ? y - h : y;
        if (left != r.Left || top != r.Top) MoveTo(left, top);
    }

    /// <summary>
    /// If less than half the overlay is on a monitor, slide it fully into the nearest monitor's work area.
    /// (Straddling two monitors is fine, since one of them holds at least half.)
    /// </summary>
    private void EnsureOnScreen()
    {
        if (!TryGetWindowRect(out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;

        if (!TryGetMonitor(MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST), out var mi)) return;

        var m = mi.rcMonitor;
        long visW = Math.Max(0, Math.Min(r.Right, m.Right) - Math.Max(r.Left, m.Left));
        long visH = Math.Max(0, Math.Min(r.Bottom, m.Bottom) - Math.Max(r.Top, m.Top));
        if (visW * visH * 2 >= (long)w * h) return;

        var wa = mi.rcWork;
        MoveTo(Math.Clamp(r.Left, wa.Left, Math.Max(wa.Left, wa.Right - w)),
               Math.Clamp(r.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - h)));

        // Moving onto a monitor with different scaling resizes the window; re-check once it settles.
        Dispatcher.BeginInvoke(EnsureOnScreenFinal, DispatcherPriority.Background);
    }

    private void EnsureOnScreenFinal()
    {
        if (!TryGetWindowRect(out var r)) return;
        if (!TryGetMonitor(MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST), out var mi)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var wa = mi.rcWork;
        int x = Math.Clamp(r.Left, wa.Left, Math.Max(wa.Left, wa.Right - w));
        int y = Math.Clamp(r.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
        if (x != r.Left || y != r.Top) MoveTo(x, y);
    }

    /// <summary>Bottom-right of the primary monitor.</summary>
    public void ResetPosition()
    {
        _userHidden = false;
        UpdateVisibility();
        if (!TryGetWindowRect(out var r)) return;
        if (!TryGetMonitor(MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY), out var mi)) return;

        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        MoveTo(mi.rcWork.Right - w - 8, mi.rcWork.Bottom - h - 8);
        SavePosition();
    }

    /// <summary>Store the window corner nearest its monitor's corner (in the work area's quadrant it sits in).</summary>
    private void SavePosition()
    {
        if (!TryGetWindowRect(out var r)) return;
        bool right = false, bottom = false;
        if (TryGetMonitor(MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST), out var mi))
        {
            var wa = mi.rcWork;
            right = r.Left + r.Right > wa.Left + wa.Right;   // centre is in the right half
            bottom = r.Top + r.Bottom > wa.Top + wa.Bottom;  // centre is in the bottom half
        }
        _settings.AnchorRight = right;
        _settings.AnchorBottom = bottom;
        _settings.X = right ? r.Right : r.Left;
        _settings.Y = bottom ? r.Bottom : r.Top;
        _settings.Save();
    }

    private void MoveTo(int x, int y) =>
        SetWindowPos(Hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    private bool TryGetWindowRect(out RECT r)
    {
        r = default;
        var h = Hwnd;
        return h != 0 && GetWindowRect(h, out r);
    }

    private static bool TryGetMonitor(nint monitor, out MONITORINFO mi)
    {
        mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return monitor != 0 && GetMonitorInfo(monitor, ref mi);
    }

    // ---------------- interop ----------------

    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const nint SPI_SETWORKAREA = 0x002F;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
