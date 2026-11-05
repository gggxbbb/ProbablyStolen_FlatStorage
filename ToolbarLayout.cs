using System;
using System.Collections.Generic;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class ToolbarLayoutResult
    {
        public IReadOnlyList<Box> Primary { get; }
        public IReadOnlyList<Box> Secondary { get; }
        public Box Status { get; }
        public float Height { get; }

        internal ToolbarLayoutResult(Box[] primary, Box[] secondary, Box status, float height)
        {
            Primary = Array.AsReadOnly(primary);
            Secondary = Array.AsReadOnly(secondary);
            Status = status;
            Height = height;
        }
    }

    internal static class ToolbarLayout
    {
        public const float Padding = 5;
        public const float Gap = 4;
        public const float ControlHeight = 20;
        public const float StatusMinimumWidth = 80;

        public static ToolbarLayoutResult Compute(float width, IReadOnlyList<float> primaryWidths,
            IReadOnlyList<float> secondaryWidths)
        {
            if (!CanvasMath.Finite(width) || width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (primaryWidths == null) throw new ArgumentNullException(nameof(primaryWidths));
            if (secondaryWidths == null) throw new ArgumentNullException(nameof(secondaryWidths));

            float padding = Math.Min(Padding, width / 4);
            float right = width - padding;
            float available = right - padding;
            float x = padding, y = padding;
            var primary = Place(primaryWidths, nameof(primaryWidths), padding, right, ref x, ref y);
            if (primary.Length > 0 && secondaryWidths.Count > 0)
            {
                x = padding;
                y += ControlHeight + Gap;
            }
            var secondary = Place(secondaryWidths, nameof(secondaryWidths), padding, right, ref x, ref y);
            bool hasControls = primary.Length > 0 || secondary.Length > 0;
            float remaining = right - x;
            if (hasControls && remaining < Math.Min(StatusMinimumWidth, available))
            {
                x = padding;
                y += ControlHeight + Gap;
                remaining = available;
            }
            var status = new Box(x, y, remaining, ControlHeight);
            return new ToolbarLayoutResult(primary, secondary, status, y + ControlHeight + padding);
        }

        private static Box[] Place(IReadOnlyList<float> widths, string parameterName, float padding,
            float right, ref float x, ref float y)
        {
            var boxes = new Box[widths.Count];
            float available = right - padding;
            for (int i = 0; i < widths.Count; i++)
            {
                float requested = widths[i];
                if (!CanvasMath.Finite(requested) || requested <= 0)
                    throw new ArgumentOutOfRangeException(parameterName, "Control widths must be finite and positive.");
                float controlWidth = Math.Min(requested, available);
                if (x > padding && controlWidth > right - x)
                {
                    x = padding;
                    y += ControlHeight + Gap;
                }
                boxes[i] = new Box(x, y, controlWidth, ControlHeight);
                x += controlWidth + Gap;
            }
            return boxes;
        }
    }
}
