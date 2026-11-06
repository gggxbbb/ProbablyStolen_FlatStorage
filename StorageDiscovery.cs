using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class StorageSource
    {
        public string Id;
        public string Label;
        public string OwnLabel;
        public string ParentId;
        public PixelWindow Window;
        public GameItem Owner;
    }

    internal static class StorageDiscovery
    {
        public static string SceneKey => ModHelper.IsOutsideStore()
            ? "outside:" + ModHelper.GetCurrentAfterhourLocationId() : "store";

        public static List<StorageSource> Collect()
        {
            var result = new List<StorageSource>();
            foreach (var source in Discover()) if (source != null) result.Add(source);
            result.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.Id, b.Id));
            return result;
        }

        internal static IEnumerable<StorageSource> Discover()
        {
            var visited = new HashSet<GraphNodeStorage>();
            var windows = new HashSet<PixelWindow>();
            var entry = EmporiumEntry.Instance;
            if (entry == null) yield break;

            IEnumerable<StorageSource> Visit(GraphNodeStorage node, string path, string parentId)
            {
                if (node == null || !visited.Add(node)) yield break;
                yield return null;
                if (node is PixelWindow window && window.isLocked) yield break;
                if (node is GameItem item)
                {
                    if (item is GameItemElement element && element.IsDestroyed()) yield break;
                    var content = item.contentWindow;
                    if (!OpenedContainers.CanExpose(item)) yield break;
                    string name = StorageLabels.Name(GeneralHelper.GetCustomName(item), item.GetDisplayName());
                    var parentItem = item.parentInventory == null ? null : GraphUtils.FindParentType<GameItem>(item.parentInventory);
                    string parentLabel = parentItem == null ? path : StorageLabels.Name(GeneralHelper.GetCustomName(parentItem), parentItem.GetDisplayName());
                    if (windows.Add(content)) yield return new StorageSource
                    {
                        Id = "item:" + OpenedContainers.PersistentId(item), Window = content, Owner = item,
                        ParentId = parentItem == null ? parentId : "item:" + OpenedContainers.PersistentId(parentItem),
                        OwnLabel = name, Label = StorageLabels.Title(parentLabel, name, item.GetDisplayName())
                    };
                    foreach (var source in Visit(content, name, "item:" + OpenedContainers.PersistentId(item))) yield return source;
                    yield break;
                }
                var children = node.children;
                if (children == null) yield break;
                foreach (var child in children.ToArray())
                    foreach (var source in Visit(child, path, parentId)) yield return source;
            }

            IEnumerable<StorageSource> Root(string id, string label, PixelWindow window)
            {
                if (window == null || window.isLocked || !windows.Add(window)) yield break;
                label = StorageLabels.Name(window.titleString, label);
                yield return new StorageSource { Id = "root:" + id, Label = label, OwnLabel = label, Window = window };
                foreach (var source in Visit(window, label, "root:" + id)) yield return source;
            }

            var roots = new List<(string Id, string Label, PixelWindow Window)>();
            void AddRoot(string id, string label, PixelWindow window) => roots.Add((id, label, window));

            if (ModHelper.IsOutsideStore())
            {
                AddRoot("afterhour", ModText.Get("storage.afterhour"), entry.afterhourWindow);
                if (entry.bazarLeftInvWindow != null && entry.bazarLeftInvWindow.handler.gameObject.activeInHierarchy)
                    AddRoot("bazar", ModText.Get("storage.bazar"), entry.bazarLeftInvWindow);
            }
            else
            {
                AddRoot("main", ModText.Get("storage.main"), entry.invWindow);
                AddRoot("back", ModText.Get("storage.back"), entry.backInvWindow);
                AddRoot("front", ModText.Get("storage.front"), entry.frontInvWindow);
                AddRoot("showcase", ModText.Get("storage.showcase"), entry.showcaseWindow);
                AddRoot("documents", ModText.Get("storage.documents"), entry.docInvWindow);
                AddRoot("hidden", ModText.Get("storage.hidden"), entry.hiddenWindow);
                AddRoot("cassette", ModText.Get("storage.cassette"), entry.cassettePlayerWindow);
                if (entry.vendingMachineElement.childItems.Count > 0) AddRoot("vending", ModText.Get("storage.vending"), entry.vendingMachineWindow);
                if (entry.vendingFountainElement.childItems.Count > 0) AddRoot("fountain", ModText.Get("storage.fountain"), entry.vendingFountainWindow);
                if (EmporiumEntry.IsWaterTapUnlocked()) AddRoot("faucet", ModText.Get("storage.faucet"), entry.faucetWindow);
                AddRoot("drain", ModText.Get("storage.drain"), entry.drainInvWindow);
                AddRoot("trash", ModText.Get("storage.trash"), entry.trashInvWindow);
                if (entry.hirelingWindow.handler.gameObject.activeInHierarchy &&
                    (!WindowLease.Active.TryGetValue(entry.hirelingWindow, out var hired) || hired.OriginallyVisible))
                    AddRoot("hireling", ModText.Get("storage.hireling"), entry.hirelingWindow);
            }
            foreach (var root in roots)
                foreach (var source in Root(root.Id, root.Label, root.Window)) yield return source;
            // 当前场景另外打开的仓储通过真实显示节点补充。
            foreach (var window in WindowsHandler.current.visibleWindows.ToArray())
            {
                if (window.handler == null || !window.handler.gameObject.activeInHierarchy || window.isLocked || windows.Contains(window) || WindowLease.Active.ContainsKey(window)) continue;
                if (window.parentItems.Count > 0)
                {
                    foreach (var owner in window.parentItems.ToArray())
                        foreach (var source in Visit(owner, "", null)) yield return source;
                }
            }
        }
    }
}
