using System;
using System.Windows.Input;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI.Civil3DPlugin.Civil3D
{
    /// <summary>"AI Landscape" ribbon tab with one "Designer" panel. Safe to call repeatedly.</summary>
    internal static class RibbonSetup
    {
        public static void AddTab()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null || ribbon.FindTab(PluginInfo.RibbonTabId) != null) return;

            var source = new RibbonPanelSource { Title = "Designer" };
            source.Items.Add(Button("Designer\nPalette", PluginInfo.PaletteCommand, "Open the AI Landscape Designer palette."));
            source.Items.Add(Button("Hello", PluginInfo.HelloCommand, "Check that the plugin, Civil 3D and Claude are ready."));

            var tab = new RibbonTab { Title = PluginInfo.RibbonTabTitle, Id = PluginInfo.RibbonTabId };
            tab.Panels.Add(new RibbonPanel { Source = source });
            ribbon.Tabs.Add(tab);
        }

        private static RibbonButton Button(string text, string command, string tip) => new RibbonButton
        {
            Text = text,
            ShowText = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = tip,
            CommandParameter = command,
            CommandHandler = SendCommand.Instance,
        };

        /// <summary>Runs the button's command on the command line, like typing it.</summary>
        private sealed class SendCommand : ICommand
        {
            public static readonly SendCommand Instance = new SendCommand();

            public event EventHandler? CanExecuteChanged { add { } remove { } }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                var command = parameter switch
                {
                    RibbonCommandItem item => item.CommandParameter as string,
                    string s => s,
                    _ => null,
                };
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null || string.IsNullOrEmpty(command)) return;
                // Leading underscore = English command name; trailing space = Enter.
                doc.SendStringToExecute("_" + command + " ", true, false, true);
            }
        }
    }
}
