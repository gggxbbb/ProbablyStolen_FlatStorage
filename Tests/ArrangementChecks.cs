using System.Diagnostics;
using ProbablyStolen_FlatStorage;

internal static class ArrangementChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static ArrangementGroup Group(string id, float width = 120, float height = 100, string parent = "root:main")
        => new ArrangementGroup { Id = id, ParentId = parent, Size = new Box(0, 0, width, height), Type = "box" };

    internal static void Similarity()
    {
        var a = Group("a"); var b = Group("b"); var c = Group("c");
        a.Contents.Add("ammo", 20); a.Contents.Add("medicine", 10);
        b.Contents.Add("ammo", 40); b.Contents.Add("medicine", 20);
        c.Contents.Add("metal", 30);
        Check(StorageArrangement.Similarity(a, b) > .9, "同比例内容应高度相似");
        Check(StorageArrangement.Similarity(a, c) < .2, "不同内容应低相似");
        a.Machine = b.Machine = c.Machine = true;
        a.Type = b.Type = "fermenter"; c.Type = "refrigerator";
        c.Contents = new Dictionary<string, double>(a.Contents);
        b.Contents.Clear();
        Check(StorageArrangement.Similarity(a, b) > StorageArrangement.Similarity(a, c), "同类机器优先");
        var empty = Group("empty"); var empty2 = Group("empty2");
        Check(StorageArrangement.Similarity(empty, empty2) >= .8, "空箱按类型归类");
        empty.Label = "原料-金属"; empty2.Label = "原料-矿石";
        Check(StorageArrangement.Similarity(empty, empty2) > .8, "相近标签辅助归类");
    }

    internal static void Layout()
    {
        var random = new Random(7201);
        var groups = new List<ArrangementGroup>
        {
            Group("root:back", 300, 120, null), Group("root:front", 220, 120, null), Group("root:main", 450, 240, null)
        };
        for (int i = 0; i < 48; i++)
        {
            var g = Group("item:" + i.ToString("D3"), random.Next(80, 420), random.Next(60, 260),
                i > 0 && i % 4 == 0 ? "item:" + (i - 1).ToString("D3") : "root:main");
            g.Type = "machine:" + i % 4; g.Machine = i % 3 == 0;
            g.Contents.Add("material:" + i % 5, random.Next(1, 30)); groups.Add(g);
        }
        var timer = Stopwatch.StartNew();
        var result = StorageArrangement.Arrange(groups, 16f / 9);
        Console.WriteLine($"  51 个分组排列耗时 {timer.ElapsedMilliseconds} ms。");
        Check(result[2].Y >= Math.Max(result[0].Y + result[0].Height, result[1].Y + result[1].Height) + StorageArrangement.Gap - .01,
            "柜子位于中央桌面下方");
        Check(result[0].X < result[1].X, "窗内外桌面顺序稳定");
        CheckLayout(groups, result);
        var reverse = groups.AsEnumerable().Reverse().ToList();
        var again = StorageArrangement.Arrange(reverse, 16f / 9);
        for (int i = 0; i < groups.Count; i++)
        {
            var b = again[reverse.FindIndex(g => g.Id == groups[i].Id)];
            Check(result[i].X == b.X && result[i].Y == b.Y, "输入顺序变化后排列保持一致");
        }
        double area = Bounds(result).Width * Bounds(result).Height;
        Console.WriteLine($"  包围面积 {area:F0}。");
    }

    internal static void Aspect()
    {
        var groups = new List<ArrangementGroup> { Group("root:main", 240, 120, null) };
        for (int i = 0; i < 24; i++) groups.Add(Group("item:" + i, 120, 100));
        foreach (float target in new[] { 4f / 3, 16f / 9, 21f / 9, 9f / 16 })
        {
            var result = StorageArrangement.Arrange(groups, target);
            CheckLayout(groups, result);
            var bounds = Bounds(result); double actual = bounds.Width / bounds.Height;
            Console.WriteLine($"  目标比例 {target:F2}，实际比例 {actual:F2}。");
            Check(Math.Abs(Math.Log(actual / target)) < .35, "排列比例应接近显示比例");
        }
    }

    internal static void Clustering()
    {
        var groups = new List<ArrangementGroup> { Group("root:main", 240, 120, null) };
        for (int i = 0; i < 20; i++)
        {
            var group = Group("item:" + i.ToString("D2"));
            group.Contents.Add(i % 2 == 0 ? "ammo" : "medicine", 12);
            groups.Add(group);
        }
        var result = StorageArrangement.Arrange(groups, 16f / 9);
        double same = 0, other = 0; int countSame = 0, countOther = 0;
        for (int i = 1; i < groups.Count; i++)
            for (int j = 1; j < i; j++)
            {
                double distance = Math.Sqrt(Math.Pow(result[i].X - result[j].X, 2) + Math.Pow(result[i].Y - result[j].Y, 2));
                if (groups[i].Contents.Keys.Single() == groups[j].Contents.Keys.Single()) { same += distance; countSame++; }
                else { other += distance; countOther++; }
            }
        Console.WriteLine($"  同类平均距离 {same / countSame:F0}，异类平均距离 {other / countOther:F0}。");
        Check(same / countSame < other / countOther, "相似内容容器应聚集");
    }

    internal static void EdgeCases()
    {
        Check(StorageArrangement.Arrange(Array.Empty<ArrangementGroup>(), 1).Count == 0, "空场景");
        var single = new[] { Group("outside", 700, 120, null) };
        Check(StorageArrangement.Arrange(single, 1).Count == 1, "单独场景仓储");
        var groups = Enumerable.Range(0, 120).Select(i => Group("large:" + i, 80 + i % 4 * 60, 60 + i % 5 * 40, null)).ToList();
        var timer = Stopwatch.StartNew();
        CheckLayout(groups, StorageArrangement.Arrange(groups, 16f / 9));
        Console.WriteLine($"  120 个分组排列耗时 {timer.ElapsedMilliseconds} ms。");
        bool rejected = false;
        try { StorageArrangement.Arrange(new[] { Group("a", parent: "b"), Group("b", parent: "a") }, 1); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "循环所属关系必须拒绝");
    }

    private static Box Bounds(IReadOnlyList<Box> boxes)
    {
        float x = boxes.Min(b => b.X), y = boxes.Min(b => b.Y);
        return new Box(x, y, boxes.Max(b => b.X + b.Width) - x, boxes.Max(b => b.Y + b.Height) - y);
    }
    private static void CheckLayout(IReadOnlyList<ArrangementGroup> groups, IReadOnlyList<Box> boxes)
    {
        for (int i = 0; i < boxes.Count; i++)
        {
            var a = boxes[i];
            Check(CanvasMath.Finite(a.X) && CanvasMath.Finite(a.Y), "坐标有限");
            Check(a.Width == groups[i].Size.Width && a.Height == groups[i].Size.Height, "保留窗格尺寸");
            for (int j = 0; j < i; j++)
            {
                var b = boxes[j]; float gap = StorageArrangement.Gap - .02f;
                Check(a.X + a.Width + gap <= b.X || b.X + b.Width + gap <= a.X ||
                    a.Y + a.Height + gap <= b.Y || b.Y + b.Height + gap <= a.Y, "窗格必须保留间距");
            }
            int parent = groups.ToList().FindIndex(g => g.Id == groups[i].ParentId);
            if (parent >= 0)
            {
                var b = boxes[parent];
                double childRadius = Math.Pow(a.X + a.Width / 2, 2) + Math.Pow(a.Y + a.Height / 2, 2);
                double parentRadius = Math.Pow(b.X + b.Width / 2, 2) + Math.Pow(b.Y + b.Height / 2, 2);
                Check(childRadius + .1 >= parentRadius, "子容器向外围排列");
            }
        }
    }
}
