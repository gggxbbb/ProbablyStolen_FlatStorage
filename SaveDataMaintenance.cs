using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal static class SaveDataMaintenance
    {
        internal static void Loaded()
        {
            var entry = EmporiumEntry.Instance ?? throw new InvalidOperationException("Flat Storage inventory is not initialized.");
            var liveItems = new HashSet<int>();
            var visited = new HashSet<GraphNodeStorage>();
            void Visit(GraphNodeStorage node)
            {
                if (node == null || !visited.Add(node)) return;
                if (node is GameItemElement element && element.IsDestroyed()) return;
                if (node is GameItem item && item.uniqueId != 0) liveItems.Add(item.uniqueId);
                if (node.children != null)
                    foreach (var child in node.children) Visit(child);
            }

            // 完整物品图包含关闭和上锁的容器；场景可见性不决定存档记录是否有效。
            foreach (var node in new GraphNodeStorage[]
            {
                entry.invElement, entry.backInvinvElement, entry.backInvinvElementCounter, entry.frontInvinvElement,
                entry.showcaseElement, entry.docInvElement, entry.trashInvElement, entry.hiddenElement,
                entry.afterhourWindow, entry.hirelingInv, entry.soldElement, entry.cassettePlayerElement,
                entry.vendingMachineElement, entry.vendingFountainElement, entry.bazarLeftinvElement,
                entry.drainInvElement, entry.faucetElement, entry.swapBufferElement,
                entry.responseInventory, entry.responseInventoryClosable, entry.dossier, entry.trashcan
            }) Visit(node);
            if (GraphHandler.windowGraphNodeRoots != null)
                foreach (var window in GraphHandler.windowGraphNodeRoots) Visit(window);

            var oldData = ModHelper.GetModDataKeys(ModEntry.Id)
                .ToDictionary(key => key, key => ModHelper.GetModData(ModEntry.Id, key), StringComparer.Ordinal);
            var newData = new Dictionary<string, string>(oldData, StringComparer.Ordinal);
            SaveDataPruning.Prune(newData, liveItems);
            foreach (var pair in oldData)
            {
                if (!newData.TryGetValue(pair.Key, out string value)) ModHelper.RemoveModData(ModEntry.Id, pair.Key);
                else if (value != pair.Value) ModHelper.SetModData(ModEntry.Id, pair.Key, value);
            }
        }
    }
}
