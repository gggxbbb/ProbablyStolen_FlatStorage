using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class StorageMiniMap : IDisposable
    {
        private readonly FlatStorageController owner;
        private readonly TreeNodeRender frame, surface;
        private readonly Dictionary<string, RectTransform> marks = new Dictionary<string, RectTransform>();
        private readonly List<RectTransform> edges = new List<RectTransform>();
        private MiniMapProjection projection;
        private List<WindowLease> groups = new List<WindowLease>();
        private readonly List<Box> boxes = new List<Box>();
        private bool geometryDirty = true, hasView;
        private Box previousView;
        private float projectedWidth, projectedHeight;

        internal StorageMiniMap(FlatStorageController owner, TreeNodeRender viewport)
        {
            this.owner = owner;
            frame = FlatUI.Panel(viewport, "Mini map border", FlatUI.Border);
            FlatUI.IsolateCanvas(frame);
            surface = FlatUI.Panel(frame, "Mini map", FlatUI.Surface);
            var click = surface.gameObject.AddComponent<MiniMapClick>();
            click.Click = point => owner.Run(() =>
            {
                if (groups.Count > 0) owner.Focus(projection.CanvasX(point.x), projection.CanvasY(point.y));
            });
            for (int i = 0; i < 4; i++) edges.Add(Image("Viewport", FlatUI.Accent));
        }

        internal bool ContainsPointer => frame.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(frame.rectTransform, Input.mousePosition);

        internal void Layout(float width, float height)
        {
            float mapWidth = Math.Min(Math.Max(1, width - 12), Math.Min(148, Math.Max(80, width * .24f)));
            float mapHeight = Math.Min(Math.Max(1, height - 12), mapWidth * .64f);
            NativeAccess.Place(frame, Math.Max(0, width - mapWidth - 6), Math.Max(0, height - mapHeight - 6), mapWidth, mapHeight);
            NativeAccess.Place(surface, 1, 1, Math.Max(1, mapWidth - 2), Math.Max(1, mapHeight - 2));
            frame.rectTransform.SetAsLastSibling();
            geometryDirty = true;
        }

        internal void Visible(bool visible)
        {
            frame.gameObject.SetActive(visible);
            hasView = false;
            owner.InvalidatePointer();
        }

        internal void InvalidateGeometry() { geometryDirty = true; }

        internal void Refresh(IEnumerable<WindowLease> current, Box view)
        {
            groups = current.ToList();
            var ids = new HashSet<string>(groups.Select(group => group.Source.Id));
            foreach (string id in marks.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                marks[id].gameObject.SetActive(false);
                UnityEngine.Object.Destroy(marks[id].gameObject);
                marks.Remove(id);
                geometryDirty = true;
            }
            foreach (var group in groups)
            {
                if (marks.ContainsKey(group.Source.Id)) continue;
                var mark = Image("Storage", group.Source.Owner == null ? new Color(.35f, .44f, .49f, 1) : new Color(.27f, .51f, .44f, 1));
                mark.GetComponent<Image>().raycastTarget = true;
                string id = group.Source.Id;
                mark.gameObject.AddComponent<Button>().onClick.AddListener(() => owner.Run(() =>
                    owner.Focus(groups.First(destination => destination.Source.Id == id).Bounds)));
                marks.Add(group.Source.Id, mark);
                geometryDirty = true;
            }
            UpdateView(view);
        }

        internal void UpdateView(Box view)
        {
            if (!frame.gameObject.activeInHierarchy) return;
            if (groups.Count == 0)
            {
                foreach (var edge in edges) edge.gameObject.SetActive(false);
                return;
            }
            float width = Math.Max(1, surface.rectTransform.rect.width), height = Math.Max(1, surface.rectTransform.rect.height);
            if (projectedWidth != width || projectedHeight != height) geometryDirty = true;
            if (geometryDirty)
            {
                geometryDirty = false; hasView = false;
                projectedWidth = width; projectedHeight = height;
                boxes.Clear(); foreach (var group in groups) boxes.Add(group.Bounds);
                projection = new MiniMapProjection(boxes, width, height);
                foreach (var group in groups)
                {
                    Box box = projection.Project(group.Bounds);
                    FlatUI.Rect(marks[group.Source.Id], box.X, box.Y, Math.Max(1, box.Width), Math.Max(1, box.Height));
                }
                foreach (var edge in edges) edge.SetAsLastSibling();
            }
            if (hasView && WindowSpatialIndex.Same(view, previousView)) return;
            previousView = view; hasView = true;
            Box visible = projection.Project(view);
            float x = Math.Max(0, visible.X), y = Math.Max(0, visible.Y);
            float right = Math.Min(surface.rectTransform.rect.width, visible.X + visible.Width);
            float bottom = Math.Min(surface.rectTransform.rect.height, visible.Y + visible.Height);
            foreach (var edge in edges)
            {
                bool show = right > x && bottom > y;
                if (edge.gameObject.activeSelf != show) edge.gameObject.SetActive(show);
            }
            if (right <= x || bottom <= y) return;
            FlatUI.Rect(edges[0], x, y, right - x, 1);
            FlatUI.Rect(edges[1], x, bottom - 1, right - x, 1);
            FlatUI.Rect(edges[2], x, y, 1, bottom - y);
            FlatUI.Rect(edges[3], right - 1, y, 1, bottom - y);
        }

        private RectTransform Image(string name, Color color)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(Image));
            node.transform.SetParent(surface.transform, false);
            node.GetComponent<Image>().color = color;
            node.GetComponent<Image>().raycastTarget = false;
            return (RectTransform)node.transform;
        }

        public void Dispose() => UnityEngine.Object.Destroy(frame.gameObject);
    }

    internal sealed class MiniMapClick : MonoBehaviour, IPointerClickHandler
    {
        internal Action<Vector2> Click;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out var point);
            Click?.Invoke(new Vector2(point.x - rect.rect.xMin, rect.rect.yMax - point.y));
        }
    }
}
