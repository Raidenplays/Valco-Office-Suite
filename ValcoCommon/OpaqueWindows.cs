// Shared by every Valco Office app. Edit here, not in the apps.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Valco.UI
{
    /// <summary>
    /// Valco windows draw their own Vista frame, so they must stay fully opaque. Transparency mods (Windhawk etc.) make
    /// every window layered with a window-wide alpha; this undoes that for Valco windows and keeps WPF's background
    /// opaque, re-applying when the window is activated or resized. The backdrop/blur itself is left alone: removing it
    /// while the mod keeps the window see-through only makes things worse.
    /// </summary>
    public static class OpaqueWindows
    {
        static bool _installed;

        /// <summary>Call once at startup (App.OnStartup).</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            PinSystemColors();
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                if (s is not Window w || e.OriginalSource != w) return;
                Apply(w);
                w.Activated -= OnActivated; w.Activated += OnActivated;
                w.SizeChanged -= OnSized; w.SizeChanged += OnSized;
                w.StateChanged -= OnActivated; w.StateChanged += OnActivated;
            }));
        }

        /// <summary>
        /// Translucent-window mods hook GetSysColor so Win32 windows paint black (which they then turn into blur).
        /// WPF's own controls read the same system colours, so text boxes and combo boxes came out black.
        /// Valco apps pin the standard Windows colours instead (left alone in High Contrast, which users rely on).
        /// </summary>
        static void PinSystemColors()
        {
            if (Application.Current == null || SystemParameters.HighContrast) return;
            var r = Application.Current.Resources;
            void Set(object key, string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); r[key] = b; }
            void SetC(object key, string hex) => r[key] = (Color)ColorConverter.ConvertFromString(hex);
            Set(SystemColors.WindowBrushKey, "#FFFFFFFF"); SetC(SystemColors.WindowColorKey, "#FFFFFFFF");
            Set(SystemColors.WindowTextBrushKey, "#FF000000"); SetC(SystemColors.WindowTextColorKey, "#FF000000");
            Set(SystemColors.ControlBrushKey, "#FFF0F0F0"); SetC(SystemColors.ControlColorKey, "#FFF0F0F0");
            Set(SystemColors.ControlTextBrushKey, "#FF000000"); SetC(SystemColors.ControlTextColorKey, "#FF000000");
            Set(SystemColors.ControlLightBrushKey, "#FFE3E3E3"); Set(SystemColors.ControlLightLightBrushKey, "#FFFFFFFF");
            Set(SystemColors.ControlDarkBrushKey, "#FFA0A0A0"); Set(SystemColors.ControlDarkDarkBrushKey, "#FF696969");
            Set(SystemColors.MenuBrushKey, "#FFF0F0F0"); Set(SystemColors.MenuTextBrushKey, "#FF000000");
            Set(SystemColors.MenuBarBrushKey, "#FFF0F0F0");
            Set(SystemColors.InfoBrushKey, "#FFFFFFE1"); Set(SystemColors.InfoTextBrushKey, "#FF000000");
            Set(SystemColors.HighlightBrushKey, "#FF3399FF"); SetC(SystemColors.HighlightColorKey, "#FF3399FF");
            Set(SystemColors.HighlightTextBrushKey, "#FFFFFFFF"); SetC(SystemColors.HighlightTextColorKey, "#FFFFFFFF");
            Set(SystemColors.InactiveSelectionHighlightBrushKey, "#FFF0F0F0"); Set(SystemColors.InactiveSelectionHighlightTextBrushKey, "#FF000000");
            Set(SystemColors.GrayTextBrushKey, "#FF6D6D6D"); SetC(SystemColors.GrayTextColorKey, "#FF6D6D6D");
            Set(SystemColors.HotTrackBrushKey, "#FF0066CC");
            Set(SystemColors.AppWorkspaceBrushKey, "#FFABABAB");
            Set(SystemColors.ScrollBarBrushKey, "#FFC8C8C8");
        }

        static void OnActivated(object sender, EventArgs e) { if (sender is Window w) Apply(w); }
        static void OnSized(object sender, SizeChangedEventArgs e) { if (sender is Window w) Apply(w); }

        public static void Apply(Window w)
        {
            // windows that are meant to be see-through (splash screens, popups) keep their transparency
            if (w.AllowsTransparency) return;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is HwndTarget target)
                {
                    var c = w.Background is SolidColorBrush sb && sb.Color.A == 255 ? sb.Color : Color.FromRgb(0xF0, 0xF0, 0xF0);
                    if (target.BackgroundColor != c) target.BackgroundColor = c;
                }
            }
            catch { }
            try
            {
                // Transparency mods make a window see-through by turning it into a layered window with a
                // window-wide alpha. A normal WPF window is never layered, so undo that: full alpha, then drop the style.
                var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                if ((ex & WS_EX_LAYERED) != 0)
                {
                    SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_LAYERED));
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }
            }
            catch { }
        }

        const int GWL_EXSTYLE = -20;
        const long WS_EX_LAYERED = 0x00080000;
        const int LWA_ALPHA = 0x2;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
