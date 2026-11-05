using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Mono.Cecil;
using Newtonsoft.Json;
using ProbablyStolen_FlatStorage;

internal static class RoadmapChecks
{
    internal static void Defaults()
    {
        foreach (float zoom in new[] { .25f, .75f, 1f, 2f })
        foreach (bool automatic in new[] { true, false })
        {
            var created = CanvasState.Create(null, zoom, automatic);
            Check(created.Zoom == zoom && created.AutoArrange == automatic, "新场景应用配置");
            var saved = new CanvasState { Zoom = .5f, AutoArrange = !automatic, PanX = -85, PanY = 77 };
            var restored = CanvasState.Create(saved.Encode(), zoom, automatic);
            Check(restored.Zoom == saved.Zoom && restored.AutoArrange == saved.AutoArrange &&
                restored.PanX == saved.PanX && restored.PanY == saved.PanY, "已有场景保留存档设置");
        }
        foreach (float invalid in new[] { 0, .24f, 2.01f, float.NaN, float.PositiveInfinity })
            Reject(() => CanvasState.Create(null, invalid, false));
    }

    internal static void Pruning()
    {
        var store = new CanvasState { Zoom = .75f, PanX = 85, AutoArrange = false };
        store.Groups.AddRange(new[]
        {
            new GroupPosition { Id = "root:main", X = 5, Y = 6 },
            new GroupPosition { Id = "root:faucet", X = 11, Y = 12 },
            new GroupPosition { Id = "root:removed" },
            new GroupPosition { Id = "item:12", X = 34, Y = 56 },
            new GroupPosition { Id = "item:13" },
            new GroupPosition { Id = "item:0" }
        });
        var outside = new CanvasState { Zoom = 1.5f };
        outside.Groups.AddRange(new[]
        {
            new GroupPosition { Id = "root:afterhour", X = -50 },
            new GroupPosition { Id = "root:bazar" },
            new GroupPosition { Id = "item:12", X = 78, Y = 90 },
            new GroupPosition { Id = "item:13" }
        });
        var data = new Dictionary<string, string>
        {
            ["opened:0"] = "true", ["opened:12"] = "true", ["opened:13"] = "true",
            ["opened:bad"] = "true", ["layout:store"] = store.Encode(),
            ["layout:outside:night_market"] = outside.Encode(), ["another_setting"] = "preserved"
        };
        SaveDataPruning.Prune(data, new HashSet<int> { 12 });
        Check(data.ContainsKey("opened:12") && !data.ContainsKey("opened:13") &&
            !data.ContainsKey("opened:0") && !data.ContainsKey("opened:bad"), "打开记录删除失效身份");
        Check(data["another_setting"] == "preserved", "保留其他配置");
        var stored = CanvasState.Decode(data["layout:store"]);
        Check(stored.Groups.Select(g => g.Id).SequenceEqual(new[] { "root:main", "root:faucet", "item:12" }), "店内分组清理");
        Check(stored.Groups.Last().X == 34 && stored.Groups.Last().Y == 56 && stored.Zoom == .75f &&
            stored.PanX == 85 && !stored.AutoArrange, "清理保持坐标和视口");
        var travelled = CanvasState.Decode(data["layout:outside:night_market"]);
        Check(travelled.Groups.Select(g => g.Id).SequenceEqual(new[] { "root:afterhour", "root:bazar", "item:12" }), "跨场景现存物品保留");
        Check(travelled.Groups.Last().X == 78 && travelled.Zoom == 1.5f, "其他场景保留独立布局");
        string retained = data["layout:store"];
        SaveDataPruning.Prune(data, new HashSet<int> { 12 });
        Check(data["layout:store"] == retained, "重复清理保持数据");
    }

    internal static void InvalidPruning()
    {
        foreach (string bad in new[] { "{", "null", "{\"Version\":99}", "{\"Zoom\":0}" })
            Reject(() => SaveDataPruning.Prune(new Dictionary<string, string> { ["layout:store"] = bad }, new HashSet<int>()));
    }

