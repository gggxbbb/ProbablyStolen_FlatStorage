using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ProbablyStolen_FlatStorage
{
    public sealed class GroupPosition
    {
        public string Id;
        public float X;
        public float Y;
    }

    public sealed class CanvasState
    {
        public int Version = 1;
        public float Zoom = 1;
        public float PanX = 16;
        public float PanY = 16;
        public List<GroupPosition> Groups = new List<GroupPosition>();

        public string Encode()
        {
            Validate();
            return JsonConvert.SerializeObject(this);
        }

        public static CanvasState Decode(string json)
        {
            if (json == null) return new CanvasState();
            var state = JsonConvert.DeserializeObject<CanvasState>(json)
                ?? throw new InvalidOperationException("画布布局数据为空。");
            state.Validate();
            return state;
        }

        public void Validate()
        {
            if (Version != 1 || !CanvasMath.Finite(Zoom) || Zoom < .25f || Zoom > 2 ||
                !CanvasMath.Finite(PanX) || !CanvasMath.Finite(PanY) || Groups == null ||
                Groups.Any(g => g == null || string.IsNullOrEmpty(g.Id) || !CanvasMath.Finite(g.X) || !CanvasMath.Finite(g.Y)) ||
                Groups.Select(g => g.Id).Distinct().Count() != Groups.Count)
                throw new InvalidOperationException("画布布局数据格式或数值无效。");
        }

        public void ZoomAt(float x, float y, float zoom)
        {
            zoom = Math.Max(.25f, Math.Min(2, zoom));
            PanX = x - (x - PanX) / Zoom * zoom;
            PanY = y - (y - PanY) / Zoom * zoom;
            Zoom = zoom;
        }
    }

    public readonly struct Box
    {
        public readonly float X, Y, Width, Height;
        public Box(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
        public bool Intersects(Box b) => X < b.X + b.Width && X + Width > b.X && Y < b.Y + b.Height && Y + Height > b.Y;
    }

    public static class CanvasMath
    {
        public static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);

        public static List<Box> Arrange(IReadOnlyList<Box> sizes, float rowWidth, float gap = 24)
        {
            var result = new List<Box>();
            float x = 0, y = 0, rowHeight = 0;
            foreach (var size in sizes)
            {
                if (x > 0 && x + size.Width > rowWidth) { x = 0; y += rowHeight + gap; rowHeight = 0; }
                result.Add(new Box(x, y, size.Width, size.Height));
                x += size.Width + gap;
                rowHeight = Math.Max(rowHeight, size.Height);
            }
            return result;
        }

        public static void Fit(CanvasState state, IReadOnlyList<Box> boxes, float width, float height)
        {
            if (boxes.Count == 0) { state.Zoom = 1; state.PanX = state.PanY = 16; return; }
            float left = boxes.Min(b => b.X), top = boxes.Min(b => b.Y);
            float w = boxes.Max(b => b.X + b.Width) - left, h = boxes.Max(b => b.Y + b.Height) - top;
            state.Zoom = Math.Max(.25f, Math.Min(2, Math.Min((width - 32) / Math.Max(1, w), (height - 32) / Math.Max(1, h))));
            state.PanX = (width - w * state.Zoom) / 2 - left * state.Zoom;
            state.PanY = (height - h * state.Zoom) / 2 - top * state.Zoom;
        }
    }
}
