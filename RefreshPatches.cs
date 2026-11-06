using System;
using HarmonyLib;

namespace ProbablyStolen_FlatStorage
{
    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Validate))]
    internal static class ObserveWindowLayout
    {
        private static void Prefix(PixelWindow __instance, out WindowLease __state)
        {
            WindowLease.Active.TryGetValue(__instance, out __state);
            __state?.BeginValidation();
        }

        private static void Postfix(WindowLease __state)
        {
            __state?.EndValidation();
            FlatStorageController.Current?.InvalidatePointer();
        }

        private static Exception Finalizer(Exception __exception, WindowLease __state)
        {
            if (__exception != null) __state?.AbortValidation();
            return __exception;
        }
    }

    internal static class RefreshNotifications
    {
        internal static void Item(GameItem item, bool layout)
        {
            var controller = FlatStorageController.Current;
            if (controller?.IsOpen != true) return;
            if (item is GameItemElement element && !element.IsDestroyed())
                WindowLease.Find(element.handler.transform)?.MarkDirty(false, layout);
            if (item.contentWindow != null && WindowLease.Active.TryGetValue(item.contentWindow, out var contents)) contents.MarkDirty(false, layout);
        }

        internal static void Tabs(MachineryTabs tabs)
        {
            if (WindowLease.Tabs.TryGetValue(tabs, out var lease)) lease.MarkDirty(true);
            FlatStorageController.Current?.InvalidatePointer();
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.SetUnitCount))]
    internal static class ObserveItemAmount
    {
        private static void Postfix(GameItem __instance) => RefreshNotifications.Item(__instance, false);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.ValidateShapeState))]
    internal static class ObserveItemShape
    {
        private static void Postfix(GameItem __instance) => RefreshNotifications.Item(__instance, true);
    }

    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.AddTab))]
    internal static class ObserveTabAdded
    {
        private static void Postfix(MachineryTabs __instance) => RefreshNotifications.Tabs(__instance);
    }

    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.Attach))]
    internal static class ObserveTabAttached
    {
        private static void Postfix(MachineryTabs __instance) => RefreshNotifications.Tabs(__instance);
    }

    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.Detach))]
    internal static class ObserveTabDetached
    {
        private static void Postfix(MachineryTabs __instance) => RefreshNotifications.Tabs(__instance);
    }

    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.SetCaption))]
    internal static class ObserveTabCaption
    {
        private static void Postfix(MachineryTabs __instance) => RefreshNotifications.Tabs(__instance);
    }

    [HarmonyPatch(typeof(MachineryTabs), nameof(MachineryTabs.Select))]
    internal static class ObserveTabSelection
    {
        private static void Prefix(MachineryTabs __instance, out int __state) => __state = __instance.selected;
        private static void Postfix(MachineryTabs __instance, int __state)
        {
            if (__state != __instance.selected) FlatStorageController.Current?.InvalidatePointer();
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Lock))]
    internal static class ObserveWindowLock
    {
        private static void Postfix(PixelWindow __instance)
        {
            if (WindowLease.Active.TryGetValue(__instance, out var lease)) lease.SetVisible(false);
            FlatStorageController.Current?.RequestSources();
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Unlock))]
    internal static class ObserveWindowUnlock
    {
        private static void Postfix() => FlatStorageController.Current?.RequestSources();
    }
}
