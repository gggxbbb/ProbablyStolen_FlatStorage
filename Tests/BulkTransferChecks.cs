using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Features(string workspace)
    {
        var resolverType = LoadMod(workspace).GetType("ProbablyStolen_FlatStorage.BulkFeatureContractResolver", true)!;
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
            { ContractResolver = (IContractResolver)Activator.CreateInstance(resolverType, true)! });
        var feature = new ItemFeature(ItemFeature.FeatureType.TemporarySelling)
            .SetValueModifier(15).SetPreExposedValueMod(5).SetPublicDisplay("议价特征");
        var original = JToken.FromObject(new[] { feature }, serializer);
        Check(JToken.DeepEquals(original, JToken.FromObject(new[] { feature }, serializer)), "读取真实报价特征保持状态");
        feature.SetValueModifier(25);
        Check(!JToken.DeepEquals(original, JToken.FromObject(new[] { feature }, serializer)), "私有报价数值变化使预览失效");
        feature.SetValueModifier(15).SetPreExposedValueMod(10);
        Check(!JToken.DeepEquals(original, JToken.FromObject(new[] { feature }, serializer)), "未公开报价数值变化使预览失效");
        feature.SetPreExposedValueMod(5);
        feature.SetFeatureDiscovered(true);
        Check(!JToken.DeepEquals(original, JToken.FromObject(new[] { feature }, serializer)), "发现状态变化使预览失效");
        feature.SetFeatureDiscovered(false);
        Check(JToken.DeepEquals(original, JToken.FromObject(new[] { feature }, serializer)), "真实报价状态恢复后序列化一致");
        Check(!JToken.DeepEquals(original, JToken.FromObject(Array.Empty<ItemFeature>(), serializer)), "报价特征数量变化使预览失效");
    }

    private static Assembly LoadMod(string workspace) => AssemblyLoadContext.Default.LoadFromAssemblyPath(
        Path.Combine(workspace, "bin/Release/netstandard2.1/ProbablyStolen_FlatStorage.dll"));

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
