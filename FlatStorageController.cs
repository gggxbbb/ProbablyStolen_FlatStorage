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
        private TreeNodeRender entry, root, viewport, content, toolbar, status, automaticLabel, automaticButton, errorLabel;
        private TreeNodeRender noticePanel, noticeText;
        private CustomUIWindow window;
        private CanvasTools tools;
        private readonly List<TreeNodeRender> toolbarButtons = new List<TreeNodeRender>();
        private readonly List<float> toolbarWidths = new List<float>();
        private bool viewDirty;
        private float appliedX, appliedY, appliedZoom, appliedWidth, appliedHeight;
        private Vector3 nativeScale;
        private ArrangementChanges arrangementChanges = new ArrangementChanges();
        private CanvasState state;
        private string scene, saveRun, outsideLocation;
        private int saveSlot;
        private bool outside;
        private Action updateFrame;
        private float nextRefresh, nextObservation;
        private int observationIndex, sourcesRevision, scanningRevision;
        private bool sourcesDirty, catalogDirty, arrangementDirty;
        private IEnumerator<StorageSource> discovery;
        private readonly List<StorageSource> discovered = new List<StorageSource>();
        private readonly Dictionary<PixelWindow, StorageSource> sourceIndex = new Dictionary<PixelWindow, StorageSource>();
        private readonly Dictionary<string, GroupPosition> positions = new Dictionary<string, GroupPosition>(StringComparer.Ordinal);
        private readonly Queue<WindowLease> refreshQueue = new Queue<WindowLease>();
        private readonly HashSet<WindowLease> scheduled = new HashSet<WindowLease>();
        private readonly WindowSpatialIndex spatial = new WindowSpatialIndex();
        private readonly HashSet<WindowLease> visibleLeases = new HashSet<WindowLease>();
        private readonly HashSet<WindowLease> candidates = new HashSet<WindowLease>();
        private readonly HashSet<WindowLease> nearby = new HashSet<WindowLease>();
        private readonly List<WindowLease> leaving = new List<WindowLease>();
        private readonly Vector3[] corners = new Vector3[4];
        private Vector2 lastPointer;
        private WindowLease moving;
        private readonly List<WindowLease> leases = new List<WindowLease>();
        private readonly CanvasPointer pointerHits = new CanvasPointer();
        private int screenWidth, screenHeight;
        private string notice;
        private float noticeUntil;
        private string shownNotice, shownMatchStatus;
        private float noticeWidth = -1, noticeViewHeight = -1;
        private int shownZoom = -1, shownCount = -1;
        private bool noticeVisible;
        internal bool ShowMiniMap { get => state.ShowMiniMap; set => state.ShowMiniMap = value; }

        private void Awake()
        {
            Current = this;
            updateFrame = UpdateFrame;
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
            notice = message; noticeUntil = Time.unscaledTime + 6;
        }

        private void Open()
        {
            if (IsOpen || PlayerStore.Instance == null || SaveManager.IsDecoding) return;
            NativeAccess.CancelDrag();
            scene = StorageDiscovery.SceneKey;
            saveRun = PlayerStore.Instance.runID; saveSlot = PlayerStore.Instance.saveSlotId;
            outside = ModHelper.IsOutsideStore();
            outsideLocation = outside ? ModHelper.GetCurrentAfterhourLocationId() : null;
            string layout = ModHelper.GetModData(ModEntry.Id, "layout:" + scene);
            state = ModSettings.CreateCanvasState(layout);
            positions.Clear(); foreach (var position in state.Groups) positions.Add(position.Id, position);
            nextRefresh = nextObservation = 0;
            shownZoom = shownCount = -1; shownMatchStatus = shownNotice = null;
            noticeVisible = false; noticeWidth = noticeViewHeight = -1;
            arrangementChanges = new ArrangementChanges();
            if (errorLabel != null) Destroy(errorLabel.gameObject);
            IsOpen = true;
            window = FlatUI.Window("canvas", ModText.Get("ui.title"), () => { window = null; Run(Close); }, true);
            FlatUI.ManualLayout(window.ContentRoot);
            root = TreeNodeRender.InstantiateAsRoot(window.ContentRoot);
            root.image.sprite = null;
            root.image.color = FlatUI.CanvasColor;
            root.image.enabled = true;
            root.image.raycastTarget = true;
            root.mask.enabled = false;
            toolbar = FlatUI.Panel(root, "Toolbar", FlatUI.Surface);
            FlatUI.IsolateCanvas(toolbar);
            toolbarButtons.Clear();
            toolbarWidths.Clear();
            AddButton(ModText.Get("ui.fit"), 48, Fit);
            AddButton("100%", 36, () => { state.ZoomAt(ViewWidth / 2, ViewHeight / 2, 1); Apply(); Save(); });
            AddButton(ModText.Get("ui.arrange"), 54, Arrange);
            AddButton(ModText.Get("ui.trading"), 54, OpenTrading);
            automaticButton = AddButton(ModText.Get(state.AutoArrange ? "ui.auto_on" : "ui.auto_off"), 86, ToggleAutomatic);
            automaticLabel = automaticButton.GetComponentsInChildren<TreeNodeRender>().First(n => n.text != null);
            FlatUI.ActiveButton(automaticButton, state.AutoArrange);
            AddButton(ModText.Get("ui.close"), 36, Close);
            status = FlatUI.Text(toolbar, "", 7);
            status.text.color = FlatUI.Muted;
            viewport = FlatUI.Panel(root, "Viewport", Color.clear);
            viewport.mask.enabled = true;
            content = FlatUI.Panel(viewport, "Canvas Content", Color.clear);
            content.image.raycastTarget = false;
            NativeAccess.Place(content, 0, 0, 0, 0);
            tools = new CanvasTools(this, window, toolbar, viewport);
            noticePanel = FlatUI.Panel(viewport, "Notice", FlatUI.Surface);
            noticePanel.image.raycastTarget = false;
            FlatUI.IsolateCanvas(noticePanel);
            noticePanel.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
            noticeText = FlatUI.Text(noticePanel, "", 7.5f);
            noticeText.text.enableWordWrapping = true;
            noticeText.text.overflowMode = TMPro.TextOverflowModes.Overflow;
            noticePanel.gameObject.SetActive(false);
            toolbar.rectTransform.SetAsLastSibling();
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
            toolbarWidths.Add(width);
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
            discovery?.Dispose(); discovery = null; discovered.Clear();
            refreshQueue.Clear(); scheduled.Clear();
            spatial.Clear(); visibleLeases.Clear(); candidates.Clear(); nearby.Clear(); sourceIndex.Clear(); positions.Clear();
            sourcesDirty = catalogDirty = arrangementDirty = false;
            InvalidatePointer();
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
            if (IsOpen && PlayerStore.Instance != null && saveRun == PlayerStore.Instance.runID && saveSlot == PlayerStore.Instance.saveSlotId)
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
            var layout = ToolbarLayout.Compute(width, tools.Widths(width), toolbarWidths);
            tools.Layout(layout.Primary);
            for (int i = 0; i < toolbarButtons.Count; i++) FlatUI.SizeButton(toolbarButtons[i], layout.Secondary[i]);
            NativeAccess.Place(toolbar, 0, Math.Max(0, height - layout.Height), width, layout.Height);
            var statusBox = layout.Status;
            NativeAccess.Place(status, statusBox.X, statusBox.Y, statusBox.Width, statusBox.Height);
            NativeAccess.Place(viewport, 0, 0, width, Math.Max(1, height - layout.Height));
            tools.ResizeDialogs(ViewWidth, ViewHeight);
            viewDirty = true;
        }

        internal bool PointerInViewport => IsOpen && RectTransformUtility.RectangleContainsScreenPoint(viewport.rectTransform, Input.mousePosition);
        internal bool OwnsInventoryPointer => CanUseInventoryPointer && SelectionInput.AllowsItems(
            SelectionRegionAt(Input.mousePosition), false, false, false, false, false);
        private bool CanUseInventoryPointer => IsOpen && tools != null && !Panning && !MovingGroup &&
            !Input.GetKey(KeyCode.Space) && !StoreUIManager.IsAnyTyping() && !tools.HasDialog && !tools.BlocksCanvasPointer;
        internal Vector2 InventoryDisplayScale => new Vector2(
            SelectionInput.RelativeScale(content.rectTransform.lossyScale.x, RenderHandler.current.overlayRoot.rectTransform.lossyScale.x),
            SelectionInput.RelativeScale(content.rectTransform.lossyScale.y, RenderHandler.current.overlayRoot.rectTransform.lossyScale.y));
        internal bool OwnsInventoryDropPointer
        {
            get
            {
                if (!CanUseInventoryPointer || !PointerInViewport) return false;
                var hit = PointerHit(Input.mousePosition);
                bool tabTarget = hit != null && WindowLease.FindTabTarget(hit.transform) != null && MachineryTabs.FindDropInventoryAt(Input.mousePosition, null,
                    ItemMouseDragHandler.current?.GetCurrentItem() as PixelElement) != null;
                return SelectionInput.AllowsDrop(SelectionRegionAt(Input.mousePosition), tabTarget,
                    false, false, false, false, false);
            }
        }
        internal bool CanContinueSelection => IsOpen && tools != null && !tools.HasDialog &&
            !StoreUIManager.IsAnyTyping() && !Panning && !MovingGroup && !Input.GetKey(KeyCode.Space);
        internal Rect SelectionViewport
        {
            get
            {
                viewport.rectTransform.GetWorldCorners(corners);
                return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            }
        }

        internal SelectionPointerRegion SelectionRegionAt(Vector2 screenPosition)
        {
            if (!IsOpen || !RectTransformUtility.RectangleContainsScreenPoint(viewport.rectTransform, screenPosition))
                return SelectionPointerRegion.Outside;
            var hit = PointerHit(screenPosition);
            if (hit == null || !hit.transform.IsChildOf(viewport.transform)) return SelectionPointerRegion.Control;
            var lease = WindowLease.Find(hit.transform);
            if (lease != null && hit.transform.IsChildOf(lease.Header.transform)) return SelectionPointerRegion.Header;
            var node = hit.GetComponent<TreeNodeRender>();
            if (node == null) return SelectionPointerRegion.Control;
            if (node == viewport || node == content || WindowLease.Groups.ContainsKey(node)) return SelectionPointerRegion.Blank;
            if (node.eventHandler != null && (node.eventHandler.mouseDragFunc.Count > 0 || node.eventHandler.mouseBeginDragFunc.Count > 0))
                return SelectionPointerRegion.Control;
            var element = node.FindParentElement();
            if (element is GameItemElement item && FlatDragOwnership.Owns(item)) return SelectionPointerRegion.Item;
            var nativeWindow = node.FindParentWindow();
            if (element is GameInventory && nativeWindow != null && WindowLease.Active.ContainsKey(nativeWindow))
                return SelectionPointerRegion.Inventory;
            return SelectionPointerRegion.Control;
        }

        internal bool IsTopItemAt(GameItemElement item, Vector2 position)
        {
            if (!PointerInSelectionViewport(position)) return false;
            var lease = WindowLease.Find(item.handler.transform);
            if (lease == null || !lease.IsVisibleInViewport) return false;
            var hit = PointerHit(position);
            return hit != null && hit.GetComponent<TreeNodeRender>()?.FindParentElement() == item;
        }

        private bool PointerInSelectionViewport(Vector2 position) => IsOpen &&
            RectTransformUtility.RectangleContainsScreenPoint(viewport.rectTransform, position);
        private GameObject PointerHit(Vector2 position) => pointerHits.At(position).Top;
        internal void InvalidatePointer() => pointerHits.Invalidate();

        internal bool OwnsPointer()
        {
            if (!IsOpen) return false;
            var first = PointerHit(Input.mousePosition);
            return first != null && first.transform.IsChildOf(root.transform);
        }

        private Vector2 Pointer()
        {
            Vector2 p = viewport.rectTransform.InverseTransformPoint(Input.mousePosition);
            return new Vector2(p.x, -p.y);
        }

        private bool PointerOnBlank()
        {
            if (!PointerInViewport || EventSystem.current == null) return false;
            var hit = pointerHits.At(Input.mousePosition).Raw;
            if (hit == null) return false;
            var node = hit.GetComponent<TreeNodeRender>();
            return node == viewport || node == content || node != null && WindowLease.Groups.ContainsKey(node);
        }

        internal void BeginGroupDrag(WindowLease group)
        {
            if (state.AutoArrange || Panning || IsItemDragging || Input.GetKey(KeyCode.Space) || tools.BlocksCanvasPointer || tools.HasDialog) return;
            ItemMultiSelectHandler.current?.Cancel();
            moving = group;
            lastPointer = Pointer();
            group.Group.rectTransform.SetAsLastSibling();
            InvalidatePointer();
        }

        internal static bool IsItemDragging => (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) ||
            (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging);

        private void Update() => Run(updateFrame);

        private void UpdateFrame()
        {
            if (RenderHandler.current == null || entry == null) { Remove(); return; }
            if (screenWidth != Screen.width || screenHeight != Screen.height || nativeScale != RenderHandler.current.windowRoot.rectTransform.lossyScale) Resize();
            if (!StoreUIManager.IsAnyTyping() && !KeyBindHelper.SuppressHotkeys && Input.GetKeyDown(ModEntry.Instance.Shortcut.Value)) Toggle();
            if (!IsOpen) return;
            if (PlayerStore.Instance == null || outside != ModHelper.IsOutsideStore() ||
                outside && outsideLocation != ModHelper.GetCurrentAfterhourLocationId() ||
                saveRun != PlayerStore.Instance.runID || saveSlot != PlayerStore.Instance.saveSlotId)
            { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && !StoreUIManager.IsAnyTyping())
            {
                if (NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen && !IsItemDragging &&
                    !(ItemMultiSelectHandler.current?.HasSelection ?? false))
                    return;
                EscapeFrame = Time.frameCount;
                if (tools.Escape()) return;
                if (IsItemDragging) { NativeAccess.CancelDrag(); return; }
                if (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.HasSelection)
                { ItemMultiSelectHandler.current.Cancel(); return; }
                Close(); return;
            }
            UpdateCanvas();
        }

        private void UpdateCanvas()
        {
            Vector2 pointer = Pointer();
            if (Input.GetMouseButtonDown(1) && !IsItemDragging && !MovingGroup && PointerOnBlank()) RightPanning = true;
            bool panKey = (RightPanning && Input.GetMouseButton(1)) || Input.GetMouseButton(2) ||
                (Input.GetKey(KeyCode.Space) && Input.GetMouseButton(0) && !StoreUIManager.IsAnyTyping());
            bool canPan = !tools.BlocksCanvasPointer && !tools.HasDialog && PointerInViewport &&
                (Panning || panKey || Input.mouseScrollDelta.y != 0) && OwnsPointer();
            if (panKey && !Panning && canPan)
            {
                if (NativeSelection.IsBanding) ItemMultiSelectHandler.current?.Cancel();
                Panning = true; moving = null; lastPointer = pointer;
            }
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
                    if (delta != Vector2.zero) moving.RefreshPlacement();
                }
            }
            lastPointer = pointer;
            if (canPan && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) && Input.mouseScrollDelta.y != 0)
            { state.ZoomAt(pointer.x, pointer.y, state.Zoom * Mathf.Pow(1.12f, Input.mouseScrollDelta.y)); Save(); }
            if (IsItemDragging && !Panning && !tools.BlocksCanvasPointer && PointerInViewport && OwnsPointer())
            {
                const float edge = 18;
                float dx = pointer.x < edge ? edge - pointer.x : pointer.x > ViewWidth - edge ? ViewWidth - edge - pointer.x : 0;
                float dy = pointer.y < edge ? edge - pointer.y : pointer.y > ViewHeight - edge ? ViewHeight - edge - pointer.y : 0;
                state.PanX += dx * Time.unscaledDeltaTime * ModSettings.EdgePanSpeed;
                state.PanY += dy * Time.unscaledDeltaTime * ModSettings.EdgePanSpeed;
            }
            UpdateDiscovery();
            FlushChanges();
            ObserveWindows();
            if (catalogDirty) { catalogDirty = false; tools.Refresh(leases); }
            ObserveArrangement();
            if (arrangementChanges.CanApply(state.AutoArrange, IsItemDragging || NativeSelection.IsBanding, Panning)) ArrangeLayout(false);
            Apply();
            UpdateStatus();
            if (IsItemDragging) UpdateDrag();
            PrepareNearby();
        }

        private void RefreshSources()
        {
            discovery?.Dispose(); discovery = null;
            sourcesDirty = false;
            SynchronizeSources(StorageDiscovery.Collect());
            nextRefresh = Time.unscaledTime + Math.Max(1, ModSettings.RefreshInterval);
            FlushChanges();
            if (catalogDirty) { catalogDirty = false; tools.Refresh(leases); }
            ObserveArrangement();
        }

        internal void RequestSources() { if (IsOpen) { sourcesDirty = true; sourcesRevision++; } }
        internal void CatalogChanged() { catalogDirty = arrangementDirty = true; }
        internal void QueueRefresh(WindowLease lease)
        {
            if (IsOpen && scheduled.Add(lease)) refreshQueue.Enqueue(lease);
        }
        internal void GeometryChanged(WindowLease lease)
        {
            if (!IsOpen) return;
            spatial.Set(lease); viewDirty = arrangementDirty = true;
            tools?.GeometryChanged(); InvalidatePointer();
        }

        private static bool WithinBudget(long start, double milliseconds) =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency < milliseconds;

        private void UpdateDiscovery()
        {
            if (discovery != null && scanningRevision != sourcesRevision) { discovery.Dispose(); discovery = null; }
            if (discovery == null)
            {
                if (!sourcesDirty && Time.unscaledTime < nextRefresh) return;
                sourcesDirty = false; scanningRevision = sourcesRevision;
                discovered.Clear(); discovery = StorageDiscovery.Discover().GetEnumerator();
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int steps = 0;
            do
            {
                if (!discovery.MoveNext())
                {
                    discovery.Dispose(); discovery = null;
                    discovered.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
                    SynchronizeSources(discovered);
                    nextRefresh = Time.unscaledTime + Math.Max(1, ModSettings.RefreshInterval);
                    return;
                }
                if (discovery.Current != null) discovered.Add(discovery.Current);
            } while (++steps < 128 && WithinBudget(start, .75));
        }

        private void SynchronizeSources(List<StorageSource> sources)
        {
            sourceIndex.Clear();
            foreach (var source in sources)
                if (source.Window.handler != null && !source.Window.isLocked &&
                    (source.Owner == null || OpenedContainers.CanExpose(source.Owner))) sourceIndex.Add(source.Window, source);
            for (int i = leases.Count - 1; i >= 0; i--)
            {
                var lease = leases[i];
                if (!sourceIndex.TryGetValue(lease.Source.Window, out var source))
                {
                    NativeAccess.CancelDrag();
                    if (moving == lease) moving = null;
                    spatial.Remove(lease); visibleLeases.Remove(lease); scheduled.Remove(lease);
                    lease.Dispose(); leases.RemoveAt(i);
                    viewDirty = catalogDirty = arrangementDirty = true;
                }
                else
                {
                    var existing = lease.Source;
                    if (existing.Label != source.Label || existing.OwnLabel != source.OwnLabel || existing.ParentId != source.ParentId || existing.Owner != source.Owner)
                    {
                        existing.Label = source.Label; existing.OwnLabel = source.OwnLabel;
                        existing.ParentId = source.ParentId; existing.Owner = source.Owner;
                        lease.MarkDirty(); catalogDirty = arrangementDirty = true;
                    }
                }
            }
            foreach (var source in sources)
            {
                if (!sourceIndex.ContainsKey(source.Window) || WindowLease.Active.ContainsKey(source.Window)) continue;
                if (!positions.TryGetValue(source.Id, out var position))
                {
                    position = new GroupPosition { Id = source.Id, X = 0, Y = leases.Count == 0 ? 0 : leases.Max(l => l.Position.Y + l.Height) + 24 };
                    state.Groups.Add(position); positions.Add(position.Id, position);
                }
                var lease = new WindowLease(source, content, position);
                leases.Add(lease); spatial.Set(lease); lease.SetVisible(false);
                viewDirty = catalogDirty = arrangementDirty = true;
            }
            InvalidatePointer();
        }

        private void FlushChanges()
        {
            int count = refreshQueue.Count;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            while (count-- > 0 && refreshQueue.Count > 0)
            {
                var lease = refreshQueue.Dequeue(); scheduled.Remove(lease);
                if (!WindowLease.Active.TryGetValue(lease.Source.Window, out var active) || active != lease) continue;
                lease.Refresh();
                if (lease.NeedsCatalog) lease.RefreshCatalog();
                if (!WithinBudget(start, 1)) break;
            }
        }

        private void ObserveWindows()
        {
            if (leases.Count == 0) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            float step = ModSettings.RefreshInterval / leases.Count;
            nextObservation = Math.Max(nextObservation, Time.unscaledTime - ModSettings.RefreshInterval);
            int remaining = leases.Count;
            while (Time.unscaledTime >= nextObservation && remaining-- > 0)
            {
                if (observationIndex >= leases.Count) observationIndex = 0;
                leases[observationIndex++].Observe();
                nextObservation += step;
                if (!WithinBudget(start, 1)) break;
            }
        }

        private void ObserveArrangement()
        {
            if (!arrangementDirty) return;
            arrangementDirty = false;
            arrangementChanges.Observe(leases.Select(l => new ArrangementLocation(l.Source.Id, l.Source.ParentId,
                l.Source.Owner?.parentInventory, l.Source.Owner?.modifiedShape.minX ?? 0,
                l.Source.Owner?.modifiedShape.minY ?? 0, l.Width, l.Height)));
        }

        private void PrepareNearby()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var lease in nearby)
            {
                if (!WindowLease.Active.ContainsKey(lease.Source.Window) || !lease.NeedsLayout || lease.IsVisibleInViewport) continue;
                lease.Refresh(true);
                if (!WithinBudget(start, .75)) break;
            }
        }

        private void Apply()
        {
            if (!viewDirty && appliedX == state.PanX && appliedY == state.PanY && appliedZoom == state.Zoom &&
                appliedWidth == ViewWidth && appliedHeight == ViewHeight) return;
            viewDirty = false;
            appliedX = state.PanX; appliedY = state.PanY; appliedZoom = state.Zoom;
            appliedWidth = ViewWidth; appliedHeight = ViewHeight;
            var scale = new Vector3(state.Zoom, state.Zoom, 1);
            var position = new Vector2(state.PanX, -state.PanY);
            if (content.rectTransform.localScale != scale) content.rectTransform.localScale = scale;
            if (content.rectTransform.anchoredPosition != position) content.rectTransform.anchoredPosition = position;
            InvalidatePointer();
            var view = ViewBounds;
            spatial.Query(WindowSpatialIndex.Expand(view, 64 / state.Zoom), candidates);
            var retain = WindowSpatialIndex.Expand(view, 160 / state.Zoom);
            leaving.Clear();
            foreach (var lease in visibleLeases) if (!lease.Bounds.Intersects(retain)) leaving.Add(lease);
            foreach (var lease in leaving) { lease.SetVisible(false); visibleLeases.Remove(lease); }
            foreach (var lease in candidates)
                if (visibleLeases.Add(lease)) lease.SetVisible(true);
            spatial.Query(WindowSpatialIndex.Expand(view, 320 / state.Zoom), nearby);
            tools.UpdateView(view);
        }

        private void UpdateStatus()
        {
            int zoom = Mathf.RoundToInt(state.Zoom * 100);
            string matches = tools.MatchStatus;
            if (shownZoom != zoom || shownCount != leases.Count || shownMatchStatus != matches)
            {
                shownZoom = zoom; shownCount = leases.Count; shownMatchStatus = matches;
                status.text.text = matches.Length > 0 ? matches : ModText.Get("ui.status", zoom, leases.Count);
            }
            bool showNotice = Time.unscaledTime < noticeUntil;
            if (noticeVisible != showNotice) { noticeVisible = showNotice; noticePanel.gameObject.SetActive(showNotice); }
            if (showNotice)
            {
                float width = Math.Max(1, Math.Min(320, ViewWidth * .65f));
                if (shownNotice == notice && noticeWidth == width && noticeViewHeight == ViewHeight) return;
                shownNotice = notice; noticeWidth = width; noticeViewHeight = ViewHeight;
                float height = noticeText.text.GetPreferredValues(notice, Math.Max(1, width - 12), float.PositiveInfinity).y + 12;
                noticeText.text.text = notice;
                NativeAccess.Place(noticePanel, 6, Math.Max(0, ViewHeight - height - 6), width, height);
                NativeAccess.Place(noticeText, 6, 6, Math.Max(1, width - 12), Math.Max(1, height - 12));
                noticePanel.rectTransform.SetAsLastSibling();
            }
        }

        private void UpdateDrag()
        {
            var item = ItemMouseDragHandler.current?.GetCurrentItem() as GameItemElement;
            if (item != null && ItemMouseDragHandler.current.IsDraggingItem) ScaleDragPreview(item);
            NativeAccess.RefreshDragTarget();
        }

        internal void ScaleDragPreview(GameItemElement item)
        {
            if (item != null && !item.IsDestroyed() &&
                item.fakeMiddle.parent == RenderHandler.current.overlayRoot)
            {
                var preview = item.fakeMiddle.rectTransform;
                var parentScale = preview.parent.lossyScale;
                var scale = new Vector3(content.rectTransform.lossyScale.x / parentScale.x,
                    content.rectTransform.lossyScale.y / parentScale.y, 1);
                if (preview.localScale == scale) return;
                var center = item.GetCenteredPosition();
                preview.localScale = scale;
                item.SetCenteredPosition(center.Item1, center.Item2);
                InvalidatePointer();
            }
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

        internal void RefreshNow()
        {
            RefreshSources();
            if (arrangementChanges.CanApply(state.AutoArrange, IsItemDragging || NativeSelection.IsBanding, Panning)) ArrangeLayout(false);
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
            { leases[i].Position.X = boxes[i].X; leases[i].Position.Y = boxes[i].Y; leases[i].RefreshPlacement(); }
            arrangementChanges.Applied();
            viewDirty = true;
            tools.GeometryChanged();
            if (fit) Fit();
            else { Apply(); Save(); }
        }

        private void ToggleAutomatic()
        {
            state.AutoArrange = !state.AutoArrange;
            automaticLabel.text.text = ModText.Get(state.AutoArrange ? "ui.auto_on" : "ui.auto_off");
            FlatUI.ActiveButton(automaticButton, state.AutoArrange);
            moving = null;
            if (state.AutoArrange)
            {
                arrangementChanges.Request();
                RefreshSources();
                if (!IsItemDragging && !NativeSelection.IsBanding && !Panning) ArrangeLayout(false);
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
            InvalidatePointer();
        }
    }
}
