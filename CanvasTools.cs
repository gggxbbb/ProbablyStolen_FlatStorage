using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class CanvasTools : IDisposable
    {
        private readonly FlatStorageController owner;
        private readonly TreeNodeRender bar;
        private readonly CustomUIElement search, category;
        private readonly TreeNodeRender next, clear, navigationButton, bulkButton, count;
        private readonly SearchHighlights highlights = new SearchHighlights();
        private List<WindowLease> groups = new List<WindowLease>();
        private StorageCatalog catalog;
        private List<CatalogItem> matches = new List<CatalogItem>();
        private List<string> types = new List<string>();
        private string query = "", selectedType = "";
        private int selectedMatch = -1;
        private CustomUIWindow navigation, preview;
        private string navigationSignature;
        private RectTransform mapRect;
        private MiniMapProjection projection;
        private readonly List<RectTransform> viewEdges = new List<RectTransform>();
        private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
        private TreeNodeRender picker;
        private BulkTransferPlan plan;
        private string targetLabel;
        internal bool PickingTarget => picker != null;
        internal bool HasDialog => navigation != null || preview != null;

        internal CanvasTools(FlatStorageController owner, CustomUIWindow window, TreeNodeRender bar)
        {
            this.owner = owner; this.bar = bar;
            search = FlatUI.Control(window, bar, CustomUIManager.ElementKind.Input, "Item search");
            if (search.input == null) throw new InvalidOperationException(ModText.Get("ui.component_missing", "TMP_InputField"));
            if (search.input.placeholder is TMP_Text placeholder) placeholder.text = ModText.Get("ui.search");
            search.input.characterLimit = 120;
            search.input.lineType = TMP_InputField.LineType.SingleLine;
            search.input.onValueChanged.AddListener(value => owner.Run(() => { query = value; Filter(true); }));
            search.input.onSubmit.AddListener(_ => owner.Run(FocusNext));
            category = FlatUI.Control(window, bar, CustomUIManager.ElementKind.Dropdown, "Item category");
            if (category.dropdown == null) throw new InvalidOperationException(ModText.Get("ui.component_missing", "TMP_Dropdown"));
            category.dropdown.onValueChanged.AddListener(index => owner.Run(() =>
            { selectedType = index == 0 ? "" : types[index - 1]; Filter(true); }));
            next = FlatUI.Button(bar, ModText.Get("ui.next"), 0, 44, () => owner.Run(FocusNext));
            clear = FlatUI.Button(bar, ModText.Get("ui.clear"), 0, 40, () => owner.Run(() =>
            { search.input.SetTextWithoutNotify(""); category.dropdown.SetValueWithoutNotify(0); query = selectedType = ""; Filter(true); }));
            navigationButton = FlatUI.Button(bar, ModText.Get("ui.navigate"), 0, 64, () => owner.Run(OpenNavigation));
            bulkButton = FlatUI.Button(bar, ModText.Get("ui.bulk"), 0, 64, () => owner.Run(BeginTargetSelection));
            count = FlatUI.Text(bar, "", 7);
        }

        internal float Layout(float width)
        {
            bool narrow = width < 500;
            float queryWidth = narrow ? Math.Max(70, width * .55f) : Math.Max(90, width * .3f);
            float categoryWidth = narrow ? Math.Max(60, width - queryWidth - 12) : Math.Max(70, width * .2f);
            FlatUI.Rect(search.Rect, 4, 3, queryWidth, 18);
            FlatUI.Rect(category.Rect, 8 + queryWidth, 3, categoryWidth, 18);
            float x = narrow ? 4 : 12 + queryWidth + categoryWidth;
            float y = narrow ? 25 : 3;
            foreach (var button in new[] { next, clear, navigationButton, bulkButton })
            {
                NativeAccess.Place(button, x, y, button.rectTransform.sizeDelta.x, 18);
                x += button.rectTransform.sizeDelta.x + 4;
            }
            NativeAccess.Place(count, narrow ? x : 4, narrow ? y : 25, Math.Max(1, width - (narrow ? x : 4) - 4), 16);
            return 46;
        }

        internal void Refresh(IEnumerable<WindowLease> leases)
        {
            groups = leases.ToList();
            catalog = new StorageCatalog(groups.Select(g => g.Source));
            var updatedTypes = catalog.Items.SelectMany(i => i.Item.GetGameItemType()).Distinct()
                .OrderBy(TypeHelper.GetTypeDisplayName, StringComparer.CurrentCulture).ToList();
            if (!types.SequenceEqual(updatedTypes))
            {
                types = updatedTypes;
                var options = new List<string> { ModText.Get("ui.category") };
                options.AddRange(types.Select(TypeHelper.GetTypeDisplayName));
                category.dropdown.ClearOptions(); category.dropdown.AddOptions(options);
                int index = types.IndexOf(selectedType);
                if (index < 0) selectedType = "";
                category.dropdown.SetValueWithoutNotify(index + 1);
            }
            else if (category.dropdown.options.Count == 0)
                category.dropdown.AddOptions(new List<string> { ModText.Get("ui.category") });
            Filter(false);
            if (navigation != null && navigationSignature != Signature()) BuildNavigation();
        }

        internal void ResizeDialogs(float viewWidth, float viewHeight)
        {
            if (navigation != null)
            {
                FlatUI.FitScreen(navigation, .75f);
                BuildNavigation();
            }
            if (preview != null)
            {
                FlatUI.FitScreen(preview, .8f);
                CustomUIBuilder.RebuildLayout(preview.ContentRoot);
            }
            if (picker != null) NativeAccess.Place(picker, 0, 0, viewWidth, viewHeight);
        }

        private void Filter(bool reset)
        {
            if (catalog == null) return;
            var previous = selectedMatch >= 0 && selectedMatch < matches.Count ? matches[selectedMatch].Item : null;
            matches = catalog.Items.Where(i => i.Matches(query, selectedType)).ToList();
            selectedMatch = reset ? -1 : matches.FindIndex(i => i.Item == previous);
            bool active = !string.IsNullOrWhiteSpace(query) || selectedType.Length != 0;
            highlights.Apply(catalog, new HashSet<GameItemElement>(matches.Select(i => i.Item)), active);
            count.text.text = active ? ModText.Get("ui.matches", matches.Count) : "";
        }

        private void FocusNext()
        {
            if (matches.Count == 0) { owner.Notice(ModText.Get("ui.no_matches")); return; }
            selectedMatch = (selectedMatch + 1) % matches.Count;
            owner.Focus(matches[selectedMatch]);
        }

        private string Signature() => string.Join("|", groups.Select(g =>
            FormattableString.Invariant($"{g.Source.Id}:{g.Source.Label}:{g.Position.X}:{g.Position.Y}:{g.Width}:{g.Height}")));

        private void OpenNavigation()
        {
            CloseDialog(ref preview);
            if (navigation != null) { CloseDialog(ref navigation); return; }
            navigation = FlatUI.Window("navigation", ModText.Get("ui.navigate"), () => { navigation = null; mapRect = null; viewEdges.Clear(); });
            FlatUI.FitScreen(navigation, .75f);
            BuildNavigation();
        }

        private void BuildNavigation()
        {
            navigationSignature = Signature();
            float scroll = navigation.root.GetComponentInChildren<ScrollRect>()?.verticalNormalizedPosition ?? 1;
            CustomUIBuilder.ClearContainer(navigation.ContentRoot);
            mapRect = null; viewEdges.Clear();
            var builder = new CustomUIBuilder(CustomUIManager.Instance, navigation);
            if (groups.Count == 0) { builder.AddLabel(ModText.Get("ui.empty_groups")); return; }
            builder.BeginPanel(height: 160, padding: 0);
            mapRect = builder.CurrentContainer;
            FlatUI.ManualLayout(mapRect);
            var background = mapRect.GetComponent<Image>() ?? mapRect.gameObject.AddComponent<Image>();
            background.color = new Color(.05f, .08f, .10f, 1);
            var click = mapRect.gameObject.AddComponent<MiniMapClick>();
            click.Click = point => owner.Run(() => owner.Focus(projection.CanvasX(point.x), projection.CanvasY(point.y)));
            builder.End();
            builder.BeginScrollFlexible(80);
            foreach (var group in groups)
            {
                var destination = group;
                builder.AddButton(group.Source.Label, () => owner.Run(() => owner.Focus(destination.Bounds)));
            }
            builder.End();
            CustomUIBuilder.RebuildLayout(navigation.ContentRoot);
            var scrollRect = navigation.root.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = scroll;
            BuildMap();
            UpdateView(owner.ViewBounds);
        }

        private void BuildMap()
        {
            projection = new MiniMapProjection(groups.Select(g => g.Bounds).ToList(),
                Math.Max(1, mapRect.rect.width), Math.Max(1, mapRect.rect.height));
            foreach (var group in groups)
            {
                Box box = projection.Project(group.Bounds);
                var mark = MapImage("Container", new Color(.26f, .55f, .65f, 1));
                FlatUI.Rect(mark, box.X, box.Y, Math.Max(2, box.Width), Math.Max(2, box.Height));
                var destination = group;
                mark.GetComponent<Image>().raycastTarget = true;
                mark.gameObject.AddComponent<Button>().onClick.AddListener(() => owner.Run(() => owner.Focus(destination.Bounds)));
            }
            for (int i = 0; i < 4; i++) viewEdges.Add(MapImage("Viewport", new Color(.95f, .85f, .35f, 1)));
        }

        private RectTransform MapImage(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(mapRect, false);
            go.GetComponent<Image>().color = color; go.GetComponent<Image>().raycastTarget = false;
            return (RectTransform)go.transform;
        }

        internal void UpdateView(Box view)
        {
            if (mapRect == null || viewEdges.Count != 4) return;
            var box = projection.Project(view);
            float x = Math.Max(0, box.X), y = Math.Max(0, box.Y);
            float right = Math.Min(mapRect.rect.width, box.X + box.Width), bottom = Math.Min(mapRect.rect.height, box.Y + box.Height);
            foreach (var edge in viewEdges) edge.gameObject.SetActive(right > x && bottom > y);
            if (right <= x || bottom <= y) return;
            FlatUI.Rect(viewEdges[0], x, y, right - x, 2);
            FlatUI.Rect(viewEdges[1], x, bottom - 2, right - x, 2);
            FlatUI.Rect(viewEdges[2], x, y, 2, bottom - y);
            FlatUI.Rect(viewEdges[3], right - 2, y, 2, bottom - y);
        }

        private void BeginTargetSelection()
        {
            CloseDialog(ref navigation); CloseDialog(ref preview);
            NativeAccess.CancelDrag();
            CancelTargetSelection();
            picker = owner.CreatePointerCover();
            owner.Notice(ModText.Get("ui.pick_target"));
        }

        internal void Update()
        {
            if (!PickingTarget || !Input.GetMouseButtonDown(0)) return;
            if (!owner.PointerInViewport || !owner.OwnsPointer()) return;
            pointerHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, pointerHits);
            var first = pointerHits.FirstOrDefault(hit => hit.gameObject != null && hit.gameObject != picker.gameObject);
            var node = first.gameObject == null ? null : first.gameObject.GetComponent<TreeNodeRender>();
            GameInventory target = null;
            while (node != null)
            {
                if (node.element is GameItem item) { target = item.parentInventory; break; }
                if (node.element is GameInventory inventory) { target = inventory; break; }
                node = node.parent;
            }
            if (!catalog.Inventories.Contains(target)) return;
            if (target == null) return;
            CancelTargetSelection();
            var targetNode = ((PixelElement)target).handler;
            targetLabel = groups.First(g => targetNode.transform.IsChildOf(g.Group.transform)).Source.Label;
            foreach (var tabs in WindowLease.Tabs.Keys)
                for (int i = 0; i < tabs.count; i++)
                    if (tabs.GetTabInventory(i) == target && !string.IsNullOrWhiteSpace(tabs.GetCaption(i)))
                        targetLabel += " / " + tabs.GetCaption(i);
            plan = BulkTransfer.Preview(target, new StorageCatalog(StorageDiscovery.Collect()).Inventories);
            OpenPreview();
        }

        private void OpenPreview()
        {
            preview = FlatUI.Window("bulk", ModText.Get("ui.preview"), () => { preview = null; plan = null; });
            FlatUI.FitScreen(preview, .8f);
            var builder = new CustomUIBuilder(CustomUIManager.Instance, preview);
            builder.AddLabel(ModText.Get("ui.bulk_target", targetLabel));
            builder.AddLabel(ModText.Get("ui.bulk_summary", plan.PlannedUnits, plan.BlockedUnits));
            builder.AddLabel(ModText.Get("ui.bulk_policy"));
            if (!string.IsNullOrEmpty(plan.UnavailableReason)) builder.AddWarning(plan.UnavailableReason);
            builder.BeginScrollFlexible(100);
            foreach (var row in plan.Rows)
                builder.AddLabel(row.Name + "  " + row.PlannedUnits + "/" + row.Units +
                    (string.IsNullOrEmpty(row.Reason) ? "" : " — " + row.Reason));
            builder.End().BeginRow();
            builder.AddButton(ModText.Get("ui.confirm"), () => owner.Run(Execute));
            builder.Last.SetInteractable(plan.CanExecute);
            builder.AddButton(ModText.Get("ui.cancel"), () => CloseDialog(ref preview));
            builder.End();
        }

        private void Execute()
        {
            var result = plan.Execute(new StorageCatalog(StorageDiscovery.Collect()).Inventories);
            CloseDialog(ref preview);
            owner.Notice(ModText.Get("ui.bulk_done", result.MovedUnits) +
                (string.IsNullOrEmpty(result.StoppedReason) ? "" : " — " + result.StoppedReason));
            owner.RefreshNow();
        }

        internal bool Escape()
        {
            if (PickingTarget) { CancelTargetSelection(); return true; }
            if (preview != null) { CloseDialog(ref preview); return true; }
            if (navigation != null) { CloseDialog(ref navigation); return true; }
            return false;
        }

        private void CancelTargetSelection()
        {
            if (picker != null) UnityEngine.Object.Destroy(picker.gameObject);
            picker = null;
        }

        private static void CloseDialog(ref CustomUIWindow dialog)
        {
            var current = dialog; dialog = null;
            if (current == null) return;
            current.onClose = null; current.Close();
        }

        public void Dispose()
        {
            CancelTargetSelection(); CloseDialog(ref navigation); CloseDialog(ref preview);
            highlights.Dispose();
        }
    }

    internal sealed class MiniMapClick : MonoBehaviour, IPointerClickHandler
    {
        internal Action<Vector2> Click;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out var point);
            Click?.Invoke(new Vector2(point.x - rect.rect.xMin, rect.rect.yMax - point.y));
        }
    }
}
