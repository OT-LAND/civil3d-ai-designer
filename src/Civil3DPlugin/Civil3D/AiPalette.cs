using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using CivilAI.Core.Ui;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI.Civil3DPlugin.Civil3D
{
    /// <summary>
    /// The designer palette (dockable PaletteSet hosting WPF). Empty shell for 0.3: shows the
    /// status from AIHELLO. Design inputs and preview arrive with 1.4.
    /// </summary>
    internal static class AiPalette
    {
        // Fixed id so Civil 3D remembers the palette's dock position between sessions.
        private static readonly Guid PaletteId = new Guid("C1A5E7D2-4B3F-4E8A-9D61-7F2B0A3C5E48");
        private static PaletteSet? _set;
        private static StatusView? _view;

        public static void Show()
        {
            if (_set == null)
            {
                _view = new StatusView();
                _set = new PaletteSet(PluginInfo.Name, PaletteId)
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new System.Drawing.Size(280, 240),
                };
                _set.AddVisual("Designer", _view);
                // Follow the AutoCAD colour theme when the user switches it (Options > Display).
                Autodesk.AutoCAD.ApplicationServices.Application.SystemVariableChanged += OnSystemVariableChanged;
            }
            ApplyTheme();
            _view?.Refresh();
            _set.Visible = true;
        }

        private static void OnSystemVariableChanged(object? sender, SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "COLORTHEME", StringComparison.OrdinalIgnoreCase))
                ApplyTheme();
        }

        // Bug 0.3a: WPF default text is black, and the palette background is dark in the dark theme.
        // Colours are set explicitly from COLORTHEME (0 = dark, 1 = light); the choice lives in Core.
        private static void ApplyTheme()
        {
            object? theme;
            try { theme = AcApp.GetSystemVariable("COLORTHEME"); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { theme = null; }
            _view?.Apply(PaletteTheme.ForColorTheme(theme));
        }

        private static Brush ToBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private sealed class StatusView : UserControl
        {
            private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            private readonly TextBlock _hint;

            public StatusView()
            {
                var refresh = new Button { Content = "Check again", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
                refresh.Click += (_, _) => Refresh();

                var panel = new StackPanel { Margin = new Thickness(10) };
                panel.Children.Add(new TextBlock { Text = PluginInfo.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
                panel.Children.Add(_status);
                panel.Children.Add(refresh);
                _hint = new TextBlock
                {
                    Text = "Describe your design here in a coming version: spaces, levels, materials and planting.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0),
                };
                panel.Children.Add(_hint);
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            /// <summary>Background and text colours; text blocks inherit Foreground, the button keeps its own style.</summary>
            public void Apply(PaletteColors colors)
            {
                Background = ToBrush(colors.Background);
                Foreground = ToBrush(colors.Foreground);
                _hint.Foreground = ToBrush(colors.Muted);
            }

            public void Refresh() => _status.Text = string.Join(Environment.NewLine, Commands.StatusLines());
        }
    }
}
