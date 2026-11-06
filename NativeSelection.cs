using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ProbablyStolen_FlatStorage
{
    internal static class NativeSelection
    {
        private static readonly FieldInfo State = AccessTools.Field(typeof(ItemMultiSelectHandler), "state")
            ?? throw new MissingFieldException(typeof(ItemMultiSelectHandler).FullName, "state");
        private static readonly FieldInfo Items = AccessTools.Field(typeof(ItemMultiSelectHandler), "selectedItems")
            ?? throw new MissingFieldException(typeof(ItemMultiSelectHandler).FullName, "selectedItems");
        private static readonly object pendingBand = Enum.Parse(State.FieldType, "PendingBand");
        private static readonly object banding = Enum.Parse(State.FieldType, "Banding");
        internal static bool Captured;
        internal static Vector2 GroupScale = Vector2.one;
        internal static bool IsBanding
        {
            get
            {
                if (!Captured || ItemMultiSelectHandler.current == null) return false;
                var state = State.GetValue(ItemMultiSelectHandler.current);
                return pendingBand.Equals(state) || banding.Equals(state);
            }
        }

        internal static void CollectDragPreviews(ISet<Transform> roots)
        {
            var handler = ItemMultiSelectHandler.current;
            if (!Captured || handler == null || !handler.IsGroupDragging) return;
            foreach (var item in (List<GameItemElement>)Items.GetValue(handler))
                if (!item.IsDestroyed()) roots.Add(item.fakeMiddle.transform);
        }

        internal static void ScaleGroup(List<GameItemElement> items, Dictionary<GameItemElement, Vector2> offsets)
        {
            var controller = FlatStorageController.Current;
            if (!Captured || controller == null || !controller.IsOpen) return;
            Vector2 scale = controller.InventoryDisplayScale;
            Vector2 ratio = new Vector2(scale.x / GroupScale.x, scale.y / GroupScale.y);
            Vector2 pointer = RenderHandler.current.overlayRoot.rectTransform.InverseTransformPoint(Input.mousePosition);
            foreach (var item in items)
            {
                if (item.IsDestroyed()) continue;
                var center = ratio != Vector2.one ? item.GetCenteredPosition() : default;
                controller.ScaleDragPreview(item);
                if (ratio != Vector2.one)
                {
                    if (offsets.TryGetValue(item, out var offset)) offsets[item] = Vector2.Scale(offset, ratio);
                    item.SetCenteredPosition(pointer.x + (center.Item1 - pointer.x) * ratio.x,
                        pointer.y + (center.Item2 - pointer.y) * ratio.y);
                }
            }
            GroupScale = scale;
        }
    }

    internal static class CanvasInventoryHover
    {
        internal static void Enter(InputActionHandler handler, out bool? previous)
        {
            previous = null;
            if (!(handler is ItemMouseDragHandler) && !(handler is ItemMultiSelectHandler)) return;
            if (handler is ItemMultiSelectHandler multi && !multi.HasSelection) NativeSelection.Captured = false;
            var controller = FlatStorageController.Current;
            if (controller == null || !controller.IsOpen || StoreUIManager.Instance == null) return;
            previous = StoreUIManager.Instance.hoveringStoreUI;
            bool continuing = handler is ItemMultiSelectHandler && NativeSelection.Captured && controller.CanContinueSelection;
            StoreUIManager.Instance.hoveringStoreUI = !(continuing || controller.OwnsInventoryPointer);
        }

        internal static void Exit(bool? previous)
        {
            if (previous.HasValue && StoreUIManager.Instance != null) StoreUIManager.Instance.hoveringStoreUI = previous.Value;
        }
    }

    [HarmonyPatch(typeof(InputActionHandler), nameof(InputActionHandler.UpdateMouseMove))]
    internal static class CanvasInventoryMove
    {
        private static void Prefix(InputActionHandler __instance, out bool? __state) => CanvasInventoryHover.Enter(__instance, out __state);
        private static void Postfix(bool? __state) => CanvasInventoryHover.Exit(__state);
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "IsPointerOverDragSurface")]
    internal static class CanvasSelectionDragSurface
    {
        private static bool Prefix(Vector2 screenPosition, ref bool __result)
        {
            var controller = FlatStorageController.Current;
            if (controller == null || !controller.IsOpen) return true;
            var region = controller.SelectionRegionAt(screenPosition);
            if (region == SelectionPointerRegion.Outside) return true;
            __result = !SelectionInput.AllowsBand(region);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventPress")]
    internal static class CaptureCanvasSelection
    {
        private static void Postfix(ItemMultiSelectHandler __instance, ISet<KeyCode> keysPressed,
            ref PixelWindow ___bandWindow, ref GameInventory ___bandInventory)
        {
            var controller = FlatStorageController.Current;
            if (controller == null || !controller.OwnsInventoryPointer || !keysPressed.Contains(__instance.key)) return;
            NativeSelection.Captured = __instance.HasSelection;
            if (NativeSelection.IsBanding)
            {
                // 框选范围覆盖画布内可见仓储，原生逐格检测保留遮挡检查。
                ___bandWindow = null;
                ___bandInventory = null;
            }
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventReset")]
    internal static class ResetCanvasSelection
    {
        private static void Postfix() { NativeSelection.Captured = false; NativeSelection.GroupScale = Vector2.one; }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "ComputeBandItems")]
    internal static class CanvasSelectionBounds
    {
        private static bool Prefix(ref Rect bandRect, ref List<GameItemElement> __result)
        {
            var controller = FlatStorageController.Current;
            if (!NativeSelection.Captured || controller == null || !controller.IsOpen) return true;
            var view = controller.SelectionViewport;
            var clipped = SelectionInput.Clip(new Box(bandRect.x, bandRect.y, bandRect.width, bandRect.height),
                new Box(view.x, view.y, view.width, view.height));
            bandRect = new Rect(clipped.X, clipped.Y, clipped.Width, clipped.Height);
            if (clipped.Width > 0 && clipped.Height > 0) return true;
            __result = new List<GameItemElement>();
            return false;
        }

        private static void Postfix(ref List<GameItemElement> __result)
        {
            if (NativeSelection.Captured) __result.RemoveAll(item => !FlatDragOwnership.Owns(item));
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "BandOverlapsItemShape")]
    internal static class CanvasSelectionOcclusion
    {
        private static void Postfix(GameItemElement element, Vector2 sweptCenter, ref bool __result)
        {
            if (!NativeSelection.Captured || !__result) return;
            var controller = FlatStorageController.Current;
            __result = controller != null && controller.IsOpen && controller.IsTopItemAt(element, sweptCenter);
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "StartGroupDrag")]
    internal static class StartCanvasGroupDrag
    {
        private static void Postfix(ItemMultiSelectHandler __instance, List<GameItemElement> ___selectedItems,
            Dictionary<GameItemElement, Vector2> ___dragOffsets)
        {
            var controller = FlatStorageController.Current;
            if (!NativeSelection.Captured || !__instance.IsGroupDragging || controller == null || !controller.IsOpen) return;
            NativeSelection.GroupScale = controller.InventoryDisplayScale;
            NativeSelection.ScaleGroup(___selectedItems, ___dragOffsets);
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "MoveGhosts")]
    internal static class ScaleCanvasGroupDrag
    {
        private static void Prefix(List<GameItemElement> ___selectedItems, Dictionary<GameItemElement, Vector2> ___dragOffsets) =>
            NativeSelection.ScaleGroup(___selectedItems, ___dragOffsets);
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    internal static class ValidateCanvasGroupDrop
    {
        private static bool Prefix(ItemMultiSelectHandler __instance)
        {
            if (!NativeSelection.Captured) return true;
            var controller = FlatStorageController.Current;
            if (controller != null && controller.OwnsInventoryDropPointer) return true;
            __instance.Cancel();
            return false;
        }
    }
}
