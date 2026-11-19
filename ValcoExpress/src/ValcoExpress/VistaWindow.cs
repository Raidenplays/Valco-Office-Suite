using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ValcoExpress
{
    /// <summary>Window with the custom Vista Aero glass frame.</summary>
    public class VistaWindow : Window
    {
        public VistaWindow()
        {
            Style = (Style)Application.Current.FindResource("VistaWindowStyle");
            Resources["TitleIcon"] = UI.AppIconImage;
            CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, e) => SystemCommands.CloseWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, e) => SystemCommands.MinimizeWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, e) =>
            {
                if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
                else SystemCommands.MaximizeWindow(this);
            }));
        }
    }

    public static class UI
    {
        static ImageSource _icon;
        public static ImageSource AppIconImage
        {
            get
            {
                if (_icon == null)
                {
                    try
                    {
                        var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri("pack://application:,,,/ValcoExpress.ico"),
                            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        _icon = dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
                    }
                    catch { _icon = Img("IconApp"); }
                }
                return _icon;
            }
        }

        public static ImageSource Img(string key) => Application.Current.TryFindResource(key) as ImageSource;

        public static Image Icon(string key, double size = 16) =>
            new Image { Source = Img(key), Width = size, Height = size, SnapsToDevicePixels = true };

        public static Button Btn(string text, RoutedEventHandler click, bool isDefault = false, bool isCancel = false)
        {
            var b = new Button
            {
                Content = text, IsDefault = isDefault, IsCancel = isCancel,
                Style = (Style)Application.Current.FindResource("DialogButton")
            };
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBlock Text(string text, double top = 0, bool bold = false) =>
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };

        public static Border Strip(params UIElement[] buttons)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in buttons) sp.Children.Add(b);
            return new Border { Style = (Style)Application.Current.FindResource("DialogStrip"), Child = sp };
        }

        /// <summary>Builds the standard Vista dialog layout: white content area + grey button strip.</summary>
        public static DockPanel DialogLayout(UIElement content, Border strip)
        {
            var dp = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(strip, Dock.Bottom);
            dp.Children.Add(strip);
            dp.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(14, 12, 14, 14), Child = content });
            return dp;
        }

        public static void ApplyTreeSelection(FrameworkElement el)
        {
            el.Resources[SystemColors.HighlightBrushKey] = Application.Current.FindResource("SelBrush");
            el.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
            el.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Application.Current.FindResource("SelInactiveBrush");
            el.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.Black;
        }

        public static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }

    public enum DlgIcon { None, Info, Question, Warning, Error }

    /// <summary>Vista-style message box.</summary>
    public class VistaDialog : VistaWindow
    {
        MessageBoxResult _result = MessageBoxResult.None;

        VistaDialog(Window owner, string text, string title, MessageBoxButton buttons, DlgIcon icon, string mainInstruction)
        {
            Title = title ?? "Valco Express";
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = owner == null;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            if (owner != null && owner.IsVisible) Owner = owner;

            var grid = new Grid { MinWidth = 300, MaxWidth = 520 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (icon != DlgIcon.None)
            {
                var img = UI.Icon("Icon" + icon, 32);
                img.VerticalAlignment = VerticalAlignment.Top;
                img.Margin = new Thickness(0, 0, 12, 0);
                grid.Children.Add(img);
            }
            var sp = new StackPanel();
            if (!string.IsNullOrEmpty(mainInstruction))
                sp.Children.Add(new TextBlock
                {
                    Text = mainInstruction, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x99)),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
                });
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            var btns = new List<Button>();
            void Add(string t, MessageBoxResult r, bool def = false, bool cancel = false) =>
                btns.Add(UI.Btn(t, (s, e) => { _result = r; Close(); }, def, cancel));
            switch (buttons)
            {
                case MessageBoxButton.OK: Add("OK", MessageBoxResult.OK, true, true); break;
                case MessageBoxButton.OKCancel: Add("OK", MessageBoxResult.OK, true); Add("Cancel", MessageBoxResult.Cancel, false, true); break;
                case MessageBoxButton.YesNo: Add("Yes", MessageBoxResult.Yes, true); Add("No", MessageBoxResult.No, false, true); break;
                case MessageBoxButton.YesNoCancel:
                    Add("Yes", MessageBoxResult.Yes, true); Add("No", MessageBoxResult.No); Add("Cancel", MessageBoxResult.Cancel, false, true); break;
            }
            Content = UI.DialogLayout(grid, UI.Strip(btns.ToArray()));

            Loaded += (s, e) =>
            {
                switch (icon)
                {
                    case DlgIcon.Error: SystemSounds.Hand.Play(); break;
                    case DlgIcon.Warning: SystemSounds.Exclamation.Play(); break;
                    case DlgIcon.Question: SystemSounds.Question.Play(); break;
                    case DlgIcon.Info: SystemSounds.Asterisk.Play(); break;
                }
                btns.FirstOrDefault()?.Focus();
            };
        }

        public static MessageBoxResult Show(Window owner, string text, string title = null,
            MessageBoxButton buttons = MessageBoxButton.OK, DlgIcon icon = DlgIcon.Info, string mainInstruction = null)
        {
            var d = new VistaDialog(owner, text, title, buttons, icon, mainInstruction);
            d.ShowDialog();
            if (d._result == MessageBoxResult.None)
                return buttons == MessageBoxButton.YesNo ? MessageBoxResult.No :
                       buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
            return d._result;
        }

        public static void Error(Window owner, string text, string mainInstruction = null) =>
            Show(owner, text, null, MessageBoxButton.OK, DlgIcon.Error, mainInstruction);

        public static bool Confirm(Window owner, string text, DlgIcon icon = DlgIcon.Question) =>
            Show(owner, text, null, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes;
    }

    /// <summary>Single-line text input dialog.</summary>
    public class InputDialog : VistaWindow
    {
        readonly TextBox _box;
        public string Value => _box.Text;

        public InputDialog(Window owner, string title, string prompt, string initial = "")
        {
            Title = title;
            Owner = owner;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 400;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 0), Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            var sp = new StackPanel();
            sp.Children.Add(UI.Text(prompt));
            sp.Children.Add(_box);
            Content = UI.DialogLayout(sp, UI.Strip(
                UI.Btn("OK", (s, e) => { if (!string.IsNullOrWhiteSpace(_box.Text)) DialogResult = true; }, true),
                UI.Btn("Cancel", null, false, true)));
            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }

        public static string Ask(Window owner, string title, string prompt, string initial = "")
        {
            var d = new InputDialog(owner, title, prompt, initial);
            return d.ShowDialog() == true ? d.Value.Trim() : null;
        }
    }

    /// <summary>Folder chooser for Move to / Copy to.</summary>
    public class FolderPickerDialog : VistaWindow
    {
        readonly TreeView _tree;
        public FolderNode Selected => _tree.SelectedItem as FolderNode;

        public FolderPickerDialog(Window owner, string title, string prompt, IEnumerable<FolderNode> roots)
        {
            Title = title;
            Owner = owner;
            Width = 340; Height = 420;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _tree = new TreeView { ItemsSource = roots.ToList(), Margin = new Thickness(0, 8, 0, 0) };
            _tree.ItemTemplate = (DataTemplate)Application.Current.FindResource("FolderTemplate");
            _tree.ItemContainerStyle = (Style)Application.Current.FindResource("FolderItemStyle");
            UI.ApplyTreeSelection(_tree);
            var dp = new DockPanel();
            var lbl = UI.Text(prompt);
            DockPanel.SetDock(lbl, Dock.Top);
            dp.Children.Add(lbl);
            dp.Children.Add(_tree);
            Content = UI.DialogLayout(dp, UI.Strip(
                UI.Btn("OK", (s, e) => { if (Selected != null && Selected.Selectable) DialogResult = true; }, true),
                UI.Btn("Cancel", null, false, true)));
        }

        public static FolderNode Pick(Window owner, string title, string prompt, IEnumerable<FolderNode> roots)
        {
            var d = new FolderPickerDialog(owner, title, prompt, roots);
            return d.ShowDialog() == true ? d.Selected : null;
        }
    }

}
