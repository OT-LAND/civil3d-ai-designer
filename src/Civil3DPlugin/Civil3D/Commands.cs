using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.Runtime;
using CivilAI.Core.Claude;
using CivilAI.Core.Diagnostics;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI.Civil3DPlugin.Civil3D
{
    /// <summary>Command-line commands. Group "CIVILAI" so they can be listed and unloaded together.</summary>
    public sealed class Commands
    {
        /// <summary>AIHELLO: proves the plugin loaded and says whether Civil 3D and Claude are ready.</summary>
        [CommandMethod("CIVILAI", PluginInfo.HelloCommand, CommandFlags.Modal)]
        public void Hello()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;
            foreach (var line in StatusLines())
                ed.WriteMessage("\n" + line);
            ed.WriteMessage("\n");
        }

        /// <summary>AIPALETTE: opens the designer palette.</summary>
        [CommandMethod("CIVILAI", PluginInfo.PaletteCommand, CommandFlags.Modal)]
        public void Palette() => AiPalette.Show();

        internal static IReadOnlyList<string> StatusLines() =>
            HelloReport.Build(
                PluginInfo.Version,
                AcApp.Version.ToString(),
                Civil3DReady(),
                () => ClaudeSettings.Load(),
                ClaudeSettings.DefaultSettingsPath);

        private static bool Civil3DReady()
        {
            try { return HasCivilDocument(); }
            catch (System.Exception) { return false; } // plain AutoCAD: AeccDbMgd is not there
        }

#if CIVIL3D_API
        // Separate, non-inlined method so a missing Civil 3D assembly fails here, inside the try.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasCivilDocument() =>
            Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument != null;
#else
        // Cloud/CI type-check build only (no Civil 3D API on NuGet); never shipped in a bundle.
        private static bool HasCivilDocument() => false;
#endif
    }
}
