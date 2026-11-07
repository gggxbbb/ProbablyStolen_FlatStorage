using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json.Linq;
using ProbablyStolen_FlatStorage;

internal static class PerformanceChecks
{
    private static PerfCapture Capture(bool detailed = true, int frames = 20, int spans = 100, int events = 100) =>
        new(new Dictionary<string, object> { ["source"] = "dotnet-real-clock-validation", ["markup"] = "</script><script>throw Error('invalid')</script>" },
            new[] { new PerfCounterInfo { Name = "GC.GetTotalMemory", Unit = "Bytes", Available = true },
                new PerfCounterInfo { Name = "unavailable", Status = "not-enumerated" } }, detailed, frames, spans, events);

    private static void Work()
    {
        using var algorithm = SHA256.Create();
        byte[] hash = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 4000))));
        Check(hash.Length == 32, "真实 SHA256 运算完成");
    }

    internal static void Timing()
    {
        var capture = Capture();
        capture.BeginFrame(1, new PerfContext { Operations = PerfOperation.Pan });
        using (capture.Measure(PerfMetric.Controller))
        {
            Work();
            using (capture.Measure(PerfMetric.ApplyView)) Work();
        }
        capture.SetCounter(0, GC.GetTotalMemory(false));
        capture.Pointer(true); capture.Pointer(false); capture.Visibility(true);
        capture.CompleteFrame(); capture.Stop("test-complete");
        var outer = capture.Aggregate(PerfMetric.Controller, 0, 1);
        var child = capture.Aggregate(PerfMetric.ApplyView, 0, 1);
        Check(outer.TotalTicks > child.TotalTicks && child.TotalTicks > 0, "真实单调时钟记录嵌套耗时");
        Check(outer.SelfTicks == outer.TotalTicks - child.TotalTicks, "父调用正确扣除子调用");
        Check(capture.Frames[0].EndTicks > capture.Frames[0].StartTicks && capture.Frames[0].Complete, "完整帧时间范围");
        Check(capture.CounterValid[0] && !capture.CounterValid[1], "缺失指标保留不可用状态");
        Check(capture.Frames[0].PointerHits == 1 && capture.Frames[0].PointerMisses == 1 && capture.Frames[0].EnteredWindows == 1, "实际记录接口维护计数");
    }

    internal static void Capacity()
    {
        var capture = Capture(frames: 1, spans: 1);
        capture.BeginFrame(1, default);
        using (capture.Measure(PerfMetric.Controller)) Work();
        using (capture.Measure(PerfMetric.ApplyView)) Work();
        Check(capture.SpanCount == 1 && capture.OmittedSpans == 1 && capture.CapacityReason == "span-capacity", "调用明细上限显式记录");
        capture.CompleteFrame();
        Check(capture.CapacityReason == "frame-capacity", "帧上限请求停止");
        capture.Stop("capacity");
        Check(capture.Aggregate(PerfMetric.ApplyView, 0, 1).Calls == 1, "明细容量保持逐帧聚合完整");
        bool rejected = false;
        try { capture.BeginFrame(2, default); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "停止状态拒绝继续写入");
    }

    internal static void ExceptionScopes()
    {
        var capture = Capture(); capture.BeginFrame(1, default);
        bool observed = false;
        try
        {
            using (capture.Measure(PerfMetric.WindowRefresh))
            {
                Work(); throw new InvalidOperationException("expected validation exception");
            }
        }
        catch (InvalidOperationException exception) { observed = exception.Message == "expected validation exception"; }
        capture.CompleteFrame(); capture.Stop("exception-test");
        Check(observed && capture.Aggregate(PerfMetric.WindowRefresh, 0, 1).Calls == 1, "真实异常传播且计时作用域完成");
    }

    internal static void Statistics()
    {
        var capture = Capture();
        capture.BeginFrame(1, default); Work(); capture.CompleteFrame();
        capture.BeginFrame(2, default); Work(); capture.Stop("partial");
        var stats = PerfStatistics.Frames(capture, 0, capture.FrameCount);
        Check(stats.Count == 1 && stats.Median == capture.Frames[0].Milliseconds, "部分帧排除于统计");
        var reference = PerfStatistics.Values(new List<double> { 1, 2, 3, 4, 100 });
        Check(reference.Median == 3 && reference.P95 == 100 && reference.P99 == 100 && reference.Over50 == 1, "最近秩公式与慢帧计数");
    }

    internal static void Export(string workspace)
    {
        var capture = Capture();
        capture.Subject("container:1", "测试 <容器>", 100, 100, 4);
        for (int i = 0; i < 8; i++)
        {
            capture.BeginFrame(i + 1, new PerfContext { Operations = i < 4 ? PerfOperation.Pan : PerfOperation.Zoom, Zoom = 1, VisibleWindows = 1 });
            using (capture.Measure(PerfMetric.Controller))
            using (capture.Measure(PerfMetric.ApplyView, 0)) Work();
            capture.SetCounter(0, GC.GetTotalMemory(false));
            capture.Event("marker", "真实计时验证"); capture.CompleteFrame();
        }
        capture.Stop("test-complete");
        string directory = Path.Combine(workspace, ".work", "performance-tests", capture.Id);
        string path = PerformanceExport.Write(capture, directory);
        using var archive = ZipFile.OpenRead(path);
        Check(archive.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "capture.json", "frames.csv", "metrics.csv", "report.html", "trace.json" }), "导出文件集合完整");
        using var reader = new StreamReader(archive.GetEntry("capture.json")!.Open());
        var data = JObject.Parse(reader.ReadToEnd());
        Check(data["frames"]!.Count() == 8 && data["statistics"]!["count"]!.Value<int>() == 8, "JSON 保留全部实际计时帧");
        Check(data["counterValues"]![0]![1]!.Type == JTokenType.Null, "缺失指标导出为 null");
        Check(data["frames"]![0]![27]!.Value<string>() == capture.Frames[0].StartTicks.ToString(CultureInfo.InvariantCulture), "原始时钟计数精确保存");
        using var csv = new TextFieldParser(archive.GetEntry("frames.csv")!.Open());
        csv.SetDelimiters(",");
        Check(csv.ReadFields()!.SequenceEqual(PerformanceExport.FrameColumns), "CSV 列定义与 JSON 一致");
        int rows = 0;
        while (!csv.EndOfData)
        {
            var fields = csv.ReadFields()!;
            Check(fields.Length == PerformanceExport.FrameColumns.Length, "CSV 字段完整");
            Check(long.Parse(fields[27], CultureInfo.InvariantCulture) == capture.Frames[rows].StartTicks, "CSV 精确时间戳"); rows++;
        }
        Check(rows == 8, "CSV 全部记录");
        using var trace = new StreamReader(archive.GetEntry("trace.json")!.Open());
        var traceData = JObject.Parse(trace.ReadToEnd());
        Check(traceData["traceEvents"]!.Count(e => e["ph"]!.Value<string>() == "X") == capture.SpanCount, "调用时间线保留全部实际采样");
        string html = File.ReadAllText(Path.ChangeExtension(path, ".html"));
        Check(!html.Contains("</script><script>throw"), "报告安全嵌入实际元数据");
        Check(!File.Exists(path + ".partial"), "导出完成后具有完整文件名");
        File.WriteAllText(Path.Combine(workspace, ".work", "performance-tests", "latest-report.txt"), Path.ChangeExtension(path, ".html"));
        Console.WriteLine("  性能导出验证：" + path);
    }

    internal static void NativeTargets(string workspace)
    {
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(workspace, "bin/Release/netstandard2.1/ProbablyStolen_FlatStorage.dll"));
        var type = assembly.GetType("ProbablyStolen_FlatStorage.PerformanceHooks", true)!;
        var targets = (Array)type.GetField("Targets", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (object target in targets)
        {
            var tuple = target.GetType(); var owner = (Type)tuple.GetField("Item1")!.GetValue(target)!;
            var name = (string)tuple.GetField("Item2")!.GetValue(target)!;
            var methods = owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == name).ToList();
            Check(methods.Count == 1 && methods[0].GetMethodBody() != null, "真实性能采样目标：" + owner.FullName + "." + name);
        }
        Console.WriteLine("  已核验 " + targets.Length + " 个实际托管采样入口。");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
