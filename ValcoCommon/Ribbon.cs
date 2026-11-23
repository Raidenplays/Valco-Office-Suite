// Shared by every Valco Office app (Word, Express, Code). Edit here, not in the apps.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Valco.UI
{
    /// <summary>
    /// Valco Office colour schemes (Blue, Silver, Black), published as app-level dynamic resources.
    /// Shared by every Valco app: ribbon keys (Rb.*) and the pane/status keys used by Express and Code.
    /// </summary>
    public static class OfficeTheme
    {
        /// <summary>Blue, Silver and Black are Office 2007's; the others match the Valco app themes (Valco Code, Valco Weave).</summary>
        public static readonly string[] Schemes = { "Blue", "Silver", "Black", "Windows Vista", "Modern Dark", "Dreamweaver CS6", "2077" };
        /// <summary>True for the schemes with a dark ribbon and dark text areas around the document.</summary>
        public static bool IsDark => Current == "Modern Dark" || Current == "2077";
        public static string Current { get; private set; } = "Blue";
        public static event Action Changed;

        static LinearGradientBrush V(params (string Color, double Offset)[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
            b.Freeze();
            return b;
        }
        static SolidColorBrush S(string c) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)); b.Freeze(); return b; }

        public static void Apply(string scheme)
        {
            if (Application.Current == null) return;
            var r = Application.Current.Resources;
            switch (scheme)
            {
                case "Silver":
                    Current = "Silver";
                    r["Rb.TabStrip"] = V(("#FFEEF1F5", 0), ("#FFDDE2E9", 1));
                    r["Rb.Body"] = V(("#FFF8F9FB", 0), ("#FFE7EAEE", 0.25), ("#FFDDE1E6", 0.8), ("#FFE9ECF0", 1));
                    r["Rb.Group"] = V(("#FFF8F8F9", 0), ("#FFE6E8EC", 0.75), ("#FFDFE2E7", 1));
                    r["Rb.GroupBorder"] = S("#FFB9BEC6");
                    r["Rb.GroupLabelBg"] = V(("#FFD5DAE1", 0), ("#FFC8CED6", 1));
                    r["Rb.Text"] = S("#FF4C535C");
                    r["Rb.TabText"] = S("#FF4C535C");
                    r["Rb.TabSel"] = V(("#FFFFFFFF", 0), ("#FFEEF0F3", 1));
                    r["Rb.TabBorder"] = S("#FFB9BEC6");
                    r["Rb.Workspace"] = V(("#FFD9DDE3", 0), ("#FFC2C7CF", 1));
                    r["Rb.Status"] = V(("#FFEEF1F4", 0), ("#FFD2D7DE", 1));
                    r["Rb.StatusText"] = S("#FF4C535C");
                    r["MenuBarBrush"] = V(("#FFEEF1F5", 0), ("#FFDDE2E9", 1));
                    r["CommandBarBrush"] = V(("#FF8C96A1", 0), ("#FF6B7581", 0.48), ("#FF545E6A", 0.5), ("#FF66707C", 1));
                    r["StatusBarBrush"] = V(("#FFEEF1F4", 0), ("#FFD2D7DE", 1));
                    r["StatusText"] = S("#FF4C535C");
                    r["PaneHeaderBrush"] = V(("#FFF8F9FB", 0), ("#FFE1E4E9", 1));
                    r["PaneHeaderText"] = S("#FF4C535C");
                    r["PreviewHeaderBrush"] = V(("#FFFDFDFE", 0), ("#FFE4E7EC", 1));
                    r["LightBarBrush"] = V(("#FFF8F8F9", 0), ("#FFE6E8EC", 1));
                    r["SplitterBrush"] = S("#FFD5D9E0");
                    r["ChromeLine"] = S("#FFB9BEC6");
                    r["Rb.CaptionText"] = S("#FF4C535C");
                    break;
                case "Black":
                    Current = "Black";
                    r["Rb.TabStrip"] = V(("#FF535353", 0), ("#FF3B3B3B", 1));
                    r["Rb.Body"] = V(("#FF616161", 0), ("#FF4A4A4A", 0.25), ("#FF3D3D3D", 0.8), ("#FF494949", 1));
                    r["Rb.Group"] = V(("#FFDCDCDC", 0), ("#FFC4C4C4", 0.75), ("#FFB9B9B9", 1));
                    r["Rb.GroupBorder"] = S("#FF7A7A7A");
                    r["Rb.GroupLabelBg"] = V(("#FF6B6B6B", 0), ("#FF505050", 1));
                    r["Rb.Text"] = S("#FF000000");
                    r["Rb.TabText"] = S("#FFFFFFFF");
                    r["Rb.TabSel"] = V(("#FF7A7A7A", 0), ("#FF5C5C5C", 1));
                    r["Rb.TabBorder"] = S("#FF2B2B2B");
                    r["Rb.Workspace"] = V(("#FF555555", 0), ("#FF3F3F3F", 1));
                    r["Rb.Status"] = V(("#FF4E4E4E", 0), ("#FF2F2F2F", 1));
                    r["Rb.StatusText"] = S("#FFFFFFFF");
                    r["MenuBarBrush"] = V(("#FF535353", 0), ("#FF3B3B3B", 1));
                    r["CommandBarBrush"] = V(("#FF606060", 0), ("#FF3F3F3F", 0.48), ("#FF262626", 0.5), ("#FF383838", 1));
                    r["StatusBarBrush"] = V(("#FF4E4E4E", 0), ("#FF2F2F2F", 1));
                    r["StatusText"] = S("#FFFFFFFF");
                    r["PaneHeaderBrush"] = V(("#FF5C5C5C", 0), ("#FF404040", 1));
                    r["PaneHeaderText"] = S("#FFFFFFFF");
                    r["PreviewHeaderBrush"] = V(("#FFF4F4F4", 0), ("#FFD8D8D8", 1));
                    r["LightBarBrush"] = V(("#FFEFEFEF", 0), ("#FFD9D9D9", 1));
                    r["SplitterBrush"] = S("#FF9A9A9A");
                    r["ChromeLine"] = S("#FF7A7A7A");
                    r["Rb.CaptionText"] = S("#FFFFFFFF");
                    break;
                case "Windows Vista":
                    Current = scheme;
                    r["Rb.TabStrip"] = V(("#FF5A7FA3", 0), ("#FF2C4E70", 0.5), ("#FF1D3A5C", 0.51), ("#FF27496B", 1));
                    r["Rb.Body"] = V(("#FFF7FAFD", 0), ("#FFE6EEF7", 0.4), ("#FFD8E5F2", 1));
                    r["Rb.Group"] = V(("#FFFFFFFF", 0), ("#FFEEF4FA", 0.7), ("#FFE2ECF6", 1));
                    r["Rb.GroupBorder"] = S("#FFA8C0DA");
                    r["Rb.GroupLabelBg"] = V(("#FFD5E3F1", 0), ("#FFC3D6EA", 1));
                    r["Rb.Text"] = S("#FF1E395B");
                    r["Rb.TabText"] = S("#FFFFFFFF");
                    r["Rb.TabSel"] = V(("#FFFFFFFF", 0), ("#FFE4EEF8", 1));
                    r["Rb.TabBorder"] = S("#FF6F8FB0");
                    r["Rb.Workspace"] = V(("#FFC9D9EA", 0), ("#FFAFC6DE", 1));
                    r["Rb.Status"] = V(("#FFF0F5FA", 0), ("#FFD9E5F1", 1));
                    r["Rb.StatusText"] = S("#FF1E395B");
                    r["MenuBarBrush"] = V(("#FF5A7FA3", 0), ("#FF2C4E70", 0.5), ("#FF1D3A5C", 0.51), ("#FF27496B", 1));
                    r["CommandBarBrush"] = V(("#FF5A7FA3", 0), ("#FF2C4E70", 0.48), ("#FF1D3A5C", 0.5), ("#FF27496B", 1));
                    r["StatusBarBrush"] = V(("#FFF0F5FA", 0), ("#FFD9E5F1", 1));
                    r["StatusText"] = S("#FF1E395B");
                    r["PaneHeaderBrush"] = V(("#FFF5F9FD", 0), ("#FFDCE7F3", 1));
                    r["PaneHeaderText"] = S("#FF1E395B");
                    r["PreviewHeaderBrush"] = V(("#FFFBFDFF", 0), ("#FFE2ECF6", 1));
                    r["LightBarBrush"] = V(("#FFFFFFFF", 0), ("#FFE8F0F8", 1));
                    r["SplitterBrush"] = S("#FFB9CDE3");
                    r["ChromeLine"] = S("#FF8FAAC8");
                    r["Rb.CaptionText"] = S("#FF1E395B");
                    Hover(r, V(("#FFEAF6FD", 0), ("#FFD9F0FC", 0.45), ("#FFBEE6FD", 0.5), ("#FFA7D9F5", 1)), "#FF3C7FB1",
                             V(("#FFE5F4FC", 0), ("#FFC4E5F6", 0.45), ("#FF98D1EF", 0.5), ("#FF68B3DB", 1)), "#FF2C628B",
                             V(("#FFDAEEF9", 0), ("#FFB2DDF4", 0.5), ("#FF8DCAEB", 1)));
                    Changed?.Invoke();
                    return;
                case "Dreamweaver CS6":
                    Current = scheme;
                    r["Rb.TabStrip"] = V(("#FFE8E8E8", 0), ("#FFCDCDCD", 1));
                    r["Rb.Body"] = V(("#FFF2F2F2", 0), ("#FFDCDCDC", 1));
                    r["Rb.Group"] = V(("#FFFAFAFA", 0), ("#FFE4E4E4", 1));
                    r["Rb.GroupBorder"] = S("#FFA8A8A8");
                    r["Rb.GroupLabelBg"] = V(("#FFD4D4D4", 0), ("#FFC4C4C4", 1));
                    r["Rb.Text"] = S("#FF1E1E1E");
                    r["Rb.TabText"] = S("#FF1E1E1E");
                    r["Rb.TabSel"] = V(("#FFFFFFFF", 0), ("#FFEFEFEF", 1));
                    r["Rb.TabBorder"] = S("#FFA8A8A8");
                    r["Rb.Workspace"] = V(("#FFB4B4B4", 0), ("#FF9E9E9E", 1));
                    r["Rb.Status"] = V(("#FFD6D6D6", 0), ("#FFBDBDBD", 1));
                    r["Rb.StatusText"] = S("#FF1E1E1E");
                    r["MenuBarBrush"] = V(("#FFE8E8E8", 0), ("#FFCDCDCD", 1));
                    r["CommandBarBrush"] = V(("#FF8A8A8A", 0), ("#FF6A6A6A", 0.48), ("#FF555555", 0.5), ("#FF646464", 1));
                    r["StatusBarBrush"] = V(("#FFD6D6D6", 0), ("#FFBDBDBD", 1));
                    r["StatusText"] = S("#FF1E1E1E");
                    r["PaneHeaderBrush"] = V(("#FFEDEDED", 0), ("#FFD6D6D6", 1));
                    r["PaneHeaderText"] = S("#FF1E1E1E");
                    r["PreviewHeaderBrush"] = V(("#FFFAFAFA", 0), ("#FFE2E2E2", 1));
                    r["LightBarBrush"] = V(("#FFFAFAFA", 0), ("#FFE4E4E4", 1));
                    r["SplitterBrush"] = S("#FFB8B8B8");
                    r["ChromeLine"] = S("#FFA8A8A8");
                    r["Rb.CaptionText"] = S("#FF1E1E1E");
                    Hover(r, V(("#FFE5F0FB", 0), ("#FFCFE3F8", 1)), "#FF7DA2CE",
                             V(("#FFBFD7F2", 0), ("#FFA6C6EC", 1)), "#FF5A84B8",
                             V(("#FFD4E5F7", 0), ("#FFBDD6F2", 1)));
                    Changed?.Invoke();
                    return;
                case "Modern Dark":
                    Current = scheme;
                    r["Rb.TabStrip"] = V(("#FF1A1C21", 0), ("#FF1A1C21", 1));
                    r["Rb.Body"] = V(("#FF262930", 0), ("#FF1F2127", 1));
                    r["Rb.Group"] = V(("#FF2C2F37", 0), ("#FF25282F", 1));
                    r["Rb.GroupBorder"] = S("#FF3A3F4B");
                    r["Rb.GroupLabelBg"] = V(("#FF30343D", 0), ("#FF2A2D35", 1));
                    r["Rb.Text"] = S("#FFD6D9E0");
                    r["Rb.TabText"] = S("#FFC9CED8");
                    r["Rb.TabSel"] = V(("#FF30343D", 0), ("#FF262930", 1));
                    r["Rb.TabBorder"] = S("#FF3A3F4B");
                    r["Rb.Workspace"] = V(("#FF16181C", 0), ("#FF111317", 1));
                    r["Rb.Status"] = V(("#FF1A1C21", 0), ("#FF16181C", 1));
                    r["Rb.StatusText"] = S("#FFC9CED8");
                    r["MenuBarBrush"] = V(("#FF1A1C21", 0), ("#FF1A1C21", 1));
                    r["CommandBarBrush"] = V(("#FF2C2F37", 0), ("#FF1F2127", 1));
                    r["StatusBarBrush"] = V(("#FF1A1C21", 0), ("#FF16181C", 1));
                    r["StatusText"] = S("#FFC9CED8");
                    r["PaneHeaderBrush"] = V(("#FF2C2F37", 0), ("#FF25282F", 1));
                    r["PaneHeaderText"] = S("#FFD6D9E0");
                    r["PreviewHeaderBrush"] = V(("#FFF4F4F4", 0), ("#FFD8D8D8", 1));
                    r["LightBarBrush"] = V(("#FFEFEFEF", 0), ("#FFD9D9D9", 1));
                    r["SplitterBrush"] = S("#FF3A3F4B");
                    r["ChromeLine"] = S("#FF3A3F4B");
                    r["Rb.CaptionText"] = S("#FF9AA1AE");
                    Hover(r, V(("#FF2F5F5A", 0), ("#FF27504C", 1)), "#FF36C2B4",
                             V(("#FF1F7A70", 0), ("#FF19665E", 1)), "#FF36C2B4",
                             V(("#FF245B55", 0), ("#FF1E4C47", 1)));
                    Changed?.Invoke();
                    return;
                case "2077":
                    Current = scheme;
                    r["Rb.TabStrip"] = V(("#FF0B082E", 0), ("#FF120C45", 1));
                    r["Rb.Body"] = V(("#FF1A1250", 0), ("#FF0E0A33", 1));
                    r["Rb.Group"] = V(("#FF21175E", 0), ("#FF160F45", 1));
                    r["Rb.GroupBorder"] = S("#FFFF2592");
                    r["Rb.GroupLabelBg"] = V(("#FF2A1366", 0), ("#FF1E0F4D", 1));
                    r["Rb.Text"] = S("#FFEAE2FF");
                    r["Rb.TabText"] = S("#FF2DE2E6");
                    r["Rb.TabSel"] = V(("#FF2A1366", 0), ("#FF1A1250", 1));
                    r["Rb.TabBorder"] = S("#FFFF2592");
                    r["Rb.Workspace"] = V(("#FF0B082E", 0), ("#FF07051F", 1));
                    r["Rb.Status"] = V(("#FF0B082E", 0), ("#FF07051F", 1));
                    r["Rb.StatusText"] = S("#FF2DE2E6");
                    r["MenuBarBrush"] = V(("#FF0B082E", 0), ("#FF120C45", 1));
                    r["CommandBarBrush"] = V(("#FF21175E", 0), ("#FF0E0A33", 1));
                    r["StatusBarBrush"] = V(("#FF0B082E", 0), ("#FF07051F", 1));
                    r["StatusText"] = S("#FF2DE2E6");
                    r["PaneHeaderBrush"] = V(("#FF21175E", 0), ("#FF160F45", 1));
                    r["PaneHeaderText"] = S("#FFFF2592");
                    r["PreviewHeaderBrush"] = V(("#FFF4F0FF", 0), ("#FFDCD4F2", 1));
                    r["LightBarBrush"] = V(("#FFF1ECFF", 0), ("#FFDCD4F2", 1));
                    r["SplitterBrush"] = S("#FFFF2592");
                    r["ChromeLine"] = S("#FFFF2592");
                    r["Rb.CaptionText"] = S("#FFFF2592");
                    Hover(r, V(("#FF3B1670", 0), ("#FF2D0F5A", 1)), "#FF2DE2E6",
                             V(("#FFFF2592", 0), ("#FFC81C74", 1)), "#FFFFE600",
                             V(("#FF5A1A80", 0), ("#FF451366", 1)));
                    Changed?.Invoke();
                    return;
                default:
                    Current = "Blue";
                    r["Rb.TabStrip"] = V(("#FFDCE8F7", 0), ("#FFCADBF1", 1));
                    r["Rb.Body"] = V(("#FFEAF1FB", 0), ("#FFD7E4F5", 0.25), ("#FFC8DAF1", 0.8), ("#FFD5E4F7", 1));
                    r["Rb.Group"] = V(("#FFF3F7FC", 0), ("#FFDFE9F6", 0.75), ("#FFD3E1F3", 1));
                    r["Rb.GroupBorder"] = S("#FF9BBBE3");
                    r["Rb.GroupLabelBg"] = V(("#FFC9DBF1", 0), ("#FFB9CFEC", 1));
                    r["Rb.Text"] = S("#FF15428B");
                    r["Rb.TabText"] = S("#FF15428B");
                    r["Rb.TabSel"] = V(("#FFFCFDFF", 0), ("#FFE5EEF9", 1));
                    r["Rb.TabBorder"] = S("#FF8DB2E3");
                    r["Rb.Workspace"] = V(("#FFB5C8E0", 0), ("#FF9EB5D2", 1));
                    r["Rb.Status"] = V(("#FFDBE7F6", 0), ("#FFB2CAEA", 1));
                    r["Rb.StatusText"] = S("#FF15428B");
                    r["MenuBarBrush"] = V(("#FFDCE8F7", 0), ("#FFCADBF1", 1));
                    r["CommandBarBrush"] = V(("#FF6E90A9", 0), ("#FF46677F", 0.48), ("#FF2A4A60", 0.5), ("#FF3E6178", 1));
                    r["StatusBarBrush"] = V(("#FFDBE7F6", 0), ("#FFB2CAEA", 1));
                    r["StatusText"] = S("#FF15428B");
                    r["PaneHeaderBrush"] = V(("#FFEAF1FB", 0), ("#FFD3E1F3", 1));
                    r["PaneHeaderText"] = S("#FF15428B");
                    r["PreviewHeaderBrush"] = V(("#FFF5F9FE", 0), ("#FFD7E4F5", 1));
                    r["LightBarBrush"] = V(("#FFF3F7FC", 0), ("#FFDFE9F6", 1));
                    r["SplitterBrush"] = S("#FFC8D8EE");
                    r["ChromeLine"] = S("#FF8DB2E3");
                    r["Rb.CaptionText"] = S("#FF15428B");
                    break;
            }
            // Office 2007's gold hover for Blue, Silver and Black
            Hover(r, V(("#FFFFFDF0", 0), ("#FFFFEDB3", 0.45), ("#FFFFD660", 0.5), ("#FFFFE7A0", 1)), "#FFDBCE99",
                     V(("#FFF8C985", 0), ("#FFF6AE5C", 0.45), ("#FFF39434", 0.5), ("#FFF8C46C", 1)), "#FFC2A15D",
                     V(("#FFFCDCA4", 0), ("#FFFAC36E", 0.45), ("#FFF8B044", 0.5), ("#FFFBD58D", 1)));
            Changed?.Invoke();
        }

        static void Hover(ResourceDictionary r, Brush hover, string hoverBorder, Brush pressed, string pressedBorder, Brush check)
        {
            r["Rb.Hover"] = hover;
            r["Rb.HoverBorder"] = S(hoverBorder);
            r["Rb.Pressed"] = pressed;
            r["Rb.PressedBorder"] = S(pressedBorder);
            r["Rb.Checked"] = check;
            r["Rb.Menu"] = S("#FFFAFAFA");
        }

        /// <summary>The Valco Office suite mark (three petals), or null if it can't be loaded.</summary>
        public static ImageSource SuiteMark
        {
            get
            {
                try { var b = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/ValcoOffice-mark.png")); b.Freeze(); return b; }
                catch { return null; }
            }
        }
    }

    // =====================================================================================
    public class RibbonTab
    {
        public string Header;
        public readonly List<FrameworkElement> Groups = new List<FrameworkElement>();
        public string ContextGroup;          // e.g. "Table Tools"
        public Color ContextColor;
        internal ToggleButton HeaderButton;
        internal StackPanel Panel;
        bool _visible = true;
        public bool Visible
        {
            get => _visible;
            set { _visible = value; if (HeaderButton != null) HeaderButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
        }
        public RibbonTab(string header) { Header = header; }
        public RibbonTab Add(FrameworkElement group) { Groups.Add(group); return this; }
    }

    /// <summary>The Ribbon: tab strip, contextual tab labels and the group area.</summary>
    public class RibbonBar : DockPanel
    {
        readonly StackPanel _tabStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(54, 0, 0, 0) };
        readonly Border _body;
        readonly ContentControl _bodyContent = new ContentControl();
        readonly List<RibbonTab> _tabs = new List<RibbonTab>();
        public RibbonTab Selected { get; private set; }
        public bool Minimized { get; private set; }
        public event Action<RibbonTab> TabChanged;
        public readonly Grid HeaderRow = new Grid { Height = 26 };

        public RibbonBar()
        {
            LastChildFill = true;
            SetResourceReference(BackgroundProperty, "Rb.TabStrip");
            HeaderRow.Children.Add(_tabStrip);
            SetDock(HeaderRow, Dock.Top);
            Children.Add(HeaderRow);
            _body = new Border { Height = 100, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0, 0, 3, 3), Padding = new Thickness(2, 2, 2, 1), Margin = new Thickness(2, 0, 2, 2), Child = _bodyContent };
            _body.SetResourceReference(Border.BackgroundProperty, "Rb.Body");
            _body.SetResourceReference(Border.BorderBrushProperty, "Rb.TabBorder");
            Children.Add(_body);
        }

        public RibbonTab AddTab(RibbonTab tab)
        {
            _tabs.Add(tab);
            var hb = new ToggleButton { Content = tab.Header, Style = TabStyle(tab), Focusable = false };
            hb.Click += (s, e) =>
            {
                if (Minimized && Selected == tab) { _body.Visibility = _body.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; hb.IsChecked = true; return; }
                Select(tab);
            };
            hb.MouseDoubleClick += (s, e) => ToggleMinimize();
            tab.HeaderButton = hb;
            tab.Visible = tab.Visible;
            _tabStrip.Children.Add(hb);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var g in tab.Groups) panel.Children.Add(g);
            tab.Panel = panel;
            if (Selected == null) Select(tab);
            return tab;
        }

        public void Select(RibbonTab tab)
        {
            if (!tab.Visible) return;
            Selected = tab;
            foreach (var t in _tabs) t.HeaderButton.IsChecked = t == tab;
            _bodyContent.Content = new ScrollViewer
            {
                Content = tab.Panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
            };
            if (Minimized) _body.Visibility = Visibility.Visible;
            TabChanged?.Invoke(tab);
        }

        public void ToggleMinimize()
        {
            Minimized = !Minimized;
            _body.Visibility = Minimized ? Visibility.Collapsed : Visibility.Visible;
        }

        public void CollapseIfMinimized() { if (Minimized) _body.Visibility = Visibility.Collapsed; }

        public void SetContextVisible(string contextGroup, bool visible)
        {
            foreach (var t in _tabs.Where(t => t.ContextGroup == contextGroup))
            {
                if (t.Visible == visible) continue;
                t.Visible = visible;
                if (!visible && Selected == t) Select(_tabs[0]);
            }
        }

        static Style TabStyle(RibbonTab tab)
        {
            var st = new Style(typeof(ToggleButton));
            st.Setters.Add(new Setter(Control.FontSizeProperty, 11.5));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 2, 12, 3)));
            st.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1, 3, 1, 0)));
            st.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Arrow));
            var t = new ControlTemplate(typeof(ToggleButton));
            var border = new FrameworkElementFactory(typeof(Border), "B");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3, 3, 0, 0));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BackgroundProperty, tab.ContextGroup != null ? (Brush)new SolidColorBrush(Color.FromArgb(0x50, tab.ContextColor.R, tab.ContextColor.G, tab.ContextColor.B)) : Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), "B"));
            var chk = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            chk.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Rb.TabSel"), "B"));
            chk.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Rb.TabBorder"), "B"));
            t.Triggers.Add(hover); t.Triggers.Add(chk);
            st.Setters.Add(new Setter(Control.TemplateProperty, t));
            st.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Rb.TabText")));
            var chkFg = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            chkFg.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Rb.Text")));
            st.Triggers.Add(chkFg);
            return st;
        }
    }

    // =====================================================================================
    /// <summary>Factory for ribbon controls in the Office 2007 look (shared by every Valco app).</summary>
    public static class RB
    {
        static Style _btn, _tgl;

        static Style ButtonStyle(Type t)
        {
            var st = new Style(t);
            st.Setters.Add(new Setter(Control.FocusableProperty, false));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3, 1, 3, 1)));
            st.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Rb.Text")));
            st.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            var tpl = new ControlTemplate(t);
            var b = new FrameworkElementFactory(typeof(Border), "B");
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            b.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            b.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            b.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            b.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(cp);
            tpl.VisualTree = b;
            Trigger T(DependencyProperty p, object v, string bg, string border)
            {
                var tr = new Trigger { Property = p, Value = v };
                tr.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(bg), "B"));
                tr.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(border), "B"));
                return tr;
            }
            tpl.Triggers.Add(T(UIElement.IsMouseOverProperty, true, "Rb.Hover", "Rb.HoverBorder"));
            if (t == typeof(ToggleButton)) tpl.Triggers.Add(T(ToggleButton.IsCheckedProperty, true, "Rb.Checked", "Rb.PressedBorder"));
            tpl.Triggers.Add(T(ButtonBase.IsPressedProperty, true, "Rb.Pressed", "Rb.PressedBorder"));
            var dis = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            dis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            tpl.Triggers.Add(dis);
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return st;
        }

        public static Style BtnStyle => _btn ??= ButtonStyle(typeof(Button));
        public static Style TglStyle => _tgl ??= ButtonStyle(typeof(ToggleButton));

        public static object Tip(string title, string body)
        {
            var sp = new StackPanel { MaxWidth = 260 };
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, body == null ? 0 : 6) });
            if (body != null) sp.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
            return sp;
        }

        static Path Arrow() => new Path { Data = Geometry.Parse("M0,0 L5,0 L2.5,3 Z"), Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x4A, 0x5C)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 1, 1, 0) };

        public static ImageSource Icon(string key) => Application.Current?.TryFindResource(key) as ImageSource;

        static Image Img(string key, double size) => new Image { Source = Icon(key), Width = size, Height = size, SnapsToDevicePixels = true };

        /// <summary>Large 32px button with label underneath.</summary>
        public static Button Large(string icon, string label, Action click, string tip = null, Func<ContextMenu> menu = null)
        {
            var b = new Button { Style = BtnStyle, Padding = new Thickness(3, 2, 3, 2), HorizontalContentAlignment = HorizontalAlignment.Center, MinWidth = 42, VerticalAlignment = VerticalAlignment.Stretch };
            var sp = new StackPanel();
            sp.Children.Add(Img(icon, 32));
            var lbl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var tb = new TextBlock { Text = label, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 62, TextWrapping = TextWrapping.Wrap };
            lbl.Children.Add(tb);
            if (menu != null) lbl.Children.Add(Arrow());
            sp.Children.Add(lbl);
            b.Content = sp;
            b.ToolTip = Tip(label.Replace("\n", " "), tip);
            Wire(b, click, menu);
            return b;
        }

        static void Wire(ButtonBase b, Action click, Func<ContextMenu> menu)
        {
            b.Click += (s, e) =>
            {
                if (menu != null && click == null) { OpenMenu(b, menu()); return; }
                click?.Invoke();
            };
        }

        public static void OpenMenu(FrameworkElement target, ContextMenu m)
        {
            if (m == null) return;
            m.PlacementTarget = target;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        }

        /// <summary>Small 16px button with optional label.</summary>
        public static Button Small(string icon, string label, Action click, string tip = null, Func<ContextMenu> menu = null)
        {
            var b = new Button { Style = BtnStyle, Height = 22, Margin = new Thickness(0, 0, 1, 0) };
            b.Content = SmallContent(icon, label, menu != null);
            b.ToolTip = Tip(label ?? tip, label != null ? tip : null);
            Wire(b, click, menu);
            return b;
        }

        public static ToggleButton Toggle(string icon, string label, Action<bool> click, string tip = null)
        {
            var b = new ToggleButton { Style = TglStyle, Height = 22, Margin = new Thickness(0, 0, 1, 0) };
            b.Content = SmallContent(icon, label, false);
            b.ToolTip = Tip(label ?? tip, label != null ? tip : null);
            b.Click += (s, e) => click(b.IsChecked == true);
            return b;
        }

        /// <summary>Toggle that shows a text glyph (B, I, U...).</summary>
        public static ToggleButton Glyph(UIElement glyph, Action click, string tip, string body = null)
        {
            var b = new ToggleButton { Style = TglStyle, Height = 22, MinWidth = 22, Margin = new Thickness(0, 0, 1, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = glyph, ToolTip = Tip(tip, body) };
            b.Click += (s, e) => click();
            return b;
        }

        static UIElement SmallContent(string icon, string label, bool arrow)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) sp.Children.Add(Img(icon, 16));
            if (label != null) sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(icon != null ? 4 : 1, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
            if (arrow) sp.Children.Add(Arrow());
            return sp;
        }

        /// <summary>Split button: main part runs the action, arrow part opens the menu.</summary>
        public static FrameworkElement Split(string icon, string label, Action click, Func<ContextMenu> menu, string tip = null, bool large = false, FrameworkElement customIcon = null)
        {
            var main = new Button { Style = BtnStyle, Height = large ? double.NaN : 22, Padding = large ? new Thickness(3, 2, 3, 0) : new Thickness(3, 1, 1, 1), HorizontalContentAlignment = HorizontalAlignment.Center };
            var arrow = new Button { Style = BtnStyle, Height = large ? double.NaN : 22, Padding = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center };
            if (large)
            {
                var sp = new StackPanel();
                sp.Children.Add(customIcon ?? Img(icon, 32));
                main.Content = sp;
                var lbl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                lbl.Children.Add(new TextBlock { Text = label, MaxWidth = 62, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
                lbl.Children.Add(Arrow());
                arrow.Content = lbl;
                var stack = new StackPanel();
                stack.Children.Add(main); stack.Children.Add(arrow);
                main.Click += (s, e) => click();
                arrow.Click += (s, e) => OpenMenu(stack, menu());
                stack.ToolTip = Tip(label, tip);
                return stack;
            }
            main.Content = customIcon ?? SmallContent(icon, label, false);
            arrow.Content = Arrow();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 1, 0) };
            row.Children.Add(main); row.Children.Add(arrow);
            main.Click += (s, e) => click();
            arrow.Click += (s, e) => OpenMenu(row, menu());
            row.ToolTip = Tip(label ?? tip, label != null ? tip : null);
            return row;
        }

        /// <summary>Column of up to 3 small controls.</summary>
        public static StackPanel Stack(params UIElement[] items)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 2, 0) };
            foreach (var i in items) { if (i is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(i); }
            return sp;
        }

        /// <summary>Horizontal row of small controls (Font/Paragraph group rows).</summary>
        public static StackPanel Row(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 2) };
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }

        public static Border Sep() => new Border { Width = 1, Margin = new Thickness(3, 2, 3, 2), Background = new SolidColorBrush(Color.FromArgb(0x60, 0x7A, 0x9C, 0xC8)) };

        /// <summary>A ribbon group: content with the caption strip below and an optional dialog launcher.</summary>
        public static Border Group(string caption, Action launcher, params UIElement[] items)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 2, 0) };
            foreach (var i in items) content.Children.Add(i);
            var label = new Grid { Height = 16 };
            label.SetResourceReference(Panel.BackgroundProperty, "Rb.GroupLabelBg");
            var tb = new TextBlock { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(6, 0, launcher != null ? 14 : 6, 0) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Rb.CaptionText");
            label.Children.Add(tb);
            if (launcher != null)
            {
                var lb = new Button { Style = BtnStyle, Width = 14, Height = 14, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 1, 0), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = Tip(caption, "Show more options for " + caption.ToLowerInvariant() + ".") };
                lb.Content = new Path { Data = Geometry.Parse("M0,0 H3 M0,0 V3 M1,1 L6,6 M6,3 V6 H3"), Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x5A, 0x86)), StrokeThickness = 1, Margin = new Thickness(0) };
                lb.Click += (s, e) => launcher();
                label.Children.Add(lb);
            }
            var dp = new DockPanel();
            DockPanel.SetDock(label, Dock.Bottom);
            dp.Children.Add(label);
            dp.Children.Add(content);
            var g = new Border { Child = dp, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(1, 0, 2, 0), ClipToBounds = true };
            g.SetResourceReference(Border.BackgroundProperty, "Rb.Group");
            g.SetResourceReference(Border.BorderBrushProperty, "Rb.GroupBorder");
            return g;
        }

        public static ComboBox Combo(double width, bool editable = false)
        {
            var c = new ComboBox { Width = width, Height = 21, IsEditable = editable, Margin = new Thickness(0, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 11.5 };
            c.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            return c;
        }

        public static MenuItem MI(string header, Action click, string icon = null, string gesture = null, bool isChecked = false)
        {
            var m = new MenuItem { Header = header, InputGestureText = gesture, IsChecked = isChecked };
            if (icon != null) m.Icon = new Image { Source = Icon(icon), Width = 16, Height = 16 };
            if (click != null) m.Click += (s, e) => click();
            return m;
        }

        public static ContextMenu Menu(params object[] items)
        {
            var m = new ContextMenu();
            foreach (var i in items) m.Items.Add(i ?? new Separator());
            return m;
        }

        /// <summary>Large preview tile used in galleries (styles, cover pages, shapes...).</summary>
        public static Button Tile(UIElement visual, string tip, Action click, double w = 64, double h = 56)
        {
            var b = new Button { Style = BtnStyle, Width = w, Height = h, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center, Content = visual, ToolTip = tip, Margin = new Thickness(1) };
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>Drop-down gallery popup with a wrap of tiles.</summary>
        public static void ShowGallery(FrameworkElement target, string title, IEnumerable<UIElement> tiles, double width = 420, IEnumerable<UIElement> footer = null)
        {
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var wrap = new WrapPanel { Width = width - 12, Margin = new Thickness(4) };
            foreach (var t in tiles)
            {
                if (t is ButtonBase bb) bb.Click += (s, e) => popup.IsOpen = false;
                wrap.Children.Add(t);
            }
            var sp = new StackPanel();
            if (title != null)
                sp.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE7, 0xEE)), Padding = new Thickness(6, 2, 6, 2),
                    Child = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) }
                });
            sp.Children.Add(new ScrollViewer { Content = wrap, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            if (footer != null)
            {
                sp.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)), Margin = new Thickness(0, 2, 0, 2) });
                foreach (var f in footer)
                {
                    if (f is ButtonBase fb) fb.Click += (s, e) => popup.IsOpen = false;
                    sp.Children.Add(f);
                }
            }
            popup.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x86)), BorderThickness = new Thickness(1),
                Child = sp, Width = width, Effect = new DropShadowEffect { ShadowDepth = 2, Opacity = 0.35, BlurRadius = 5 }, Margin = new Thickness(0, 0, 5, 5)
            };
            popup.IsOpen = true;
        }

        public static Button MenuButton(string text, Action click, string icon = null)
        {
            var b = new Button { Style = BtnStyle, Height = 24, Margin = new Thickness(2, 0, 2, 1), Content = SmallContent(icon ?? "IconBlank", text, false) };
            b.Click += (s, e) => click();
            return b;
        }
    }
}
