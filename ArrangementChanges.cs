using System;
using System.Collections.Generic;

namespace ProbablyStolen_FlatStorage
{
    internal readonly struct ArrangementLocation : IEquatable<ArrangementLocation>
    {
        internal readonly string Id, Parent;
        internal readonly object Inventory;
        internal readonly int X, Y;
        internal readonly float Width, Height;
        internal ArrangementLocation(string id, string parent, object inventory, int x, int y, float width, float height)
        { Id = id; Parent = parent; Inventory = inventory; X = x; Y = y; Width = width; Height = height; }
        public bool Equals(ArrangementLocation other) => Id == other.Id && Parent == other.Parent &&
            ReferenceEquals(Inventory, other.Inventory) && Width == other.Width && Height == other.Height;
    }

    internal sealed class ArrangementChanges
    {
        private Dictionary<string, ArrangementLocation> previous = new Dictionary<string, ArrangementLocation>(StringComparer.Ordinal);
        private Dictionary<string, ArrangementLocation> current = new Dictionary<string, ArrangementLocation>(StringComparer.Ordinal);
        internal bool Pending { get; private set; }
        internal void Observe(IEnumerable<ArrangementLocation> locations)
        {
            current.Clear();
            foreach (var location in locations)
            {
                current.Add(location.Id, location);
                if (!previous.TryGetValue(location.Id, out var old) || !old.Equals(location)) Pending = true;
            }
            if (previous.Count != current.Count) Pending = true;
            var buffer = previous; previous = current; current = buffer;
        }
        internal bool CanApply(bool enabled, bool dragging, bool panning) => enabled && Pending && !dragging && !panning;
        internal void Request() => Pending = true;
        internal void Applied() => Pending = false;
    }
}