    internal static void Localization(string workspace)
    {
        var chinese = ReadCsv(Path.Combine(workspace, "Localization/zh.csv"));
        var english = ReadCsv(Path.Combine(workspace, "Localization/en.csv"));
        Check(chinese.Keys.Order().SequenceEqual(english.Keys.Order()), "中英文键集一致");
        foreach (string key in english.Keys)
        {
            var en = CompositeFormat.Parse(english[key]);
            var zh = CompositeFormat.Parse(chinese[key]);
            Check(en.MinimumArgumentCount == zh.MinimumArgumentCount, "格式参数数量不匹配：" + key);
            var arguments = Enumerable.Range(0, en.MinimumArgumentCount).Select(i => (object)(i + 1)).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, english[key], arguments);
            _ = string.Format(CultureInfo.GetCultureInfo("zh-CN"), chinese[key], arguments);
        }
        Check(english.ContainsKey("ui.search") && english.ContainsKey("ui.preview") && english.ContainsKey("settings.zoom"), "功能词条存在");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void LocaleRegistration(string workspace)
    {
        var mod = new LoadedMod();
        mod.Localization["zh"] = ReadCsv(Path.Combine(workspace, "Localization/zh.csv"));
        mod.Localization["en"] = ReadCsv(Path.Combine(workspace, "Localization/en.csv"));
        var register = LoadMod(workspace).GetType("ProbablyStolen_FlatStorage.ModText", true)
            .GetMethod("RegisterLocales", BindingFlags.Static | BindingFlags.NonPublic)!;
        register.Invoke(null, new object[] { mod });
        Check(ReferenceEquals(mod.Localization["zh-Hans"], mod.Localization["zh"]), "真实游戏语言代码注册");
        Check(mod.Localization["zh-Hans"]["ui.search"] == "搜索物品", "中文词条读取");
        Check(mod.Localization["en"]["ui.search"] == "Search items", "英文词条保留");
        mod.Localization.Remove("en");
        bool rejected = false;
        try { register.Invoke(null, new object[] { mod }); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
        Check(rejected, "缺失语言资源立即报错");
    }

    internal static void NativeTabs(string workspace) => LoadMod(workspace)
        .GetType("ProbablyStolen_FlatStorage.NativeTabsAccess", true)
        .GetMethod("Check", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

    internal static void GameApi(string managed)
    {
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"));
        void Method(string type, string name, params string[] parameters)
        {
            var actual = game.MainModule.Types.Single(t => t.FullName == type);
            Check(actual.Methods.Count(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(parameters)) == 1, "游戏接口发生变化：" + type + "." + name);
        }
        Method("CustomUIManager", "CreateWindow", "System.String", "System.String", "System.String", "UnityEngine.GameObject", "CustomUIManager/Style");
        Method("CustomUIManager", "CloseWindow", "System.String");
        Method("CustomUIBuilder", "AddInput", "System.String", "System.String", "System.Action`1<System.String>", "System.String", "System.Action`1<System.String>");
        Method("CustomUIBuilder", "AddDropdown", "System.Collections.Generic.IList`1<System.String>", "System.Int32", "System.Action`1<System.Int32>", "System.String");
        Method("CustomUIBuilder", "AddButton", "System.String", "System.Action", "System.String");
        Method("GameGridScrollableInventory", "SetScroll", "System.Single");
        Method("GameGridScrollableInventory", "GetVisibleHeight");
        Method("GameGridScrollableInventory", "GetMinVisibleHeight");
        Method("ModHelper", "GetLocalized", "System.String", "System.String", "System.Object[]");
        Method("ModHelper", "GetModDataKeys", "System.String");
        Method("ModHelper", "RemoveModData", "System.String", "System.String");
        var hooks = game.MainModule.Types.Single(t => t.FullName == "ModHook");
        Check(hooks.Events.Single(e => e.Name == "OnGameLoadedInit").EventType.FullName == "System.Action", "读档前回调类型");
        Check(hooks.Events.Single(e => e.Name == "OnAfterhourArriveEarly").EventType.FullName == "System.Action`1<System.String>", "到达前回调类型");
        Check(hooks.Events.Single(e => e.Name == "OnGameLoadedLate").EventType.FullName == "System.Action", "读档后回调类型");
    }

    private static Dictionary<string, string> ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            string[] fields = parser.ReadFields()!;
            Check(fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[0]) && !string.IsNullOrWhiteSpace(fields[1]), "无效 CSV 词条：" + path);
            Check(result.TryAdd(fields[0], fields[1]), "重复 CSV 键：" + fields[0]);
        }
        return result;
    }

    private static Assembly LoadMod(string workspace) => AssemblyLoadContext.Default.LoadFromAssemblyPath(
        Path.Combine(workspace, "bin/Release/netstandard2.1/ProbablyStolen_FlatStorage.dll"));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidOperationException) { rejected = true; }
        catch (JsonException) { rejected = true; }
        Check(rejected, "无效数据必须抛出错误");
    }
}
