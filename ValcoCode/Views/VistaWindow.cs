using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ValcoCode.Views;

/// <summary>Window with the Valco Office Vista Aero glass frame (same as Valco Word and Valco Express).</summary>
public class VistaWindow : Window
{
    static ImageSource? icon;
    public static ImageSource? AppIcon
    {
        get
        {
            if (icon != null) return icon;
            try
            {
                var dec = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/ValcoCode.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                icon = dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
            }
            catch { }
            return icon;
        }
    }

    public VistaWindow()
    {
        Style = (Style)Application.Current.FindResource("VistaWindowStyle");
        Resources["TitleIcon"] = AppIcon;
        // WindowChrome + SizeToContent can leave an unpainted margin; re-measure once rendered
        ContentRendered += (_, _) =>
        {
            var mode = SizeToContent;
            if (mode == SizeToContent.Manual) return;
            SizeToContent = SizeToContent.Manual;
            SizeToContent = mode;
            InvalidateVisual();
        };
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) =>
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }));
    }
}
