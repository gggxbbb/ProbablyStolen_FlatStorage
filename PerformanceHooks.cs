using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class PerformanceHooks : IDisposable
    {
        private readonly Harmony harmony = new Harmony(ModEntry.Id + ".performance");
        private static readonly Dictionary<MethodBase, PerfMetric> metrics = new Dictionary<MethodBase, PerfMetric>();
        internal static readonly (Type Type, string Method, PerfMetric Metric)[] Targets =
        {
            (typeof(FlatStorageController), "Update", PerfMetric.Controller),
            (typeof(FlatStorageController), "UpdateDiscovery", PerfMetric.Discovery),
            (typeof(FlatStorageController), "SynchronizeSources", PerfMetric.SynchronizeSources),
            (typeof(FlatStorageController), "FlushChanges", PerfMetric.FlushChanges),
            (typeof(FlatStorageController), "ObserveWindows", PerfMetric.ObserveWindows),
            (typeof(FlatStorageController), "Apply", PerfMetric.ApplyView),
            (typeof(FlatStorageController), "PrepareNearby", PerfMetric.PrepareNearby),
            (typeof(FlatStorageController), "ArrangeLayout", PerfMetric.Arrange),
            (typeof(FlatStorageController), "Save", PerfMetric.SaveLayout),
            (typeof(CanvasTools), "Refresh", PerfMetric.ToolsRefresh),
            (typeof(CanvasTools), "Filter", PerfMetric.Search),
            (typeof(StorageMiniMap), "UpdateView", PerfMetric.MiniMap),
            (typeof(CanvasPointer), "At", PerfMetric.PointerQuery),
            (typeof(WindowLease), "Refresh", PerfMetric.WindowRefresh),
            (typeof(WindowLease), "ScanHierarchy", PerfMetric.ScanHierarchy),
            (typeof(StorageCatalog), "Refresh", PerfMetric.Catalog),
            (typeof(EventSystem), "RaycastAll", PerfMetric.EventRaycast),
            (typeof(GraphicRaycaster), "Raycast", PerfMetric.GraphicRaycast),
            (typeof(RectMask2D), "PerformClipping", PerfMetric.Clipping),
            (typeof(CanvasUpdateRegistry), "PerformUpdate", PerfMetric.CanvasUpdate),
            (typeof(PixelWindow), "Validate", PerfMetric.WindowValidate),
            (typeof(GameGridInventory), "Validate", PerfMetric.GridValidate),
            (typeof(GameItemElement), "Validate", PerfMetric.ItemValidate),
            (typeof(MachineryTabs), "Validate", PerfMetric.TabsValidate)
        };

        internal PerformanceHooks()
        {
            var prefix = new HarmonyMethod(typeof(PerformanceHooks), nameof(Begin)) { priority = Priority.First };
            var finalizer = new HarmonyMethod(typeof(PerformanceHooks), nameof(Finish)) { priority = Priority.Last };
            try
            {
                foreach (var target in Targets)
                {
                    // GraphicRaycaster 同时具有实例和静态同名方法，明确绑定公开实例入口。
                    var method = target.Type == typeof(GraphicRaycaster)
                        ? AccessTools.Method(target.Type, target.Method, new[] { typeof(PointerEventData), typeof(List<RaycastResult>) })
                        : AccessTools.DeclaredMethod(target.Type, target.Method);
                    if (method == null) throw new MissingMethodException(target.Type.FullName, target.Method);
                    metrics.Add(method, target.Metric);
                    harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                }
            }
            catch { Dispose(); throw; }
        }

        private static void Begin(object __instance, MethodBase __originalMethod, out PerfScope __state)
        {
            __state = default;
            var capture = PerformanceMonitor.Active;
            if (capture == null || !capture.HasFrame) return;
            int subject = -1;
            if (__instance is WindowLease lease)
                subject = capture.Subject(lease.Source.Id, lease.Source.Label, lease.Width, lease.Height, lease.Catalog?.Items.Count ?? 0);
            __state = capture.Measure(metrics[__originalMethod], subject);
        }

        private static Exception Finish(Exception __exception, PerfScope __state)
        {
            __state.Dispose();
            return __exception;
        }

        public void Dispose() { harmony.UnpatchAll(ModEntry.Id + ".performance"); metrics.Clear(); }
    }
}
