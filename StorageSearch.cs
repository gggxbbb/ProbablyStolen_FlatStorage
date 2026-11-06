using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal static class StorageSearch
    {
        internal static bool Matches(string name, string identifier, IEnumerable<string> categories,
            string query, string category)
        {
            if (!string.IsNullOrEmpty(category) && !categories.Contains(category, StringComparer.Ordinal)) return false;
            query = (query ?? "").Trim();
            return query.Length == 0 || Contains(name, query) || Contains(identifier, query) || categories.Any(t => Contains(t, query));
        }

        private static bool Contains(string text, string query) =>
            text != null && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    internal readonly struct MiniMapProjection
    {
        internal readonly float Scale, OffsetX, OffsetY;
        internal MiniMapProjection(IReadOnlyList<Box> groups, float width, float height)
        {
            if (width <= 0 || height <= 0 || groups.Count == 0) throw new ArgumentException("Invalid map bounds.");
            float left = groups.Min(b => b.X), top = groups.Min(b => b.Y);
            float extentX = Math.Max(1, groups.Max(b => b.X + b.Width) - left);
            float extentY = Math.Max(1, groups.Max(b => b.Y + b.Height) - top);
            Scale = Math.Min(width / extentX, height / extentY);
            OffsetX = (width - extentX * Scale) / 2 - left * Scale;
            OffsetY = (height - extentY * Scale) / 2 - top * Scale;
        }
        internal Box Project(Box box) => new Box(box.X * Scale + OffsetX, box.Y * Scale + OffsetY,
            box.Width * Scale, box.Height * Scale);
        internal float CanvasX(float x) => (x - OffsetX) / Scale;
        internal float CanvasY(float y) => (y - OffsetY) / Scale;
    }
}
