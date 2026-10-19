using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CivilAI.Civil3DPlugin.Civil3D.PluginApp))]
[assembly: CommandClass(typeof(CivilAI.Civil3DPlugin.Civil3D.Commands))]

namespace CivilAI.Civil3DPlugin.Civil3D
{
    /// <summary>
    /// Entry point: Civil 3D calls Initialize when the bundle loads the DLL at startup.
    /// Adds the "AI Landscape" ribbon tab once the ribbon exists, and again after a workspace switch.
    /// Never throws: a failing plugin must not disturb Civil 3D startup.
    /// </summary>
    public sealed class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                if (ComponentManager.Ribbon != null)
                    RibbonSetup.AddTab();
                else
                    ComponentManager.ItemInitialized += OnRibbonItemInitialized;

                AcApp.SystemVariableChanged += OnSystemVariableChanged;

                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n{PluginInfo.Name} {PluginInfo.Version} loaded. Type {PluginInfo.HelloCommand} or {PluginInfo.PaletteCommand}.\n");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n{PluginInfo.Name}: start-up problem: {ex.Message}\n");
            }
        }

        public void Terminate()
        {
            ComponentManager.ItemInitialized -= OnRibbonItemInitialized;
            AcApp.SystemVariableChanged -= OnSystemVariableChanged;
        }

        private static void OnRibbonItemInitialized(object? sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnRibbonItemInitialized;
            RibbonSetup.AddTab();
        }

        // A workspace switch rebuilds the ribbon and drops tabs added in code.
        private static void OnSystemVariableChanged(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
                RibbonSetup.AddTab();
        }
    }
}
