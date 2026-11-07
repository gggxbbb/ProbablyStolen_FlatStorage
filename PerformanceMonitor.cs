using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProbablyStolen_FlatStorage
{
    internal enum PerfRecordingMode { Summary, Detailed, FrameTimes }

    [DefaultExecutionOrder(-32000)]
    public sealed class PerformanceMonitor : MonoBehaviour
    {
        internal static PerformanceMonitor Current { get; private set; }
        internal static PerfCapture Active => Current != null && Thread.CurrentThread.ManagedThreadId == Current.mainThread
            && Current.Capture?.IsRecording == true ? Current.Capture : null;
        internal PerfCapture Capture { get; private set; }
        internal PerfRecordingMode Mode { get; set; }
        internal bool Detailed => Mode == PerfRecordingMode.Detailed;
        internal bool IsRecording => Capture?.IsRecording == true;
        internal bool IsExporting => export != null;
        internal bool CanStart => !IsRecording && !IsExporting && (Capture == null || ExportPath != null);
        internal string ExportPath { get; private set; }
        internal string Error { get; private set; }
        internal string StopRequested { get; private set; }
        internal PerformancePanel Panel { get; private set; }
        private PerformanceHooks hooks;
        private PerformanceLoop loop;
        private PerformanceCounters counters;
        private Task<string> export;
        private int mainThread, markers, geometryFrame = -1, itemCount, graphicCount, raycastCount;
        private float nextGeometrySample;
        private PerfContext context;
        private bool shuttingDown;
        private string directory;
        private Action updateRecording, updateContext;

        internal static void Install()
        {
            if (Current == null) new GameObject("Flat Storage Performance").AddComponent<PerformanceMonitor>();
        }

        private void Awake()
        {
            Current = this; mainThread = Thread.CurrentThread.ManagedThreadId;
            updateRecording = UpdateRecording; updateContext = UpdateContext;
            DontDestroyOnLoad(gameObject);
            directory = Path.Combine(Application.persistentDataPath, "FlatStorage", "Performance");
            Panel = new PerformancePanel(this);
        }

        internal static void TogglePanel() { Install(); Current.Run(Current.Panel.Toggle); }
        internal static void Boundary(string reason)
        {
            if (Current == null) return;
            Current.RequestStop(reason);
            Current.Panel.Close();
        }

        internal static void Shutdown()
        {
            if (Current == null) return;
            Current.Run(() =>
            {
                Current.StopNow("mod-disabled");
                if (Current.export != null) Current.CompleteExport();
                Current.shuttingDown = true;
                Destroy(Current.gameObject);
            });
        }

        internal void Run(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                Error = exception.ToString();
                ModEntry.Instance.Log.Error(exception);
                if (IsRecording)
                {
                    Capture.Metadata["recordingError"] = Error;
                    RequestStop("recording-error");
                }
                Panel.Invalidate();
            }
        }

        internal void StartRecording()
        {
            if (!CanStart) throw new InvalidOperationException("Export the current recording before starting another recording.");
            Error = StopRequested = null; markers = 0;
            counters = new PerformanceCounters();
            try
            {
                if (Mode != PerfRecordingMode.FrameTimes) { hooks = new PerformanceHooks(); loop = new PerformanceLoop(); }
                var metadata = new Dictionary<string, object>
                {
                    ["modVersion"] = typeof(ModEntry).Assembly.GetName().Version.ToString(),
                    ["recordingMode"] = Mode.ToString(),
                    ["methodInstrumentation"] = hooks != null,
                    ["assemblySha256"] = AssemblyHash(),
                    ["enabledMods"] = ModLoader.Mods.Where(mod => mod.IsEnabled).Select(mod => new
                        { mod.Manifest.ID, mod.Manifest.Name, version = mod.Manifest.ModVersion, source = mod.Source.ToString() }).ToArray(),
                    ["gameVersion"] = Application.version, ["unityVersion"] = Application.unityVersion,
                    ["batchMode"] = Application.isBatchMode,
                    ["platform"] = Application.platform.ToString(), ["operatingSystem"] = SystemInfo.operatingSystem,
                    ["processor"] = SystemInfo.processorType, ["processorCount"] = SystemInfo.processorCount,
                    ["memoryMiB"] = SystemInfo.systemMemorySize, ["graphicsDevice"] = SystemInfo.graphicsDeviceName,
                    ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(), ["graphicsMemoryMiB"] = SystemInfo.graphicsMemorySize,
                    ["screenWidth"] = Screen.width, ["screenHeight"] = Screen.height,
                    ["targetFrameRate"] = Application.targetFrameRate, ["vSyncCount"] = QualitySettings.vSyncCount,
                    ["refreshInterval"] = ModSettings.RefreshInterval, ["edgePanSpeed"] = ModSettings.EdgePanSpeed,
                    ["scene"] = FlatStorageController.Current?.PerformanceScene,
                    ["cpuTimerFrequency"] = counters.CpuTimerFrequency, ["gpuTimerFrequency"] = counters.GpuTimerFrequency,
                    ["availableEngineMetrics"] = counters.Available,
                    ["uiCanvases"] = UnityEngine.Object.FindObjectsOfType<Canvas>().Select(canvas => new
                    {
                        name = canvas.name, root = canvas.rootCanvas.name, canvas.pixelPerfect, canvas.overridePixelPerfect,
                        storageId = WindowLease.Find(canvas.transform)?.Source.Id,
                        canvas.scaleFactor, mode = canvas.renderMode.ToString(), canvas.overrideSorting,
                        graphics = UnityEngine.UI.GraphicRegistry.GetGraphicsForCanvas(canvas).Count
                    }).ToArray(),
                    ["uiTiming"] = "NativeCanvasTransform times EarlyUpdate.UpdateCanvasRectTransform before the closing Update boundary. Other Native metrics time PostLateUpdate: UpdateRectTransform, PlayerUpdateCanvases, PlayerEmitCanvasGeometry, FinishFrameRendering and PresentAfterDraw. These include native work and waits. CanvasPreRender and CanvasRender wrap Unity callbacks; TextUpdates and CanvasUpdate are nested. GraphicGeometry, GraphicMaterial, TextGenerate, TextScale, Clipping and raycast metrics identify component subjects. Parent total times overlap; use self times for attribution. Native engine counters may overlap across threads and are not additive.",
                    ["counterSampling"] = "Read new engine samples at early Update without resetting collection. A single new sample is associated with the preceding frame interval. Missing or multiple ambiguous samples are null; engine-counter-gap events record ambiguity. Startup samples are discarded.",
                    ["nativeTimingAssociation"] = "Native GPU/CPU timings retain native timestamps and observation frame only. Exact source Unity frame is unknown.",
                    ["frameInterval"] = "Elapsed monotonic time between consecutive recorder sampling boundaries in early Update; includes engine work and waits. Partial final frames are excluded from percentiles.",
                    ["operationSampling"] = "Operation flags and pointer state sampled in LateUpdate. Pan includes observed view movement; Panel means pointer inside panel. panelVisible tracks panel rendering. Explicit pan/zoom/open/close markers are emitted at the corresponding control action.",
                    ["selfTime"] = "Elapsed call time excluding directly nested instrumented calls on the main thread; includes uninstrumented work and scheduling waits.",
                    ["instrumentation"] = "Summary and Detailed install targeted main-thread Harmony hooks and Unity player-loop phase timers. FrameTimes records frame intervals, operations and engine counters without those hooks or phase timers. Compare matching FrameTimes and Summary runs to quantify instrumentation overhead. Recorder and panel work are measured in every mode.",
                    ["percentiles"] = "Nearest rank over complete recorded frame intervals.",
                    ["geometrySampling"] = "Visible group Graphic registry counts and catalog item counts sampled every 0.5 seconds; geometrySampleFrame indicates age."
                };
                Capture = new PerfCapture(metadata, counters.Info, Detailed);
                ExportPath = null;
                Capture.Event("start", Mode.ToString());
                nextGeometrySample = 0; geometryFrame = -1;
                FlatStorageController.Current?.PerformanceChanged();
                Panel.Invalidate();
            }
            catch
            {
                loop?.Dispose(); loop = null;
                hooks?.Dispose(); hooks = null;
                counters.Dispose(); counters = null;
                throw;
            }
        }

        private static string AssemblyHash()
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(typeof(ModEntry).Assembly.Location))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal void RequestStop(string reason = "user") { if (IsRecording) StopRequested = reason; }

        private void Update() => Run(updateRecording);
        private void UpdateRecording()
        {
            if (export != null && export.IsCompleted) CompleteExport();
            if (StopRequested == "recording-error") StopNow(StopRequested);
            if (IsRecording)
            {
                if (Capture.HasFrame)
                {
                    using (Capture.Measure(PerfMetric.Recorder)) counters.ReadCompletedFrame(Capture, Time.frameCount);
                    Capture.CompleteFrame();
                }
                if (StopRequested != null || Capture.CapacityReason != null)
                    StopNow(StopRequested ?? Capture.CapacityReason);
                else
                {
                    if (Capture.FrameCount == 0) counters.DiscardPendingSamples();
                    Capture.BeginFrame(Time.frameCount, context);
                }
            }
            using (Active?.Measure(PerfMetric.Panel) ?? default) Panel.Tick();
        }

        private void LateUpdate()
        {
            if (!IsRecording) return;
            Run(updateContext);
        }

        private void UpdateContext()
        {
            using (Capture.Measure(PerfMetric.Recorder))
            {
                var controller = FlatStorageController.Current;
                if (Time.unscaledTime >= nextGeometrySample)
                {
                    nextGeometrySample = Time.unscaledTime + .5f;
                    controller?.SamplePerformanceGeometry(out itemCount, out graphicCount, out raycastCount);
                    if (controller == null) itemCount = graphicCount = raycastCount = 0;
                    geometryFrame = Time.frameCount;
                }
                var previous = context;
                context = controller == null ? default : controller.PerformanceContext();
                if ((context.Operations & previous.Operations & PerfOperation.StorageOpen) != 0)
                {
                    if (context.Zoom != previous.Zoom)
                        context.Operations |= PerfOperation.Zoom;
                    if (context.PanX != previous.PanX || context.PanY != previous.PanY)
                        context.Operations |= PerfOperation.Pan;
                }
                context.Items = itemCount; context.Graphics = graphicCount; context.RaycastGraphics = raycastCount;
                context.GeometrySampleFrame = geometryFrame;
                context.PointerX = Input.mousePosition.x; context.PointerY = Input.mousePosition.y;
                context.Scroll = Input.mouseScrollDelta.y;
                context.Buttons = (Input.GetMouseButton(0) ? 1 : 0) | (Input.GetMouseButton(1) ? 2 : 0) | (Input.GetMouseButton(2) ? 4 : 0);
                context.ScreenWidth = Screen.width; context.ScreenHeight = Screen.height; context.Focused = Application.isFocused;
                context.PanelVisible = Panel.IsOpen;
                if (Panel.ContainsPointer) context.Operations |= PerfOperation.Panel;
                Capture.SetContext(context);
                FrameTimingManager.CaptureFrameTimings();
            }
        }

        private void StopNow(string reason)
        {
            if (!IsRecording) return;
            Capture.Stop(reason);
            loop?.Dispose(); loop = null;
            hooks?.Dispose(); hooks = null;
            counters?.Dispose(); counters = null;
            StopRequested = null;
            FlatStorageController.Current?.PerformanceChanged();
            Panel.Invalidate();
            Export();
        }

        internal void Mark()
        {
            if (IsRecording) Capture.Event("marker", (++markers).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Panel.Invalidate();
        }

        internal void Export()
        {
            if (IsRecording) { RequestStop("export-requested"); return; }
            if (Capture == null || IsExporting || ExportPath != null) return;
            Error = Capture.Metadata.TryGetValue("recordingError", out var recordingError) ? (string)recordingError : null;
            var stopped = Capture;
            export = Task.Run(() => PerformanceExport.Write(stopped, directory));
            Panel.Invalidate();
        }

        private void CompleteExport()
        {
            var completed = export; export = null;
            ExportPath = completed.GetAwaiter().GetResult();
            ModEntry.Instance.Log.Log("Performance capture exported: " + ExportPath);
            Panel.Invalidate();
        }

        private void OnApplicationQuit()
        {
            Run(() => { StopNow("application-quit"); if (export != null) CompleteExport(); });
        }

        private void OnDestroy()
        {
            if (!shuttingDown) Run(() => { StopNow("monitor-destroyed"); if (export != null) CompleteExport(); });
            loop?.Dispose(); hooks?.Dispose(); counters?.Dispose(); Panel?.Dispose();
            if (Current == this) Current = null;
        }
    }
}
