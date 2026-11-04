using System;
using System.Collections.Generic;

namespace ProbablyStolen_FlatStorage
{
    internal static class StorageArrangementSource
    {
        internal static ArrangementGroup Read(StorageSource source, Box size)
        {
            var result = new ArrangementGroup
            {
                Id = source.Id, ParentId = source.ParentId, Size = size,
                Type = source.Owner?.identifier ?? source.Id,
                Label = source.Owner == null ? source.OwnLabel : GeneralHelper.GetCustomName(source.Owner),
                Machine = source.Owner != null && source.Owner.IsTag("STANDARD_MACHINE_TAG")
            };
            var visited = new HashSet<GraphNodeStorage>();
            void Collect(GraphNodeStorage node)
            {
                if (node == null || !visited.Add(node)) return;
                if (node is GameItem item)
                {
                    if (item is GameItemElement element && element.IsDestroyed()) return;
                    int occupied = 0;
                    var shape = item.modifiedShape;
                    for (int y = 0; y < shape.height; y++)
                        for (int x = 0; x < shape.width; x++)
                            if (shape.GetLocal(x, y) != 0) occupied++;
                    string type = item.identifier;
                    if (string.IsNullOrEmpty(type)) type = item.GetDisplayName(false);
                    result.Contents.TryGetValue(type, out double weight);
                    result.Contents[type] = weight + Math.Max(1, occupied);
                    return;
                }
                if (node is PixelWindow window && window.isLocked) return;
                if (node.children != null) foreach (var child in node.children) Collect(child);
            }
            Collect(source.Window);
            return result;
        }
    }
}
