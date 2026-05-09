using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Openza.Flow.Services;

public static partial class WindowInterop
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    public static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    public static void Hide(Window window)
    {
        _ = ShowWindow(WindowNative.GetWindowHandle(window), SwHide);
    }

    public static void Show(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        _ = ShowWindow(hwnd, SwShow);
        _ = SetForegroundWindow(hwnd);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
