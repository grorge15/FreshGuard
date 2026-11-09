using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public static class BattleRouteSampler
    {
        public static IReadOnlyList<Vector3> SampleDrops(IReadOnlyList<Vector3> points, Vector3 target, int count, float spacing)
        {
            EntityValidation.RequirePositive(count, nameof(count));
            EntityValidation.RequireFiniteNonNegative(spacing, nameof(spacing));
            if (spacing <= 0f) throw new ArgumentOutOfRangeException(nameof(spacing));
            if (points == null || points.Count < 2) return Array.Empty<Vector3>();
            var lengths = new double[points.Count];
            var nearest = double.PositiveInfinity; var start = 0d;
            for (var i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                var dx = (double)b.x - a.x; var dy = (double)b.y - a.y;
                var squared = dx * dx + dy * dy;
                var length = Math.Sqrt(squared);
                lengths[i] = lengths[i - 1] + length;
                var t = squared == 0d ? 0d : Math.Max(0d, Math.Min(1d, ((target.x - a.x) * dx + (target.y - a.y) * dy) / squared));
                var ox = a.x + t * dx - target.x; var oy = a.y + t * dy - target.y;
                var distance = ox * ox + oy * oy;
                var along = lengths[i - 1] + t * length;
                if (distance < nearest || distance == nearest && along < start) { nearest = distance; start = along; }
            }
            var drops = new List<Vector3>();
            var segment = 1;
            for (var i = 0; i < count; i++)
            {
                var distance = start + (double)i * spacing;
                if (distance > lengths[lengths.Length - 1] + 0.0000001d) break;
                while (segment < points.Count - 1 && lengths[segment] < distance) segment++;
                var length = lengths[segment] - lengths[segment - 1];
                var t = length <= 0d ? 0f : (float)((distance - lengths[segment - 1]) / length);
                drops.Add(Vector3.Lerp(points[segment - 1], points[segment], t));
            }
            return drops.AsReadOnly();
        }
    }
}
