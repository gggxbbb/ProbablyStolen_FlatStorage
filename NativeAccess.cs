using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal static class NativeAccess
    {
        internal static readonly FieldInfo CanMove = AccessTools.Field(typeof(PixelWindow), "canMove");
        internal static readonly FieldInfo TabWidth = AccessTools.Field(typeof(MachineryTabs), "<widthPixels>k__BackingField");
        internal static readonly FieldInfo TabHeight = AccessTools.Field(typeof(MachineryTabs), "<heightPixels>k__BackingField");
        internal static readonly FieldInfo TabStrip = AccessTools.Field(typeof(MachineryTabs), "strip");
        internal static readonly FieldInfo TabUnderline = AccessTools.Field(typeof(MachineryTabs), "underline");
        internal static readonly MethodInfo Abort = AccessTools.Method(typeof(ItemMouseDragHandler), "AbortDrag");
        internal static readonly MethodInfo RefreshDrag = AccessTools.Method(typeof(ItemMouseDragHandler), "Drag");
        private static readonly object[] dragArguments = { Vector3.zero };

        internal static void Check()
        {
            NativeLayoutSizes.Check();
            NativeTabsAccess.Check();
            foreach (var member in new MemberInfo[] { CanMove, TabWidth, TabHeight, TabStrip, TabUnderline, Abort, RefreshDrag })
                if (member == null) throw new MissingMemberException("Flat Storage is incompatible with the current game API.");
        }

        internal static void CancelDrag()
        {
            var handler = ItemMouseDragHandler.current;
            if (handler != null)
            {
                var item = handler.GetCurrentItem() as GameItemElement;
                Abort.Invoke(handler, null);
                if (item != null && !item.IsDestroyed()) { item.ToggleFakeBackground(false); item.Validate(); }
            }
            ItemMultiSelectHandler.current?.Cancel();
        }

        internal static void RefreshDragTarget()
        {
            if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
                RefreshDrag.Invoke(ItemMouseDragHandler.current, dragArguments);
        }

        internal static void Place(TreeNodeRender node, float x, float y, float width, float height)
        {
            FlatUI.Rect(node.rectTransform, x, y, width, height);
        }
    }

    internal sealed class WindowLease : IDisposable
    {
        internal static readonly Dictionary<PixelWindow, WindowLease> Active = new Dictionary<PixelWindow, WindowLease>();
        internal static readonly Dictionary<MachineryTabs, WindowLease> Tabs = new Dictionary<MachineryTabs, WindowLease>();
        internal static readonly Dictionary<TreeNodeRender, WindowLease> Groups = new Dictionary<TreeNodeRender, WindowLease>();
        private static readonly Dictionary<Transform, WindowLease> roots = new Dictionary<Transform, WindowLease>();
        private static readonly Dictionary<Transform, MachineryTabs> tabTargets = new Dictionary<Transform, MachineryTabs>();
        public StorageSource Source;
        public readonly TreeNodeRender Group;
        public readonly TreeNodeRender Header;
        public readonly TreeNodeRender Label;
        public readonly GroupPosition Position;
        private readonly TreeNodeRender parent;
        private readonly Transform transformParent;
        private readonly Vector2 anchorMin, anchorMax, pivot, anchored, size;
        private readonly Vector3 scale;
        private readonly int sibling;
        private readonly int originalWidth, originalHeight;
        private readonly bool visible, registered, movable;
        private readonly Action closeCallback;
        private readonly Dictionary<MachineryTabs, TabSnapshot> selections = new Dictionary<MachineryTabs, TabSnapshot>();
        private readonly CanvasGroup visibility;
        private readonly Canvas renderCanvas;
        private readonly GraphicRaycaster raycaster;
        private readonly NativeLayoutSizes layoutSizes = new NativeLayoutSizes();
        private readonly List<TreeNodeRender> nodes = new List<TreeNodeRender>();
        private readonly List<MachineryTabs> removedTabs = new List<MachineryTabs>();
        private readonly HashSet<GameInventory> subscriptions = new HashSet<GameInventory>();
        private readonly List<GameInventory> removedInventories = new List<GameInventory>();
        private readonly Dictionary<MachineryTabs, long> validatedTabs = new Dictionary<MachineryTabs, long>();
        private readonly StorageSource[] scope;
        private readonly Action<GameItem, GameInventory, SlotMarker> inventoryChanged;
        private readonly Action<GameItem, GameInventory, SlotMarker> amountChanged;
        internal StorageCatalog Catalog { get; private set; }
        private bool structureDirty = true, layoutDirty = true, catalogDirty = true, disposed;
        private int validationDepth;
        private long revision;
        private Box placed;
        private string placedLabel;
        internal bool NeedsLayout => structureDirty || layoutDirty;
        internal bool NeedsCatalog => catalogDirty;
        internal bool IsAvailable => !disposed && Source.Window.handler != null && !Source.Window.isLocked &&
            !(Source.Owner is GameItemElement item && item.IsDestroyed());
        internal bool IsVisibleInViewport { get; private set; } = true;
        internal bool OriginallyVisible => visible;
        internal int PerformanceGraphics => GraphicRegistry.GetGraphicsForCanvas(renderCanvas).Count;
        internal int PerformanceRaycastGraphics => GraphicRegistry.GetRaycastableGraphicsForCanvas(renderCanvas).Count;
        public float Width => Math.Max(1, Source.Window.widthPixels);
        public float Height => Source.Window.heightPixels + 20;
        public Box Bounds => new Box(Position.X, Position.Y, Width, Height);

        public WindowLease(StorageSource source, TreeNodeRender canvas, GroupPosition position)
        {
            Source = source; Position = position;
            scope = new[] { source };
            inventoryChanged = OnInventoryChanged;
            amountChanged = (item, inventory, slot) => MarkDirty(false, false);
            var window = source.Window;
            var rect = window.rectTransform;
            parent = window.handler.parent; transformParent = rect.parent;
            anchorMin = rect.anchorMin; anchorMax = rect.anchorMax; pivot = rect.pivot;
            anchored = rect.anchoredPosition; size = rect.sizeDelta; scale = rect.localScale;
            sibling = rect.GetSiblingIndex(); visible = window.IsVisible(); movable = window.canMove;
            originalWidth = window.widthPixels; originalHeight = window.heightPixels;
            registered = WindowsHandler.current.visibleWindows.Contains(window);
            closeCallback = window.closeCallback;
            CaptureLayoutSizes(window.handler.GetComponentsInChildren<TreeNodeRender>(true));
            Group = FlatUI.Panel(canvas, "Storage Group", FlatUI.Surface);
            Header = FlatUI.Panel(Group, "Group Header", FlatUI.Field);
            Label = FlatUI.Text(Header, source.Label, 7);
            Label.text.enableWordWrapping = false;
            Label.text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            Header.InitEventHandler();
            Header.eventHandler.mouseDownFunc.Add(e =>
            {
                if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                    FlatStorageController.Current.BeginGroupDrag(this);
                return true;
            });
            visibility = Group.gameObject.AddComponent<CanvasGroup>();
            renderCanvas = FlatUI.IsolateCanvas(Group, true);
            raycaster = Group.GetComponent<GraphicRaycaster>();
            Active.Add(window, this);
            Groups.Add(Group, this);
            roots.Add(Group.transform, this);
            try
            {
                NativeAccess.CanMove.SetValue(window, false);
                window.closeCallback = null;
                window.handler.SetParent(Group);
                rect.localScale = Vector3.one;
                window.handler.gameObject.SetActive(true);
                WindowsHandler.TryAddWindow(window);
                window.openCallback?.Invoke();
                Refresh();
                RefreshCatalog();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static WindowLease Find(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
                if (roots.TryGetValue(current, out var lease)) return lease;
            return null;
        }

        internal static MachineryTabs FindTabTarget(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
                if (tabTargets.TryGetValue(current, out var tabs)) return tabs;
            return null;
        }

        internal void MarkDirty(bool structure = false, bool layout = true)
        {
            if (disposed) return;
            structureDirty |= structure; layoutDirty |= layout; catalogDirty = true;
            revision++;
            FlatStorageController.Current?.QueueRefresh(this);
        }

        private void OnInventoryChanged(GameItem item, GameInventory inventory, SlotMarker slot)
        {
            PerformanceMonitor.Active?.Event("inventory-change", Source.Id + ";item=" + item.identifier);
            MarkDirty();
            if (item.contentWindow != null) FlatStorageController.Current?.RequestSources();
        }

        internal void BeginValidation()
        {
            if (validationDepth++ == 0) validatedTabs.Clear();
        }

        internal void EndValidation()
        {
            validationDepth--;
            if (validationDepth == 0) RefreshPlacement();
        }

        internal void AbortValidation() { validationDepth = 0; validatedTabs.Clear(); }

        internal bool WasValidated(MachineryTabs tabs) => validationDepth > 0 &&
            validatedTabs.TryGetValue(tabs, out long version) && version == revision;
        internal long Revision => revision;
        internal void Validated(MachineryTabs tabs, long version)
        {
            if (validationDepth > 0) validatedTabs[tabs] = version;
        }

        public void Refresh(bool prepare = false)
        {
            if (!IsAvailable || !IsVisibleInViewport && !prepare) return;
            var window = Source.Window;
            bool scan = structureDirty, validate = layoutDirty || scan;
            structureDirty = layoutDirty = false;
            if (scan) ScanHierarchy();
            if (validate) window.Validate();
            RefreshPlacement();
        }

        private void ScanHierarchy()
        {
            var window = Source.Window;
            nodes.Clear(); window.handler.GetComponentsInChildren(true, nodes);
            foreach (var node in nodes)
                if (node.element is MachineryTabs transferred && Tabs.TryGetValue(transferred, out var previous) && previous != this)
                    previous.ReleaseTab(transferred);
            CaptureLayoutSizes(nodes);
            removedTabs.Clear();
            foreach (var tabs in selections.Keys)
            {
                if (tabs.handler != null && tabs.handler.transform.IsChildOf(window.handler.transform)) continue;
                removedTabs.Add(tabs);
            }
            foreach (var tabs in removedTabs)
                ReleaseTab(tabs);
            foreach (var node in nodes)
            {
                if (node.element is MachineryTabs tabs && !selections.ContainsKey(tabs))
                {
                    selections.Add(tabs, new TabSnapshot(tabs));
                    Tabs.Add(tabs, this);
                }
            }
            foreach (var pair in selections) pair.Value.Capture(pair.Key);
        }

        internal void CaptureTabState(MachineryTabs tabs) => selections[tabs].Capture(tabs);

        private void ReleaseTab(MachineryTabs tabs)
        {
            var snapshot = selections[tabs];
            snapshot.RemoveTargets(); Tabs.Remove(tabs); selections.Remove(tabs);
            if (tabs.handler != null)
            {
                layoutSizes.Restore(tabs);
                snapshot.Restore(tabs);
                tabs.Validate();
            }
            MarkDirty(true);
        }

        internal bool RefreshCatalog()
        {
            if (!IsAvailable) return false;
            catalogDirty = false;
            bool first = Catalog == null;
            bool changed;
            if (Catalog == null) { Catalog = new StorageCatalog(scope); changed = true; }
            else changed = Catalog.Refresh(scope);
            removedInventories.Clear();
            foreach (var inventory in subscriptions)
                if (!Catalog.ContainsInventory(inventory)) removedInventories.Add(inventory);
            foreach (var inventory in removedInventories) Unsubscribe(inventory);
            foreach (var inventory in Catalog.Inventories)
            {
                if (!subscriptions.Add(inventory)) continue;
                inventory.onSlotAddItemLateFunc += inventoryChanged;
                inventory.onSlotRemoveItemLateFunc += inventoryChanged;
                inventory.onSlotChangeAmountItemFunc += amountChanged;
            }
            if (!first && Catalog.StructureChanged) MarkDirty(true);
            else if (!first && Catalog.LayoutChanged) MarkDirty(false);
            if (changed) FlatStorageController.Current?.CatalogChanged();
            return changed;
        }

        internal void Observe()
        {
            if (!IsAvailable) { SetVisible(false); FlatStorageController.Current?.RequestSources(); return; }
            RefreshCatalog();
            foreach (var pair in selections)
                if (pair.Value.HasChanged(pair.Key)) { MarkDirty(true); break; }
            RefreshPlacement();
        }

        internal void RefreshPlacement()
        {
            if (disposed || Source.Window.handler == null) return;
            var window = Source.Window;
            var box = Bounds;
            if (WindowSpatialIndex.Same(placed, box) && placedLabel == Source.Label) return;
            bool geometry = !WindowSpatialIndex.Same(placed, box);
            placed = box; placedLabel = Source.Label;
            Label.text.text = Source.Label;
            NativeAccess.Place(window.handler, 0, 20, window.widthPixels, window.heightPixels);
            NativeAccess.Place(Group, Position.X, Position.Y, Width, Height);
            NativeAccess.Place(Header, 0, 0, Width, 18);
            NativeAccess.Place(Label, 5, 0, Math.Max(0, Width - 10), 18);
            if (geometry) FlatStorageController.Current?.GeometryChanged(this);
        }

        internal void SetVisible(bool show)
        {
            if (show && !IsAvailable) return;
            if (show == IsVisibleInViewport) return;
            var capture = PerformanceMonitor.Active;
            capture?.Visibility(show);
            capture?.Event(show ? "window-enter" : "window-leave", Source.Id);
            if (show) Refresh(true);
            IsVisibleInViewport = show;
            visibility.alpha = show ? 1 : 0;
            visibility.blocksRaycasts = show;
            visibility.interactable = show;
            renderCanvas.enabled = show;
            raycaster.enabled = show;
            FlatStorageController.Current?.InvalidatePointer();
        }

        public void Dispose()
        {
            var window = Source.Window;
            disposed = true;
            foreach (var inventory in new List<GameInventory>(subscriptions)) Unsubscribe(inventory);
            Groups.Remove(Group); roots.Remove(Group.transform);
            Active.Remove(window);
            foreach (var pair in selections) { pair.Value.RemoveTargets(); Tabs.Remove(pair.Key); }
            if (window.handler != null)
            {
                layoutSizes.Restore();
                foreach (var pair in selections)
                {
                    if (pair.Key.handler == null) continue;
                    pair.Value.Restore(pair.Key);
                }
                NativeAccess.CanMove.SetValue(window, movable);
                window.closeCallback = closeCallback;
                if (parent != null) window.handler.SetParent(parent);
                RestoreRect(window.rectTransform);
                window.ResizePixels(originalWidth, originalHeight);
                window.handler.gameObject.SetActive(visible && !window.isLocked && !(Source.Owner is GameItemElement item && item.IsDestroyed()));
                WindowsHandler.current?.visibleWindows.Remove(window);
                if (registered && window.IsVisible()) WindowsHandler.TryAddWindow(window);
            }
            if (Group != null) UnityEngine.Object.Destroy(Group.gameObject);
        }

        private void Unsubscribe(GameInventory inventory)
        {
            inventory.onSlotAddItemLateFunc -= inventoryChanged;
            inventory.onSlotRemoveItemLateFunc -= inventoryChanged;
            inventory.onSlotChangeAmountItemFunc -= amountChanged;
            subscriptions.Remove(inventory);
        }

        private void RestoreRect(RectTransform rect)
        {
            rect.SetParent(transformParent, false);
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
            rect.anchoredPosition = anchored; rect.sizeDelta = size; rect.localScale = scale;
            rect.SetSiblingIndex(sibling);
        }

        private void CaptureLayoutSizes(IEnumerable<TreeNodeRender> nodes)
        {
            layoutSizes.Capture(Source.Window);
            foreach (var node in nodes)
                layoutSizes.Capture(node.element);
        }

        private sealed class TabSnapshot
        {
            private readonly int selected;
            private readonly Vector2 size;
            private readonly Dictionary<GameObject, bool> stripVisibility = new Dictionary<GameObject, bool>();
            private readonly Dictionary<TreeNodeRender, (TMPro.TextOverflowModes Overflow, bool Wrapping)> textStyles =
                new Dictionary<TreeNodeRender, (TMPro.TextOverflowModes, bool)>();
            private readonly List<Transform> targets = new List<Transform>();
            private readonly List<PixelElement> pages = new List<PixelElement>();
            private readonly List<string> captions = new List<string>();
            private readonly List<(int Width, int Height, bool Hidden)> pageSizes = new List<(int, int, bool)>();
            private int capturedChildren = -1;

            public TabSnapshot(MachineryTabs tabs)
            {
                selected = tabs.selected;
                size = tabs.rectTransform.sizeDelta;
                Capture(tabs);
            }

            public void Capture(MachineryTabs tabs)
            {
                var strip = (TreeNodeRender)NativeAccess.TabStrip.GetValue(tabs);
                if (capturedChildren == strip.transform.childCount && !HasChanged(tabs)) return;
                capturedChildren = strip.transform.childCount;
                RemoveTargets(); pages.Clear(); captions.Clear(); pageSizes.Clear();
                foreach (Transform child in strip.transform)
                    if (!stripVisibility.ContainsKey(child.gameObject)) stripVisibility.Add(child.gameObject, child.gameObject.activeSelf);
                for (int i = 0; i < tabs.count; i++)
                {
                    var text = NativeTabsAccess.Get(tabs, i).Text;
                    if (!textStyles.ContainsKey(text)) textStyles.Add(text, (text.text.overflowMode, text.text.enableWordWrapping));
                    var nodes = NativeTabsAccess.Get(tabs, i);
                    targets.Add(nodes.Plate.transform); targets.Add(nodes.Text.transform);
                    tabTargets[nodes.Plate.transform] = tabs; tabTargets[nodes.Text.transform] = tabs;
                    pages.Add(nodes.Page); captions.Add(tabs.GetCaption(i));
                    pageSizes.Add((nodes.Page?.widthPixels ?? 0, nodes.Page?.heightPixels ?? 0,
                        nodes.Page is MachineryPanel panel && panel.isHidden));
                }
            }

            internal bool HasChanged(MachineryTabs tabs)
            {
                if (pages.Count != tabs.count) return true;
                for (int i = 0; i < pages.Count; i++)
                {
                    var page = tabs.GetPage(i);
                    if (page != pages[i] || captions[i] != tabs.GetCaption(i) ||
                        pageSizes[i] != (page?.widthPixels ?? 0, page?.heightPixels ?? 0, page is MachineryPanel panel && panel.isHidden)) return true;
                }
                return false;
            }

            internal void RemoveTargets()
            {
                foreach (var target in targets) tabTargets.Remove(target);
                targets.Clear();
            }

            public void Restore(MachineryTabs tabs)
            {
                tabs.Select(Math.Min(selected, tabs.count - 1));
                tabs.rectTransform.sizeDelta = size;
                var strip = (TreeNodeRender)NativeAccess.TabStrip.GetValue(tabs);
                foreach (Transform child in strip.transform)
                    child.gameObject.SetActive(!stripVisibility.TryGetValue(child.gameObject, out bool active) || active);
                for (int i = 0; i < tabs.count; i++)
                {
                    var text = NativeTabsAccess.Get(tabs, i).Text;
                    text.text.text = tabs.GetCaption(i) ?? "";
                    if (textStyles.TryGetValue(text, out var style))
                    {
                        text.text.overflowMode = style.Overflow;
                        text.text.enableWordWrapping = style.Wrapping;
                    }
                }
            }
        }
    }
}
