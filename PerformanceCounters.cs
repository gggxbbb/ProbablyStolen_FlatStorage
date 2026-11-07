using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class PerformanceCounters : IDisposable
    {
        private static readonly string[] requested =
        {
            "Main Thread", "Render Thread", "CPU Total Frame Time", "CPU Main Thread Frame Time",
            "CPU Render Thread Frame Time", "CPU Present Wait Time", "GPU Frame Time",
            "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count",
            "GC Allocated In Frame", "GC Used Memory", "GC Reserved Memory", "Total Used Memory",
            "Total Reserved Memory", "System Used Memory", "Canvas.BuildBatch", "Canvas.SendWillRenderCanvases",
            "PlayerLoop", "Camera.Render", "Gfx.WaitForPresentOnGfxThread", "RenderLoopDevice.Idle", "GC.Collect",
            "WaitForTargetFPS", "Gfx.WaitForGfxCommandsFromMainThread", "Canvas.UpdateBatches",
            "UI.Layout", "UI.Render", "Vertex Buffer Upload In Frame Bytes", "Index Buffer Upload In Frame Bytes"
        };
        internal readonly PerfCounterInfo[] Info = new PerfCounterInfo[requested.Length];
        internal readonly List<PerfCounterInfo> Available = new List<PerfCounterInfo>();
        private readonly ProfilerRecorder[] recorders = new ProfilerRecorder[requested.Length];
        private readonly int[] consumedSamples = new int[requested.Length];
        private readonly FrameTiming[] timings = new FrameTiming[8];
        private ulong lastComplete;
        internal readonly ulong CpuTimerFrequency;
        internal readonly ulong GpuTimerFrequency;

        internal PerformanceCounters()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var descriptions = new Dictionary<string, ProfilerRecorderDescription>(StringComparer.Ordinal);
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                Available.Add(new PerfCounterInfo { Name = description.Name, Category = description.Category.ToString(),
                    Unit = description.UnitType.ToString(), Available = true, Status = "enumerated" });
                if (!descriptions.ContainsKey(description.Name)) descriptions.Add(description.Name, description);
            }
            try
            {
                for (int i = 0; i < requested.Length; i++)
                {
                    var info = Info[i] = new PerfCounterInfo { Name = requested[i], Status = "not-enumerated" };
                    if (!descriptions.TryGetValue(requested[i], out var description)) continue;
                    info.Category = description.Category.ToString(); info.Unit = description.UnitType.ToString();
                    recorders[i] = ProfilerRecorder.StartNew(description.Category, description.Name,
                        PerfCapture.DefaultFrameCapacity + 128, ProfilerRecorderOptions.SumAllSamplesInFrame);
                    info.Available = recorders[i].Valid && recorders[i].IsRunning;
                    info.Status = info.Available ? "awaiting-sample" : "recorder-unavailable";
                }
                CpuTimerFrequency = FrameTimingManager.GetCpuTimerFrequency();
                GpuTimerFrequency = FrameTimingManager.GetGpuTimerFrequency();
            }
            catch { Dispose(); throw; }
        }

        internal void ReadCompletedFrame(PerfCapture capture, int observedUnityFrame)
        {
            for (int i = 0; i < recorders.Length; i++)
            {
                if (!Info[i].Available) continue;
                int count = recorders[i].Count;
                if (count == recorders[i].Capacity) throw new InvalidOperationException("Engine metric sample capacity reached: " + Info[i].Name);
                int added = count - consumedSamples[i];
                if (added == 1)
                {
                    capture.SetCounter(i, recorders[i].LastValue);
                    Info[i].Status = "sampled";
                }
                else if (added > 1) capture.Event("engine-counter-gap", Info[i].Name + ": " + added);
                consumedSamples[i] = count;
            }
            uint timingCount = FrameTimingManager.GetLatestTimings((uint)timings.Length, timings);
            // 保留引擎自己的时间戳和观察帧，延迟返回的 GPU 数据独立保存。
            ulong newest = lastComplete;
            for (int i = 0; i < timingCount; i++)
            {
                var timing = timings[i];
                if (timing.cpuTimeFrameComplete <= lastComplete) continue;
                newest = Math.Max(newest, timing.cpuTimeFrameComplete);
                capture.NativeTimings.Add(new PerfNativeTiming { ObservedUnityFrame = observedUnityFrame,
                    ObservedTicks = Stopwatch.GetTimestamp(), PresentTimestamp = timing.cpuTimePresentCalled,
                    CompleteTimestamp = timing.cpuTimeFrameComplete, CpuMilliseconds = timing.cpuFrameTime,
                    GpuMilliseconds = timing.gpuFrameTime, SyncInterval = timing.syncInterval });
            }
            lastComplete = newest;
        }

        internal void DiscardPendingSamples()
        {
            for (int i = 0; i < recorders.Length; i++) if (recorders[i].Valid) consumedSamples[i] = recorders[i].Count;
        }
        public void Dispose()
        {
            for (int i = 0; i < recorders.Length; i++)
            {
                if (recorders[i].Valid) recorders[i].Dispose();
                if (Info[i]?.Status == "awaiting-sample") Info[i].Status = "no-samples";
            }
        }
    }
}
