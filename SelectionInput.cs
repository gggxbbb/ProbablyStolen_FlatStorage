using System;

namespace ProbablyStolen_FlatStorage
{
    internal enum SelectionPointerRegion { Outside, Blank, Inventory, Item, Header, Control }

    internal static class SelectionInput
    {
        internal static bool AllowsItems(SelectionPointerRegion region, bool obstructed, bool typing,
            bool panning, bool movingGroup, bool spaceHeld) =>
            !obstructed && !typing && !panning && !movingGroup && !spaceHeld &&
            (region == SelectionPointerRegion.Blank || region == SelectionPointerRegion.Inventory || region == SelectionPointerRegion.Item);

        internal static bool AllowsBand(SelectionPointerRegion region) =>
            region == SelectionPointerRegion.Blank || region == SelectionPointerRegion.Inventory;

        internal static bool AllowsDrop(SelectionPointerRegion region, bool nativeTabTarget, bool obstructed,
            bool typing, bool panning, bool movingGroup, bool spaceHeld) =>
            AllowsItems(region, obstructed, typing, panning, movingGroup, spaceHeld) ||
            nativeTabTarget && !obstructed && !typing && !panning && !movingGroup && !spaceHeld;

        internal static float RelativeScale(float canvasScale, float overlayScale)
        {
            if (!CanvasMath.Finite(canvasScale) || !CanvasMath.Finite(overlayScale) || canvasScale <= 0 || overlayScale <= 0)
                throw new ArgumentOutOfRangeException(nameof(canvasScale), "Canvas and overlay scales must be positive and finite.");
            return canvasScale / overlayScale;
        }

        internal static Box Clip(Box band, Box viewport)
        {
            float left = Math.Max(band.X, viewport.X), bottom = Math.Max(band.Y, viewport.Y);
            float right = Math.Min(band.X + band.Width, viewport.X + viewport.Width);
            float top = Math.Min(band.Y + band.Height, viewport.Y + viewport.Height);
            return new Box(left, bottom, Math.Max(0, right - left), Math.Max(0, top - bottom));
        }
    }
}
