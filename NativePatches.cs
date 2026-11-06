using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen_FlatStorage
{
    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.Validate))]
    internal static class FlatTabsLayout
    {
        private static readonly HashSet<MachineryTabs> validating = new HashSet<MachineryTabs>();

        private static bool Prefix(MachineryTabs __instance)
        {
            if (!WindowLease.Tabs.TryGetValue(__instance, out var lease)) return true;
            if (lease.WasValidated(__instance)) return false;
            if (!validating.Add(__instance)) return false;
            long revision = lease.Revision;
            try
            {
                lease.CaptureTabState(__instance);
                var strip = (TreeNodeRender)NativeAccess.TabStrip.GetValue(__instance);
                var underline = (TreeNodeRender)NativeAccess.TabUnderline.GetValue(__instance);
                float x = 0, height = 0;
                for (int i = 0; i < __instance.count; i++)
                {
                    var nodes = NativeTabsAccess.Get(__instance, i);
                    var page = nodes.Page;
                    if (page == null) continue;
                    var plate = nodes.Plate;
                    var text = nodes.Text;
                    text.text.text = StorageLabels.Title(lease.Source.OwnLabel, __instance.GetCaption(i),
                        lease.Source.Owner?.GetDisplayName() ?? lease.Source.OwnLabel);
                    text.text.enableWordWrapping = false;
                    text.text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                    bool visible = !(page is MachineryPanel panel && panel.isHidden);
                    plate.gameObject.SetActive(visible);
                    text.gameObject.SetActive(visible);
                    page.handler.gameObject.SetActive(visible);
                    if (!visible) continue;
                    page.Validate();
                    float width = Mathf.Max(32, page.widthPixels);
                    NativeAccess.Place(plate, x, 0, width, 12);
                    NativeAccess.Place(text, x + 4, 0, width - 8, 12);
                    NativeAccess.Place(page.handler, x, 0, page.widthPixels, page.heightPixels);
                    page.handler.gameObject.SetActive(true);
                    x += width + 12;
                    height = Mathf.Max(height, page.heightPixels);
                }
                int totalWidth = Mathf.CeilToInt(Mathf.Max(0, x - 12));
                int totalHeight = Mathf.CeilToInt(height + 16);
                NativeAccess.TabWidth.SetValue(__instance, totalWidth);
                NativeAccess.TabHeight.SetValue(__instance, totalHeight);
                NativeAccess.Place(strip, 0, 0, totalWidth, 12);
                NativeAccess.Place(underline, 0, 13, totalWidth, 1);
                NativeAccess.Place(__instance.inner, 0, 16, totalWidth, height);
                __instance.rectTransform.sizeDelta = new Vector2(totalWidth, totalHeight);
                lease.Validated(__instance, revision);
                return false;
            }
            finally { validating.Remove(__instance); }
        }
    }

    [HarmonyPatch(typeof(MachineryTabs), "RefreshTabs")]
    internal static class FlatTabsVisibility
    {
        private static void Postfix(MachineryTabs __instance)
        {
            if (!WindowLease.Tabs.ContainsKey(__instance)) return;
            for (int i = 0; i < __instance.count; i++)
            {
                var page = __instance.GetPage(i);
                page?.handler.gameObject.SetActive(!(page is MachineryPanel panel && panel.isHidden));
            }
        }
    }

    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.ResizePixels))]
    internal static class FlatTabsResize
    {
        private static bool Prefix(MachineryTabs __instance)
        {
            if (!WindowLease.Tabs.ContainsKey(__instance)) return true;
            __instance.Validate();
            return false;
        }
    }

    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.OnPointerDown))]
    internal static class PageContext
    {
        private static bool Prefix(TreeNodeRender __instance, PointerEventData eventData)
        {
            var controller = FlatStorageController.Current;
            if (controller == null || !controller.IsOpen) return true;
            if (controller.Panning || Input.GetKey(KeyCode.Space) || eventData.button == PointerEventData.InputButton.Middle) return false;
            foreach (var pair in WindowLease.Tabs.ToArray())
            {
                var tabs = pair.Key;
                for (int i = 0; i < tabs.count; i++)
                {
                    var page = tabs.GetPage(i);
                    if (page != null && __instance.transform.IsChildOf(page.handler.transform))
                    { tabs.Select(i, true); break; }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Hide))]
    internal static class KeepFlatWindowVisible
    {
        private static bool Prefix(PixelWindow __instance, ref PixelWindow __result)
        {
            if (!WindowLease.Active.ContainsKey(__instance)) return true;
            __result = __instance;
            return false;
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Show), new System.Type[0])]
    internal static class FlatWindowShow
    {
        private static void Postfix(PixelWindow __instance)
        { OpenedContainers.Remember(__instance); FlatStorageController.Current?.RequestSources(); FlatStorageController.Current?.InvalidatePointer(); }

        private static bool Prefix(PixelWindow __instance, ref PixelWindow __result)
        {
            if (!WindowLease.Active.ContainsKey(__instance)) return true;
            __result = __instance;
            return false;
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Show), typeof(Vector2), typeof(Vector2), typeof(bool))]
    internal static class FlatWindowShowAt
    {
        private static void Postfix(PixelWindow __instance)
        { OpenedContainers.Remember(__instance); FlatStorageController.Current?.RequestSources(); FlatStorageController.Current?.InvalidatePointer(); }

        private static bool Prefix(PixelWindow __instance, ref PixelWindow __result)
        {
            if (!WindowLease.Active.ContainsKey(__instance)) return true;
            __result = __instance;
            return false;
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.FixBounds))]
    internal static class FlatWindowBounds
    {
        private static bool Prefix(PixelWindow __instance) => !WindowLease.Active.ContainsKey(__instance);
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.ToFront))]
    internal static class FlatWindowFront
    {
        private static bool Prefix(PixelWindow __instance) => !WindowLease.Active.ContainsKey(__instance);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.RememberContentWindowPositionIfMine))]
    internal static class PreserveWindowPosition
    {
        private static bool Prefix(PixelWindow window) => !WindowLease.Active.ContainsKey(window);
    }

    [HarmonyPatch(typeof(InputActionHandler), nameof(InputActionHandler.UpdatePress))]
    internal static class CanvasInputPriority
    {
        private static void Prefix(InputActionHandler __instance, ref ISet<KeyCode> keysPressed, out bool? __state)
        {
            __state = null;
            var controller = FlatStorageController.Current;
            if (controller == null || (!controller.IsOpen && controller.EscapeFrame != Time.frameCount)) return;
            if (keysPressed.Count == 0) { CanvasInventoryHover.Enter(__instance, out __state); return; }
            keysPressed = new HashSet<KeyCode>(keysPressed);
            if (controller.EscapeFrame == Time.frameCount) keysPressed.Remove(KeyCode.Escape);
            if (!controller.IsOpen) return;
            if ((__instance is ItemMouseDragHandler || __instance is ItemMultiSelectHandler) && !controller.OwnsInventoryPointer)
                keysPressed.Remove(KeyCode.Mouse0);
            keysPressed.Remove(KeyCode.Mouse2);
            if (controller.RightPanning || controller.PanReleaseFrame == Time.frameCount) keysPressed.Remove(KeyCode.Mouse1);
            if (controller.Panning || controller.MovingGroup || Input.GetKey(KeyCode.Space))
                keysPressed.Remove(KeyCode.Mouse0);
            CanvasInventoryHover.Enter(__instance, out __state);
        }

        private static void Postfix(bool? __state) => CanvasInventoryHover.Exit(__state);
    }

    [HarmonyPatch(typeof(InputActionHandler), nameof(InputActionHandler.UpdateMouseScroll))]
    internal static class CanvasScrollPriority
    {
        private static bool Prefix()
        {
            if (Input.mouseScrollDelta == Vector2.zero) return true;
            var c = FlatStorageController.Current;
            return c == null || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || !c.PointerInViewport || !c.OwnsPointer();
        }
    }

    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.OnScroll))]
    internal static class CanvasPointerScrollPriority
    {
        private static bool Prefix() => FlatStorageController.Current == null || !FlatStorageController.Current.PointerInViewport ||
            !FlatStorageController.Current.OwnsPointer() ||
            Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static void Postfix() => FlatStorageController.Current?.InvalidatePointer();
    }

    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.OnPointerClick))]
    internal static class PreventPanClick
    {
        private static bool Prefix()
        {
            var c = FlatStorageController.Current;
            return c == null || !c.IsOpen || (!c.Panning && c.PanReleaseFrame != Time.frameCount && !Input.GetKey(KeyCode.Space));
        }
    }

    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.SetParent), typeof(TreeNodeRender), typeof(bool))]
    internal static class PreserveInventoryItemScale
    {
        private static void Prefix(TreeNodeRender __instance, TreeNodeRender parent, ref bool worldPositionStays, out WindowLease __state)
        {
            __state = FlatStorageController.Current?.IsOpen == true && __instance.element != null ? WindowLease.Find(__instance.transform) : null;
            if (!(__instance.element is GameItemElement item) || item.handler != __instance) return;
            // 物品通过临时父节点转移时保留局部变换，缩放由目标容器继承。
            worldPositionStays = RenderingTransfer.KeepWorldTransform(worldPositionStays, true,
                FlatDragOwnership.Owns(item), parent == RenderHandler.current.tempRoot);
        }

        private static void Postfix(TreeNodeRender __instance, WindowLease __state)
        {
            var controller = FlatStorageController.Current;
            if (controller?.IsOpen == true)
            {
                controller.InvalidatePointer();
                if (__instance.element != null && !(__instance.element is GameItem))
                {
                    var lease = WindowLease.Find(__instance.transform);
                    __state?.MarkDirty(true);
                    if (lease != __state) lease?.MarkDirty(true);
                }
            }
            if (__instance.element is GameItemElement item && item.handler == __instance && FlatDragOwnership.Owns(item))
                __instance.rectTransform.localScale = Vector3.one;
        }
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ValidateSelect))]
    internal static class PreserveActiveDragPreview
    {
        private static bool Prefix(GameItemElement __instance)
        {
            // 原生刷新会归还预览并重置位置；放下和取消先归还预览，随后正常刷新。
            return !FlatDragOwnership.Owns(__instance) || !FlatStorageController.IsItemDragging ||
                __instance.fakeMiddle.parent != RenderHandler.current.overlayRoot;
        }
    }

    internal static class FlatDragOwnership
    {
        internal static bool Owns(GameItemElement item)
        {
            if (FlatStorageController.Current == null || !FlatStorageController.Current.IsOpen || item.IsDestroyed()) return false;
            var window = item.handler.FindParentWindow();
            return window != null && WindowLease.Active.ContainsKey(window);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ToggleFakeBackground), typeof(bool), typeof(Vector4))]
    internal static class RestoreReturnedDragPreview
    {
        private static void Prefix(GameItemElement __instance, bool enabled, out bool __state)
        {
            __state = !enabled && FlatDragOwnership.Owns(__instance);
        }

        private static void Postfix(GameItemElement __instance, bool __state)
        {
            if (!__state) return;
            var preview = __instance.fakeMiddle.rectTransform;
            var original = __instance.middle.rectTransform;
            preview.anchorMin = original.anchorMin;
            preview.anchorMax = original.anchorMax;
            preview.pivot = original.pivot;
            preview.localRotation = original.localRotation;
            preview.localScale = original.localScale;
            preview.sizeDelta = original.sizeDelta;
            preview.anchoredPosition3D = original.anchoredPosition3D;
        }
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.GetCenteredPosition))]
    internal static class ScaledDragCenter
    {
        private static bool Prefix(GameItemElement __instance, ref (float, float) __result)
        {
            if (FlatStorageController.Current == null || !FlatStorageController.Current.IsOpen || __instance.fakeMiddle.parent != RenderHandler.current.overlayRoot) return true;
            Vector2 center = __instance.fakeMiddle.rectTransform.parent.InverseTransformPoint(__instance.GetFakeRotatorCenteredPosition());
            __result = (center.x, center.y);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.SetCenteredPosition))]
    internal static class ScaledDragPosition
    {
        private static bool Prefix(GameItemElement __instance, float x, float y)
        {
            if (FlatStorageController.Current == null || !FlatStorageController.Current.IsOpen || __instance.fakeMiddle.parent != RenderHandler.current.overlayRoot) return true;
            var rect = __instance.fakeMiddle.rectTransform;
            Vector3 target = rect.parent.TransformPoint(new Vector3(x, y));
            Vector3 center = __instance.GetFakeRotatorCenteredPosition();
            rect.position += new Vector3(target.x - center.x, target.y - center.y, 0);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SaveGame))]
    internal static class SaveLayout
    {
        private static void Prefix() => FlatStorageController.Current?.Save();
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class ValidateDrop
    {
        private static bool Prefix(ItemMouseDragHandler __instance, ref GameInventory ___lastInventory, int ___currentRemove)
        {
            var controller = FlatStorageController.Current;
            if (controller == null || !controller.IsOpen) return true;
            var item = __instance.GetCurrentItem();
            if (item == null || (item is GameItemElement element && element.IsDestroyed()) || !item.MayRemove() || !controller.OwnsInventoryDropPointer)
            { NativeAccess.CancelDrag(); return false; }
            NativeAccess.RefreshDragTarget();
            if (___lastInventory != null && item is GameItemElement graphic)
            {
                var slot = ___lastInventory.TryInventorySlot(item, ___currentRemove, graphic.GetFakeRotatorCenteredPosition(), graphic.tempShape, item.modifiedState);
                if (!GeneralHelper.MayPlayerInsertInto(___lastInventory) || slot == null || !slot.IsValid())
                    controller.Notice(ModText.Get("drop.rejected"));
            }
            return true;
        }
    }
}
