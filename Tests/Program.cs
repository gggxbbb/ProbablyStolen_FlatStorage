using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Mono.Cecil;
using Newtonsoft.Json;
using ProbablyStolen_FlatStorage;

var workspace = Path.GetFullPath(args[0]);
var managed = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(workspace, "../../Probably Stolen_Data/Managed"));
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var tests = new (string Name, Action Run)[]
{
    ("内容、机器与标签相似度", ArrangementChecks.Similarity),
    ("柜台中心与嵌套容器紧密排列", ArrangementChecks.Layout),
    ("排列适应游戏显示比例", ArrangementChecks.Aspect),
    ("相似内容容器聚集", ArrangementChecks.Clustering),
    ("排列边界与大量容器", ArrangementChecks.EdgeCases),
    ("连续跨容器转移的仿射变换", () =>
    {
        // 使用实际交接策略验证矩阵组合；Unity 节点交接由游戏内清单验证。
        foreach (float zoom in new[] { .25f, .5f, .75f, 1f, 1.5f, 2f })
        {
            var local = System.Numerics.Matrix3x2.Identity;
            var source = System.Numerics.Matrix3x2.CreateScale(zoom) * System.Numerics.Matrix3x2.CreateTranslation(100, 200);
            var temporary = System.Numerics.Matrix3x2.CreateTranslation(-500, -500);
            Check(System.Numerics.Matrix3x2.Invert(temporary, out var inverseTemporary), "临时父节点矩阵可逆");
            for (int transfer = 0; transfer < 100; transfer++)
            {
                if (RenderingTransfer.KeepWorldTransform(true, true, true, true))
                    local = local * source * inverseTemporary;
                var destination = System.Numerics.Matrix3x2.CreateScale(zoom) * System.Numerics.Matrix3x2.CreateTranslation(700, 300);
                var rendered = local * destination;
                var point = System.Numerics.Vector2.Transform(new System.Numerics.Vector2(24, 36), rendered);
                Near(rendered.M11, zoom); Near(rendered.M22, zoom);
                Near(point.X, 700 + 24 * zoom); Near(point.Y, 300 + 36 * zoom);
                source = destination;
            }
        }
        Check(RenderingTransfer.KeepWorldTransform(true, false, true, true), "预览节点保持世界变换");
        Check(RenderingTransfer.KeepWorldTransform(true, true, false, true), "原生窗口保持原有行为");
        Check(RenderingTransfer.KeepWorldTransform(true, true, true, false), "其他目标保持原有行为");
        Check(!RenderingTransfer.KeepWorldTransform(false, true, false, false), "局部变换请求保持原值");
    }),
    ("容器与页面标题", () =>
    {
        Check(StorageLabels.Title("父箱标签", "子箱标签", "箱子") == "父箱标签 / 子箱标签", "双层标签");
        Check(StorageLabels.Title("父箱标签", "  ", "箱子") == "父箱标签 / 箱子", "空标签使用名字");
        Check(StorageLabels.Title(null, "工具", "箱子") == "工具", "根容器标签");
        Check(StorageLabels.Title("柜子", "原料页", "仓储") == "柜子 / 原料页", "页面标签");
    }),
    ("剧情箱使用游戏打开标记", () =>
    {
        Check(!StorageVisibility.CanExpose(true, true, false, true, true, false, false), "未打开的剧情箱");
        Check(!StorageVisibility.CanExpose(true, true, false, true, true, false, true), "剧情标记优先于窗口历史");
        Check(StorageVisibility.CanExpose(true, true, false, true, true, true, false), "原生打开后的剧情箱");
    }),
    ("普通容器需要真实打开记录", () =>
    {
        Check(!StorageVisibility.CanExpose(true, true, false, true, false, false, false), "未打开的普通箱子");
        Check(StorageVisibility.CanExpose(true, true, false, true, false, false, true), "已打开的普通箱子");
        Check(StorageVisibility.CanExpose(true, true, false, false, false, false, false), "机器控件访问");
    }),
    ("锁定与所有权限制优先", () =>
    {
        Check(!StorageVisibility.CanExpose(true, true, true, true, true, true, true), "重新锁定的箱子");
        Check(!StorageVisibility.CanExpose(true, false, false, true, false, true, true), "所有权变化");
        Check(!StorageVisibility.CanExpose(false, true, false, true, false, true, true), "没有可访问内容页面的剧情物品");
        Check(!StorageVisibility.CanExpose(true, true, false, true, false, false, false), "已解锁但尚未打开的箱子");
    }),
    ("缩放保持指针下的画布坐标", () =>
    {
        foreach (float zoom in new[] { .25f, .5f, 1f, 1.5f, 2f })
        {
            var state = new CanvasState { PanX = -137, PanY = 83, Zoom = zoom };
            float x = (315 - state.PanX) / zoom, y = (221 - state.PanY) / zoom;
            state.ZoomAt(315, 221, zoom * 1.12f);
            Near((315 - state.PanX) / state.Zoom, x);
            Near((221 - state.PanY) / state.Zoom, y);
        }
    }),
    ("缩放上下限", () =>
    {
        var s = new CanvasState(); s.ZoomAt(0, 0, 100); Near(s.Zoom, 2);
        s.ZoomAt(0, 0, .01f); Near(s.Zoom, .25f);
    }),
    ("不同尺寸分组排列保持间隔", () =>
    {
        var sizes = new[] { new Box(0, 0, 200, 180), new Box(0, 0, 400, 100), new Box(0, 0, 900, 120), new Box(0, 0, 70, 30) };
        var boxes = CanvasMath.Arrange(sizes, 700);
        for (int i = 0; i < boxes.Count; i++)
        {
            Near(boxes[i].Width, sizes[i].Width); Near(boxes[i].Height, sizes[i].Height);
            for (int j = i + 1; j < boxes.Count; j++) Check(!boxes[i].Intersects(boxes[j]), "分组重叠");
        }
        Near(boxes[1].X, 224); Near(boxes[2].Y, 204); Near(boxes[3].Y, 348);
    }),
    ("适应内容支持负坐标", () =>
    {
        var s = new CanvasState(); var box = new Box(-200, -100, 600, 400);
        CanvasMath.Fit(s, new[] { box }, 800, 600);
        Check(box.X * s.Zoom + s.PanX >= 15.9f, "左边界");
        Check((box.Y + box.Height) * s.Zoom + s.PanY <= 584.1f, "下边界");
    }),
    ("空画布及超大分组", () =>
    {
        var s = new CanvasState(); CanvasMath.Fit(s, Array.Empty<Box>(), 800, 600); Near(s.Zoom, 1);
        CanvasMath.Fit(s, new[] { new Box(0, 0, 100000, 100000) }, 800, 600); Near(s.Zoom, .25f);
    }),
    ("视口交集判定", () =>
    {
        var view = new Box(-100, -50, 200, 100);
        Check(view.Intersects(new Box(99, 49, 10, 10)), "边缘可见");
        Check(!view.Intersects(new Box(101, 0, 10, 10)), "视口外隐藏");
    }),
    ("布局实际文件往返与区域设置", () =>
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var s = new CanvasState { Zoom = .75f, PanX = -123.5f, PanY = 77.25f };
            s.Groups.Add(new GroupPosition { Id = "item:中文/23", X = -10.5f, Y = 89.25f });
            string directory = Path.Combine(workspace, ".work/test-output"); System.IO.Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "layout.json"); File.WriteAllText(path, s.Encode());
            var read = CanvasState.Decode(File.ReadAllText(path));
            Near(read.Zoom, s.Zoom); Near(read.PanX, s.PanX); Near(read.Groups[0].Y, s.Groups[0].Y);
            Check(read.Groups[0].Id == s.Groups[0].Id, "分组标识");
        }
        finally { CultureInfo.CurrentCulture = old; }
    }),
    ("损坏布局立即报错", () =>
    {
        Reject(() => CanvasState.Decode("{"));
        Reject(() => CanvasState.Decode("null"));
        Reject(() => CanvasState.Decode("{\"Version\":9}"));
        Reject(() => CanvasState.Decode("{\"Zoom\":0}"));
        Reject(() => CanvasState.Decode("{\"PanX\":\"NaN\"}"));
        Reject(() => CanvasState.Decode("{\"Groups\":[{\"Id\":\"a\"},{\"Id\":\"a\"}]}"));
    }),
    ("程序集引用与 Harmony 目标", () => VerifyAssembly(workspace, managed)),
    ("游戏 ModScanner 实际扫描", () => VerifyScanner(workspace, managed))
};
foreach (var test in tests) { test.Run(); Console.WriteLine("PASS " + test.Name); }
Console.WriteLine($"通过 {tests.Length} 项游戏外验证。");

