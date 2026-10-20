namespace CivilAI.Civil3DPlugin
{
    /// <summary>
    /// Autodesk-free constants. Everything that needs AutoCAD/Civil 3D lives in Civil3D/ and is
    /// compiled only where Civil 3D 2027 is installed (see the .csproj).
    /// </summary>
    public static class PluginInfo
    {
        public const string Name = "Civil 3D AI Landscape Designer";
        public const string RibbonTabId = "CIVILAI_TAB";
        public const string RibbonTabTitle = "AI Landscape";
        public const string HelloCommand = "AIHELLO";
        public const string PaletteCommand = "AIPALETTE";

        /// <summary>Plugin assembly version (major.minor.build).</summary>
        public static string Version =>
            typeof(PluginInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
