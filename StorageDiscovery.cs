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
            var visited = new HashSet<GraphNodeStorage>();
            var windows = new HashSet<PixelWindow>();
            var entry = EmporiumEntry.Instance;
            if (entry == null) return result;

            void Visit(GraphNodeStorage node, string path, string parentId)
            {
                if (node == null || !visited.Add(node)) return;
                if (node is PixelWindow window && window.isLocked) return;
                if (node is GameItem item)
                {
                    if (item is GameItemElement element && element.IsDestroyed()) return;
                    var content = item.contentWindow;
                    if (!OpenedContainers.CanExpose(item)) return;
                    string name = StorageLabels.Name(GeneralHelper.GetCustomName(item), item.GetDisplayName());
                    var parentItem = item.parentInventory == null ? null : GraphUtils.FindParentType<GameItem>(item.parentInventory);
                    string parentLabel = parentItem == null ? path : StorageLabels.Name(GeneralHelper.GetCustomName(parentItem), parentItem.GetDisplayName());
                    if (windows.Add(content)) result.Add(new StorageSource
                    {
                        Id = "item:" + OpenedContainers.PersistentId(item), Window = content, Owner = item,
                        ParentId = parentItem == null ? parentId : "item:" + OpenedContainers.PersistentId(parentItem),
                        OwnLabel = name, Label = StorageLabels.Title(parentLabel, name, item.GetDisplayName())
                    });
                    Visit(content, name, "item:" + OpenedContainers.PersistentId(item));
                    return;
                }
                if (node.children == null) return;
                foreach (var child in node.children) Visit(child, path, parentId);
            }

            void Root(string id, string label, PixelWindow window)
            {
                if (window == null || window.isLocked || !windows.Add(window)) return;
                label = StorageLabels.Name(window.titleString, label);
                result.Add(new StorageSource { Id = "root:" + id, Label = label, OwnLabel = label, Window = window });
                Visit(window, label, "root:" + id);
            }

            if (ModHelper.IsOutsideStore())
            {
                Root("afterhour", ModText.Get("storage.afterhour"), entry.afterhourWindow);
                if (entry.bazarLeftInvWindow != null && entry.bazarLeftInvWindow.handler.gameObject.activeInHierarchy)
                    Root("bazar", ModText.Get("storage.bazar"), entry.bazarLeftInvWindow);
            }
            else
            {
                Root("main", ModText.Get("storage.main"), entry.invWindow);
                Root("back", ModText.Get("storage.back"), entry.backInvWindow);
                Root("front", ModText.Get("storage.front"), entry.frontInvWindow);
                Root("showcase", ModText.Get("storage.showcase"), entry.showcaseWindow);
                Root("documents", ModText.Get("storage.documents"), entry.docInvWindow);
                Root("hidden", ModText.Get("storage.hidden"), entry.hiddenWindow);
                Root("cassette", ModText.Get("storage.cassette"), entry.cassettePlayerWindow);
                if (entry.vendingMachineElement.childItems.Count > 0) Root("vending", ModText.Get("storage.vending"), entry.vendingMachineWindow);
                if (entry.vendingFountainElement.childItems.Count > 0) Root("fountain", ModText.Get("storage.fountain"), entry.vendingFountainWindow);
                if (EmporiumEntry.IsWaterTapUnlocked()) Root("faucet", ModText.Get("storage.faucet"), entry.faucetWindow);
                Root("drain", ModText.Get("storage.drain"), entry.drainInvWindow);
                Root("trash", ModText.Get("storage.trash"), entry.trashInvWindow);
                if (entry.hirelingWindow.handler.gameObject.activeInHierarchy &&
                    (!WindowLease.Active.TryGetValue(entry.hirelingWindow, out var hired) || hired.OriginallyVisible))
                    Root("hireling", ModText.Get("storage.hireling"), entry.hirelingWindow);
            }
            // 当前场景另外打开的仓储通过真实显示节点补充。
            foreach (var window in WindowsHandler.current.visibleWindows.ToArray())
            {
                if (window.handler == null || !window.handler.gameObject.activeInHierarchy || window.isLocked || windows.Contains(window) || WindowLease.Active.ContainsKey(window)) continue;
                if (window.parentItems.Count > 0)
                {
                    foreach (var owner in window.parentItems) Visit(owner, "", null);
                }
            }
            return result.OrderBy(s => s.Id, System.StringComparer.Ordinal).ToList();
        }
    }
}
