using Mono.Cecil;
using ProbablyStolen_FlatStorage;

internal static class SelectionChecks
{
    internal static void Input()
    {
        foreach (var region in Enum.GetValues<SelectionPointerRegion>())
        {
            bool expected = region is SelectionPointerRegion.Blank or SelectionPointerRegion.Inventory or SelectionPointerRegion.Item;
            Check(SelectionInput.AllowsItems(region, false, false, false, false, false) == expected, "只有画布空白、库存和物品接收原生物品输入");
            Check(SelectionInput.AllowsBand(region) == (region is SelectionPointerRegion.Blank or SelectionPointerRegion.Inventory), "空白和库存开始框选");
            Check(!SelectionInput.AllowsItems(region, true, false, false, false, false), "小地图、菜单与对话框阻止物品输入");
            Check(!SelectionInput.AllowsItems(region, false, true, false, false, false), "输入文字时阻止物品输入");
            Check(!SelectionInput.AllowsItems(region, false, false, true, false, false), "平移期间阻止物品输入");
            Check(!SelectionInput.AllowsItems(region, false, false, false, true, false), "拖动分组期间阻止物品输入");
            Check(!SelectionInput.AllowsItems(region, false, false, false, false, true), "空格加左键用于平移");
        }
        Check(SelectionInput.AllowsDrop(SelectionPointerRegion.Control, true, false, false, false, false, false), "真实分页标题支持放置物品");
        Check(!SelectionInput.AllowsBand(SelectionPointerRegion.Control), "分页标题保留点击交互");
        Check(!SelectionInput.AllowsDrop(SelectionPointerRegion.Control, false, false, false, false, false, false), "普通控件阻止物品放置");
        Check(!SelectionInput.AllowsDrop(SelectionPointerRegion.Control, true, true, false, false, false, false), "分页标题受到界面遮挡时阻止放置");
    }

    internal static void Bounds()
    {
        var view = new Box(50, 30, 640, 400);
        Same(SelectionInput.Clip(new Box(-100, -200, 1500, 1200), view), view);
        Same(SelectionInput.Clip(new Box(65, 85, 100, 200), view), new Box(65, 85, 100, 200));
        Same(SelectionInput.Clip(new Box(20, 10, 100, 100), view), new Box(50, 30, 70, 80));
        foreach (var band in new[] { new Box(-200, 50, 100, 60), new Box(800, 50, 40, 60), new Box(90, 500, 40, 60), new Box(90, 60, 0, 30) })
        {
            var clipped = SelectionInput.Clip(band, view);
            Check(clipped.Width == 0 || clipped.Height == 0, "视口外与空框选没有命中面积");
        }
        foreach (float zoom in new[] { .25f, .5f, 1, 1.5f, 2 })
        foreach (float pan in new[] { -900f, -75, 0, 450 })
        {
            var displayed = new Box(50 + pan + 300 * zoom, 30 + pan + 200 * zoom, 90 * zoom, 60 * zoom);
            var clipped = SelectionInput.Clip(displayed, view);
            Check(clipped.Width >= 0 && clipped.Height >= 0, "缩放和平移后的屏幕区域保持有效");
            if (clipped.Width > 0 && clipped.Height > 0)
                Check(clipped.X >= view.X && clipped.Y >= view.Y && clipped.X + clipped.Width <= 690 && clipped.Y + clipped.Height <= 430,
                    "屏幕裁剪保持在视口内");
        }
    }

    internal static void Scale()
    {
        foreach (float zoom in new[] { .25f, .5f, 1, 1.5f, 2 })
        foreach (float displayScale in new[] { .75f, 1, 1.5f, 2, 3 })
        {
            float relative = SelectionInput.RelativeScale(zoom * displayScale, displayScale);
            Check(Math.Abs(relative - zoom) < 1e-6, "分辨率与游戏界面整体缩放保持拖动局部偏移");
            float resized = SelectionInput.RelativeScale(zoom * displayScale * 2, displayScale * 2);
            Check(Math.Abs(resized / relative - 1) < 1e-6, "画布和预览父节点共同缩放保持分组选中物品间距");
            float zoomed = SelectionInput.RelativeScale(zoom * displayScale * 1.25f, displayScale);
            Check(Math.Abs(zoomed / relative - 1.25f) < 1e-6, "画布缩放同步改变预览间距");
        }
    }

    internal static void Native(string managed)
    {
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"));
        var handler = game.MainModule.Types.Single(type => type.Name == "ItemMultiSelectHandler");
        Check(handler.Fields.Single(field => field.Name == "selectedItems").FieldType.FullName.Contains("GameItemElement"), "使用真实多选物品集合");
        Check(handler.Fields.Single(field => field.Name == "dragOffsets").FieldType.FullName.Contains("UnityEngine.Vector2"), "使用真实分组拖动偏移");
        var state = handler.NestedTypes.Single(type => type.Name == "State");
        Check(state.IsEnum && handler.Fields.Single(field => field.Name == "state").FieldType.FullName == state.FullName,
            "多选状态字段使用真实原生枚举");
        Check(new[] { "PendingBand", "Banding", "Selected", "PendingGroupDrag", "GroupDragging" }.All(name => state.Fields.Any(field => field.Name == name)), "真实多选状态存在");
        Calls(handler, "OnEventPress", "IsPointerOverDragSurface");
        Calls(handler, "OnEventMouseMove", "UpdateBand", "StartGroupDrag");
        Calls(handler, "BandOverlapsItemShape", "get_lossyScale", "ForwardTransform", "GetLocal");
        Calls(handler, "ComputeBandItems", "MayRemove", "MaxNumRemove", "IsPinned", "BandOverlapsItemShape");
        Calls(handler, "IsFormationValid", "MayPlayerInsertInto", "CheckShapePlacement", "MayRemove", "MaxNumRemove");
        Calls(handler, "EndGroupDrag", "IsFormationValid", "TryAcceptAll", "GetStackSlot", "TryAcceptOnce", "FindDropInventoryAt");
        Calls(handler, "RestoreGhosts", "ToggleFakeBackground", "Validate");
        foreach (string name in new[] { "OnEventPress", "OnEventMouseMove" })
            Check(handler.Methods.Single(method => method.Name == name).Body.Instructions.Any(instruction =>
                instruction.Operand is FieldReference field && field.Name == "hoveringStoreUI"), "原生按下和移动阶段都检查界面遮挡");
    }

    private static void Calls(TypeDefinition type, string name, params string[] expected)
    {
        var calls = type.Methods.Single(method => method.Name == name).Body.Instructions
            .Select(instruction => instruction.Operand).OfType<MethodReference>().Select(method => method.Name).ToHashSet();
        Check(expected.All(calls.Contains), "真实原生调用存在：" + name);
    }
    private static void Same(Box actual, Box expected) => Check(actual.X == expected.X && actual.Y == expected.Y &&
        actual.Width == expected.Width && actual.Height == expected.Height, "屏幕区域裁剪结果");
    private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
}
