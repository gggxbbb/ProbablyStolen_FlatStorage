using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal enum PerfMetric
    {
        Controller, Discovery, SynchronizeSources, FlushChanges, ObserveWindows, ApplyView, PrepareNearby,
        Arrange, ToolsRefresh, Search, MiniMap, PointerQuery, WindowRefresh, Catalog, ScanHierarchy,
        EventRaycast, GraphicRaycast, Clipping, CanvasUpdate, WindowValidate, GridValidate, ItemValidate,
        TabsValidate, SaveLayout, Recorder, Panel, Count
    }

    [Flags]
    internal enum PerfOperation
    {
        None = 0, StorageOpen = 1, Pan = 2, Zoom = 4, GroupDrag = 8, ItemDrag = 16,
        Selection = 32, Typing = 64, Panel = 128
    }

    internal struct PerfContext
    {
        public PerfOperation Operations;
        public float PanX, PanY, Zoom, PointerX, PointerY, Scroll;
        public int Buttons, Windows, VisibleWindows, Items, Graphics, RaycastGraphics, GeometrySampleFrame;
        public int RefreshQueue, Nearby, ScreenWidth, ScreenHeight;
        public bool Focused, PanelVisible;
    }

    internal struct PerfFrame
    {
        public int UnityFrame;
        public long StartTicks, EndTicks;
        public PerfContext Context;
        public int PointerHits, PointerMisses, EnteredWindows, LeftWindows;
        public bool Complete;
        public double Milliseconds => PerfCapture.ToMilliseconds(EndTicks - StartTicks);
    }

    internal struct PerfAggregate
    {
        public int Calls;
        public long TotalTicks, SelfTicks, MaxTicks;
        internal void Add(long total, long self)
        {
            Calls++; TotalTicks += total; SelfTicks += self; MaxTicks = Math.Max(MaxTicks, total);
        }
    }

    internal struct PerfSpan
    {
        public int Frame, UnityFrame, Metric, Depth, Subject;
        public long StartTicks, DurationTicks, SelfTicks;
    }

    internal struct PerfEvent
    {
        public int Frame, UnityFrame;
        public long Ticks;
        public string Kind, Detail;
    }

    internal sealed class PerfCounterInfo
    {
        public string Name, Category, Unit, Status;
        public bool Available;
    }

    internal struct PerfNativeTiming
    {
        public int ObservedUnityFrame;
        public long ObservedTicks;
        public ulong PresentTimestamp, CompleteTimestamp;
        public double CpuMilliseconds, GpuMilliseconds;
        public uint SyncInterval;
    }

    internal sealed class PerfSubject
    {
        public string Id, Label;
        public float Width, Height;
        public int Items;
    }

    internal readonly struct PerfScope : IDisposable
    {
        private readonly PerfCapture capture;
        private readonly int depth, sequence;
        internal PerfScope(PerfCapture capture, int depth, int sequence)
        { this.capture = capture; this.depth = depth; this.sequence = sequence; }
        public void Dispose() => capture?.EndScope(depth, sequence);
    }

    internal sealed class PerfCapture
    {
        internal const int DefaultFrameCapacity = 60000;
        internal const int DefaultSpanCapacity = 200000;
        internal const int DefaultEventCapacity = 16000;
        internal static readonly int MetricCount = (int)PerfMetric.Count;
        internal static readonly string[] MetricNames = Enum.GetNames(typeof(PerfMetric)).Take(MetricCount).ToArray();
        internal readonly PerfFrame[] Frames;
        internal readonly PerfAggregate[] Metrics;
        internal readonly PerfSpan[] Spans;
        internal readonly PerfEvent[] Events;
        internal readonly long[] CounterValues;
        internal readonly bool[] CounterValid;
        internal readonly PerfCounterInfo[] Counters;
        internal readonly List<PerfNativeTiming> NativeTimings;
        internal readonly List<PerfSubject> Subjects = new List<PerfSubject>();
        internal readonly Dictionary<string, object> Metadata;
        internal readonly string Id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        internal readonly DateTime StartedUtc = DateTime.UtcNow;
        internal readonly long OriginTicks = Stopwatch.GetTimestamp();
        internal readonly bool Detailed;
        internal readonly double SlowSpanMilliseconds;
        internal int FrameCount { get; private set; }
        internal int SpanCount { get; private set; }
        internal int EventCount { get; private set; }
        internal int OmittedSpans { get; private set; }
        internal int OmittedEvents { get; private set; }
        internal bool IsRecording { get; private set; } = true;
        internal bool HasFrame => IsRecording && frameOpen;
        internal string StopReason { get; private set; }
        internal long StoppedTicks { get; private set; }
        internal string CapacityReason { get; private set; }
        internal int CounterCount => Counters.Length;
        internal int CurrentUnityFrame => frameOpen ? Frames[FrameCount].UnityFrame : FrameCount > 0 ? Frames[FrameCount - 1].UnityFrame : -1;
        private bool frameOpen;
        private int depth, sequence;
        private readonly ScopeState[] activeScopes = new ScopeState[128];
        private readonly Dictionary<string, int> subjectIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private struct ScopeState { internal int Metric, Sequence, Subject; internal long Start, Children; }

        internal PerfCapture(Dictionary<string, object> metadata, PerfCounterInfo[] counters, bool detailed = false,
            int frameCapacity = DefaultFrameCapacity, int spanCapacity = DefaultSpanCapacity, int eventCapacity = DefaultEventCapacity,
            double slowSpanMilliseconds = .25)
        {
            if (frameCapacity < 1 || spanCapacity < 1 || eventCapacity < 1 || slowSpanMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(frameCapacity));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Counters = counters ?? throw new ArgumentNullException(nameof(counters));
            Detailed = detailed; SlowSpanMilliseconds = slowSpanMilliseconds;
            Frames = new PerfFrame[frameCapacity]; Metrics = new PerfAggregate[checked(frameCapacity * MetricCount)];
            Spans = new PerfSpan[spanCapacity]; Events = new PerfEvent[eventCapacity];
            CounterValues = new long[checked(frameCapacity * CounterCount)]; CounterValid = new bool[CounterValues.Length];
            NativeTimings = new List<PerfNativeTiming>(frameCapacity);
        }

        internal static double ToMilliseconds(long ticks) => ticks * (1000.0 / Stopwatch.Frequency);
        internal static double ToMicroseconds(long ticks) => ticks * (1000000.0 / Stopwatch.Frequency);

        internal void BeginFrame(int unityFrame, PerfContext context)
        {
            if (!IsRecording || frameOpen || FrameCount >= Frames.Length)
                throw new InvalidOperationException("Performance frame cannot begin in the current state.");
            if (FrameCount > 0 && unityFrame <= Frames[FrameCount - 1].UnityFrame)
                throw new InvalidOperationException("Performance frame identifiers must increase.");
            Frames[FrameCount] = new PerfFrame { UnityFrame = unityFrame,
                StartTicks = FrameCount == 0 ? Stopwatch.GetTimestamp() : Frames[FrameCount - 1].EndTicks, Context = context };
            frameOpen = true;
        }

        internal void CompleteFrame(bool complete = true)
        {
            if (!frameOpen || depth != 0) throw new InvalidOperationException("Performance frame contains unfinished calls.");
            ref var frame = ref Frames[FrameCount];
            frame.EndTicks = Stopwatch.GetTimestamp(); frame.Complete = complete;
            if (FrameCount == 0 || Frames[FrameCount - 1].Context.Operations != frame.Context.Operations)
                Event("operation", frame.Context.Operations.ToString());
            frameOpen = false; FrameCount++;
            if (FrameCount == Frames.Length) CapacityReason = "frame-capacity";
        }

        internal void SetContext(PerfContext context) { if (HasFrame) Frames[FrameCount].Context = context; }
        internal void SetCounter(int index, long value)
        {
            if (!HasFrame) return;
            if (index < 0 || index >= CounterCount) throw new ArgumentOutOfRangeException(nameof(index));
            int offset = FrameCount * CounterCount + index;
            CounterValues[offset] = value; CounterValid[offset] = true;
        }
        internal void Pointer(bool hit)
        {
            if (!HasFrame) return;
            if (hit) Frames[FrameCount].PointerHits++; else Frames[FrameCount].PointerMisses++;
        }
        internal void Visibility(bool show)
        {
            if (!HasFrame) return;
            if (show) Frames[FrameCount].EnteredWindows++; else Frames[FrameCount].LeftWindows++;
        }
        internal int Subject(string id, string label, float width, float height, int items)
        {
            if (!subjectIndices.TryGetValue(id, out int index))
            { index = Subjects.Count; subjectIndices.Add(id, index); Subjects.Add(new PerfSubject { Id = id }); }
            var subject = Subjects[index]; subject.Label = label; subject.Width = width; subject.Height = height; subject.Items = items;
            return index;
        }

        internal PerfScope Measure(PerfMetric metric, int subject = -1)
        {
            if (!HasFrame) return default;
            if ((uint)metric >= MetricCount) throw new ArgumentOutOfRangeException(nameof(metric));
            if (depth == activeScopes.Length) throw new InvalidOperationException("Performance call nesting exceeds its capacity.");
            int current = depth++;
            activeScopes[current] = new ScopeState { Metric = (int)metric, Sequence = ++sequence, Start = Stopwatch.GetTimestamp(), Subject = subject };
            return new PerfScope(this, current, sequence);
        }

        internal void EndScope(int expectedDepth, int expectedSequence)
        {
            long end = Stopwatch.GetTimestamp();
            if (!HasFrame || depth != expectedDepth + 1 || activeScopes[expectedDepth].Sequence != expectedSequence)
                throw new InvalidOperationException("Performance calls must finish in reverse opening order.");
            var scope = activeScopes[--depth];
            long total = end - scope.Start, self = total - scope.Children;
            Metrics[FrameCount * MetricCount + scope.Metric].Add(total, self);
            if (depth > 0) activeScopes[depth - 1].Children += total;
            if (Detailed || ToMilliseconds(total) >= SlowSpanMilliseconds)
            {
                if (SpanCount == Spans.Length) { OmittedSpans++; CapacityReason = "span-capacity"; return; }
                Spans[SpanCount++] = new PerfSpan { Frame = FrameCount, UnityFrame = CurrentUnityFrame,
                    Metric = scope.Metric, Depth = depth, Subject = scope.Subject, StartTicks = scope.Start,
                    DurationTicks = total, SelfTicks = self };
            }
        }

        internal void Event(string kind, string detail = null)
        {
            if (!IsRecording) return;
            if (EventCount == Events.Length) { OmittedEvents++; CapacityReason = "event-capacity"; return; }
            Events[EventCount++] = new PerfEvent { Frame = FrameCount, UnityFrame = CurrentUnityFrame,
                Ticks = Stopwatch.GetTimestamp(), Kind = kind, Detail = detail };
        }

        internal void Stop(string reason)
        {
            if (!IsRecording) return;
            if (frameOpen) CompleteFrame(false);
            Event("stop", reason);
            IsRecording = false; StopReason = reason; StoppedTicks = Stopwatch.GetTimestamp();
        }

        internal PerfAggregate Aggregate(PerfMetric metric, int first, int end)
        {
            if (first < 0 || end > FrameCount || first > end) throw new ArgumentOutOfRangeException(nameof(first));
            var result = new PerfAggregate();
            for (int i = first; i < end; i++)
            {
                var sample = Metrics[i * MetricCount + (int)metric];
                result.Calls += sample.Calls; result.TotalTicks += sample.TotalTicks; result.SelfTicks += sample.SelfTicks;
                result.MaxTicks = Math.Max(result.MaxTicks, sample.MaxTicks);
            }
            return result;
        }
    }

    internal sealed class PerfStatistics
    {
        public int Count;
        public double Mean, Median, P95, P99, Maximum;
        public int Over16, Over33, Over50;
        internal static PerfStatistics Frames(PerfCapture capture, int first, int end)
        {
            var values = new List<double>(end - first);
            for (int i = first; i < end; i++) if (capture.Frames[i].Complete) values.Add(capture.Frames[i].Milliseconds);
            return Values(values);
        }
        internal static PerfStatistics Values(List<double> values)
        {
            var result = new PerfStatistics { Count = values.Count };
            if (values.Count == 0) return result;
            values.Sort();
            result.Mean = values.Average(); result.Median = Quantile(values, .5); result.P95 = Quantile(values, .95);
            result.P99 = Quantile(values, .99); result.Maximum = values[values.Count - 1];
            foreach (double value in values) { if (value > 1000.0 / 60) result.Over16++; if (value > 1000.0 / 30) result.Over33++; if (value > 50) result.Over50++; }
            return result;
        }
        private static double Quantile(List<double> sorted, double q) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * q) - 1)];
    }
}
