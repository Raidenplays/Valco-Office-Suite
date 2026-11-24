// The parts around the ribbon that follow the colour scheme too: the window frame and title,
// ribbon combo boxes (font, size...), Valco Word's ruler and the scroll bars.
// Blue, Silver, Black and Windows Vista keep the Vista glass frame and the standard controls, like Office 2007;
// Dreamweaver CS6, Modern Dark and 2077 get their own frame, fields, ruler and scroll bars.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace Valco.UI
{
    public static class ChromeTheme
    {
        static Color C(string h) => (Color)ColorConverter.ConvertFromString(h);
        static SolidColorBrush S(string h) { var b = new SolidColorBrush(C(h)); b.Freeze(); return b; }
        static LinearGradientBrush V(string a, string b) { var g = new LinearGradientBrush(C(a), C(b), 90); g.Freeze(); return g; }

        static Style _scroll;

        public static void Apply(string scheme)
        {
            var app = Application.Current;
            if (app == null) return;
            var r = app.Resources;
            bool custom = true;
            switch (scheme)
            {
                case "Dreamweaver CS6":
                    r["Rb.Frame"] = V("#FF5A5A5A", "#FF3C3C3C"); r["Rb.FrameInactive"] = V("#FF6E6E6E", "#FF575757");
                    r["Rb.FrameText"] = S("#FFEDEDED"); r["Rb.FrameBorder"] = S("#FF1E1E1E");
                    r["Rb.Field"] = S("#FFFFFFFF"); r["Rb.FieldText"] = S("#FF1E1E1E"); r["Rb.FieldBorder"] = S("#FF8E8E8E");
                    r["Ruler.Bar"] = V("#FFE2E2E2", "#FFCCCCCC"); r["Ruler.Margin"] = S("#FFA8A8A8"); r["Ruler.Page"] = S("#FFFFFFFF");
                    r["Ruler.Tick"] = S("#FF333333"); r["Ruler.Edge"] = S("#FF8E8E8E");
                    r["Grid.Header"] = V("#FFEEEEEE", "#FFD8D8D8"); r["Grid.HeaderText"] = S("#FF333333"); r["Grid.HeaderLine"] = S("#FFA8A8A8");
                    r["Scroll.Track"] = S("#FFD6D6D6"); r["Scroll.Thumb"] = S("#FF9A9A9A"); r["Scroll.ThumbHover"] = S("#FF7DA2CE"); r["Scroll.Arrow"] = S("#FF333333");
                    break;
                case "Modern Dark":
                    r["Rb.Frame"] = V("#FF22252B", "#FF1A1C21"); r["Rb.FrameInactive"] = V("#FF2C2F36", "#FF23262C");
                    r["Rb.FrameText"] = S("#FFD6D9E0"); r["Rb.FrameBorder"] = S("#FF0E0F12");
                    r["Rb.Field"] = S("#FF1F2127"); r["Rb.FieldText"] = S("#FFD6D9E0"); r["Rb.FieldBorder"] = S("#FF3A3F4B");
                    r["Ruler.Bar"] = V("#FF2C2F37", "#FF25282F"); r["Ruler.Margin"] = S("#FF1A1C21"); r["Ruler.Page"] = S("#FF3A3F4B");
                    r["Ruler.Tick"] = S("#FF9AA1AE"); r["Ruler.Edge"] = S("#FF3A3F4B");
                    r["Grid.Header"] = V("#FF2C2F37", "#FF25282F"); r["Grid.HeaderText"] = S("#FFC9CED8"); r["Grid.HeaderLine"] = S("#FF3A3F4B");
                    r["Scroll.Track"] = S("#FF1A1C21"); r["Scroll.Thumb"] = S("#FF3F444F"); r["Scroll.ThumbHover"] = S("#FF36C2B4"); r["Scroll.Arrow"] = S("#FF9AA1AE");
                    break;
                case "2077":
                    r["Rb.Frame"] = V("#FF120C45", "#FF0B082E"); r["Rb.FrameInactive"] = V("#FF1A1250", "#FF120C45");
                    r["Rb.FrameText"] = S("#FF2DE2E6"); r["Rb.FrameBorder"] = S("#FFFF2592");
                    r["Rb.Field"] = S("#FF0E0A33"); r["Rb.FieldText"] = S("#FFEAE2FF"); r["Rb.FieldBorder"] = S("#FFFF2592");
                    r["Ruler.Bar"] = V("#FF21175E", "#FF160F45"); r["Ruler.Margin"] = S("#FF0B082E"); r["Ruler.Page"] = S("#FF2A1366");
                    r["Ruler.Tick"] = S("#FF2DE2E6"); r["Ruler.Edge"] = S("#FFFF2592");
                    r["Grid.Header"] = V("#FF21175E", "#FF160F45"); r["Grid.HeaderText"] = S("#FF2DE2E6"); r["Grid.HeaderLine"] = S("#FFFF2592");
                    r["Scroll.Track"] = S("#FF0B082E"); r["Scroll.Thumb"] = S("#FF3B1670"); r["Scroll.ThumbHover"] = S("#FFFF2592"); r["Scroll.Arrow"] = S("#FF2DE2E6");
                    break;
                default:
                    custom = false;
                    // the Vista glass frame from the app's window template
                    r["Rb.Frame"] = app.TryFindResource("GlassFrameBrush") ?? S("#FFA7C0DD");
                    r["Rb.FrameInactive"] = app.TryFindResource("GlassFrameInactiveBrush") ?? S("#FFCFDAE6");
                    r["Rb.FrameText"] = S("#FF0B0F14"); r["Rb.FrameBorder"] = S("#FF26323F");
                    r["Rb.Field"] = S("#FFFFFFFF"); r["Rb.FieldText"] = S("#FF000000"); r["Rb.FieldBorder"] = S("#FFABADB3");
                    r["Ruler.Bar"] = V("#FFF4F7FB", "#FFDCE5F0"); r["Ruler.Margin"] = S("#FFA9BFD9"); r["Ruler.Page"] = S("#FFFFFFFF");
                    r["Ruler.Tick"] = S("#FF505A66"); r["Ruler.Edge"] = S("#FF8DA3BE");
                    r.Remove("Grid.Header"); r.Remove("Grid.HeaderText"); r.Remove("Grid.HeaderLine");
                    break;
            }

            // themed scroll bars only for the custom schemes; the Office ones keep Windows' own
            if (custom) { try { _scroll ??= ScrollStyle(); } catch { } if (_scroll != null) r[typeof(ScrollBar)] = _scroll; }
            else r.Remove(typeof(ScrollBar));
        }

        /// <summary>Ribbon combo boxes: field colours, including the drop-down list, follow the scheme.</summary>
        public static void Field(ComboBox c)
        {
            c.SetResourceReference(Control.BackgroundProperty, "Rb.Field");
            c.SetResourceReference(Control.ForegroundProperty, "Rb.FieldText");
            c.SetResourceReference(Control.BorderBrushProperty, "Rb.FieldBorder");
            void Sync()
            {
                // the drop-down and the editable box draw with the system window colours
                if (Application.Current?.TryFindResource("Rb.Field") is Brush bg) { c.Resources[SystemColors.WindowBrushKey] = bg; c.Resources[SystemColors.ControlLightLightBrushKey] = bg; }
                if (Application.Current?.TryFindResource("Rb.FieldText") is Brush fg) { c.Resources[SystemColors.WindowTextBrushKey] = fg; c.Resources[SystemColors.ControlTextBrushKey] = fg; }
            }
            Sync();
            OfficeTheme.Changed += Sync;
            c.Unloaded += (s, e) => OfficeTheme.Changed -= Sync;
            c.Loaded += (s, e) => { OfficeTheme.Changed -= Sync; OfficeTheme.Changed += Sync; Sync(); };
        }

        static Style ScrollStyle()
        {
            const string xaml = @"
<Style TargetType='ScrollBar' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style.Resources>
    <Style x:Key='Pg' TargetType='RepeatButton'>
      <Setter Property='Focusable' Value='False'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='Ar' TargetType='RepeatButton'>
      <Setter Property='Focusable' Value='False'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
        <Border x:Name='B' Background='Transparent'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='{DynamicResource Scroll.Thumb}'/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='Th' TargetType='Thumb'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
        <Border x:Name='T' Background='{DynamicResource Scroll.Thumb}' CornerRadius='3' Margin='2'/>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='T' Property='Background' Value='{DynamicResource Scroll.ThumbHover}'/></Trigger>
          <Trigger Property='IsDragging' Value='True'><Setter TargetName='T' Property='Background' Value='{DynamicResource Scroll.ThumbHover}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
  </Style.Resources>
  <Setter Property='Background' Value='{DynamicResource Scroll.Track}'/>
  <Setter Property='Width' Value='17'/>
  <Setter Property='MinWidth' Value='17'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
    <Grid Background='{TemplateBinding Background}'>
      <Grid.RowDefinitions><RowDefinition Height='17'/><RowDefinition Height='*'/><RowDefinition Height='17'/></Grid.RowDefinitions>
      <RepeatButton Grid.Row='0' Style='{StaticResource Ar}' Command='ScrollBar.LineUpCommand'><Path Data='M0,4 L4,0 L8,4 Z' Fill='{DynamicResource Scroll.Arrow}'/></RepeatButton>
      <Track x:Name='PART_Track' Grid.Row='1' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource Pg}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource Pg}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Style='{StaticResource Th}'/></Track.Thumb>
      </Track>
      <RepeatButton Grid.Row='2' Style='{StaticResource Ar}' Command='ScrollBar.LineDownCommand'><Path Data='M0,0 L8,0 L4,4 Z' Fill='{DynamicResource Scroll.Arrow}'/></RepeatButton>
    </Grid>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers>
    <Trigger Property='Orientation' Value='Horizontal'>
      <Setter Property='Width' Value='Auto'/>
      <Setter Property='MinWidth' Value='0'/>
      <Setter Property='Height' Value='17'/>
      <Setter Property='MinHeight' Value='17'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
        <Grid Background='{TemplateBinding Background}'>
          <Grid.ColumnDefinitions><ColumnDefinition Width='17'/><ColumnDefinition Width='*'/><ColumnDefinition Width='17'/></Grid.ColumnDefinitions>
          <RepeatButton Grid.Column='0' Style='{StaticResource Ar}' Command='ScrollBar.LineLeftCommand'><Path Data='M4,0 L0,4 L4,8 Z' Fill='{DynamicResource Scroll.Arrow}'/></RepeatButton>
          <Track x:Name='PART_Track' Grid.Column='1'>
            <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource Pg}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource Pg}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Style='{StaticResource Th}'/></Track.Thumb>
          </Track>
          <RepeatButton Grid.Column='2' Style='{StaticResource Ar}' Command='ScrollBar.LineRightCommand'><Path Data='M0,0 L4,4 L0,8 Z' Fill='{DynamicResource Scroll.Arrow}'/></RepeatButton>
        </Grid>
      </ControlTemplate></Setter.Value></Setter>
    </Trigger>
  </Style.Triggers>
</Style>";
            var st = (Style)XamlReader.Parse(xaml);
            return st;
        }
    }
}
