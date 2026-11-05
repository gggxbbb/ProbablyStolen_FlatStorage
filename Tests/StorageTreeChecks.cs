using System.Globalization;
using ProbablyStolen_FlatStorage;

internal static class StorageTreeChecks
{
    internal static void ParentOrder()
    {
        var items = new[]
        {
            new StorageTreeItem("item:3", "item:2", "小盒子"),
            new StorageTreeItem("item:2", "root:main", "背包"),
            new StorageTreeItem("root:main", null, "仓库"),
            new StorageTreeItem("item:4", "root:main", "工具箱")
        };
        var rows = StorageTree.Build(items, Array.Empty<string>());
        Check(rows.Select(row => row.Id).SequenceEqual(new[] { "root:main", "item:4", "item:2", "item:3" }),
            "父容器先于子容器，同层名称按 Ordinal 排序");
        Check(rows.Select(row => row.Depth).SequenceEqual(new[] { 0, 1, 1, 2 }), "完整保留嵌套深度");
        Check(rows.Select(row => row.HasChildren).SequenceEqual(new[] { true, false, true, false }),
            "只有存在直接子容器的行显示展开操作");
        Check(rows.Select(row => row.Name).SequenceEqual(new[] { "仓库", "工具箱", "背包", "小盒子" }),
            "保留原始中文名称");
        Check(StorageTree.Build(Array.Empty<StorageTreeItem>(), Array.Empty<string>()).Count == 0, "空目录返回空行");
    }

    internal static void Collapse()
    {
        var items = new[]
        {
            new StorageTreeItem("root", null, "仓库"),
            new StorageTreeItem("bag", "root", "背包"),
            new StorageTreeItem("box", "bag", "盒子"),
            new StorageTreeItem("pouch", "box", "口袋"),
            new StorageTreeItem("tools", "root", "工具箱"),
            new StorageTreeItem("another", null, "背包")
        };
        var collapsed = new HashSet<string> { "bag", "box", "removed" };
        var rows = StorageTree.Build(items, collapsed);
        Check(rows.Select(row => row.Id).SequenceEqual(new[] { "root", "tools", "bag", "another" }),
            "折叠祖先隐藏全部后代，保留其他分支");
        Check(rows.Single(row => row.Id == "bag").HasChildren, "折叠行继续保留展开操作");
        collapsed.Remove("bag");
        rows = StorageTree.Build(items, collapsed);
        Check(rows.Select(row => row.Id).SequenceEqual(new[] { "root", "tools", "bag", "box", "another" }),
            "展开祖先后保留后代自己的折叠状态");
        rows = StorageTree.Build(items, new[] { "root", "tools", "another" });
        Check(rows.Select(row => row.Id).SequenceEqual(new[] { "root", "another" }), "折叠根节点和叶节点");
        Check(collapsed.SetEquals(new[] { "box", "removed" }), "生成目录保留调用方折叠集合");
    }

    internal static void StableOrderingAndMissingParents()
    {
        var items = new[]
        {
            new StorageTreeItem("orphan", "outside-scope", "A"),
            new StorageTreeItem("orphan-child", "orphan", "小盒子"),
            new StorageTreeItem("root-b", "", "背包"),
            new StorageTreeItem("item:2", "root-a", "容器"),
            new StorageTreeItem("item:-103", "root-a", "容器"),
            new StorageTreeItem("root-a", null, "仓库"),
            new StorageTreeItem("Root-a", null, "仓库")
        };
        string[] expected = { "Root-a", "root-a", "item:-103", "item:2", "root-b", "orphan", "orphan-child" };
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "zh-CN", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var input in new[] { items, items.Reverse().ToArray(), items.Skip(3).Concat(items.Take(3)).ToArray() })
                {
                    var rows = StorageTree.Build(input, new[] { "ROOT-A" });
                    Check(rows.Select(row => row.Id).SequenceEqual(expected), "排序保持输入顺序与系统文化无关");
                    Check(rows.Single(row => row.Id == "orphan").Depth == 0 &&
                        rows.Single(row => row.Id == "orphan-child").Depth == 1, "缺失父容器的节点成为当前范围顶层");
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    internal static void RejectInvalidGraph()
    {
        Reject<ArgumentException>(() => StorageTree.Build(new[]
        {
            new StorageTreeItem("duplicate", null, "容器"), new StorageTreeItem("duplicate", null, "另一容器")
        }, Array.Empty<string>()));
        Reject<ArgumentException>(() => StorageTree.Build(new[]
        {
            new StorageTreeItem("self", "self", "容器")
        }, Array.Empty<string>()));
        foreach (bool collapse in new[] { false, true })
            Reject<ArgumentException>(() => StorageTree.Build(new[]
            {
                new StorageTreeItem("root", null, "仓库"), new StorageTreeItem("child", "a", "子容器"),
                new StorageTreeItem("a", "b", "容器甲"), new StorageTreeItem("b", "c", "容器乙"),
                new StorageTreeItem("c", "a", "容器丙")
            }, collapse ? new[] { "a", "b", "c" } : Array.Empty<string>()));
        Reject<ArgumentNullException>(() => StorageTree.Build(null, Array.Empty<string>()));
        Reject<ArgumentNullException>(() => StorageTree.Build(Array.Empty<StorageTreeItem>(), null));
        Reject<ArgumentException>(() => StorageTree.Build(new StorageTreeItem[] { null }, Array.Empty<string>()));
        Reject<ArgumentException>(() => new StorageTreeItem("", null, "容器"));
        Reject<ArgumentNullException>(() => new StorageTreeItem("id", null, null));
        Reject<ArgumentException>(() => StorageTree.Build(Array.Empty<StorageTreeItem>(), new string[] { null }));
    }

    internal static void DeepTree()
    {
        const int count = 4096;
        var items = Enumerable.Range(0, count).Select(i => new StorageTreeItem(
            "item:" + i, i == 0 ? null : "item:" + (i - 1), "容器 " + i)).Reverse().ToArray();
        var rows = StorageTree.Build(items, Array.Empty<string>());
        Check(rows.Count == count && rows[^1].Depth == count - 1 && rows[^1].Id == "item:4095",
            "深层嵌套完整遍历");
        Check(rows.Take(count - 1).All(row => row.HasChildren) && !rows[^1].HasChildren, "深层节点分支状态");
        rows = StorageTree.Build(items, new[] { "item:10" });
        Check(rows.Count == 11 && rows[^1].Depth == 10 && rows[^1].HasChildren, "深层折叠隐藏全部后代");
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("应当立即拒绝无效容器树。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
