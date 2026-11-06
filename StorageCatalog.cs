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
        internal string Name { get; private set; }
        internal string[] Types { get; private set; } = Array.Empty<string>();
        private string identifier;
        private string[] typeNames = Array.Empty<string>();
        private int width, height;
        internal bool SizeChanged { get; private set; }

        internal bool Refresh(StorageSource source, bool languageChanged)
        {
            string name = Item.GetDisplayName(true);
            var currentTypes = Item.GetGameItemType();
            bool typesChanged = !Types.SequenceEqual(currentTypes);
            SizeChanged = width != Item.widthPixels || height != Item.heightPixels;
            bool changed = Name != name || identifier != Item.identifier || SizeChanged || typesChanged || languageChanged || Source != source;
            Source = source; Name = name; identifier = Item.identifier;
            width = Item.widthPixels; height = Item.heightPixels;
            if (typesChanged) Types = currentTypes.ToArray();
            if (typesChanged || languageChanged) typeNames = Types.Select(TypeHelper.GetTypeDisplayName).ToArray();
            return changed;
        }

        internal bool Matches(string query, string category)
        {
            if (category.Length != 0 && Array.IndexOf(Types, category) < 0) return false;
            if (query.Length == 0) return true;
            if (Contains(Name, query) || Contains(identifier, query)) return true;
            for (int i = 0; i < Types.Length; i++)
                if (Contains(Types[i], query) || Contains(typeNames[i], query)) return true;
            return false;
        }

        private static bool Contains(string text, string query) =>
            text != null && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    internal sealed class StorageCatalog
    {
        internal readonly List<CatalogItem> Items = new List<CatalogItem>();
        internal readonly List<GameInventory> Inventories = new List<GameInventory>();
        private readonly Dictionary<GameItemElement, CatalogItem> entries = new Dictionary<GameItemElement, CatalogItem>();
        private readonly Dictionary<GraphNodeStorage, NodeState> nodes = new Dictionary<GraphNodeStorage, NodeState>();
        private readonly HashSet<GraphNodeStorage> visited = new HashSet<GraphNodeStorage>();
        private readonly List<CatalogItem> currentItems = new List<CatalogItem>();
        private readonly List<GameInventory> currentInventories = new List<GameInventory>();
        private readonly List<GraphNodeStorage> removedNodes = new List<GraphNodeStorage>();
        private readonly List<GameItemElement> removedItems = new List<GameItemElement>();
        internal bool StructureChanged { get; private set; }
        internal bool LayoutChanged { get; private set; }
        private bool changed, languageChanged;
        private string language;

        internal StorageCatalog() { }
        internal StorageCatalog(IEnumerable<StorageSource> sources) => Refresh(sources);
        internal bool ContainsInventory(GameInventory inventory) => visited.Contains(inventory);

        internal void Combine(IReadOnlyList<WindowLease> groups)
        {
            Items.Clear(); Inventories.Clear(); visited.Clear();
            foreach (var group in groups)
            {
                foreach (var entry in group.Catalog.Items)
                    if (visited.Add(entry.Item)) Items.Add(entry);
                foreach (var inventory in group.Catalog.Inventories)
                    if (visited.Add(inventory)) Inventories.Add(inventory);
            }
        }

        internal bool Refresh(IEnumerable<StorageSource> sources)
        {
            string currentLanguage = ModText.Get("ui.category");
            languageChanged = language != currentLanguage; language = currentLanguage;
            changed = languageChanged; StructureChanged = LayoutChanged = false;
            visited.Clear(); currentItems.Clear(); currentInventories.Clear();
            foreach (var source in sources) Visit(source.Window, null, source);
            removedNodes.Clear();
            foreach (var pair in nodes) if (!visited.Contains(pair.Key)) removedNodes.Add(pair.Key);
            foreach (var node in removedNodes) { nodes.Remove(node); StructureChanged = true; }
            removedItems.Clear();
            foreach (var pair in entries) if (!visited.Contains(pair.Key)) removedItems.Add(pair.Key);
            foreach (var item in removedItems) { entries.Remove(item); changed = true; }
            if (!Items.SequenceEqual(currentItems)) { Items.Clear(); Items.AddRange(currentItems); changed = true; }
            if (!Inventories.SequenceEqual(currentInventories))
            { Inventories.Clear(); Inventories.AddRange(currentInventories); StructureChanged = true; }
            return changed || StructureChanged || LayoutChanged;
        }

        private void Visit(GraphNodeStorage node, GraphNodeStorage parent, StorageSource source)
        {
            if (node == null || !visited.Add(node)) return;
            if (node is GameItem item)
            {
                if (item is GameItemElement element && !element.IsDestroyed() && element.unitCount > 0)
                {
                    if (!entries.TryGetValue(element, out var entry))
                    { entries.Add(element, entry = new CatalogItem { Item = element }); changed = true; }
                    changed |= entry.Refresh(source, languageChanged);
                    LayoutChanged |= entry.SizeChanged;
                    currentItems.Add(entry);
                }
                return;
            }
            var state = new NodeState(node, parent);
            if (!nodes.TryGetValue(node, out var previous)) StructureChanged = true;
            else
            {
                StructureChanged |= previous.Parent != state.Parent || previous.Child != state.Child || previous.Hidden != state.Hidden;
                LayoutChanged |= previous.Width != state.Width || previous.Height != state.Height;
            }
            nodes[node] = state;
            if (state.Hidden) return;
            if (node is GameInventory inventory) currentInventories.Add(inventory);
            var children = node.children;
            if (children != null) foreach (var child in children) Visit(child, node, source);
        }

        private readonly struct NodeState
        {
            internal readonly object Parent, Child;
            internal readonly int Width, Height;
            internal readonly bool Hidden;
            internal NodeState(GraphNodeStorage node, GraphNodeStorage parent)
            {
                Parent = parent; Child = (node as PixelWindow)?.childElement;
                Width = (node as PixelElement)?.widthPixels ?? 0;
                Height = (node as PixelElement)?.heightPixels ?? 0;
                Hidden = node is PixelWindow window && window.isLocked || node is MachineryPanel panel && panel.isHidden;
            }
        }
    }

    internal sealed class SearchHighlights : IDisposable
    {
        private sealed class Visual
        {
            internal CanvasGroup Alpha;
            internal float Original;
            internal bool Owned;
            internal bool? Match;
            internal float Width = -1, Height = -1;
            internal readonly List<TreeNodeRender> Outline = new List<TreeNodeRender>();
        }
        private readonly Dictionary<GameItemElement, Visual> visuals = new Dictionary<GameItemElement, Visual>();

        internal void Apply(StorageCatalog catalog, HashSet<GameItemElement> matches, bool active)
        {
            if (!active) { Dispose(); return; }
            var current = new HashSet<GameItemElement>(catalog.Items.Select(i => i.Item));
            foreach (var item in visuals.Keys.ToArray())
                if (!current.Contains(item) || item.IsDestroyed()) Remove(item);
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
                if (visual.Match != match)
                {
                    visual.Match = match;
                    visual.Alpha.alpha = visual.Original * (match ? 1 : .22f);
                    foreach (var line in visual.Outline) line.gameObject.SetActive(match);
                }
                float width = item.widthPixels, height = item.heightPixels;
                if (visual.Width == width && visual.Height == height) continue;
                visual.Width = width; visual.Height = height;
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