static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
static void Near(float actual, float expected) => Check(Math.Abs(actual - expected) < .001f, $"{actual} != {expected}");
static void Reject(Action action)
{
    bool rejected = false;
    try { action(); }
    catch (InvalidOperationException) { rejected = true; }
    catch (JsonException) { rejected = true; }
    Check(rejected, "无效布局必须抛出错误");
}

static void VerifyAssembly(string workspace, string managed)
{
    using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"));
    using var mod = AssemblyDefinition.ReadAssembly(Path.Combine(workspace, "bin/Release/netstandard2.1/ProbablyStolen_FlatStorage.dll"));
    foreach (var reference in mod.MainModule.AssemblyReferences)
        Check(reference.Name == "netstandard" || File.Exists(Path.Combine(managed, reference.Name + ".dll")), "缺少引用 " + reference.Name);
    int patches = 0;
    foreach (var type in mod.MainModule.Types)
    foreach (var attr in type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    {
        var targetType = (TypeReference)attr.ConstructorArguments[0].Value;
        var targetName = (string)attr.ConstructorArguments[1].Value;
        var actualType = game.MainModule.Types.Single(t => t.FullName == targetType.FullName);
        var methods = actualType.Methods.Where(m => m.Name == targetName).ToList();
        if (attr.ConstructorArguments.Count == 3)
        {
            var parameters = (CustomAttributeArgument[])attr.ConstructorArguments[2].Value;
            methods = methods.Where(m => m.Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(parameters.Select(p => ((TypeReference)p.Value).FullName))).ToList();
        }
        Check(methods.Count == 1, "Harmony 目标存在歧义或缺失 " + targetType.FullName + "." + targetName);
        foreach (var hook in type.Methods.Where(m => m.Name is "Prefix" or "Postfix"))
        foreach (var parameter in hook.Parameters)
        {
            if (parameter.Name.StartsWith("___"))
                Check(actualType.Fields.Any(f => f.Name == parameter.Name.Substring(3)), "Harmony 字段缺失 " + parameter.Name);
            else if (!parameter.Name.StartsWith("__"))
                Check(methods[0].Parameters.Any(p => p.Name == parameter.Name), "Harmony 参数缺失 " + parameter.Name);
        }
        patches++;
    }
    Check(patches >= 15, "Harmony 接入数量");
    Console.WriteLine($"  已核验 {patches} 个 Harmony 目标。");
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static void VerifyScanner(string workspace, string managed)
{
    string dll = Path.Combine(workspace, "bin/Release/netstandard2.1/ProbablyStolen_FlatStorage.dll");
    var result = ModScanner.Scan(new[] { dll }, System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension));
    Check(string.IsNullOrEmpty(result.Error), result.Error);
    foreach (var finding in result.HardFlags) Console.WriteLine("  " + finding.Kind + ": " + finding.Location + " " + finding.Target);
    Check(result.HardFlags.Count == 0, "能力扫描发现阻止加载的问题");
    var manifest = XDocument.Load(Path.Combine(workspace, "manifest.xml"));
    var declared = manifest.Root.Element("Capabilities").Elements().Select(e => e.Value).ToHashSet();
    Check(result.Capabilities.All(declared.Contains), "能力声明缺失");
    Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
}
