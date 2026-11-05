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
        private TreeNodeRender entry, root, viewport, content, toolbar, status, automaticLabel, searchbar, errorLabel;
        private CustomUIWindow window;
        private CanvasTools tools;
        private readonly List<TreeNodeRender> toolbarButtons = new List<TreeNodeRender>();
        private bool viewDirty;
        private float appliedX, appliedY, appliedZoom, appliedWidth, appliedHeight;
        private Vector3 nativeScale;
        private ArrangementChanges arrangementChanges = new ArrangementChanges();
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
            entry = FlatUI.Button(RenderHandler.current.windowRoot, ModText.Get("ui.title"), 0, 78, Toggle);
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

        public void Toggle() => Run(() => { if (IsOpen) Close(); else Open(); });

        internal void Run(Action action)
        {
            try { action(); }
            catch (Exception error) { ReportFailure(error); }
        }

        private void ReportFailure(Exception error)
        {
            ModEntry.Instance.Log.Error(error);
            CloseWithoutSave();
            if (entry == null) return;
            if (errorLabel != null) Destroy(errorLabel.gameObject);
            errorLabel = FlatUI.Text(entry, ModText.Get("ui.error", error.Message), 7);
            NativeAccess.Place(errorLabel, -240, 24, 320, 60);
            errorLabel.text.enableWordWrapping = true;
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
            string layout = ModHelper.GetModData(ModEntry.Id, "layout:" + scene);
            state = ModSettings.CreateCanvasState(layout);
            arrangementChanges = new ArrangementChanges();
            if (errorLabel != null) Destroy(errorLabel.gameObject);
            IsOpen = true;
            window = FlatUI.Window("canvas", ModText.Get("ui.title"), () => { window = null; Run(Close); }, true);
            FlatUI.ManualLayout(window.ContentRoot);
            root = TreeNodeRender.InstantiateAsRoot(window.ContentRoot);
            root.image.sprite = null;
            root.image.color = new Color(.07f, .085f, .10f, .99f);
            root.image.enabled = true;
            root.image.raycastTarget = true;
            root.mask.enabled = false;
            toolbar = FlatUI.Panel(root, "Toolbar", new Color(.13f, .16f, .19f));
            toolbarButtons.Clear();
            AddButton(ModText.Get("ui.fit"), 58, Fit);
            AddButton("100%", 40, () => { state.ZoomAt(ViewWidth / 2, ViewHeight / 2, 1); Apply(); Save(); });
            AddButton(ModText.Get("ui.arrange"), 62, Arrange);
            AddButton(ModText.Get("ui.trading"), 62, OpenTrading);
            var automatic = AddButton(ModText.Get(state.AutoArrange ? "ui.auto_on" : "ui.auto_off"), 96, ToggleAutomatic);
            automaticLabel = automatic.GetComponentsInChildren<TreeNodeRender>().First(n => n.text != null);
            AddButton(ModText.Get("ui.close"), 44, Close);
            status = FlatUI.Text(toolbar, "", 7);
            searchbar = FlatUI.Panel(root, "Search", new Color(.13f, .16f, .19f));
            viewport = FlatUI.Panel(root, "Viewport", Color.clear);
            viewport.mask.enabled = true;
            content = FlatUI.Panel(viewport, "Canvas Content", Color.clear);
            content.image.raycastTarget = false;
            NativeAccess.Place(content, 0, 0, 0, 0);
            tools = new CanvasTools(this, window, searchbar);
            entry.gameObject.SetActive(false);
            Resize();
            RefreshSources();
            if (state.AutoArrange || layout == null) ArrangeLayout(false);
            if (layout == null && leases.Count > 0)
            {
                var center = leases.FirstOrDefault(l => l.Source.Id == "root:back") ?? leases[0];
                Focus(center.Bounds);
            }
            Apply();
            UpdateStatus();
        }

        private TreeNodeRender AddButton(string label, float width, Action action)
        {
            var button = FlatUI.Button(toolbar, label, 0, width, () => Run(action));
            toolbarButtons.Add(button);
            return button;
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
            tools?.Dispose(); tools = null;
            for (int i = leases.Count - 1; i >= 0; i--) leases[i].Dispose();
            leases.Clear();
            if (window != null)
            {
                var closing = window; window = null;
                closing.onClose = null; closing.Close();
            }
            else if (root != null) Destroy(root.gameObject);
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
        internal Box ViewBounds => new Box(-state.PanX / state.Zoom, -state.PanY / state.Zoom,
            ViewWidth / state.Zoom, ViewHeight / state.Zoom);

        private void Resize()
        {
            screenWidth = Screen.width; screenHeight = Screen.height;
            var parent = RenderHandler.current.windowRoot.rectTransform;
            Vector2 top = parent.InverseTransformPoint(new Vector3(0, screenHeight));
            Vector2 bottom = parent.InverseTransformPoint(new Vector3(screenWidth, 0));
            float topOffset = parent.rect.yMax - top.y;
            NativeAccess.Place(entry, bottom.x - parent.rect.xMin - 86, topOffset + 4, 78, 18);
            nativeScale = parent.lossyScale;
            if (!IsOpen) return;
            arrangementChanges.Request();
            FlatUI.FitScreen(window);
            var host = window.ContentRoot;
            float sx = nativeScale.x / host.lossyScale.x, sy = nativeScale.y / host.lossyScale.y;
            root.rectTransform.localScale = new Vector3(sx, sy, 1);
            float width = host.rect.width / sx, height = host.rect.height / sy;
            NativeAccess.Place(root, 0, 0, width, height);
            float searchHeight = tools.Layout(width);
            NativeAccess.Place(searchbar, 0, 0, width, searchHeight);
            float x = 4, y = 3;
            foreach (var button in toolbarButtons)
            {
                float w = button.rectTransform.sizeDelta.x;
                if (x + w > width - 4 && x > 4) { x = 4; y += 22; }
                NativeAccess.Place(button, x, y, w, 18); x += w + 4;
            }
            float toolbarHeight = y + 38;
            NativeAccess.Place(toolbar, 0, Math.Max(searchHeight, height - toolbarHeight), width, toolbarHeight);
            NativeAccess.Place(status, 4, y + 20, Math.Max(1, width - 8), 16);
            NativeAccess.Place(viewport, 0, searchHeight, width, Math.Max(1, height - toolbarHeight - searchHeight));
            tools.ResizeDialogs(ViewWidth, ViewHeight);
            viewDirty = true;
        }

        internal bool PointerInViewport => IsOpen && RectTransformUtility.RectangleContainsScreenPoint(viewport.rectTransform, Input.mousePosition);
        internal bool OwnsInventoryPointer => PointerInViewport && tools != null && !tools.HasDialog &&
            !tools.PickingTarget && !StoreUIManager.IsAnyTyping() && OwnsPointer();
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
            if (state.AutoArrange || Panning || IsItemDragging || tools.PickingTarget || tools.HasDialog) return;
            moving = group;
            lastPointer = Pointer();
            group.Group.rectTransform.SetAsLastSibling();
        }

        internal static bool IsItemDragging => (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) ||
            (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging);

        private void Update() => Run(UpdateFrame);

        private void UpdateFrame()
        {
            if (RenderHandler.current == null || entry == null) { Remove(); return; }
            if (screenWidth != Screen.width || screenHeight != Screen.height || nativeScale != RenderHandler.current.windowRoot.rectTransform.lossyScale) Resize();
            if (!StoreUIManager.IsAnyTyping() && !KeyBindHelper.SuppressHotkeys && Input.GetKeyDown(ModEntry.Instance.Shortcut.Value)) Toggle();
            if (!IsOpen) return;
            if (PlayerStore.Instance == null || scene != StorageDiscovery.SceneKey || saveIdentity != PlayerStore.Instance.runID + ":" + PlayerStore.Instance.saveSlotId)
            { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && !StoreUIManager.IsAnyTyping())
            {
                if (NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen && !IsItemDragging)
                    return;
                EscapeFrame = Time.frameCount;
                if (tools.Escape()) return;
                if (IsItemDragging) { NativeAccess.CancelDrag(); return; }
                Close(); return;
            }
            UpdateCanvas();
        }

        private void UpdateCanvas()
        {
            Vector2 pointer = Pointer();
            tools.Update();
            if (Input.GetMouseButtonDown(1) && !IsItemDragging && !MovingGroup && PointerOnBlank()) RightPanning = true;
            bool panKey = (RightPanning && Input.GetMouseButton(1)) || Input.GetMouseButton(2) ||
                (Input.GetKey(KeyCode.Space) && Input.GetMouseButton(0) && !StoreUIManager.IsAnyTyping());
            bool canPan = PointerInViewport && OwnsPointer() && !tools.PickingTarget && !tools.HasDialog;
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
                    if (delta != Vector2.zero) viewDirty = true;
                }
            }
            lastPointer = pointer;
            if (canPan && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) && Input.mouseScrollDelta.y != 0)
            { state.ZoomAt(pointer.x, pointer.y, state.Zoom * Mathf.Pow(1.12f, Input.mouseScrollDelta.y)); Save(); }
            if (IsItemDragging && PointerInViewport && OwnsPointer() && !Panning)
            {
                const float edge = 18;
                float dx = pointer.x < edge ? edge - pointer.x : pointer.x > ViewWidth - edge ? ViewWidth - edge - pointer.x : 0;
                float dy = pointer.y < edge ? edge - pointer.y : pointer.y > ViewHeight - edge ? ViewHeight - edge - pointer.y : 0;
                state.PanX += dx * Time.unscaledDeltaTime * ModSettings.EdgePanSpeed;
                state.PanY += dy * Time.unscaledDeltaTime * ModSettings.EdgePanSpeed;
            }
            if (Time.unscaledTime >= nextRefresh)
            { nextRefresh = Time.unscaledTime + ModSettings.RefreshInterval; RefreshSources(); }
            if (arrangementChanges.CanApply(state.AutoArrange, IsItemDragging, Panning)) ArrangeLayout(false);
            Apply();
            UpdateStatus();
            if (IsItemDragging) UpdateDrag();
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
                    viewDirty = true;
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
                viewDirty = true;
            }
            foreach (var lease in leases)
            {
                float width = lease.Width, height = lease.Height;
                lease.Refresh();
                if (width != lease.Width || height != lease.Height) viewDirty = true;
            }
            arrangementChanges.Observe(leases.Select(l => new ArrangementLocation(l.Source.Id, l.Source.ParentId,
                l.Source.Owner?.parentInventory, l.Source.Owner?.modifiedShape.minX ?? 0,
                l.Source.Owner?.modifiedShape.minY ?? 0, l.Width, l.Height)));
            tools.Refresh(leases);
        }

        private void Apply()
        {
            if (!viewDirty && appliedX == state.PanX && appliedY == state.PanY && appliedZoom == state.Zoom &&
                appliedWidth == ViewWidth && appliedHeight == ViewHeight) return;
            viewDirty = false;
            appliedX = state.PanX; appliedY = state.PanY; appliedZoom = state.Zoom;
            appliedWidth = ViewWidth; appliedHeight = ViewHeight;
            content.rectTransform.localScale = new Vector3(state.Zoom, state.Zoom, 1);
            content.rectTransform.anchoredPosition = new Vector2(state.PanX, -state.PanY);
            var view = ViewBounds;
            foreach (var lease in leases) lease.Cull(view);
            tools.UpdateView(view);
        }

        private void UpdateStatus()
        {
            string value = Time.unscaledTime < noticeUntil ? notice : ModText.Get("ui.status", Mathf.RoundToInt(state.Zoom * 100), leases.Count);
            if (status.text.text != value) status.text.text = value;
        }

        private void UpdateDrag()
        {
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

        internal void Focus(float x, float y)
        {
            state.PanX = ViewWidth / 2 - x * state.Zoom;
            state.PanY = ViewHeight / 2 - y * state.Zoom;
            Apply(); Save();
        }

        internal void Focus(Box bounds) => Focus(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

        internal void Focus(CatalogItem entry)
        {
            var lease = leases.FirstOrDefault(l => l.Source.Id == entry.Source.Id);
            if (lease == null || entry.Item.IsDestroyed()) return;
            Focus(lease.Bounds);
            if (entry.Item.parentInventory is GameGridScrollableInventory scrollable)
            {
                int range = scrollable.GetMinVisibleHeight() - scrollable.GetVisibleHeight();
                if (range > 0)
                {
                    float center = entry.Item.modifiedShape.minY + entry.Item.modifiedShape.globalHeight / 2f;
                    scrollable.SetScroll((center - scrollable.GetVisibleHeight() / 2f) / range);
                }
            }
            var rect = entry.Item.middle.rectTransform;
            Vector2 point = content.rectTransform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Focus(point.x, -point.y);
        }

        internal TreeNodeRender CreatePointerCover()
        {
            var cover = FlatUI.Panel(viewport, "Choose destination", new Color(.2f, .5f, .7f, .06f));
            NativeAccess.Place(cover, 0, 0, ViewWidth, ViewHeight);
            return cover;
        }

        internal void RefreshNow()
        {
            RefreshSources();
            if (arrangementChanges.CanApply(state.AutoArrange, IsItemDragging, Panning)) ArrangeLayout(false);
            Apply();
        }

        private void Fit()
        {
            CanvasMath.Fit(state, leases.Select(l => l.Bounds).ToList(), ViewWidth, ViewHeight);
            Apply(); Save();
        }

        private void Arrange()
        {
            NativeAccess.CancelDrag();
            ArrangeLayout(true);
        }

        private void ArrangeLayout(bool fit)
        {
            moving = null;
            var groups = leases.Select(l => StorageArrangementSource.Read(l.Source, l.Bounds)).ToList();
            var boxes = StorageArrangement.Arrange(groups, (float)Screen.width / Math.Max(1, Screen.height));
            for (int i = 0; i < leases.Count; i++)
            { leases[i].Position.X = boxes[i].X; leases[i].Position.Y = boxes[i].Y; leases[i].Refresh(); }
            arrangementChanges.Applied();
            viewDirty = true;
            tools.Refresh(leases);
            if (fit) Fit();
            else { Apply(); Save(); }
        }

        private void ToggleAutomatic()
        {
            state.AutoArrange = !state.AutoArrange;
            automaticLabel.text.text = ModText.Get(state.AutoArrange ? "ui.auto_on" : "ui.auto_off");
            moving = null;
            if (state.AutoArrange)
            {
                arrangementChanges.Request();
                RefreshSources();
                if (!IsItemDragging && !Panning) ArrangeLayout(false);
            }
            Save();
        }

        private void OpenTrading()
        {
            if (ModHelper.IsOutsideStore()) { Notice(ModText.Get("ui.trading_outside")); return; }
            var trading = NegociationUIManager.Instance;
            if (trading == null || trading.uiObject == null)
            {
                string message = ModText.Get("ui.trading_unavailable");
                ModEntry.Instance.Log.Warning(message); Notice(message); return;
            }
            NativeAccess.CancelDrag();
            moving = null; Panning = false; RightPanning = false;
            Save();
            UIFamilyLayer.BringPanelsToFront();
            trading.OpenUI();
        }
    }
}
