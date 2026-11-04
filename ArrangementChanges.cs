using System;
using System.Collections.Generic;
using System.Linq;

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
            ReferenceEquals(Inventory, other.Inventory) && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    internal sealed class ArrangementChanges
    {
        private ArrangementLocation[] previous = Array.Empty<ArrangementLocation>();
        internal bool Pending { get; private set; }
        internal void Observe(IEnumerable<ArrangementLocation> locations)
        {
            var current = locations.OrderBy(l => l.Id, StringComparer.Ordinal).ToArray();
            if (!previous.SequenceEqual(current)) Pending = true;
            previous = current;
        }
        internal bool CanApply(bool enabled, bool dragging, bool panning) => enabled && Pending && !dragging && !panning;
        internal void Request() => Pending = true;
        internal void Applied() => Pending = false;
    }
}
