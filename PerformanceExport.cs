using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ProbablyStolen_FlatStorage
{
    internal static class PerformanceExport
    {
        internal static readonly string[] FrameColumns =
        {
            "unityFrame", "startMs", "intervalMs", "complete", "operations", "panX", "panY", "zoom",
            "pointerX", "pointerY", "scroll", "buttons", "windows", "visibleWindows", "items", "graphics",
            "raycastGraphics", "geometrySampleFrame", "refreshQueue", "nearby", "enteredWindows", "leftWindows",
            "pointerCacheHits", "pointerCacheMisses", "screenWidth", "screenHeight", "focused", "startTicks", "endTicks", "panelVisible"
        };
        private static readonly JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            Culture = CultureInfo.InvariantCulture
        };

        internal static string Write(PerfCapture capture, string directory)
        {
            if (capture == null || capture.IsRecording) throw new InvalidOperationException("Only stopped performance recordings can be exported.");
            directory = Path.GetFullPath(directory);
            System.IO.Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "FlatStorage-" + capture.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
            string htmlPath = Path.ChangeExtension(path, ".html");
            var document = Document(capture);
            using (var stream = new FileStream(path + ".partial", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteJson(zip, "capture.json", document);
                using (var writer = Writer(zip, "frames.csv"))
                {
                    writer.WriteLine(string.Join(",", FrameColumns));
                    foreach (var row in Frames(capture)) NumericRow(writer, row);
                }
                using (var writer = Writer(zip, "metrics.csv"))
                {
                    writer.WriteLine("frameIndex,metricIndex,calls,totalMs,selfMs,maxMs");
                    foreach (var row in Metrics(capture)) NumericRow(writer, row);
                }
                using (var writer = Writer(zip, "report.html")) WriteReport(writer, document);
                WriteJson(zip, "trace.json", new { traceEvents = Trace(capture), displayTimeUnit = "ms" });
            }
            using (var writer = new StreamWriter(new FileStream(htmlPath + ".partial", FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)))
                WriteReport(writer, document);
            File.Move(htmlPath + ".partial", htmlPath);
            File.Move(path + ".partial", path);
            return path;
        }

        private static StreamWriter Writer(ZipArchive archive, string name) =>
            new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        private static void WriteJson(ZipArchive archive, string name, object value)
        {
            using (var writer = Writer(archive, name)) Serialize(writer, value);
        }
        private static void Serialize(TextWriter writer, object value)
        {
            using (var json = new JsonTextWriter(writer) { CloseOutput = false })
                JsonSerializer.Create(settings).Serialize(json, value);
        }

        private static void WriteReport(TextWriter writer, object document)
        {
            string template;
            using (var stream = typeof(PerformanceExport).Assembly.GetManifestResourceStream("FlatStorage.PerformanceReport")
                ?? throw new MissingManifestResourceException("FlatStorage.PerformanceReport"))
            using (var reader = new StreamReader(stream)) template = reader.ReadToEnd();
            const string token = "__CAPTURE_JSON__";
            int index = template.IndexOf(token, StringComparison.Ordinal);
            if (index < 0 || index != template.LastIndexOf(token, StringComparison.Ordinal))
                throw new InvalidDataException("Performance report template has an invalid data placeholder.");
            writer.Write(template.Substring(0, index)); Serialize(writer, document); writer.Write(template.Substring(index + token.Length));
        }

        // CSV 的字段限定为数字、布尔值和十进制时间戳，文字元数据保存在 JSON 中。
        private static void NumericRow(TextWriter writer, object[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0) writer.Write(',');
                if (values[i] is bool flag) writer.Write(flag ? "1" : "0");
                else writer.Write(Convert.ToString(values[i], CultureInfo.InvariantCulture));
            }
            writer.WriteLine();
        }

        private static object Document(PerfCapture capture) => new
        {
            schemaVersion = 1, capture.Id, capture.StartedUtc, capture.StopReason, capture.Detailed, capture.SlowSpanMilliseconds,
            clock = new { frequency = Stopwatch.Frequency, originTicks = capture.OriginTicks.ToString(CultureInfo.InvariantCulture),
                stoppedTicks = capture.StoppedTicks.ToString(CultureInfo.InvariantCulture) },
            capacity = new { frames = capture.Frames.Length, spans = capture.Spans.Length, events = capture.Events.Length,
                capture.OmittedSpans, capture.OmittedEvents },
            metadata = capture.Metadata, metricNames = PerfCapture.MetricNames,
            operations = Enum.GetValues(typeof(PerfOperation)).Cast<PerfOperation>().ToDictionary(value => value.ToString(), value => (int)value),
            frameColumns = FrameColumns, frames = Frames(capture),
            metricColumns = new[] { "frameIndex", "metricIndex", "calls", "totalMs", "selfMs", "maxMs" }, metrics = Metrics(capture),
            counterInfo = capture.Counters, counterValues = Counters(capture),
            spanColumns = new[] { "frameIndex", "metricIndex", "startMs", "durationMs", "selfMs", "depth", "subjectIndex", "startTicks", "durationTicks", "selfTicks" },
            spans = Spans(capture), events = Events(capture), subjects = capture.Subjects,
            nativeTimings = capture.NativeTimings.Select(t => new
            {
                t.ObservedUnityFrame, observedTicks = t.ObservedTicks.ToString(CultureInfo.InvariantCulture),
                presentTimestamp = t.PresentTimestamp.ToString(CultureInfo.InvariantCulture),
                completeTimestamp = t.CompleteTimestamp.ToString(CultureInfo.InvariantCulture),
                t.CpuMilliseconds, t.GpuMilliseconds, t.SyncInterval
            }),
            statistics = PerfStatistics.Frames(capture, 0, capture.FrameCount)
        };

        private static IEnumerable<object[]> Frames(PerfCapture capture)
        {
            for (int i = 0; i < capture.FrameCount; i++)
            {
                var frame = capture.Frames[i]; var c = frame.Context;
                yield return new object[] { frame.UnityFrame, PerfCapture.ToMilliseconds(frame.StartTicks - capture.OriginTicks), frame.Milliseconds,
                    frame.Complete, (int)c.Operations, c.PanX, c.PanY, c.Zoom, c.PointerX, c.PointerY, c.Scroll, c.Buttons,
                    c.Windows, c.VisibleWindows, c.Items, c.Graphics, c.RaycastGraphics, c.GeometrySampleFrame, c.RefreshQueue, c.Nearby,
                    frame.EnteredWindows, frame.LeftWindows, frame.PointerHits, frame.PointerMisses, c.ScreenWidth, c.ScreenHeight, c.Focused,
                    frame.StartTicks.ToString(CultureInfo.InvariantCulture), frame.EndTicks.ToString(CultureInfo.InvariantCulture), c.PanelVisible };
            }
        }

        private static IEnumerable<object[]> Metrics(PerfCapture capture)
        {
            for (int frame = 0; frame < capture.FrameCount; frame++)
            for (int metric = 0; metric < PerfCapture.MetricCount; metric++)
            {
                var value = capture.Metrics[frame * PerfCapture.MetricCount + metric];
                if (value.Calls == 0) continue;
                yield return new object[] { frame, metric, value.Calls, PerfCapture.ToMilliseconds(value.TotalTicks),
                    PerfCapture.ToMilliseconds(value.SelfTicks), PerfCapture.ToMilliseconds(value.MaxTicks) };
            }
        }

        private static IEnumerable<object[]> Counters(PerfCapture capture)
        {
            for (int frame = 0; frame < capture.FrameCount; frame++)
            {
                var row = new object[capture.CounterCount];
                for (int counter = 0; counter < row.Length; counter++)
                {
                    int offset = frame * row.Length + counter;
                    if (capture.CounterValid[offset]) row[counter] = capture.CounterValues[offset];
                }
                yield return row;
            }
        }

        private static IEnumerable<object[]> Spans(PerfCapture capture)
        {
            for (int i = 0; i < capture.SpanCount; i++)
            {
                var s = capture.Spans[i];
                yield return new object[] { s.Frame, s.Metric, PerfCapture.ToMilliseconds(s.StartTicks - capture.OriginTicks),
                    PerfCapture.ToMilliseconds(s.DurationTicks), PerfCapture.ToMilliseconds(s.SelfTicks), s.Depth, s.Subject,
                    s.StartTicks.ToString(CultureInfo.InvariantCulture), s.DurationTicks.ToString(CultureInfo.InvariantCulture), s.SelfTicks.ToString(CultureInfo.InvariantCulture) };
            }
        }

        private static IEnumerable<object> Events(PerfCapture capture)
        {
            for (int i = 0; i < capture.EventCount; i++)
            {
                var e = capture.Events[i];
                yield return new { e.Frame, e.UnityFrame, e.Kind, e.Detail, timeMs = PerfCapture.ToMilliseconds(e.Ticks - capture.OriginTicks),
                    ticks = e.Ticks.ToString(CultureInfo.InvariantCulture) };
            }
        }

        private static IEnumerable<object> Trace(PerfCapture capture)
        {
            yield return new { ph = "M", pid = 1, tid = 1, name = "thread_name", args = new { name = "Unity main thread — instrumented calls" } };
            for (int i = 0; i < capture.SpanCount; i++)
            {
                var s = capture.Spans[i];
                yield return new { ph = "X", pid = 1, tid = 1, name = PerfCapture.MetricNames[s.Metric], cat = "FlatStorage",
                    ts = PerfCapture.ToMicroseconds(s.StartTicks - capture.OriginTicks), dur = PerfCapture.ToMicroseconds(s.DurationTicks),
                    args = new { s.Frame, s.UnityFrame, s.Subject, selfMicroseconds = PerfCapture.ToMicroseconds(s.SelfTicks) } };
            }
            for (int i = 0; i < capture.FrameCount; i++)
            {
                var frame = capture.Frames[i];
                yield return new { ph = "C", pid = 1, tid = 1, name = "Frame interval", ts = PerfCapture.ToMicroseconds(frame.StartTicks - capture.OriginTicks),
                    args = new { milliseconds = frame.Milliseconds, visibleWindows = frame.Context.VisibleWindows, graphics = frame.Context.Graphics } };
            }
            for (int i = 0; i < capture.EventCount; i++)
            {
                var e = capture.Events[i];
                yield return new { ph = "i", s = "t", pid = 1, tid = 1, name = e.Kind, ts = PerfCapture.ToMicroseconds(e.Ticks - capture.OriginTicks),
                    args = new { e.Detail, e.UnityFrame } };
            }
        }
    }
}
