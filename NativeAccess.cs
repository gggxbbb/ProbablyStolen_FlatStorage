using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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

        internal static void Check()
        {
            NativeLayoutSizes.Check();
            foreach (var member in new MemberInfo[] { CanMove, TabWidth, TabHeight, TabStrip, TabUnderline, Abort, RefreshDrag })
                if (member == null) throw new MissingMemberException("Flat Storage 与当前游戏接口不兼容。");
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
                RefreshDrag.Invoke(ItemMouseDragHandler.current, new object[] { Vector3.zero });
        }

        internal static void Place(TreeNodeRender node, float x, float y, float width, float height)
        {
            var rect = node.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }

    internal sealed class WindowLease : IDisposable
    {
        internal static readonly Dictionary<PixelWindow, WindowLease> Active = new Dictionary<PixelWindow, WindowLease>();
        internal static readonly Dictionary<MachineryTabs, WindowLease> Tabs = new Dictionary<MachineryTabs, WindowLease>();
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
        private readonly NativeLayoutSizes layoutSizes = new NativeLayoutSizes();
        internal bool OriginallyVisible => visible;
        public float Width => Math.Max(Math.Max(120, Source.Window.widthPixels), Label.text.preferredWidth + 10);
        public float Height => Source.Window.heightPixels + 20;
        public Box Bounds => new Box(Position.X, Position.Y, Width, Height);

        public WindowLease(StorageSource source, TreeNodeRender canvas, GroupPosition position)
        {
            Source = source; Position = position;
            var window = source.Window;
            var rect = window.rectTransform;
            parent = window.handler.parent; transformParent = rect.parent;
            anchorMin = rect.anchorMin; anchorMax = rect.anchorMax; pivot = rect.pivot;
            anchored = rect.anchoredPosition; size = rect.sizeDelta; scale = rect.localScale;
            sibling = rect.GetSiblingIndex(); visible = window.IsVisible(); movable = window.canMove;
            originalWidth = window.widthPixels; originalHeight = window.heightPixels;
            registered = WindowsHandler.current.visibleWindows.Contains(window);
            closeCallback = window.closeCallback;
            CaptureLayoutSizes();
            Group = FlatUI.Panel(canvas, "Storage Group", new Color(.10f, .12f, .14f, .96f));
            Header = FlatUI.Panel(Group, "Group Header", new Color(.20f, .25f, .29f));
            Label = FlatUI.Text(Header, source.Label, 7);
            Header.InitEventHandler();
            Header.eventHandler.mouseDownFunc.Add(e =>
            {
                if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                    FlatStorageController.Current.BeginGroupDrag(this);
                return true;
            });
            visibility = Group.gameObject.AddComponent<CanvasGroup>();
            Active.Add(window, this);
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
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Refresh()
        {
            var window = Source.Window;
            CaptureLayoutSizes();
            foreach (var tabs in new List<MachineryTabs>(selections.Keys))
            {
                if (tabs.handler != null && tabs.handler.transform.IsChildOf(window.handler.transform)) continue;
                Tabs.Remove(tabs);
                if (tabs.handler != null)
                {
                    layoutSizes.Restore(tabs);
                    selections[tabs].Restore(tabs);
                    tabs.Validate();
                }
                selections.Remove(tabs);
            }
            foreach (var node in window.handler.GetComponentsInChildren<TreeNodeRender>(true))
            {
                if (node.element is MachineryTabs tabs && !selections.ContainsKey(tabs))
                {
                    selections.Add(tabs, new TabSnapshot(tabs));
                    Tabs.Add(tabs, this);
                }
            }
            window.Validate();
            Label.text.text = Source.Label;
            NativeAccess.Place(window.handler, 0, 20, window.widthPixels, window.heightPixels);
            NativeAccess.Place(Group, Position.X, Position.Y, Width, Height);
            NativeAccess.Place(Header, 0, 0, Width, 18);
            NativeAccess.Place(Label, 5, 0, Width - 10, 18);
        }

        public void Cull(Box viewport)
        {
            bool show = Bounds.Intersects(viewport);
            visibility.alpha = show ? 1 : 0;
            visibility.blocksRaycasts = show;
            visibility.interactable = show;
        }

        public void Dispose()
        {
            var window = Source.Window;
            Active.Remove(window);
            foreach (var pair in selections) Tabs.Remove(pair.Key);
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

        private void RestoreRect(RectTransform rect)
        {
            rect.SetParent(transformParent, false);
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
            rect.anchoredPosition = anchored; rect.sizeDelta = size; rect.localScale = scale;
            rect.SetSiblingIndex(sibling);
        }

        private void CaptureLayoutSizes()
        {
            layoutSizes.Capture(Source.Window);
            foreach (var node in Source.Window.handler.GetComponentsInChildren<TreeNodeRender>(true))
                layoutSizes.Capture(node.element);
        }

        private sealed class TabSnapshot
        {
            private readonly int selected;
            private readonly Vector2 size;
            private readonly Dictionary<GameObject, bool> stripVisibility = new Dictionary<GameObject, bool>();

            public TabSnapshot(MachineryTabs tabs)
            {
                selected = tabs.selected;
                size = tabs.rectTransform.sizeDelta;
                var strip = (TreeNodeRender)NativeAccess.TabStrip.GetValue(tabs);
                foreach (Transform child in strip.transform) stripVisibility.Add(child.gameObject, child.gameObject.activeSelf);
            }

            public void Restore(MachineryTabs tabs)
            {
                tabs.Select(Math.Min(selected, tabs.count - 1));
                tabs.rectTransform.sizeDelta = size;
                var strip = (TreeNodeRender)NativeAccess.TabStrip.GetValue(tabs);
                foreach (Transform child in strip.transform)
                    child.gameObject.SetActive(!stripVisibility.TryGetValue(child.gameObject, out bool active) || active);
                for (int i = 0; i < tabs.count; i++)
                    strip.transform.GetChild(i * 2 + 1).GetComponent<TreeNodeRender>().text.text = tabs.GetCaption(i) ?? "";
            }
        }
    }
}
