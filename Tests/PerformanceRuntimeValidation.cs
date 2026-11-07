#if FLAT_STORAGE_RUNTIME_VALIDATION
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    public sealed class PerformanceRuntimeValidation : MonoBehaviour
    {
        private static string directory;
        private readonly Harmony isolation = new Harmony("flat_storage.runtime-validation");
        private int stage, began;
        private float deadline;
        private bool finished;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private PointerEventData pointer;
        private string firstResult;
        private Type[] originalLoop;
        internal static void Install()
        {
            directory = Path.GetFullPath(typeof(PerformanceRuntimeValidation).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "FlatStorageValidationDirectory").Value);
            if (!File.Exists(Path.Combine(directory, "save_1.es3"))) throw new FileNotFoundException("Runtime validation save copy is missing.");
            new GameObject("Flat Storage Runtime Validation").AddComponent<PerformanceRuntimeValidation>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject); deadline = Time.realtimeSinceStartup + 150;
            isolation.Patch(AccessTools.PropertyGetter(typeof(ES3Settings), "FullPath"),
                postfix: new HarmonyMethod(typeof(PerformanceRuntimeValidation), nameof(IsolateSavePath)));
            Debug.Log("[FlatStorageRuntime] Starting real-player validation with isolated ES3 files: " + directory);
        }

        private static void IsolateSavePath(ref string __result)
        {
            if (string.Equals(Path.GetExtension(__result), ".es3", StringComparison.OrdinalIgnoreCase))
                __result = Path.Combine(directory, Path.GetFileName(__result));
        }

        private void Update()
        {
            if (finished) return;
            try { Step(); }
            catch (Exception exception)
            {
                finished = true;
                Debug.LogError("[FlatStorageRuntime] FAIL " + exception);
                File.WriteAllText(Path.Combine(directory, "result.txt"), "FAIL\n" + exception);
                Application.Quit(1);
            }
        }

        private void Step()
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Runtime validation did not finish.");
            var monitor = PerformanceMonitor.Current;
            if (monitor?.Error != null && stage != 5 && stage != 7) throw new InvalidOperationException(monitor.Error);
            if (stage == 0)
            {
                if (MainMenuUIController.Instance == null || NewGameData.Instance == null) return;
                NewGameData.Instance.saveSlot = 1;
                SceneManager.LoadScene("InventoryScene"); stage = 1;
                return;
            }
            var controller = FlatStorageController.Current;
            if (stage == 1)
            {
                if (controller == null || CustomUIManager.Instance == null || EventSystem.current == null) return;
                controller.Toggle();
                if (!controller.IsOpen) throw new InvalidOperationException("Storage did not open.");
                Canvas.ForceUpdateCanvases();
                CheckRaycastBounds();
                var state = (CanvasState)AccessTools.Field(typeof(FlatStorageController), "state").GetValue(controller);
                var viewWidth = (float)AccessTools.Property(typeof(FlatStorageController), "ViewWidth").GetValue(controller);
                var viewHeight = (float)AccessTools.Property(typeof(FlatStorageController), "ViewHeight").GetValue(controller);
                state.ZoomAt(viewWidth / 2, viewHeight / 2, .360610038f);
                controller.RefreshNow();
                Canvas.ForceUpdateCanvases();
                CheckRaycastBounds();
                originalLoop = LoopTypes(UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop()).ToArray();
                File.WriteAllLines(Path.Combine(directory, "player-loop.txt"), originalLoop.Select(type => type?.FullName ?? "root"));
                AccessTools.Field(typeof(PerformanceMonitor), "directory").SetValue(monitor, Path.Combine(directory, "captures"));
                monitor.Panel.Toggle();
                Click("perf.start");
                if (!monitor.IsRecording) throw new InvalidOperationException("Record button did not start recording.");
                pointer = new PointerEventData(EventSystem.current);
                began = Time.frameCount; stage = 2;
                return;
            }
            if (stage == 2)
            {
                int elapsed = Time.frameCount - began;
                if (elapsed >= 30 && elapsed < 100)
                {
                    var view = controller.ViewBounds;
                    controller.Focus(view.X + view.Width / 2 + 2, view.Y + view.Height / 2);
                }
                pointer.position = new Vector2(Screen.width * .35f, Screen.height * .4f);
                hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
                if (elapsed == 45) WindowLease.Active.Keys.First().Validate();
                if (elapsed == 60) Click("perf.mark");
                if (elapsed == 70) Click("perf.compact");
                if (elapsed == 75) CheckPanelBounds();
                if (elapsed == 80) Click("perf.expand");
                if (elapsed >= 120)
                {
                    var chart = UnityEngine.Object.FindObjectOfType<PerformanceChart>();
                    if (chart == null || chart.rectTransform.rect.width <= 0) throw new InvalidOperationException("Performance chart has invalid bounds.");
                    if (chart.RenderedVertices == 0) throw new InvalidOperationException("Performance chart has no rendered geometry.");
                    Click("perf.stop"); stage = 3;
                }
                return;
            }
            if (stage == 3 && monitor.ExportPath != null)
            {
                var capture = monitor.Capture;
                if (capture.FrameCount < 100) throw new InvalidOperationException("Recorded frame count is too small.");
                foreach (var metric in new[] { PerfMetric.Controller, PerfMetric.ApplyView, PerfMetric.EventRaycast, PerfMetric.GraphicRaycast, PerfMetric.Clipping, PerfMetric.Panel, PerfMetric.WindowValidate,
                    PerfMetric.GameInput, PerfMetric.NativeCanvasUpdate, PerfMetric.NativeCanvasGeometry, PerfMetric.NativeFrameRender, PerfMetric.NativePresent })
                    if (capture.Aggregate(metric, 0, capture.FrameCount).Calls == 0) throw new InvalidOperationException("Missing real calls: " + metric);
                if (!File.Exists(monitor.ExportPath)) throw new FileNotFoundException("Recording export is missing.");
                if (!capture.Frames.Take(capture.FrameCount).Any(f => (f.Context.Operations & PerfOperation.Pan) != 0))
                    throw new InvalidOperationException("Actual view movement was not recorded.");
                foreach (string name in new[] { "Draw Calls Count", "GC Used Memory", "Main Thread" })
                {
                    int counter = Array.FindIndex(capture.Counters, info => info.Name == name);
                    if (counter < 0 || capture.Counters[counter].Status != "sampled")
                        throw new InvalidOperationException("Missing real engine metric: " + name);
                }
                firstResult = "summaryFrames=" + capture.FrameCount + "\nsummarySpans=" + capture.SpanCount + "\nsummaryExport=" + monitor.ExportPath;
                CheckLoopRestored();
                Click("perf.worst"); Click("perf.previous"); Click("perf.next");
                Click("perf.mode_summary"); monitor.Panel.Tick();
                Click("perf.start");
                if (!monitor.IsRecording || !monitor.Capture.Detailed) throw new InvalidOperationException("Detailed recording did not restart.");
                monitor.Panel.Close(); began = Time.frameCount; stage = 4;
                return;
            }
            if (stage == 4)
            {
                int elapsed = Time.frameCount - began;
                if (elapsed == 25) monitor.Panel.Toggle();
                if (elapsed == 27) { Click("perf.view_calls"); monitor.Panel.Tick(); }
                if (elapsed == 29) { Click("perf.view_engine"); monitor.Panel.Tick(); }
                if (elapsed == 31) { Click("perf.view_events"); monitor.Panel.Tick(); }
                if (elapsed >= 45)
                {
                    CheckPanelBounds();
                    AccessTools.Field(typeof(PerformanceMonitor), "directory").SetValue(monitor, Path.Combine(directory, "save_1.es3"));
                    Click("perf.export"); stage = 5;
                }
                return;
            }
            if (stage == 5 && monitor.Error != null)
            {
                stage = 7;
                return;
            }
            if (stage == 7)
            {
                if (!monitor.Error.Contains("System.IO.IOException") || monitor.IsRecording || monitor.CanStart)
                    throw new InvalidOperationException("Failed export did not preserve its stopped recording: " + monitor.Error);
                AccessTools.Field(typeof(PerformanceMonitor), "directory").SetValue(monitor, Path.Combine(directory, "captures"));
                Click("perf.export"); stage = 6;
                return;
            }
            if (stage == 6 && monitor.ExportPath != null)
            {
                var capture = monitor.Capture;
                if (capture.FrameCount < 40 || capture.SpanCount == 0 || !File.Exists(monitor.ExportPath))
                    throw new InvalidOperationException("Detailed recording is incomplete.");
                if (!capture.Frames.Take(capture.FrameCount).Any(f => !f.Context.PanelVisible))
                    throw new InvalidOperationException("Recording did not continue with the panel closed.");
                var targets = PerformanceHooks.Targets;
                foreach (var target in targets)
                {
                    var method = target.Type == typeof(GraphicRaycaster)
                        ? AccessTools.Method(target.Type, target.Method, new[] { typeof(PointerEventData), typeof(List<RaycastResult>) })
                        : AccessTools.DeclaredMethod(target.Type, target.Method);
                    if (Harmony.GetPatchInfo(method)?.Owners.Contains(ModEntry.Id + ".performance") == true)
                        throw new InvalidOperationException("Recording hooks remain installed after stopping.");
                }
                firstResult += "\ndetailedFrames=" + capture.FrameCount + "\ndetailedSpans=" + capture.SpanCount + "\ndetailedExport=" + monitor.ExportPath;
                monitor.Mode = PerfRecordingMode.FrameTimes;
                Click("perf.start");
                if (Harmony.GetPatchInfo(AccessTools.Method(typeof(CanvasUpdateRegistry), "PerformUpdate"))?.Owners.Contains(ModEntry.Id + ".performance") == true)
                    throw new InvalidOperationException("Frame timing baseline installed method hooks.");
                began = Time.frameCount; stage = 8;
                return;
            }
            if (stage == 8 && Time.frameCount - began >= 45)
            {
                Click("perf.stop"); stage = 9;
                return;
            }
            if (stage == 9 && monitor.ExportPath != null)
            {
                CheckLoopRestored();
                if (monitor.Capture.Aggregate(PerfMetric.Controller, 0, monitor.Capture.FrameCount).Calls != 0)
                    throw new InvalidOperationException("Frame timing baseline contains hooked controller calls.");
                if (monitor.Capture.FrameCount < 40) throw new InvalidOperationException("Frame timing baseline is incomplete.");
                finished = true;
                string result = "PASS\n" + firstResult + "\nframeTimingFrames=" + monitor.Capture.FrameCount + "\nframeTimingExport=" + monitor.ExportPath + "\nraycastBounds=PASS\nexportFailureRecovery=PASS";
                File.WriteAllText(Path.Combine(directory, "result.txt"), result);
                Debug.Log("[FlatStorageRuntime] " + result);
                Application.Quit();
            }
        }

        private static IEnumerable<Type> LoopTypes(UnityEngine.LowLevel.PlayerLoopSystem system)
        {
            yield return system.type;
            if (system.subSystemList != null)
                foreach (var child in system.subSystemList)
                    foreach (var type in LoopTypes(child)) yield return type;
        }

        private void CheckLoopRestored()
        {
            if (!originalLoop.SequenceEqual(LoopTypes(UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop())))
                throw new InvalidOperationException("Player loop differs after recording stopped.");
        }

        private static void CheckRaycastBounds()
        {
            var pointer = new PointerEventData(EventSystem.current);
            var expected = new List<RaycastResult>();
            var actual = new List<RaycastResult>();
            int checks = 0;
            foreach (var lease in WindowLease.Active.Values)
            {
                var canvas = lease.Group.GetComponent<Canvas>();
                if (!canvas.enabled) continue;
                if (canvas.pixelPerfect || !canvas.overridePixelPerfect) throw new InvalidOperationException("Storage canvas still inherits pixel snapping.");
                var bounded = lease.Group.GetComponent<StorageRaycaster>();
                var reference = lease.Group.gameObject.AddComponent<GraphicRaycaster>();
                reference.enabled = false;
                var corners = new Vector3[4];
                var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
                for (int index = 0; index < graphics.Count; index++)
                {
                    var graphic = graphics[index];
                    graphic.rectTransform.GetWorldCorners(corners);
                    var points = new[] { corners[0], corners[1], corners[2], corners[3], (corners[0] + corners[2]) / 2 };
                    foreach (var point in points)
                    {
                        pointer.position = RectTransformUtility.WorldToScreenPoint(bounded.eventCamera, point);
                        expected.Clear(); actual.Clear();
                        reference.Raycast(pointer, expected); bounded.Raycast(pointer, actual);
                        if (!expected.Select(hit => hit.gameObject).SequenceEqual(actual.Select(hit => hit.gameObject)))
                            throw new InvalidOperationException("Raycast differs at " + lease.Source.Id + "/" + graphic.name + "/" + pointer.position);
                        checks++;
                    }
                }
                UnityEngine.Object.Destroy(reference);
            }
            if (checks == 0) throw new InvalidOperationException("No real graphics were checked.");
            Debug.Log("[FlatStorageRuntime] Matching native raycast results: " + checks);
        }

        private static void CheckPanelBounds()
        {
            var panel = PerformanceMonitor.Current.Panel;
            var window = (CustomUIWindow)AccessTools.Field(typeof(PerformancePanel), "window").GetValue(panel);
            foreach (var button in window.ContentRoot.GetComponentsInChildren<Button>())
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(window.ContentRoot, button.transform);
                var area = window.ContentRoot.rect;
                if (bounds.min.x < area.xMin - 1 || bounds.max.x > area.xMax + 1 || bounds.min.y < area.yMin - 1 || bounds.max.y > area.yMax + 1)
                    throw new InvalidOperationException("Panel control exceeds content bounds: " + button.name);
            }
        }

        private static void Click(string key)
        {
            string label = ModText.Get(key);
            var button = UnityEngine.Object.FindObjectsOfType<Button>().FirstOrDefault(b => b.GetComponentInChildren<TMPro.TMP_Text>()?.text == label);
            if (button == null || !button.interactable) throw new InvalidOperationException("Runtime UI button unavailable: " + key);
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }
    }
}
#endif
