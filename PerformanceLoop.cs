using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class PerformanceLoop : IDisposable
    {
        private struct BeginMarker { }
        private struct EndMarker { }
        private sealed class Timer
        {
            internal PerfMetric Metric;
            private PerfScope scope;
            internal void Begin() => scope = PerformanceMonitor.Active?.Measure(Metric) ?? default;
            internal void End() { scope.Dispose(); scope = default; }
        }

        private readonly Dictionary<Type, Timer> timers = new Dictionary<Type, Timer>
        {
            [typeof(PostLateUpdate.UpdateRectTransform)] = new Timer { Metric = PerfMetric.NativeRectTransform },
            [typeof(EarlyUpdate.UpdateCanvasRectTransform)] = new Timer { Metric = PerfMetric.NativeCanvasTransform },
            [typeof(PostLateUpdate.PlayerUpdateCanvases)] = new Timer { Metric = PerfMetric.NativeCanvasUpdate },
            [typeof(PostLateUpdate.PlayerEmitCanvasGeometry)] = new Timer { Metric = PerfMetric.NativeCanvasGeometry },
            [typeof(PostLateUpdate.FinishFrameRendering)] = new Timer { Metric = PerfMetric.NativeFrameRender },
            [typeof(PostLateUpdate.PresentAfterDraw)] = new Timer { Metric = PerfMetric.NativePresent }
        };
        private bool installed;
        private readonly HashSet<Type> found = new HashSet<Type>();

        internal PerformanceLoop()
        {
            int count = 0;
            var loop = Add(PlayerLoop.GetCurrentPlayerLoop(), ref count);
            if (count != timers.Count) throw new InvalidOperationException("Required Unity player-loop phases are missing: " + string.Join(", ", Missing()));
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
        }

        private PlayerLoopSystem Add(PlayerLoopSystem system, ref int count)
        {
            if (system.subSystemList == null) return system;
            var children = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (var original in system.subSystemList)
            {
                var child = Add(original, ref count);
                if (child.type != null && timers.TryGetValue(child.type, out var timer))
                {
                    children.Add(new PlayerLoopSystem { type = typeof(BeginMarker), updateDelegate = timer.Begin });
                    children.Add(child);
                    children.Add(new PlayerLoopSystem { type = typeof(EndMarker), updateDelegate = timer.End });
                    count++;
                    found.Add(child.type);
                }
                else children.Add(child);
            }
            system.subSystemList = children.ToArray();
            return system;
        }

        private IEnumerable<string> Missing()
        {
            foreach (var type in timers.Keys) if (!found.Contains(type)) yield return type.FullName;
        }

        private static PlayerLoopSystem Remove(PlayerLoopSystem system)
        {
            if (system.subSystemList == null) return system;
            var children = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (var child in system.subSystemList)
                if (child.type != typeof(BeginMarker) && child.type != typeof(EndMarker)) children.Add(Remove(child));
            system.subSystemList = children.ToArray();
            return system;
        }

        public void Dispose()
        {
            if (!installed) return;
            // 只移除本次采样的计时节点，保留其他模组对循环的修改。
            PlayerLoop.SetPlayerLoop(Remove(PlayerLoop.GetCurrentPlayerLoop()));
            installed = false;
        }
    }
}
