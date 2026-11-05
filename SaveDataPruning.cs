using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal static class SaveDataPruning
    {
        private static readonly HashSet<string> StoreRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "root:main", "root:back", "root:front", "root:showcase", "root:documents", "root:hidden",
            "root:cassette", "root:vending", "root:fountain", "root:faucet", "root:drain", "root:trash", "root:hireling"
        };
        private static readonly HashSet<string> OutsideRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "root:afterhour", "root:bazar"
        };

        internal static void Prune(IDictionary<string, string> data, ISet<int> existingItems)
        {
            foreach (string key in data.Keys.ToArray())
            {
                if (key.StartsWith("opened:", StringComparison.Ordinal))
                {
                    if (!ExistingItem(key.Substring("opened:".Length), existingItems)) data.Remove(key);
                    continue;
                }
                if (!key.StartsWith("layout:", StringComparison.Ordinal)) continue;
                var state = CanvasState.Decode(data[key]);
                var roots = key == "layout:store" ? StoreRoots :
                    key.StartsWith("layout:outside:", StringComparison.Ordinal) ? OutsideRoots : null;
                int removed = state.Groups.RemoveAll(group => group.Id.StartsWith("item:", StringComparison.Ordinal)
                    ? !ExistingItem(group.Id.Substring("item:".Length), existingItems)
                    : roots != null && !roots.Contains(group.Id));
                if (removed > 0) data[key] = state.Encode();
            }
        }

        private static bool ExistingItem(string text, ISet<int> existingItems) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 && existingItems.Contains(id);
    }
}
