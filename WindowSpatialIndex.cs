using System;
using System.Collections.Generic;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class WindowSpatialIndex
    {
        private const float CellSize = 512;
        private const long MaximumCells = 64;
        private readonly Dictionary<(int X, int Y), HashSet<WindowLease>> cells = new Dictionary<(int, int), HashSet<WindowLease>>();
        private readonly Dictionary<WindowLease, Box> bounds = new Dictionary<WindowLease, Box>();
        private readonly HashSet<WindowLease> large = new HashSet<WindowLease>();

        private static (int X, int Y, int Right, int Bottom) Range(Box box) =>
            (checked((int)Math.Floor(box.X / CellSize)), checked((int)Math.Floor(box.Y / CellSize)),
             checked((int)Math.Floor((box.X + box.Width) / CellSize)), checked((int)Math.Floor((box.Y + box.Height) / CellSize)));
        private static long Count((int X, int Y, int Right, int Bottom) range)
        {
            long width = (long)range.Right - range.X + 1, height = (long)range.Bottom - range.Y + 1;
            return width > long.MaxValue / height ? long.MaxValue : width * height;
        }

        internal void Set(WindowLease lease)
        {
            var box = lease.Bounds;
            if (bounds.TryGetValue(lease, out var old) && Same(old, box)) return;
            Remove(lease);
            bounds.Add(lease, box);
            var range = Range(box);
            if (Count(range) > MaximumCells) { large.Add(lease); return; }
            for (int x = range.X; x <= range.Right; x++)
                for (int y = range.Y; y <= range.Bottom; y++)
                {
                    var key = (x, y);
                    if (!cells.TryGetValue(key, out var entries)) cells.Add(key, entries = new HashSet<WindowLease>());
                    entries.Add(lease);
                }
        }

        internal void Remove(WindowLease lease)
        {
            if (!bounds.TryGetValue(lease, out var old)) return;
            bounds.Remove(lease);
            if (large.Remove(lease)) return;
            var range = Range(old);
            for (int x = range.X; x <= range.Right; x++)
                for (int y = range.Y; y <= range.Bottom; y++)
                {
                    var key = (x, y);
                    var entries = cells[key]; entries.Remove(lease);
                    if (entries.Count == 0) cells.Remove(key);
                }
        }

        internal void Query(Box view, HashSet<WindowLease> result)
        {
            result.Clear();
            var range = Range(view);
            if (Count(range) > Math.Max(MaximumCells, cells.Count * 2L))
            {
                foreach (var pair in bounds) if (pair.Value.Intersects(view)) result.Add(pair.Key);
                return;
            }
            for (int x = range.X; x <= range.Right; x++)
                for (int y = range.Y; y <= range.Bottom; y++)
                    if (cells.TryGetValue((x, y), out var entries))
                        foreach (var lease in entries) if (bounds[lease].Intersects(view)) result.Add(lease);
            foreach (var lease in large) if (bounds[lease].Intersects(view)) result.Add(lease);
        }

        internal void Clear() { cells.Clear(); bounds.Clear(); large.Clear(); }
        internal static bool Same(Box a, Box b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
        internal static Box Expand(Box box, float margin) => new Box(box.X - margin, box.Y - margin, box.Width + 2 * margin, box.Height + 2 * margin);
    }
}
