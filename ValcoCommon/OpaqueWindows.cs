// Shared by every Valco Office app. Edit here, not in the apps.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Valco.UI
{
    /// <summary>
    /// Valco windows draw their own Vista frame, so they must stay fully opaque. Transparency tweaks such as Windhawk's
    /// translucent-window mods or Mica/acrylic "for every app" tools extend DWM glass or a backdrop into the whole window,
    /// and WPF (whose WindowChrome clears to transparent) then shows the blur through the ribbon and text.
    /// This opts every Valco window out: opaque WPF background, no DWM backdrop, no accent blur, no extended frame,
    /// and re-applies it whenever the window is activated or resized (some mods re-apply theirs).
    /// </summary>
    public static class OpaqueWindows
    {
        static bool _installed;

        /// <summary>Call once at startup (App.OnStartup).</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                if (s is not Window w || e.OriginalSource != w) return;
                Apply(w);
                w.Activated -= OnActivated; w.Activated += OnActivated;
                w.SizeChanged -= OnSized; w.SizeChanged += OnSized;
                w.StateChanged -= OnActivated; w.StateChanged += OnActivated;
            }));
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
                var none = 1; // DWMSBT_NONE
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
                var off = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref off, sizeof(int));
                var m = new MARGINS();
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                // undo an acrylic/blur accent set from outside (ACCENT_DISABLED)
                var accent = new AccentPolicy { AccentState = 0 };
                var size = Marshal.SizeOf(accent);
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { /* older Windows or DWM off: nothing to undo */ }
        }

        const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int WCA_ACCENT_POLICY = 19;

        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    }
}
