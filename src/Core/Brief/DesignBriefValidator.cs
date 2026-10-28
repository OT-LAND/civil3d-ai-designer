using System;
using System.Collections.Generic;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Brief
{
    /// <summary>
    /// Content checks for a Design Brief that the JSON Schema cannot express: unique ids, usable
    /// geometry, slider ranges, a complete global slider set (D15), and fields that only make
    /// sense on some tags (level on contours, entrances and elevation points; accessible on the
    /// walking network; species on trees). Messages are written for the designer.
    /// Issues reuse <see cref="IntentIssue"/> (JSON-style path + message).
    /// </summary>
    public static class DesignBriefValidator
    {
        public static IReadOnlyList<IntentIssue> Validate(DesignBrief brief)
        {
            if (brief is null) throw new ArgumentNullException(nameof(brief));
            var issues = new List<IntentIssue>();
            void Add(string path, string message) => issues.Add(new IntentIssue(path, message));

            if (brief.SchemaVersion != DesignBrief.CurrentSchemaVersion)
                Add("schemaVersion", $"Expected schema version \"{DesignBrief.CurrentSchemaVersion}\", got \"{brief.SchemaVersion}\".");
            if (brief.Units != DesignBrief.Metres)
                Add("units", $"The brief must be in metres (\"m\"), got \"{brief.Units}\". Convert drawing units when capturing.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            void CheckId(string path, string id)
            {
                if (string.IsNullOrWhiteSpace(id)) Add(path, "Id is missing.");
                else if (!ids.Add(id)) Add(path, $"Id \"{id}\" is used more than once.");
            }

            // Background references
            var bg = brief.Background ?? new BackgroundRefs();
            CheckRefs("background.survey", bg.Survey, Add);
            CheckRefs("background.parcel", bg.Parcel, Add);
            CheckRefs("background.structures", bg.Structures, Add);
            CheckRefs("background.roads", bg.Roads, Add);
            CheckRefs("background.utilities", bg.Utilities, Add);
            CheckRefs("background.orthophoto", bg.Orthophoto, Add);

            // Style
            var style = brief.Style ?? new StyleInput();
            if (style.Images.Count > StyleInput.MaxImages)
                Add("style.images", $"Up to {StyleInput.MaxImages} reference images; this brief has {style.Images.Count}.");
            CheckRefs("style.images", style.Images, Add);
            if (style.Prompt is not null && string.IsNullOrWhiteSpace(style.Prompt))
                Add("style.prompt", "The style prompt is empty; leave it out or write a few words.");

            // Global sliders: all nine set, each 0–100 (D15).
            var global = brief.Character ?? new Character();
            foreach (var (name, value) in global.Sliders())
            {
                if (value is null) Add($"character.{name}", $"The global \"{name}\" slider is not set.");
                else CheckSlider($"character.{name}", value.Value, Add);
            }

            // Zones
            if (brief.Zones.Count == 0)
                Add("zones", "Pick at least one design zone (closed polygon).");
            for (int i = 0; i < brief.Zones.Count; i++)
            {
                var z = brief.Zones[i];
                var p = $"zones[{i}]";
                CheckId($"{p}.id", z.Id);
                DesignIntentValidator.CheckPolygon($"{p}.outline", z.Outline, "zone outline", Add);
                if (z.Character is Character over)
                    foreach (var (name, value) in over.Sliders())
                        if (value is int v) CheckSlider($"{p}.character.{name}", v, Add);
            }

            // Curves
            for (int i = 0; i < brief.Curves.Count; i++)
            {
                var c = brief.Curves[i];
                var p = $"curves[{i}]";
                CheckId($"{p}.id", c.Id);
                if (c.Points.Count < 2) Add($"{p}.points", "A curve needs at least 2 points.");
                else DesignIntentValidator.CheckPolyline($"{p}.points", c.Points, "curve", Add);

                if (c.Level is double level)
                {
                    if (c.Tag != CurveTag.Contour) Add($"{p}.level", "Only a contour can have a level.");
                    else DesignIntentValidator.CheckFinite($"{p}.level", level, "Contour level", Add);
                }
                if (c.Accessible is not null && c.Tag != CurveTag.WalkingNetwork)
                    Add($"{p}.accessible", "Only the walking network can be marked accessible or not accessible.");
            }

            // Points
            for (int i = 0; i < brief.Points.Count; i++)
            {
                var pt = brief.Points[i];
                var p = $"points[{i}]";
                CheckId($"{p}.id", pt.Id);
                DesignIntentValidator.CheckPoint($"{p}.at", pt.At, Add);

                if (pt.Level is double level)
                {
                    if (pt.Tag != PointTag.Elevation && pt.Tag != PointTag.Entrance)
                        Add($"{p}.level", "Only an elevation point or an entrance can have a level.");
                    else DesignIntentValidator.CheckFinite($"{p}.level", level, "Level", Add);
                }
                else if (pt.Tag == PointTag.Elevation)
                {
                    Add($"{p}.level", "An elevation point needs a level.");
                }

                if (pt.Species is not null)
                {
                    if (pt.Tag != PointTag.Tree) Add($"{p}.species", "Only a tree point can have a species.");
                    else if (string.IsNullOrWhiteSpace(pt.Species)) Add($"{p}.species", "The species is empty; leave it out to let the AI choose.");
                }
            }

            return issues;
        }

        private static void CheckSlider(string path, int value, Action<string, string> add)
        {
            if (value < Character.Min || value > Character.Max)
                add(path, $"Slider value must be between {Character.Min} and {Character.Max}, got {value}.");
        }

        private static void CheckRefs(string path, List<string> refs, Action<string, string> add)
        {
            for (int i = 0; i < refs.Count; i++)
                if (string.IsNullOrWhiteSpace(refs[i])) add($"{path}[{i}]", "Empty reference.");
        }
    }
}
