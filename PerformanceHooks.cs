using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class PerformanceHooks : IDisposable
    {
        private readonly Harmony harmony = new Harmony(ModEntry.Id + ".performance");
        private static readonly Dictionary<MethodBase, PerfMetric> metrics = new Dictionary<MethodBase, PerfMetric>();
        private static readonly Dictionary<int, int> components = new Dictionary<int, int>();
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
            (typeof(MachineryTabs), "Validate", PerfMetric.TabsValidate),
            (typeof(Canvas), "SendPreWillRenderCanvases", PerfMetric.CanvasPreRender),
            (typeof(Canvas), "SendWillRenderCanvases", PerfMetric.CanvasRender),
            (typeof(Graphic), "Rebuild", PerfMetric.GraphicRebuild),
            (typeof(Graphic), "UpdateGeometry", PerfMetric.GraphicGeometry),
            (typeof(Graphic), "UpdateMaterial", PerfMetric.GraphicMaterial),
            (typeof(LayoutRebuilder), "Rebuild", PerfMetric.LayoutRebuild),
            (typeof(TMP_UpdateManager), "DoRebuilds", PerfMetric.TextUpdates),
            (typeof(TextMeshProUGUI), "Rebuild", PerfMetric.TextRebuild),
            (typeof(TextMeshProUGUI), "GenerateTextMesh", PerfMetric.TextGenerate),
            (typeof(TextMeshProUGUI), "UpdateSDFScale", PerfMetric.TextScale),
            (typeof(StorageRaycaster), "Raycast", PerfMetric.RaycastBounds),
            (typeof(InputActionManager), "Update", PerfMetric.GameInput)
        };

        internal PerformanceHooks()
        {
            var prefix = new HarmonyMethod(typeof(PerformanceHooks), nameof(Begin)) { priority = Priority.First };
            var staticPrefix = new HarmonyMethod(typeof(PerformanceHooks), nameof(BeginStatic)) { priority = Priority.First };
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
                    harmony.Patch(method, prefix: method.IsStatic ? staticPrefix : prefix, finalizer: finalizer);
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
            else if (__instance is Component component && (component is Graphic || component is RectMask2D || component is GraphicRaycaster))
            {
                int id = component.GetInstanceID();
                if (!components.TryGetValue(id, out subject))
                {
                    var names = new List<string>();
                    for (var node = component.transform; node != null; node = node.parent) names.Add(node.name);
                    names.Reverse();
                    var size = ((RectTransform)component.transform).rect.size;
                    var componentLease = WindowLease.Find(component.transform);
                    string location = componentLease == null ? "" : componentLease.Source.Id + " / " + componentLease.Source.Label + " / ";
                    subject = capture.Subject("ui:" + id, location + component.GetType().Name + " / " + string.Join("/", names), size.x, size.y, 0);
                    components.Add(id, subject);
                }
            }
            __state = capture.Measure(metrics[__originalMethod], subject);
        }

        private static void BeginStatic(MethodBase __originalMethod, out PerfScope __state) => Begin(null, __originalMethod, out __state);

        private static Exception Finish(Exception __exception, PerfScope __state)
        {
            __state.Dispose();
            return __exception;
        }

        public void Dispose() { harmony.UnpatchAll(ModEntry.Id + ".performance"); metrics.Clear(); components.Clear(); }
    }
}
