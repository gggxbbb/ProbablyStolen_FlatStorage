namespace ProbablyStolen_FlatStorage
{
    internal static class OpenedContainers
    {
        internal static bool CanExpose(GameItem item)
        {
            if (item.IsTag(GunHelper.ADV_GUN_TAG)) return false;
            var window = item.contentWindow;
            bool tracked = item.IsTag(ContainerHelper.TRACK_OPENED_TAG);
            bool container = tracked || item.IsTag(ContainerHelper.CONTAINER_TAG) ||
                item.IsTag(ContainerHelper.BACKPACK_TAG) || item.IsTag(ContainerHelper.POUCH_TAG) ||
                item.IsTag(ContainerHelper.SAFE_TAG) || item.IsTag(LockHelper.LOCKED_CONTAINER_TAG) ||
                item.identifier == "luxury_supply_crate";
            bool locked = window == null || window.isLocked ||
                (item.IsTag(LockHelper.LOCKED_CONTAINER_TAG) && item.GetTagReadonly(LockHelper.LOCK_STATUS).GetBool());
            bool remembered = item.uniqueId != 0 && (ModHelper.GetModDataBool(ModEntry.Id, "opened:" + item.uniqueId) ||
                (PlayerStore.Instance.contentWindowPositions?.ContainsKey(item.uniqueId) ?? false));
            if (window != null && !WindowLease.Active.ContainsKey(window) && window.handler.gameObject.activeInHierarchy)
                remembered = true;
            return StorageVisibility.CanExpose(window != null, GeneralHelper.IsItemOwned(item), locked, container,
                tracked, item.IsTag(ContainerHelper.OPENED_TAG), remembered);
        }

        internal static void Remember(PixelWindow window)
        {
            if (PlayerStore.Instance == null || window.isLocked || !window.IsVisible() || WindowLease.Active.ContainsKey(window)) return;
            foreach (var item in window.parentItems)
                if (item.contentWindow == window && GeneralHelper.IsItemOwned(item))
                    ModHelper.SetModDataBool(ModEntry.Id, "opened:" + PersistentId(item), true);
        }

        internal static int PersistentId(GameItem item)
        {
            int id = item.GetUniqueID();
            if (id == 0) throw new System.InvalidOperationException("Invalid Flat Storage item identity: " + id);
            return id;
        }
    }
}
