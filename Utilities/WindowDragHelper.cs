using System;
using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input;

namespace Zer0Talk.Utilities;

internal static class WindowDragHelper
{
    private const uint WmNclButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = new(0x2);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static bool TryBeginMoveDrag(Window window, PointerPressedEventArgs e)
    {
        try
        {
            if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            {
                return false;
            }

            if (OperatingSystem.IsWindows() && TryBeginMoveDragWin32(window))
            {
                return true;
            }

            window.BeginMoveDrag(e);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBeginMoveDragWin32(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero)
            {
                return false;
            }

            if (!string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ReleaseCapture();
            SendMessage(handle.Handle, WmNclButtonDown, HtCaption, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
