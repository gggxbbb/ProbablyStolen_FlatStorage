using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class CanvasTools : IDisposable
    {
        private readonly FlatStorageController owner;
        private readonly CustomUIElement search, category;
        private readonly TreeNodeRender next, clear, navigationButton, storageButton, mapButton;
        private readonly SearchHighlights highlights = new SearchHighlights();
        private readonly StorageMiniMap miniMap;
        private List<WindowLease> groups = new List<WindowLease>();
        private readonly StorageCatalog catalog = new StorageCatalog();
        private List<CatalogItem> matches = new List<CatalogItem>();
        private List<string> types = new List<string>();
        private string query = "", selectedType = "";
        private int selectedMatch = -1;
        private GameItemElement selectedItem;
        private bool? clearActive;
        private CustomUIWindow navigation, preview;
        private bool navigationDirty = true;
        private string matchStatus = "";
        private readonly List<(string Id, string Parent, string OwnLabel, string Label)> navigationState = new List<(string, string, string, string)>();
        private readonly HashSet<string> collapsedContainers = new HashSet<string>(StringComparer.Ordinal);
        private AutoStoragePlan plan;
        internal bool HasDialog => navigation != null || preview != null;
        internal bool BlocksCanvasPointer => miniMap.ContainsPointer || category.dropdown.IsExpanded;
        internal string MatchStatus => matchStatus;

        internal CanvasTools(FlatStorageController owner, CustomUIWindow window, TreeNodeRender bar, TreeNodeRender viewport)
        {
            this.owner = owner;
            search = FlatUI.Control(window, bar, CustomUIManager.ElementKind.Input, "Item search");
            if (search.input == null) throw new InvalidOperationException(ModText.Get("ui.component_missing", "TMP_InputField"));
            if (search.input.placeholder is TMP_Text placeholder) placeholder.text = ModText.Get("ui.search");
            search.input.characterLimit = 120;
            search.input.lineType = TMP_InputField.LineType.SingleLine;
            search.input.onValueChanged.AddListener(value => owner.Run(() => { query = value.Trim(); Filter(true); }));
            search.input.onSubmit.AddListener(_ => owner.Run(FocusNext));
            category = FlatUI.Control(window, bar, CustomUIManager.ElementKind.Dropdown, "Item category");
            if (category.dropdown == null) throw new InvalidOperationException(ModText.Get("ui.component_missing", "TMP_Dropdown"));
            category.dropdown.onValueChanged.AddListener(index => owner.Run(() =>
            { selectedType = index == 0 ? "" : types[index - 1]; Filter(true); }));
            next = FlatUI.Button(bar, ModText.Get("ui.next"), 0, 36, () => owner.Run(FocusNext));
            clear = FlatUI.Button(bar, ModText.Get("ui.clear"), 0, 32, () => owner.Run(() =>
            { search.input.SetTextWithoutNotify(""); category.dropdown.SetValueWithoutNotify(0); query = selectedType = ""; Filter(true); }));
            navigationButton = FlatUI.Button(bar, ModText.Get("ui.navigate"), 0, 54, () => owner.Run(OpenNavigation));
            storageButton = FlatUI.Button(bar, ModText.Get("ui.store"), 0, 58, () => owner.Run(OpenPreview));
            mapButton = FlatUI.Button(bar, ModText.Get("ui.minimap"), 0, 54, () => owner.Run(() =>
            {
                owner.ShowMiniMap = !owner.ShowMiniMap;
                miniMap.Visible(owner.ShowMiniMap);
                miniMap.UpdateView(owner.ViewBounds);
                FlatUI.ActiveButton(mapButton, owner.ShowMiniMap);
                owner.Save();
            }));
            miniMap = new StorageMiniMap(owner, viewport);
            miniMap.Visible(owner.ShowMiniMap);
            FlatUI.ActiveButton(mapButton, owner.ShowMiniMap);
            FlatUI.ActiveButton(storageButton, true);
        }

        internal float[] Widths(float width) => new[]
        {
            Math.Max(66, Math.Min(160, width * .27f)), Math.Max(58, Math.Min(92, width * .17f)),
            36, 32, 54, 58, 54
        };

        internal void Layout(IReadOnlyList<Box> positions)
        {
            category.dropdown.Hide();
            Box input = positions[0], dropdown = positions[1];
            FlatUI.Rect(search.Rect, input.X, input.Y, input.Width, input.Height);
            FlatUI.Rect(category.Rect, dropdown.X, dropdown.Y, dropdown.Width, dropdown.Height);
            var buttons = new[] { next, clear, navigationButton, storageButton, mapButton };
            for (int i = 0; i < buttons.Length; i++) FlatUI.SizeButton(buttons[i], positions[i + 2]);
        }

        internal void Refresh(IEnumerable<WindowLease> leases)
        {
            groups = leases.ToList();
            catalog.Combine(groups);
            var updatedTypes = catalog.Items.SelectMany(item => item.Types).Distinct()
                .OrderBy(TypeHelper.GetTypeDisplayName, StringComparer.CurrentCulture).ToList();
            if (!types.SequenceEqual(updatedTypes))
            {
                category.dropdown.Hide();
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
            miniMap.Refresh(groups, owner.ViewBounds);
            CheckNavigation();
            if (navigation != null && navigationDirty) BuildNavigation();
        }

        internal void GeometryChanged() => miniMap.InvalidateGeometry();

        internal void ResizeDialogs(float width, float height)
        {
            miniMap.Layout(width, height);
            miniMap.UpdateView(owner.ViewBounds);
            if (navigation != null) { FlatUI.FitScreen(navigation, .65f); BuildNavigation(); }
            if (preview != null) { FlatUI.FitScreen(preview, .75f); CustomUIBuilder.RebuildLayout(preview.ContentRoot); }
        }

        private void Filter(bool reset)
        {
            if (reset) PerformanceMonitor.Active?.Event("search", "length=" + query.Length + ";category=" + selectedType);
            if (reset) selectedItem = null;
            bool active = query.Length != 0 || selectedType.Length != 0;
            matches = active ? catalog.Items.Where(item => item.Matches(query, selectedType)).ToList() : catalog.Items;
            selectedMatch = selectedItem == null ? -1 : matches.FindIndex(item => item.Item == selectedItem);
            if (selectedMatch < 0) selectedItem = null;
            highlights.Apply(catalog, active ? new HashSet<GameItemElement>(matches.Select(item => item.Item)) : null, active);
            matchStatus = active ? ModText.Get("ui.matches", matches.Count) : "";
            if (clearActive != active) { clearActive = active; FlatUI.ActiveButton(clear, active); }
            owner.InvalidatePointer();
        }

        private void FocusNext()
        {
            if (matches.Count == 0) { owner.Notice(ModText.Get("ui.no_matches")); return; }
            selectedMatch = (selectedMatch + 1) % matches.Count;
            selectedItem = matches[selectedMatch].Item;
            owner.Focus(matches[selectedMatch]);
        }

        private void CheckNavigation()
        {
            bool changed = navigationState.Count != groups.Count;
            for (int i = 0; i < groups.Count && !changed; i++)
            {
                var source = groups[i].Source;
                changed = navigationState[i] != (source.Id, source.ParentId, source.OwnLabel, source.Label);
            }
            if (!changed) return;
            navigationDirty = true; navigationState.Clear();
            foreach (var group in groups)
                navigationState.Add((group.Source.Id, group.Source.ParentId, group.Source.OwnLabel, group.Source.Label));
        }

        private void OpenNavigation()
        {
            PerformanceMonitor.Active?.Event("container-directory");
            CloseDialog(ref preview);
            if (navigation != null) { CloseDialog(ref navigation); return; }
            navigation = FlatUI.Window("navigation", ModText.Get("ui.navigate"), () => navigation = null);
            owner.InvalidatePointer();
            FlatUI.FitScreen(navigation, .65f);
            BuildNavigation();
        }

        private void BuildNavigation()
        {
            navigationDirty = false;
            collapsedContainers.IntersectWith(groups.Select(group => group.Source.Id));
            StorageTreeView.Build(navigation, groups, collapsedContainers,
                id => owner.Run(() =>
                {
                    owner.Focus(groups.First(group => group.Source.Id == id).Bounds);
                    CloseDialog(ref navigation);
                }),
                id => owner.Run(() =>
                {
                    if (!collapsedContainers.Add(id)) collapsedContainers.Remove(id);
                    BuildNavigation();
                }),
                () => CloseDialog(ref navigation));
        }

        internal void UpdateView(Box view) => miniMap.UpdateView(view);

        private void OpenPreview()
        {
            PerformanceMonitor.Active?.Event("auto-storage-preview");
            NativeAccess.CancelDrag();
            CloseDialog(ref navigation); CloseDialog(ref preview);
            plan = AutoStorage.Preview(StorageDiscovery.Collect());
            preview = FlatUI.Window("storage", ModText.Get("ui.store_preview"), () => { preview = null; plan = null; });
            owner.InvalidatePointer();
            FlatUI.FitScreen(preview, .75f);
            var builder = new CustomUIBuilder(CustomUIManager.Instance, preview);
            builder.AddLabel(ModText.Get("ui.store_summary", plan.PlannedUnits, plan.BlockedUnits));
            builder.AddLabel(ModText.Get("ui.store_policy"));
            if (!string.IsNullOrEmpty(plan.UnavailableReason)) builder.AddWarning(plan.UnavailableReason);
            builder.BeginScrollFlexible(100);
            foreach (var row in plan.Rows)
            {
                builder.AddLabel(row.Name + "  " + row.PlannedUnits + "/" + row.Units);
                foreach (var destination in row.Destinations)
                    builder.AddLabel(ModText.Get("ui.store_destination", destination.Label, destination.Units,
                        Math.Round(destination.Similarity * 100)));
                if (!string.IsNullOrEmpty(row.Reason)) builder.AddLabel(row.Reason);
            }
            builder.End().BeginRow();
            builder.AddButton(ModText.Get("ui.confirm"), () => owner.Run(Execute));
            builder.Last.SetInteractable(plan.CanExecute);
            builder.AddButton(ModText.Get("ui.cancel"), () => CloseDialog(ref preview));
            builder.End();
        }

        private void Execute()
        {
            PerformanceMonitor.Active?.Event("auto-storage-execute");
            var result = plan.Execute(StorageDiscovery.Collect());
            CloseDialog(ref preview);
            owner.Notice(ModText.Get("ui.store_done", result.MovedUnits) +
                (string.IsNullOrEmpty(result.StoppedReason) ? "" : " — " + result.StoppedReason));
            owner.RefreshNow();
        }

        internal bool Escape()
        {
            if (category.dropdown.IsExpanded) { category.dropdown.Hide(); return true; }
            if (preview != null) { CloseDialog(ref preview); return true; }
            if (navigation != null) { CloseDialog(ref navigation); return true; }
            return false;
        }

        private static void CloseDialog(ref CustomUIWindow dialog)
        {
            var current = dialog; dialog = null;
            if (current == null) return;
            current.onClose = null; current.Close();
            FlatStorageController.Current?.InvalidatePointer();
        }

        public void Dispose()
        {
            category.dropdown.Hide();
            CloseDialog(ref navigation); CloseDialog(ref preview);
            miniMap.Dispose(); highlights.Dispose();
        }
    }
}
