using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen_FlatStorage
{
    [DefaultExecutionOrder(-1000)]
    public sealed class FlatStorageController : MonoBehaviour
    {
        public static FlatStorageController Current { get; private set; }
        public bool IsOpen { get; private set; }
        internal bool Panning { get; private set; }
        internal bool RightPanning { get; private set; }
        internal bool MovingGroup => moving != null;
        internal int EscapeFrame { get; private set; } = -1;
        internal int PanReleaseFrame { get; private set; } = -1;
        private TreeNodeRender entry, root, viewport, content, toolbar, status;
        private CanvasState state;
        private string scene, saveIdentity;
        private float nextRefresh;
        private Vector2 lastPointer;
        private WindowLease moving;
        private readonly List<WindowLease> leases = new List<WindowLease>();
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private int screenWidth, screenHeight;
        private string notice;
        private float noticeUntil;

        private void Awake()
        {
            Current = this;
            entry = FlatUI.Button(RenderHandler.current.windowRoot, "扁平存储", 0, 62, Toggle);
            Resize();
        }

        internal static void Remove()
        {
            if (Current == null) return;
            Current.Close();
            Destroy(Current.gameObject);
            Current = null;
        }

        private void OnDestroy()
        {
            Close();
            if (entry != null) Destroy(entry.gameObject);
            if (Current == this) Current = null;
        }

        public void Toggle()
        {
            if (!enabled) return;
            try { if (IsOpen) Close(); else Open(); }
            catch (Exception error)
            {
                ReportFailure(error);
                throw;
            }
        }

        private void ReportFailure(Exception error)
        {
            CloseWithoutSave();
            enabled = false;
            var label = entry.GetComponentsInChildren<TreeNodeRender>().First(n => n.text != null);
            label.text.text = "仓储错误";
            label.text.overflowMode = TMPro.TextOverflowModes.Overflow;
            var details = FlatUI.Text(entry, "扁平存储已停止：" + error.Message, 7);
            NativeAccess.Place(details, -260, 24, 320, 44);
            details.text.enableWordWrapping = true;
            Debug.LogException(error);
        }

        internal void Notice(string message)
        {
            notice = message; noticeUntil = Time.unscaledTime + 3;
        }

        private void Open()
        {
            if (IsOpen || PlayerStore.Instance == null || SaveManager.IsDecoding) return;
            NativeAccess.CancelDrag();
            scene = StorageDiscovery.SceneKey;
            saveIdentity = PlayerStore.Instance.runID + ":" + PlayerStore.Instance.saveSlotId;
            state = CanvasState.Decode(ModHelper.GetModData(ModEntry.Id, "layout:" + scene));
            root = FlatUI.Panel(RenderHandler.current.windowRoot, "Flat Storage Canvas", new Color(.07f, .085f, .10f, .99f));
            toolbar = FlatUI.Panel(root, "Toolbar", new Color(.13f, .16f, .19f));
            FlatUI.Button(toolbar, "适应全部", 4, 56, Fit);
            FlatUI.Button(toolbar, "100%", 64, 40, () => { state.ZoomAt(ViewWidth / 2, ViewHeight / 2, 1); Apply(); Save(); });
            FlatUI.Button(toolbar, "重新排列", 108, 56, Arrange);
            FlatUI.Button(toolbar, "交易页面", 168, 56, OpenTrading);
            FlatUI.Button(toolbar, "关闭", 228, 36, Close);
            status = FlatUI.Text(toolbar, "", 7);
            viewport = FlatUI.Panel(root, "Viewport", Color.clear);
            viewport.mask.enabled = true;
            content = FlatUI.Panel(viewport, "Canvas Content", Color.clear);
            content.image.raycastTarget = false;
            NativeAccess.Place(content, 0, 0, 0, 0);
            IsOpen = true;
            entry.gameObject.SetActive(false);
            Resize();
            try
            {
                RefreshSources();
                if (ModHelper.GetModData(ModEntry.Id, "layout:" + scene) == null) Arrange();
                Apply();
            }
            catch
            {
                CloseWithoutSave();
                throw;
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            Save();
            CloseWithoutSave();
        }

        internal void CloseWithoutSave()
        {
            if (!IsOpen) return;
            NativeAccess.CancelDrag();
            IsOpen = false;
            moving = null; Panning = false; RightPanning = false;
            for (int i = leases.Count - 1; i >= 0; i--) leases[i].Dispose();
            leases.Clear();
            if (root != null) Destroy(root.gameObject);
            if (entry != null) entry.gameObject.SetActive(true);
            root = null;
        }

        internal void Save()
        {
            if (IsOpen && PlayerStore.Instance != null && saveIdentity == PlayerStore.Instance.runID + ":" + PlayerStore.Instance.saveSlotId)
                ModHelper.SetModData(ModEntry.Id, "layout:" + scene, state.Encode());
        }

        private float ViewWidth => viewport.rectTransform.rect.width;
        private float ViewHeight => viewport.rectTransform.rect.height;

        private void Resize()
        {
            screenWidth = Screen.width; screenHeight = Screen.height;
            var parent = RenderHandler.current.windowRoot.rectTransform;
            Vector2 top = parent.InverseTransformPoint(new Vector3(0, screenHeight));
            Vector2 bottom = parent.InverseTransformPoint(new Vector3(screenWidth, 0));
            float left = top.x - parent.rect.xMin, topOffset = parent.rect.yMax - top.y;
            NativeAccess.Place(entry, bottom.x - parent.rect.xMin - 70, topOffset + 4, 62, 18);
            if (!IsOpen) return;
            float width = bottom.x - top.x, height = top.y - bottom.y;
            NativeAccess.Place(root, left, topOffset, width, height);
            NativeAccess.Place(toolbar, 0, Math.Max(0, height - 24), width, 24);
            NativeAccess.Place(status, 270, 0, Math.Max(1, width - 274), 24);
            NativeAccess.Place(viewport, 0, 0, width, Math.Max(1, height - 24));
        }

        internal bool PointerInViewport => IsOpen && RectTransformUtility.RectangleContainsScreenPoint(viewport.rectTransform, Input.mousePosition);
        internal bool OwnsPointer()
        {
            if (!IsOpen || EventSystem.current == null) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, hits);
            var item = ItemMouseDragHandler.current?.GetCurrentItem() as GameItemElement;
            var first = hits.FirstOrDefault(h => h.gameObject != null &&
                (item == null || !h.gameObject.transform.IsChildOf(item.fakeMiddle.transform)));
            return first.gameObject != null && first.gameObject.transform.IsChildOf(root.transform);
        }

        private Vector2 Pointer()
        {
            Vector2 p = viewport.rectTransform.InverseTransformPoint(Input.mousePosition);
            return new Vector2(p.x, -p.y);
        }

        private bool PointerOnBlank()
        {
            if (!PointerInViewport || EventSystem.current == null) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, hits);
            var hit = hits.FirstOrDefault(h => h.gameObject != null);
            if (hit.gameObject == null) return false;
            var node = hit.gameObject.GetComponent<TreeNodeRender>();
            return node == viewport || node == content || leases.Any(l => l.Group == node);
        }

        internal void BeginGroupDrag(WindowLease group)
        {
            if (Panning || IsItemDragging) return;
            moving = group;
            lastPointer = Pointer();
            group.Group.rectTransform.SetAsLastSibling();
        }

        internal static bool IsItemDragging => (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) ||
            (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging);

        private void Update()
        {
            if (RenderHandler.current == null || entry == null) { Remove(); return; }
            if (screenWidth != Screen.width || screenHeight != Screen.height) Resize();
            if (!StoreUIManager.IsAnyTyping() && !KeyBindHelper.SuppressHotkeys && Input.GetKeyDown(ModEntry.Instance.Shortcut.Value)) Toggle();
            if (!IsOpen) return;
            if (PlayerStore.Instance == null || scene != StorageDiscovery.SceneKey || saveIdentity != PlayerStore.Instance.runID + ":" + PlayerStore.Instance.saveSlotId)
            { CloseWithoutSave(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && !StoreUIManager.IsAnyTyping())
            {
                EscapeFrame = Time.frameCount;
                if (IsItemDragging) { NativeAccess.CancelDrag(); return; }
                Close(); return;
            }
            try { UpdateCanvas(); }
            catch (Exception error)
            {
                ReportFailure(error);
                throw;
            }
        }

        private void UpdateCanvas()
        {
            Vector2 pointer = Pointer();
            if (Input.GetMouseButtonDown(1) && !IsItemDragging && !MovingGroup && PointerOnBlank()) RightPanning = true;
            bool panKey = (RightPanning && Input.GetMouseButton(1)) || Input.GetMouseButton(2) ||
                (Input.GetKey(KeyCode.Space) && Input.GetMouseButton(0) && !StoreUIManager.IsAnyTyping());
            bool canPan = PointerInViewport && (OwnsPointer() || IsItemDragging);
            if (panKey && !Panning && canPan) { Panning = true; moving = null; lastPointer = pointer; }
            if (Panning)
            {
                if (!panKey) { Panning = false; RightPanning = false; PanReleaseFrame = Time.frameCount; Save(); }
                else { var delta = pointer - lastPointer; state.PanX += delta.x; state.PanY += delta.y; }
            }
            if (moving != null)
            {
                if (!Input.GetMouseButton(0)) { moving = null; Save(); }
                else
                {
                    var delta = (pointer - lastPointer) / state.Zoom;
                    moving.Position.X += delta.x; moving.Position.Y += delta.y;
                    NativeAccess.Place(moving.Group, moving.Position.X, moving.Position.Y, moving.Width, moving.Height);
                }
            }
            lastPointer = pointer;
            if (canPan && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) && Input.mouseScrollDelta.y != 0)
            { state.ZoomAt(pointer.x, pointer.y, state.Zoom * Mathf.Pow(1.12f, Input.mouseScrollDelta.y)); Save(); }
            if (IsItemDragging && PointerInViewport && !Panning)
            {
                const float edge = 18;
                float dx = pointer.x < edge ? edge - pointer.x : pointer.x > ViewWidth - edge ? ViewWidth - edge - pointer.x : 0;
                float dy = pointer.y < edge ? edge - pointer.y : pointer.y > ViewHeight - edge ? ViewHeight - edge - pointer.y : 0;
                state.PanX += dx * Time.unscaledDeltaTime * 12;
                state.PanY += dy * Time.unscaledDeltaTime * 12;
            }
            if (Time.unscaledTime >= nextRefresh)
            { nextRefresh = Time.unscaledTime + .25f; RefreshSources(); }
            Apply();
        }

        private void RefreshSources()
        {
            var sources = StorageDiscovery.Collect();
            foreach (var lease in leases.ToArray())
            {
                var source = sources.FirstOrDefault(s => s.Window == lease.Source.Window);
                if (source == null)
                {
                    NativeAccess.CancelDrag();
                    if (moving == lease) moving = null;
                    lease.Dispose(); leases.Remove(lease);
                }
                else lease.Source = source;
            }
            foreach (var source in sources)
            {
                if (leases.Any(l => l.Source.Window == source.Window)) continue;
                var position = state.Groups.FirstOrDefault(g => g.Id == source.Id);
                if (position == null)
                {
                    position = new GroupPosition { Id = source.Id, X = 0, Y = leases.Count == 0 ? 0 : leases.Max(l => l.Position.Y + l.Height) + 24 };
                    state.Groups.Add(position);
                }
                leases.Add(new WindowLease(source, content, position));
            }
            foreach (var lease in leases) lease.Refresh();
        }

        private void Apply()
        {
            content.rectTransform.localScale = new Vector3(state.Zoom, state.Zoom, 1);
            content.rectTransform.anchoredPosition = new Vector2(state.PanX, -state.PanY);
            var view = new Box(-state.PanX / state.Zoom, -state.PanY / state.Zoom, ViewWidth / state.Zoom, ViewHeight / state.Zoom);
            foreach (var lease in leases) lease.Cull(view);
            status.text.text = Time.unscaledTime < noticeUntil ? notice : $"{Mathf.RoundToInt(state.Zoom * 100)}%   {leases.Count} 个分组";
            var item = ItemMouseDragHandler.current?.GetCurrentItem() as GameItemElement;
            if (item != null && ItemMouseDragHandler.current.IsDraggingItem && !item.IsDestroyed() &&
                item.fakeMiddle.parent == RenderHandler.current.overlayRoot)
            {
                var center = item.GetCenteredPosition();
                var preview = item.fakeMiddle.rectTransform;
                var parentScale = preview.parent.lossyScale;
                preview.localScale = new Vector3(content.rectTransform.lossyScale.x / parentScale.x,
                    content.rectTransform.lossyScale.y / parentScale.y, 1);
                item.SetCenteredPosition(center.Item1, center.Item2);
            }
            NativeAccess.RefreshDragTarget();
        }

        private void Fit()
        {
            CanvasMath.Fit(state, leases.Select(l => l.Bounds).ToList(), ViewWidth, ViewHeight);
            Apply(); Save();
        }

        private void Arrange()
        {
            NativeAccess.CancelDrag();
            moving = null;
            var groups = leases.Select(l => StorageArrangementSource.Read(l.Source, l.Bounds)).ToList();
            var boxes = StorageArrangement.Arrange(groups, (float)Screen.width / Math.Max(1, Screen.height));
            for (int i = 0; i < leases.Count; i++)
            { leases[i].Position.X = boxes[i].X; leases[i].Position.Y = boxes[i].Y; leases[i].Refresh(); }
            Fit();
        }

        private void OpenTrading()
        {
            if (ModHelper.IsOutsideStore()) { Notice("交易页面用于店内柜台交易。"); return; }
            var trading = NegociationUIManager.Instance;
            if (trading == null || trading.uiObject == null)
                throw new InvalidOperationException("游戏交易页面尚未初始化。");
            Close();
            trading.OpenUI();
        }
    }
}
