using System.Globalization;
using ProbablyStolen_FlatStorage;

internal static class SearchChecks
{
    internal static void NamesAndIdentifiers()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "zh-CN", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Check(StorageSearch.Matches("维修工具箱", "repair_kit", new[] { "Tools" }, "工具", null), "中文名称部分匹配");
                Check(StorageSearch.Matches("Repair Kit", "repair_kit", new[] { "Tools" }, "  REPAIR  ", null), "名称匹配忽略大小写及两端空格");
                Check(StorageSearch.Matches("维修工具箱", "repair_kit", new[] { "Tools" }, "REPAIR_", null), "物品标识符匹配忽略大小写");
                Check(StorageSearch.Matches(null, "repair_kit", Array.Empty<string>(), "KIT", null), "空名称仍可按标识符匹配");
                Check(!StorageSearch.Matches("维修工具箱", "repair_kit", new[] { "Tools" }, "药品", null), "无关查询排除物品");
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    internal static void Categories()
    {
        string[] categories = { "Tools", "维修用品" };
        Check(StorageSearch.Matches("维修工具箱", "repair_kit", categories, "tools", null), "查询可以匹配类别名称");
        Check(StorageSearch.Matches("维修工具箱", "repair_kit", categories, "维修", "Tools"), "查询与选定类别取交集");
        Check(StorageSearch.Matches("维修工具箱", "repair_kit", categories, "kit", "维修用品"), "标识符查询与中文类别取交集");
        Check(!StorageSearch.Matches("维修工具箱", "repair_kit", categories, "维修", "Food"), "名称匹配仍受类别筛选约束");
        Check(!StorageSearch.Matches("维修工具箱", "repair_kit", categories, "弹药", "Tools"), "类别匹配仍受查询约束");
        Check(!StorageSearch.Matches("维修工具箱", "repair_kit", Array.Empty<string>(), "", "Tools"), "无类别物品遵循类别筛选");
    }

    internal static void EmptyQuery()
    {
        foreach (string query in new string[] { null, "", "  \t\r\n" })
        {
            Check(StorageSearch.Matches(null, null, Array.Empty<string>(), query, null), "空查询包含全部物品");
            Check(StorageSearch.Matches("工具箱", "repair_kit", new[] { "Tools" }, query, "Tools"), "空查询保留匹配类别");
            Check(!StorageSearch.Matches("工具箱", "repair_kit", new[] { "Tools" }, query, "Food"), "空查询保留类别限制");
        }
        Check(!StorageSearch.Matches(null, null, Array.Empty<string>(), "box", null), "缺少可搜索文字时返回空结果");
    }

    internal static void MapNegativeCoordinates()
    {
        var groups = new[] { new Box(-200, -100, 200, 100), new Box(100, 100, 300, 200) };
        var map = new MiniMapProjection(groups, 300, 300);
        Near(map.Scale, .5f);
        var first = map.Project(groups[0]);
        Near(first.X, 0); Near(first.Y, 50); Near(first.Width, 100); Near(first.Height, 50);
        var second = map.Project(groups[1]);
        Near(second.X, 150); Near(second.Y, 150); Near(second.Width, 150); Near(second.Height, 100);
        Near(map.CanvasX(0), -200); Near(map.CanvasY(50), -100);
    }

    internal static void MapAspectAndRoundTrip()
    {
        var groups = new[] { new Box(-200, -100, 200, 100), new Box(100, 100, 300, 200) };
        foreach (var size in new[] { (320f, 180f), (180f, 320f), (300f, 300f), (900f, 120f) })
        {
            var map = new MiniMapProjection(groups, size.Item1, size.Item2);
            var mapped = groups.Select(map.Project).ToArray();
            float left = mapped.Min(box => box.X), top = mapped.Min(box => box.Y);
            float right = mapped.Max(box => box.X + box.Width), bottom = mapped.Max(box => box.Y + box.Height);
            Near(left, size.Item1 - right); Near(top, size.Item2 - bottom);
            Check(left >= -.001f && top >= -.001f && right <= size.Item1 + .001f && bottom <= size.Item2 + .001f, "完整布局位于小地图内");
            Check(Math.Abs(right - left - size.Item1) < .001f || Math.Abs(bottom - top - size.Item2) < .001f, "小地图使用可用宽度或高度");
            for (int i = 0; i < groups.Length; i++)
            {
                Near(mapped[i].Width / mapped[i].Height, groups[i].Width / groups[i].Height);
                Near(map.CanvasX(mapped[i].X), groups[i].X);
                Near(map.CanvasY(mapped[i].Y), groups[i].Y);
                Near(map.CanvasX(mapped[i].X + mapped[i].Width), groups[i].X + groups[i].Width);
                Near(map.CanvasY(mapped[i].Y + mapped[i].Height), groups[i].Y + groups[i].Height);
            }
        }
    }

    internal static void MapBounds()
    {
        Reject(() => new MiniMapProjection(Array.Empty<Box>(), 200, 100));
        Reject(() => new MiniMapProjection(new[] { new Box(0, 0, 10, 10) }, 0, 100));
        Reject(() => new MiniMapProjection(new[] { new Box(0, 0, 10, 10) }, 200, -1));
        var point = new MiniMapProjection(new[] { new Box(-5, -7, 0, 0) }, 100, 100);
        Near(point.CanvasX(point.Project(new Box(-5, -7, 0, 0)).X), -5);
        Near(point.CanvasY(point.Project(new Box(-5, -7, 0, 0)).Y), -7);
    }

    private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected) => Check(Math.Abs(actual - expected) < .001f, $"{actual} != {expected}");
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "小地图尺寸无效时立即拒绝");
    }
}
