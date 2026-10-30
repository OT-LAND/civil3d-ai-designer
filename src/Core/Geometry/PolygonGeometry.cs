using System;
using System.Collections.Generic;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Geometry
{
    /// <summary>Plan geometry on polylines and polygons (metres). Closed polygons do not repeat the first point.</summary>
    public static class PolygonGeometry
    {
        /// <summary>Signed area by the shoelace formula; positive when counter-clockwise.</summary>
        public static double SignedArea(IReadOnlyList<Point2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0;
            double sum = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2.0;
        }

        public static double Length(IReadOnlyList<Point2> line, bool closed)
        {
            if (line == null || line.Count < 2) return 0;
            double len = 0;
            int n = closed ? line.Count : line.Count - 1;
            for (int i = 0; i < n; i++) len += Distance(line[i], line[(i + 1) % line.Count]);
            return len;
        }

        /// <summary>The point halfway along an open polyline.</summary>
        public static Point2 PointAtHalfLength(IReadOnlyList<Point2> line)
        {
            if (line == null || line.Count == 0) throw new ArgumentException("Line has no points.", nameof(line));
            double half = Length(line, closed: false) / 2.0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double d = Distance(line[i], line[i + 1]);
                if (d >= half && d > 0)
                {
                    double t = half / d;
                    return new Point2(line[i].X + t * (line[i + 1].X - line[i].X), line[i].Y + t * (line[i + 1].Y - line[i].Y));
                }
                half -= d;
            }
            return line[line.Count - 1];
        }

        /// <summary>True when <paramref name="p"/> is inside the closed <paramref name="polygon"/> (ray casting; boundary counts as either).</summary>
        public static bool Contains(IReadOnlyList<Point2> polygon, Point2 p)
        {
            if (polygon == null || polygon.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.Y > p.Y) != (b.Y > p.Y)
                    && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Splits the length of a polyline into curved and straight parts. A vertex is a smooth
        /// bend when its turn is above <paramref name="collinearDegrees"/> and at most
        /// <paramref name="smoothBendMaxDegrees"/>; a segment is curved when both its ends are
        /// smooth bends (an arc tessellated by the capture step). Straight runs into a corner,
        /// a single slight kink and open ends stay straight.
        /// </summary>
        public static (double Curved, double Straight) CurvedStraightLength(
            IReadOnlyList<Point2> line, bool closed, double collinearDegrees, double smoothBendMaxDegrees)
        {
            if (line == null || line.Count < 2) return (0, 0);
            int count = line.Count;
            var bend = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (!closed && (i == 0 || i == count - 1)) continue;
                var prev = line[(i - 1 + count) % count];
                var next = line[(i + 1) % count];
                double turn = TurnDegrees(prev, line[i], next);
                bend[i] = turn > collinearDegrees && turn <= smoothBendMaxDegrees;
            }

            double curved = 0, straight = 0;
            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % count;
                double d = Distance(line[i], line[j]);
                if (bend[i] && bend[j]) curved += d; else straight += d;
            }
            return (curved, straight);
        }

        /// <summary>Change of direction at <paramref name="at"/>, 0–180 degrees; 0 when a segment has no length.</summary>
        public static double TurnDegrees(Point2 prev, Point2 at, Point2 next)
        {
            double ax = at.X - prev.X, ay = at.Y - prev.Y, bx = next.X - at.X, by = next.Y - at.Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-12 || lb < 1e-12) return 0;
            double cos = Math.Max(-1, Math.Min(1, (ax * bx + ay * by) / (la * lb)));
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        public static double Distance(Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
