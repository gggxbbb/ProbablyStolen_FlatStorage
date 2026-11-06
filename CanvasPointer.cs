using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class CanvasPointer
    {
        internal readonly struct Hit
        {
            internal readonly GameObject Raw, Top;
            internal Hit(GameObject raw, GameObject top) { Raw = raw; Top = top; }
        }

        private readonly Dictionary<Vector2, Hit> cache = new Dictionary<Vector2, Hit>();
        private readonly List<RaycastResult> results = new List<RaycastResult>(64);
        private readonly HashSet<Transform> previews = new HashSet<Transform>();
        private EventSystem events;
        private PointerEventData pointer;
        private int frame = -1;
        private GameItem single;
        private bool groupDragging;

        internal void Invalidate() { frame = -1; cache.Clear(); }

        internal Hit At(Vector2 position)
        {
            var current = EventSystem.current;
            if (current == null) return default;
            var item = ItemMouseDragHandler.current?.GetCurrentItem();
            bool group = ItemMultiSelectHandler.current?.IsGroupDragging ?? false;
            if (events != current)
            {
                events = current;
                pointer = new PointerEventData(current);
                Invalidate();
            }
            if (frame != Time.frameCount || single != item || groupDragging != group)
            {
                cache.Clear(); previews.Clear();
                frame = Time.frameCount; single = item; groupDragging = group;
                if (item is GameItemElement element && !element.IsDestroyed()) previews.Add(element.fakeMiddle.transform);
                NativeSelection.CollectDragPreviews(previews);
            }
            if (cache.TryGetValue(position, out var cached)) return cached;
            pointer.Reset(); pointer.position = position;
            results.Clear(); events.RaycastAll(pointer, results);
            GameObject raw = null, top = null;
            foreach (var result in results)
            {
                if (result.gameObject == null) continue;
                if (raw == null) raw = result.gameObject;
                bool preview = false;
                for (var parent = result.gameObject.transform; parent != null; parent = parent.parent)
                    if (previews.Contains(parent)) { preview = true; break; }
                if (preview) continue;
                top = result.gameObject;
                break;
            }
            var hit = new Hit(raw, top);
            cache.Add(position, hit);
            return hit;
        }
    }
}
