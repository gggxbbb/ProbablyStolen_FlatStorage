using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class BulkTransferChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Shapes(string workspace)
    {
        var equals = LoadMod(workspace).GetType("ProbablyStolen_FlatStorage.BulkShape", true)!
            .GetMethod("Equal", BindingFlags.Static | BindingFlags.NonPublic)!;
        GridShape shape = new GridShapeBuilder().SetData("111001", 3).SetPosition(4, 5).Build();
        Check((bool)equals.Invoke(null, new object[] { shape, shape.Clone() })!, "形状副本保持相同占格");
        Check(!(bool)equals.Invoke(null, new object[] { shape, new GridShapeBuilder(shape).SetOrientation(1).Build() })!, "旋转变化使预览失效");
        Check(!(bool)equals.Invoke(null, new object[] { shape, new GridShapeBuilder(shape).SetFlipped(true).Build() })!, "翻转变化使预览失效");
        Check(!(bool)equals.Invoke(null, new object[] { shape, new GridShapeBuilder().SetData("110001", 3).SetPosition(4, 5).Build() })!, "相同外形范围内占格变化使预览失效");
        Check((bool)equals.Invoke(null, new object[] { null, null })!, "空形状比较");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Reservations(string workspace)
    {
        var type = LoadMod(workspace).GetType("ProbablyStolen_FlatStorage.BulkPlacementReservations", true)!;
        var add = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var intersects = type.GetMethod("Intersects", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reservations = Activator.CreateInstance(type, true);
        var original = new GridShapeBuilder().SetData("111001", 3).SetPosition(4, 5);
        add.Invoke(reservations, new object[] { original });
        original.SetPosition(100, 100);
        GridShape occupiedCell = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(4, 5).Build();
        GridShape openCell = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(4, 6).Build();
        Check((bool)intersects.Invoke(reservations, new object[] { occupiedCell })!, "预留占格保留独立副本");
        Check(!(bool)intersects.Invoke(reservations, new object[] { openCell })!, "不规则物品空格保持可用");
        GridShape rotated = new GridShapeBuilder(2, 1).SetDataFill(1).SetTransform(4, 5, false, 1).Build();
        Check((bool)intersects.Invoke(reservations, new object[] { rotated })!, "旋转物品识别占格冲突");
        GridShape outside = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(-3, -2).Build();
        Check(!(bool)intersects.Invoke(reservations, new object[] { outside })!, "分离位置保持独立");
    }

    private static Assembly LoadMod(string workspace) => AssemblyLoadContext.Default.LoadFromAssemblyPath(
        Path.Combine(workspace, "bin/Release/netstandard2.1/ProbablyStolen_FlatStorage.dll"));

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
