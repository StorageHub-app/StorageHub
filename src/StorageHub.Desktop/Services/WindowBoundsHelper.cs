using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace StorageHub.Desktop.Services;

/// <summary>
/// Clamps secondary windows and dialogs so they never exceed the screen's working area
/// or push controls and buttons outside the visible viewport at high DPI scaling.
/// </summary>
internal static class WindowBoundsHelper
{
    private static readonly Thickness FrameAllowance = new(8, 32, 8, 8);

    public static void FitToScreen(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        void Clamp()
        {
            var screens = window.Screens;
            if (screens is null) return;

            var screen = screens.ScreenFromWindow(window)
                         ?? screens.ScreenFromPoint(window.Position)
                         ?? screens.Primary;
            if (screen is null) return;

            var area = screen.WorkingArea;
            if (area.Width <= 0 || area.Height <= 0) return;

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var workWidth = area.Width / scaling;
            var workHeight = area.Height / scaling;

            var maxAllowedWidth = Math.Max(320, workWidth - (FrameAllowance.Left + FrameAllowance.Right));
            var maxAllowedHeight = Math.Max(240, workHeight - (FrameAllowance.Top + FrameAllowance.Bottom));

            if (double.IsPositiveInfinity(window.MaxWidth) || window.MaxWidth > maxAllowedWidth)
            {
                window.MaxWidth = maxAllowedWidth;
            }
            if (double.IsPositiveInfinity(window.MaxHeight) || window.MaxHeight > maxAllowedHeight)
            {
                window.MaxHeight = maxAllowedHeight;
            }

            if (!double.IsNaN(window.MinWidth) && window.MinWidth > maxAllowedWidth)
            {
                window.MinWidth = maxAllowedWidth;
            }
            if (!double.IsNaN(window.MinHeight) && window.MinHeight > maxAllowedHeight)
            {
                window.MinHeight = maxAllowedHeight;
            }

            if (!double.IsNaN(window.Width) && window.Width > maxAllowedWidth)
            {
                window.Width = maxAllowedWidth;
            }
            if (!double.IsNaN(window.Height) && window.Height > maxAllowedHeight)
            {
                window.Height = maxAllowedHeight;
            }
        }

        Clamp();
        window.Opened += (_, _) => Clamp();
    }
}
