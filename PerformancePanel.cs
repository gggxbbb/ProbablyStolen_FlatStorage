using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class PerformancePanel : IDisposable
    {
        private readonly PerformanceMonitor owner;
        private CustomUIWindow window;
        private TreeNodeRender root, status, summary, detailsViewport, detailsContent, detailsText;
        private PerformanceChart chart;
        private ScrollRect scroll;
        private readonly List<(TreeNodeRender Node, float Width)> buttons = new List<(TreeNodeRender, float)>();
        private TreeNodeRender start, stop, marker, mode, export, report, compactButton, viewButton;
        private bool dirty = true, compact, follow = true;
        private int selected = -1, view;
        private float nextDraw, lastWidth, lastHeight;
        private readonly StringBuilder text = new StringBuilder(4096);
        internal bool IsOpen => window != null && window.IsAlive;
        internal bool ContainsPointer => IsOpen &&
            RectTransformUtility.RectangleContainsScreenPoint(window.Rect, Input.mousePosition);

        internal PerformancePanel(PerformanceMonitor owner) { this.owner = owner; }
        internal void Invalidate() { dirty = true; }
        internal void Toggle() { if (window != null) Close(); else Open(); }

        private TreeNodeRender Button(string key, float width, Action action)
        {
            width *= 2;
            var node = FlatUI.Button(root, ModText.Get(key), 0, width, () => { });
            node.GetComponentInChildren<TMP_Text>().fontSize = 16;
            node.GetComponent<Button>().onClick.AddListener(() => owner.Run(action));
            buttons.Add((node, width)); return node;
        }

        private void Open()
        {
            window = FlatUI.Window("performance", ModText.Get("perf.title"), () => { window = null; root = null; });
            if (window.frame.dragHandle != null) window.frame.dragHandle.enabled = true;
            FlatUI.ManualLayout(window.ContentRoot);
            root = FlatUI.Panel(TreeNodeRender.InstantiateAsRoot(window.ContentRoot), "Performance panel", FlatUI.Surface);
            FlatUI.IsolateCanvas(root);
            buttons.Clear();
            start = Button("perf.start", 44, () => { follow = true; selected = -1; owner.StartRecording(); });
            stop = Button("perf.stop", 36, () => owner.RequestStop());
            marker = Button("perf.mark", 36, owner.Mark);
            mode = Button("perf.mode_summary", 64, () => { owner.Mode = (PerfRecordingMode)(((int)owner.Mode + 1) % 3); Invalidate(); });
            export = Button("perf.export", 36, owner.Export);
            report = Button("perf.copy_path", 50, () => GUIUtility.systemCopyBuffer = Path.GetDirectoryName(owner.ExportPath));
            compactButton = Button("perf.compact", 36, () => { compact = !compact; Resize(); Invalidate(); });
            Button("perf.previous", 32, () => Select(selected - 1));
            Button("perf.next", 32, () => Select(selected + 1));
            Button("perf.worst", 50, () =>
            {
                var capture = owner.Capture;
                if (capture == null || capture.FrameCount == 0) return;
                int worst = 0;
                for (int i = 1; i < capture.FrameCount; i++) if (capture.Frames[i].Milliseconds > capture.Frames[worst].Milliseconds) worst = i;
                Select(worst);
            });
            Button("perf.live", 36, () => { follow = true; Invalidate(); });
            viewButton = Button("perf.view_calls", 54, () => { view = (view + 1) % 3; scroll.verticalNormalizedPosition = 1; Invalidate(); });
            status = Label(root, 16); summary = Label(root, 16);
            var chartObject = new GameObject("Performance frame chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(PerformanceChart));
            chartObject.transform.SetParent(root.transform, false);
            chart = chartObject.GetComponent<PerformanceChart>();
            chart.SelectFrame = Select;
            detailsViewport = FlatUI.Panel(root, "Performance details viewport", FlatUI.Field);
            detailsViewport.mask.enabled = true;
            detailsContent = FlatUI.Panel(detailsViewport, "Performance details", Color.clear);
            detailsContent.image.raycastTarget = false;
            detailsText = Label(detailsContent, 16);
            scroll = detailsViewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = detailsViewport.rectTransform; scroll.content = detailsContent.rectTransform;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20;
            Resize(); Invalidate();
            FlatStorageController.Current?.InvalidatePointer();
        }

        private static TreeNodeRender Label(TreeNodeRender parent, float fontSize)
        {
            var label = FlatUI.Text(parent, "", fontSize);
            label.text.enableWordWrapping = true; label.text.richText = false;
            label.text.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        private void Resize()
        {
            if (window == null) return;
            FlatUI.FitScreen(window, compact ? .42f : .78f);
            if (compact) window.SetSize(window.Rect.rect.width, Math.Min(window.Rect.rect.height, 210));
            lastWidth = lastHeight = -1;
            Layout();
        }

        private void Layout()
        {
            float width = window.ContentRoot.rect.width, height = window.ContentRoot.rect.height;
            if (width == lastWidth && height == lastHeight) return;
            lastWidth = width; lastHeight = height;
            NativeAccess.Place(root, 0, 0, width, height);
            float x = 10, y = 10;
            foreach (var button in buttons)
            {
                bool visible = !compact || button.Node == start || button.Node == stop || button.Node == marker || button.Node == compactButton;
                button.Node.gameObject.SetActive(visible);
                if (!visible) continue;
                if (x > 10 && x + button.Width > width - 10) { x = 10; y += 40; }
                FlatUI.SizeButton(button.Node, new Box(x, y, button.Width, 32)); x += button.Width + 8;
            }
            y += 42;
            NativeAccess.Place(status, 10, y, Math.Max(1, width - 20), 40); y += 42;
            NativeAccess.Place(summary, 10, y, Math.Max(1, width - 20), 44); y += 46;
            chart.gameObject.SetActive(!compact); detailsViewport.gameObject.SetActive(!compact);
            if (compact) return;
            float graphHeight = Math.Max(56, Math.Min(150, (height - y) * .3f));
            FlatUI.Rect(chart.rectTransform, 7, y, Math.Max(1, width - 14), graphHeight); y += graphHeight + 7;
            NativeAccess.Place(detailsViewport, 7, y, Math.Max(1, width - 14), Math.Max(20, height - y - 7));
            LayoutDetails();
        }

        private void LayoutDetails()
        {
            float width = Math.Max(1, detailsViewport.rectTransform.rect.width - 8);
            float height = Math.Max(detailsViewport.rectTransform.rect.height, detailsText.text.GetPreferredValues(detailsText.text.text, width, 100000).y + 10);
            var rect = detailsContent.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(detailsViewport.rectTransform.rect.width, height);
            NativeAccess.Place(detailsText, 4, 4, width, height - 8);
        }

        private void Select(int index)
        {
            if (owner.Capture == null || owner.Capture.FrameCount == 0) return;
            selected = Math.Max(0, Math.Min(owner.Capture.FrameCount - 1, index)); follow = false; Invalidate();
        }

        internal void Tick()
        {
            if (window == null) return;
            if (!window.IsAlive) { window = null; return; }
            Layout();
            if (!dirty && (!owner.IsRecording || Time.unscaledTime < nextDraw)) return;
            dirty = false; nextDraw = Time.unscaledTime + .2f;
            var capture = owner.Capture;
            start.GetComponent<Button>().interactable = owner.CanStart;
            stop.GetComponent<Button>().interactable = marker.GetComponent<Button>().interactable = owner.IsRecording;
            mode.GetComponent<Button>().interactable = !owner.IsRecording && !owner.IsExporting;
            export.GetComponent<Button>().interactable = capture != null && !owner.IsExporting && owner.ExportPath == null;
            report.GetComponent<Button>().interactable = owner.ExportPath != null;
            SetButton(mode, owner.Mode == PerfRecordingMode.FrameTimes ? "perf.mode_frames" : owner.Detailed ? "perf.mode_detail" : "perf.mode_summary");
            SetButton(compactButton, compact ? "perf.expand" : "perf.compact");
            SetButton(viewButton, view == 0 ? "perf.view_calls" : view == 1 ? "perf.view_engine" : "perf.view_events");
            string state = owner.IsRecording ? (owner.StopRequested == null ? "perf.recording" : "perf.stopping") : owner.IsExporting ? "perf.exporting" : capture == null ? "perf.ready" : "perf.stopped";
            status.text.text = ModText.Get(state) + (capture == null ? "" : " · " + capture.FrameCount + "/" + capture.Frames.Length + " " + ModText.Get("perf.frames"));
            if (owner.Error != null) status.text.text = ModText.Get("perf.failed");
            if (capture == null || capture.FrameCount == 0)
            {
                summary.text.text = ModText.Get("perf.ready_hint");
                detailsText.text.text = owner.Error ?? ModText.Get("perf.capacity_hint", PerfCapture.DefaultFrameCapacity, PerfCapture.DefaultSpanCapacity);
                LayoutDetails(); return;
            }
            if (follow || selected < 0) selected = capture.FrameCount - 1;
            selected = Math.Min(selected, capture.FrameCount - 1);
            int first = owner.IsRecording ? Math.Max(0, capture.FrameCount - 300) : 0;
            var stats = PerfStatistics.Frames(capture, first, capture.FrameCount);
            summary.text.text = ModText.Get(owner.IsRecording ? "perf.recent_stats" : "perf.all_stats",
                stats.Median.ToString("F2"), stats.P95.ToString("F2"), stats.P99.ToString("F2"), stats.Maximum.ToString("F2"));
            int chartFirst = owner.IsRecording ? Math.Max(0, (follow ? capture.FrameCount : selected + 1) - 600) : 0;
            int chartEnd = owner.IsRecording && !follow ? Math.Min(capture.FrameCount, chartFirst + 600) : capture.FrameCount;
            chart.SetData(capture, chartFirst, chartEnd, selected);
            BuildDetails(capture);
            LayoutDetails();
        }

        private static void SetButton(TreeNodeRender node, string key)
        {
            var label = node.GetComponentInChildren<TMP_Text>(); string value = ModText.Get(key);
            if (label.text != value) label.text = value;
        }

        private void BuildDetails(PerfCapture capture)
        {
            text.Clear(); var frame = capture.Frames[selected]; var c = frame.Context;
            text.AppendLine(ModText.Get("perf.frame_detail", selected, frame.UnityFrame, frame.Milliseconds.ToString("F3"), c.Operations));
            text.AppendLine(ModText.Get("perf.context", c.VisibleWindows, c.Windows, c.Items, c.Graphics, c.RaycastGraphics));
            text.AppendLine(ModText.Get("perf.context_work", frame.EnteredWindows, frame.LeftWindows, c.RefreshQueue, frame.PointerHits, frame.PointerMisses));
            text.AppendLine();
            if (view == 0)
            {
                text.AppendLine(ModText.Get("perf.call_columns"));
                var order = Enumerable.Range(0, PerfCapture.MetricCount).OrderByDescending(i => capture.Metrics[selected * PerfCapture.MetricCount + i].SelfTicks);
                foreach (int i in order)
                {
                    var m = capture.Metrics[selected * PerfCapture.MetricCount + i]; if (m.Calls == 0) continue;
                    text.Append(PerfCapture.MetricNames[i]).Append("  ").Append(m.Calls).Append("  ")
                        .Append(PerfCapture.ToMilliseconds(m.TotalTicks).ToString("F3")).Append(" / ")
                        .Append(PerfCapture.ToMilliseconds(m.SelfTicks).ToString("F3")).Append(" / ")
                        .Append(PerfCapture.ToMilliseconds(m.MaxTicks).ToString("F3")).AppendLine(" ms");
                }
                text.AppendLine().AppendLine(ModText.Get("perf.call_note"));
            }
            else if (view == 1)
            {
                for (int i = 0; i < capture.CounterCount; i++)
                {
                    var info = capture.Counters[i]; int offset = selected * capture.CounterCount + i;
                    text.Append(info.Name).Append("  ");
                    if (capture.CounterValid[offset]) text.Append(capture.CounterValues[offset].ToString("N0")).Append(' ').Append(info.Unit);
                    else text.Append(ModText.Get("perf.unavailable"));
                    text.AppendLine();
                }
                text.AppendLine().AppendLine(ModText.Get("perf.native_samples", capture.NativeTimings.Count));
                text.AppendLine(ModText.Get("perf.native_note"));
            }
            else
            {
                int first = Math.Max(0, capture.EventCount - 100);
                for (int i = first; i < capture.EventCount; i++)
                {
                    var e = capture.Events[i];
                    text.Append(PerfCapture.ToMilliseconds(e.Ticks - capture.OriginTicks).ToString("F1"))
                        .Append(" ms  ").Append(e.Kind).Append("  ").AppendLine(e.Detail);
                }
                text.AppendLine(ModText.Get("perf.event_count", capture.EventCount));
            }
            text.AppendLine().AppendLine(ModText.Get("perf.sample_count", capture.SpanCount, capture.OmittedSpans, capture.OmittedEvents));
            if (capture.StopReason != null) text.AppendLine(ModText.Get("perf.stop_reason", capture.StopReason));
            if (owner.ExportPath != null) text.AppendLine(owner.ExportPath);
            if (owner.Error != null) text.AppendLine(owner.Error);
            detailsText.text.text = text.ToString();
        }

        internal void Close()
        {
            if (window == null) return;
            var current = window; window = null;
            current.onClose = null; current.Close(); root = null;
            FlatStorageController.Current?.InvalidatePointer();
        }
        public void Dispose() => Close();
    }

    public sealed class PerformanceChart : MaskableGraphic, IPointerDownHandler, IDragHandler
    {
        internal Action<int> SelectFrame;
        internal int RenderedVertices { get; private set; }
        private PerfCapture capture;
        private int first, end, selected;
        private double maximum;
        private readonly UIVertex[] quad = new UIVertex[4];
        private static readonly double[] thresholds = { 1000.0 / 60, 1000.0 / 30, 50 };
        internal void SetData(PerfCapture value, int from, int to, int selection)
        {
            capture = value; first = from; end = to; selected = selection; maximum = 50;
            for (int i = first; i < end; i++) maximum = Math.Max(maximum, capture.Frames[i].Milliseconds);
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear(); RenderedVertices = 0; if (capture == null || end <= first) return;
            Rect r = rectTransform.rect; float height = Math.Max(1, r.height - 5);
            foreach (double threshold in thresholds)
                Rectangle(helper, r.xMin, r.yMin + 5 + (float)(threshold / maximum) * height, r.width, .4f, new Color(.22f, .3f, .39f));
            int pixels = Math.Max(1, (int)r.width);
            for (int p = 0; p < pixels; p++)
            {
                int a = first + p * (end - first) / pixels, b = first + (p + 1) * (end - first) / pixels;
                if (a == b) continue;
                int worst = a;
                for (int i = a + 1; i < b; i++) if (capture.Frames[i].Milliseconds > capture.Frames[worst].Milliseconds) worst = i;
                var frame = capture.Frames[worst];
                Rectangle(helper, r.xMin + p, r.yMin + 5, 1, (float)(frame.Milliseconds / maximum) * height,
                    frame.Milliseconds > 1000.0 / 30 ? new Color(.98f, .68f, .4f) : new Color(.45f, .85f, .74f));
                var operation = frame.Context.Operations;
                Color band = (operation & PerfOperation.ItemDrag) != 0 ? new Color(.96f, .51f, .67f) :
                    (operation & PerfOperation.Zoom) != 0 ? new Color(.77f, .59f, 1) :
                    (operation & PerfOperation.Pan) != 0 ? new Color(.34f, .65f, 1) : FlatUI.Border;
                Rectangle(helper, r.xMin + p, r.yMin, 1, 3, band);
            }
            if (selected >= first && selected < end)
                Rectangle(helper, r.xMin + (selected - first + .5f) / (end - first) * r.width, r.yMin, .6f, r.height, Color.white);
            RenderedVertices = helper.currentVertCount;
        }
        private void Rectangle(VertexHelper helper, float x, float y, float width, float height, Color color)
        {
            for (int i = 0; i < 4; i++) { quad[i] = UIVertex.simpleVert; quad[i].color = color; }
            quad[0].position = new Vector3(x, y); quad[1].position = new Vector3(x, y + height);
            quad[2].position = new Vector3(x + width, y + height); quad[3].position = new Vector3(x + width, y);
            helper.AddUIVertexQuad(quad);
        }
        public void OnPointerDown(PointerEventData eventData) => Choose(eventData);
        public void OnDrag(PointerEventData eventData) => Choose(eventData);
        private void Choose(PointerEventData eventData)
        {
            if (capture == null || end <= first || eventData.button != PointerEventData.InputButton.Left) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out var point);
            var r = rectTransform.rect; int pixels = Math.Max(1, (int)r.width);
            int pixel = Math.Min(pixels - 1, Math.Max(0, (int)(point.x - r.xMin)));
            int a = first + pixel * (end - first) / pixels, b = Math.Min(end, Math.Max(a + 1, first + (pixel + 1) * (end - first) / pixels));
            int worst = Math.Min(end - 1, a);
            for (int i = worst + 1; i < b; i++) if (capture.Frames[i].Milliseconds > capture.Frames[worst].Milliseconds) worst = i;
            SelectFrame?.Invoke(worst);
        }
    }
}
