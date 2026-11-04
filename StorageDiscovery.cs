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
                        Id = "item:" + item.GetUniqueID(), Window = content, Owner = item,
                        ParentId = parentItem == null ? parentId : "item:" + parentItem.GetUniqueID(),
                        OwnLabel = name, Label = StorageLabels.Title(parentLabel, name, item.GetDisplayName())
                    });
                    Visit(content, name, "item:" + item.GetUniqueID());
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
                Root("afterhour", "随身与当前地点", entry.afterhourWindow);
                if (entry.bazarLeftInvWindow != null && entry.bazarLeftInvWindow.handler.gameObject.activeInHierarchy)
                    Root("bazar", "当前地点仓储", entry.bazarLeftInvWindow);
            }
            else
            {
                Root("main", "店内仓储", entry.invWindow);
                Root("back", "柜台仓储", entry.backInvWindow);
                Root("front", "交易区域", entry.frontInvWindow);
                Root("showcase", "展示柜", entry.showcaseWindow);
                Root("documents", "文件", entry.docInvWindow);
                Root("hidden", "隐蔽仓储", entry.hiddenWindow);
                Root("cassette", "播放器", entry.cassettePlayerWindow);
                if (entry.vendingMachineElement.childItems.Count > 0) Root("vending", "售货机", entry.vendingMachineWindow);
                if (entry.vendingFountainElement.childItems.Count > 0) Root("fountain", "饮料机", entry.vendingFountainWindow);
                if (EmporiumEntry.IsWaterTapUnlocked()) Root("faucet", "水龙头", entry.faucetWindow);
                Root("drain", "排水口", entry.drainInvWindow);
                Root("trash", "废弃物", entry.trashInvWindow);
                if (entry.hirelingWindow.handler.gameObject.activeInHierarchy &&
                    (!WindowLease.Active.TryGetValue(entry.hirelingWindow, out var hired) || hired.OriginallyVisible))
                    Root("hireling", "雇员仓储", entry.hirelingWindow);
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
