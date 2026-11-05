using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class CatalogItem
    {
        internal StorageSource Source;
        internal GameItemElement Item;
        internal string Name => Item.GetDisplayName(true);
        internal bool Matches(string query, string category) =>
            StorageSearch.Matches(Name, Item.identifier, Item.GetGameItemType(), query, category) ||
            (string.IsNullOrEmpty(category) || Item.HaveType(category)) &&
            !string.IsNullOrWhiteSpace(query) && Item.GetGameItemType().Any(t =>
                TypeHelper.GetTypeDisplayName(t).IndexOf(query.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    internal sealed class StorageCatalog
    {
        internal readonly List<CatalogItem> Items = new List<CatalogItem>();
        internal readonly List<GameInventory> Inventories = new List<GameInventory>();

        internal StorageCatalog(IEnumerable<StorageSource> sources)
        {
            var visited = new HashSet<GraphNodeStorage>();
            void Visit(GraphNodeStorage node, StorageSource source)
            {
                if (node == null || !visited.Add(node)) return;
                if (node is PixelWindow window && window.isLocked) return;
                if (node is MachineryPanel panel && panel.isHidden) return;
                if (node is GameItem item)
                {
                    if (item is GameItemElement element && !element.IsDestroyed() && element.unitCount > 0)
                        Items.Add(new CatalogItem { Source = source, Item = element });
                    return;
                }
                if (node is GameInventory inventory) Inventories.Add(inventory);
                if (node.children != null) foreach (var child in node.children) Visit(child, source);
            }
            foreach (var source in sources) Visit(source.Window, source);
        }
    }

    internal sealed class SearchHighlights : IDisposable
    {
        private sealed class Visual
        {
            internal CanvasGroup Alpha;
            internal float Original;
            internal bool Owned;
            internal readonly List<TreeNodeRender> Outline = new List<TreeNodeRender>();
        }
        private readonly Dictionary<GameItemElement, Visual> visuals = new Dictionary<GameItemElement, Visual>();

        internal void Apply(StorageCatalog catalog, HashSet<GameItemElement> matches, bool active)
        {
            var current = new HashSet<GameItemElement>(catalog.Items.Select(i => i.Item));
            foreach (var item in visuals.Keys.ToArray())
                if (!active || !current.Contains(item) || item.IsDestroyed()) Remove(item);
            if (!active) return;
            foreach (var entry in catalog.Items)
            {
                var item = entry.Item;
                if (!visuals.TryGetValue(item, out var visual))
                {
                    var alpha = item.middle.GetComponent<CanvasGroup>();
                    visual = new Visual { Owned = alpha == null, Original = alpha == null ? 1 : alpha.alpha,
                        Alpha = alpha ?? item.middle.gameObject.AddComponent<CanvasGroup>() };
                    for (int i = 0; i < 4; i++)
                    {
                        var line = FlatUI.Panel(item.middle, "Search highlight", new Color(.35f, .85f, 1f, .95f));
                        line.image.raycastTarget = false;
                        visual.Outline.Add(line);
                    }
                    visuals.Add(item, visual);
                }
                bool match = matches.Contains(item);
                visual.Alpha.alpha = visual.Original * (match ? 1 : .22f);
                foreach (var line in visual.Outline) line.gameObject.SetActive(match);
                float width = item.widthPixels, height = item.heightPixels;
                NativeAccess.Place(visual.Outline[0], 0, 0, width, 1);
                NativeAccess.Place(visual.Outline[1], 0, height - 1, width, 1);
                NativeAccess.Place(visual.Outline[2], 0, 0, 1, height);
                NativeAccess.Place(visual.Outline[3], width - 1, 0, 1, height);
            }
        }

        private void Remove(GameItemElement item)
        {
            var visual = visuals[item];
            if (visual.Alpha != null)
            {
                visual.Alpha.alpha = visual.Original;
                if (visual.Owned) UnityEngine.Object.Destroy(visual.Alpha);
            }
            foreach (var line in visual.Outline) if (line != null) UnityEngine.Object.Destroy(line.gameObject);
            visuals.Remove(item);
        }

        public void Dispose()
        {
            foreach (var item in visuals.Keys.ToArray()) Remove(item);
        }
    }
}
