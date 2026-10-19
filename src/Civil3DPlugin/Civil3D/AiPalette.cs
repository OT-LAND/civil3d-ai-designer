using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.Windows;

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
            }
            _view?.Refresh();
            _set.Visible = true;
        }

        private sealed class StatusView : UserControl
        {
            private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

            public StatusView()
            {
                var refresh = new Button { Content = "Check again", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
                refresh.Click += (_, _) => Refresh();

                var panel = new StackPanel { Margin = new Thickness(10) };
                panel.Children.Add(new TextBlock { Text = PluginInfo.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
                panel.Children.Add(_status);
                panel.Children.Add(refresh);
                panel.Children.Add(new TextBlock
                {
                    Text = "Describe your design here in a coming version: spaces, levels, materials and planting.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Margin = new Thickness(0, 12, 0, 0),
                });
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            public void Refresh() => _status.Text = string.Join(Environment.NewLine, Commands.StatusLines());
        }
    }
}
