using System;
using System.Collections.Generic;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class StorageTreeItem
    {
        public string Id { get; }
        public string ParentId { get; }
        public string Name { get; }

        public StorageTreeItem(string id, string parentId, string name)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Storage tree identity is required.", nameof(id));
            if (name == null) throw new ArgumentNullException(nameof(name));
            Id = id;
            ParentId = parentId;
            Name = name;
        }
    }

    internal readonly struct StorageTreeRow
    {
        public string Id { get; }
        public string Name { get; }
        public int Depth { get; }
        public bool HasChildren { get; }

        internal StorageTreeRow(StorageTreeItem item, int depth, bool hasChildren)
        {
            Id = item.Id;
            Name = item.Name;
            Depth = depth;
            HasChildren = hasChildren;
        }
    }

    internal static class StorageTree
    {
        private sealed class Node
        {
            internal readonly StorageTreeItem Item;
            internal readonly List<Node> Children = new List<Node>();
            internal Node Parent;
            internal byte VisitState;

            internal Node(StorageTreeItem item) { Item = item; }
        }

        internal static IReadOnlyList<StorageTreeRow> Build(IEnumerable<StorageTreeItem> items,
            IEnumerable<string> collapsedIds)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (collapsedIds == null) throw new ArgumentNullException(nameof(collapsedIds));
            var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("Storage tree item cannot be null.", nameof(items));
                if (!nodes.TryAdd(item.Id, new Node(item)))
                    throw new ArgumentException("Duplicate storage tree identity: " + item.Id, nameof(items));
                if (string.Equals(item.Id, item.ParentId, StringComparison.Ordinal))
                    throw new ArgumentException("Storage tree item is its own parent: " + item.Id, nameof(items));
            }
            var collapsed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in collapsedIds)
            {
                if (string.IsNullOrEmpty(id))
                    throw new ArgumentException("Collapsed storage identity is required.", nameof(collapsedIds));
                collapsed.Add(id);
            }

            var roots = new List<Node>();
            foreach (var node in nodes.Values)
            {
                if (!string.IsNullOrEmpty(node.Item.ParentId) && nodes.TryGetValue(node.Item.ParentId, out var parent))
                {
                    node.Parent = parent;
                    parent.Children.Add(node);
                }
                else roots.Add(node);
            }
            ValidateParents(nodes.Values);
            foreach (var node in nodes.Values) node.Children.Sort(CompareSiblings);
            roots.Sort((left, right) =>
            {
                int kind = string.IsNullOrEmpty(left.Item.ParentId).CompareTo(string.IsNullOrEmpty(right.Item.ParentId));
                return kind == 0 ? CompareSiblings(left, right) : -kind;
            });

            var rows = new List<StorageTreeRow>();
            var pending = new Stack<(Node Node, int Depth)>();
            for (int i = roots.Count - 1; i >= 0; i--) pending.Push((roots[i], 0));
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                var node = current.Node;
                rows.Add(new StorageTreeRow(node.Item, current.Depth, node.Children.Count > 0));
                if (collapsed.Contains(node.Item.Id)) continue;
                for (int i = node.Children.Count - 1; i >= 0; i--)
                    pending.Push((node.Children[i], current.Depth + 1));
            }
            return rows.AsReadOnly();
        }

        private static int CompareSiblings(Node left, Node right)
        {
            int name = StringComparer.Ordinal.Compare(left.Item.Name, right.Item.Name);
            return name == 0 ? StringComparer.Ordinal.Compare(left.Item.Id, right.Item.Id) : name;
        }

        private static void ValidateParents(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.VisitState != 0) continue;
                var path = new List<Node>();
                var current = node;
                while (current != null && current.VisitState == 0)
                {
                    current.VisitState = 1;
                    path.Add(current);
                    current = current.Parent;
                }
                if (current != null && current.VisitState == 1)
                    throw new ArgumentException("Storage tree parent cycle includes: " + current.Item.Id, "items");
                foreach (var visited in path) visited.VisitState = 2;
            }
        }
    }
}
