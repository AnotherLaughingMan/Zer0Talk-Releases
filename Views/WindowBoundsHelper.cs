/*
    WindowBoundsHelper: shared utilities to restore/save window geometry safely.
    - Ensures restored windows are visible on current monitors (no off-screen positions).
    - Clamps size to the working area and enforces minimal dimensions.
    - Local-only; does not introduce any network or UI behavior changes.
*/
using Avalonia;
using Avalonia.Controls;

namespace P2PTalk.Views;

internal static class WindowBoundsHelper
{
    // Coerce the given width/height/position to fit a visible working area.
    public static void EnsureVisible(Window win, ref double width, ref double height, ref PixelPoint position)
    {
        // Minimal usable size to avoid tiny, inaccessible windows
        double minW = 400, minH = 300;
        if (width <= 0) width = minW;
        if (height <= 0) height = minH;

        var screens = win.Screens;
        // Fallback: if Screens/Primary is not available, use safe defaults
        PixelRect area;
        if (screens?.Primary is { } primary)
        {
            area = primary.WorkingArea;
        }
        else
        {
            area = new PixelRect(0, 0, 1280, 720);
        }
        if (area.Width <= 0 || area.Height <= 0)
        {
            // Hard defaults (HD-ish) when we can't query screens yet
            area = new PixelRect(0, 0, 1280, 720);
        }

        // If target position is not within any screen working area, move to primary working area origin
        bool onAny = false;
        if (screens != null)
        {
            foreach (var s in screens.All)
            {
                if (s.WorkingArea.Contains(position)) { onAny = true; break; }
            }
        }
        if (!onAny)
        {
            position = new PixelPoint(area.X + 50, area.Y + 50);
        }

        // Cap size to fit within the selected working area (primary area is a safe baseline)
        double maxW = area.Width * 0.95; // leave a small margin
        double maxH = area.Height * 0.95;
        if (width > maxW) width = maxW;
        if (height > maxH) height = maxH;

        if (width < minW) width = minW;
        if (height < minH) height = minH;

        // Ensure the window's bottom-right is inside the area
        int right = position.X + (int)width;
        int bottom = position.Y + (int)height;
        if (right > area.Right) position = new PixelPoint(area.Right - (int)width, position.Y);
        if (bottom > area.Bottom) position = new PixelPoint(position.X, area.Bottom - (int)height);
        if (position.X < area.X) position = new PixelPoint(area.X, position.Y);
        if (position.Y < area.Y) position = new PixelPoint(position.X, area.Y);
    }
}
