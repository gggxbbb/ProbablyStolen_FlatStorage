using ProbablyStolen_FlatStorage;

internal static class ToolbarLayoutChecks
{
    private static readonly float[] PrimaryWidths = { 154, 108, 48, 44, 72, 72, 64 };
    private static readonly float[] SecondaryWidths = { 64, 44, 72, 64, 116, 48 };

    internal static void Wide()
    {
        var result = ToolbarLayout.Compute(640, PrimaryWidths, SecondaryWidths);
        Validate(result, 640, PrimaryWidths, SecondaryWidths);
        Check(result.Height == 54, "宽屏工具栏高度");
        Check(result.Primary.All(box => box.Y == 5), "第一组使用第一行");
        Check(result.Secondary.All(box => box.Y == 29), "第二组使用第二行");
        Check(result.Status.Y == 29 && result.Status.Width >= 80, "状态使用第二行剩余空间");
        foreach (float width in new[] { 640, 960, 1280, 1920 })
        {
            var wide = ToolbarLayout.Compute(width, PrimaryWidths, SecondaryWidths);
            Validate(wide, width, PrimaryWidths, SecondaryWidths);
            Check(wide.Height == 54, "宽屏保持两行");
        }
    }

    internal static void Narrow()
    {
        foreach (float width in new[] { .01f, 1, 12, 20, 32, 64, 96, 128, 220, 320, 480, 500, 596 })
            Validate(ToolbarLayout.Compute(width, PrimaryWidths, SecondaryWidths), width, PrimaryWidths, SecondaryWidths);
        for (float width = 16; width <= 1600; width += 7)
            Validate(ToolbarLayout.Compute(width, PrimaryWidths, SecondaryWidths), width, PrimaryWidths, SecondaryWidths);

        var narrow = ToolbarLayout.Compute(320, PrimaryWidths, SecondaryWidths);
        Check(narrow.Height > 54, "窄屏增加控件行数");
        Check(narrow.Primary.Select(box => box.Y).Distinct().Count() > 1, "搜索操作换行");
        Check(narrow.Secondary.Select(box => box.Y).Distinct().Count() > 1, "画布操作换行");
        var compressed = ToolbarLayout.Compute(96, new[] { 500f }, Array.Empty<float>());
        Check(compressed.Primary[0].Width == 86, "单个宽控件缩小至可用宽度");
        Check(compressed.Status.Y == 29, "状态获得独立可用行");
    }

    internal static void EmptyAndValidation()
    {
        float[] empty = Array.Empty<float>();
        var result = ToolbarLayout.Compute(640, empty, empty);
        Validate(result, 640, empty, empty);
        Check(result.Height == 30 && result.Status.X == 5 && result.Status.Width == 630,
            "空操作组保留状态行");
        foreach (float width in new[] { 1, 320, float.MaxValue })
        {
            Validate(ToolbarLayout.Compute(width, PrimaryWidths, empty), width, PrimaryWidths, empty);
            Validate(ToolbarLayout.Compute(width, empty, SecondaryWidths), width, empty, SecondaryWidths);
        }
        foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject<ArgumentOutOfRangeException>(() => ToolbarLayout.Compute(invalid, empty, empty));
            Reject<ArgumentOutOfRangeException>(() => ToolbarLayout.Compute(640, new[] { invalid }, empty));
            Reject<ArgumentOutOfRangeException>(() => ToolbarLayout.Compute(640, empty, new[] { invalid }));
        }
        Reject<ArgumentNullException>(() => ToolbarLayout.Compute(640, null, empty));
        Reject<ArgumentNullException>(() => ToolbarLayout.Compute(640, empty, null));
    }

    private static void Validate(ToolbarLayoutResult result, float width,
        IReadOnlyList<float> primaryWidths, IReadOnlyList<float> secondaryWidths)
    {
        Check(result.Primary.Count == primaryWidths.Count && result.Secondary.Count == secondaryWidths.Count,
            "全部操作器获得位置");
        var controls = result.Primary.Concat(result.Secondary).ToArray();
        var boxes = controls.Append(result.Status).ToArray();
        Check(CanvasMath.Finite(result.Height) && result.Height > 0, "工具栏高度有效");
        for (int i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            Check(CanvasMath.Finite(box.X) && CanvasMath.Finite(box.Y) && CanvasMath.Finite(box.Width) &&
                box.X >= 0 && box.Y >= 0 && box.Width > 0 && box.Height == 20, "操作器尺寸有效");
            Check(box.X + box.Width <= width && box.Y + box.Height <= result.Height, "操作器保持在工具栏内");
            for (int j = i + 1; j < boxes.Length; j++)
                Check(!box.Intersects(boxes[j]), "操作器互不覆盖");
        }
        for (int i = 1; i < controls.Length; i++)
            Check(controls[i].Y > controls[i - 1].Y ||
                controls[i].Y == controls[i - 1].Y && controls[i].X > controls[i - 1].X, "保持操作顺序");
        if (result.Primary.Count > 0 && result.Secondary.Count > 0)
            Check(result.Secondary[0].Y >= result.Primary[^1].Y + 24, "两组操作器分行");
        var requestedWidths = primaryWidths.Concat(secondaryWidths).ToArray();
        float available = width - 2 * Math.Min(5, width / 4);
        for (int i = 0; i < controls.Length; i++)
            Check(Math.Abs(controls[i].Width - Math.Min(requestedWidths[i], available)) <= .0001f,
                "控件仅在自身超过可用宽度时收缩");
        Check(result.Status.Width >= Math.Min(80, available), "状态保留可读宽度");
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("应当立即拒绝无效工具栏输入。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
