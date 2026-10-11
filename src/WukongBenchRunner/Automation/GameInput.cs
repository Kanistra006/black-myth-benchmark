using System.Runtime.InteropServices;
using static WukongBenchRunner.Automation.NativeMethods;

namespace WukongBenchRunner.Automation;

public enum UiAnchor
{
    /// <summary>Element is attached to the left edge (main menu list).</summary>
    Left,
    /// <summary>Element is centred (modal dialogs).</summary>
    Center,
}

/// <summary>
/// Sends input to the game window. Unreal Engine reads raw input, so keys are sent as
/// hardware scan codes through SendInput, and clicks are real cursor clicks.
/// </summary>
internal static class GameInput
{
    public const ushort VK_E = 0x45;

    /// <summary>The game lays its UI out on a 1280x720 canvas scaled by window height.</summary>
    private const double RefWidth = 1280, RefHeight = 720;

    public static bool BringToFront(nint hwnd)
    {
        if (hwnd == 0) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        // Windows only lets the foreground thread change the foreground window;
        // attaching to its input queue lifts that restriction for our call.
        var foreground = GetForegroundWindow();
        if (foreground == hwnd) return true;
        var fgThread = GetWindowThreadProcessId(foreground, out _);
        var ourThread = GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != ourThread && AttachThreadInput(ourThread, fgThread, true);
        try
        {
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, fgThread, false);
        }
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Moves the cursor to the top-left corner of the window so hovering cannot steal menu focus.</summary>
    public static void ParkCursor(nint hwnd)
    {
        var p = new POINT { X = 2, Y = 2 };
        ClientToScreen(hwnd, ref p);
        SetCursorPos(p.X, p.Y);
    }

    public static void PressKey(ushort virtualKey, int holdMs = 80)
    {
        var scan = (ushort)MapVirtualKeyW(virtualKey, 0);
        SendKey(scan, KEYEVENTF_SCANCODE);
        Thread.Sleep(holdMs);
        SendKey(scan, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP);
    }

    /// <summary>
    /// Clicks a UI element given in the game's 1280x720 reference layout.
    /// The vertical position is taken relative to the window centre, the horizontal one relative to
    /// the left edge or the centre depending on <paramref name="anchor"/>, so the click lands on the
    /// same element at any resolution or aspect ratio.
    /// </summary>
    public static void ClickUi(nint hwnd, double refX, double refY, UiAnchor anchor)
    {
        if (!GetClientRect(hwnd, out var rc)) return;
        var (x, y) = MapUiPoint(rc.Right - rc.Left, rc.Bottom - rc.Top, refX, refY, anchor);
        var p = new POINT { X = x, Y = y };
        ClientToScreen(hwnd, ref p);

        SetCursorPos(p.X, p.Y);
        Thread.Sleep(120);
        SendMouse(MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(80);
        SendMouse(MOUSEEVENTF_LEFTUP);
    }

    internal static (int X, int Y) MapUiPoint(int clientWidth, int clientHeight, double refX, double refY, UiAnchor anchor)
    {
        var scale = clientHeight / RefHeight;
        var x = anchor == UiAnchor.Left
            ? refX * scale
            : clientWidth / 2.0 + (refX - RefWidth / 2) * scale;
        var y = clientHeight / 2.0 + (refY - RefHeight / 2) * scale;
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private static void SendKey(ushort scan, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION { ki = new KEYBDINPUT { wScan = scan, dwFlags = flags } },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendMouse(uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flags } },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }
}
