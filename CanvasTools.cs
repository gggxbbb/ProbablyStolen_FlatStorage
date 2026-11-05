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
        private StorageCatalog catalog;
        private List<CatalogItem> matches = new List<CatalogItem>();
        private List<string> types = new List<string>();
        private string query = "", selectedType = "";
        private int selectedMatch = -1;
        private CustomUIWindow navigation, preview;
        private string navigationSignature;
        private AutoStoragePlan plan;
        internal bool HasDialog => navigation != null || preview != null;
        internal bool BlocksCanvasPointer => miniMap.ContainsPointer || category.dropdown.IsExpanded;
        internal string MatchStatus => !string.IsNullOrWhiteSpace(query) || selectedType.Length != 0 ? ModText.Get("ui.matches", matches.Count) : "";

        internal CanvasTools(FlatStorageController owner, CustomUIWindow window, TreeNodeRender bar, TreeNodeRender viewport)
        {
            this.owner = owner;
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
            catalog = new StorageCatalog(groups.Select(group => group.Source));
            var updatedTypes = catalog.Items.SelectMany(item => item.Item.GetGameItemType()).Distinct()
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
            if (navigation != null && navigationSignature != Signature()) BuildNavigation();
        }

        internal void ResizeDialogs(float width, float height)
        {
            miniMap.Layout(width, height);
            miniMap.UpdateView(owner.ViewBounds);
            if (navigation != null) { FlatUI.FitScreen(navigation, .65f); BuildNavigation(); }
            if (preview != null) { FlatUI.FitScreen(preview, .75f); CustomUIBuilder.RebuildLayout(preview.ContentRoot); }
        }

        private void Filter(bool reset)
        {
            if (catalog == null) return;
            var previous = selectedMatch >= 0 && selectedMatch < matches.Count ? matches[selectedMatch].Item : null;
            matches = catalog.Items.Where(item => item.Matches(query, selectedType)).ToList();
            selectedMatch = reset ? -1 : matches.FindIndex(item => item.Item == previous);
            bool active = !string.IsNullOrWhiteSpace(query) || selectedType.Length != 0;
            highlights.Apply(catalog, new HashSet<GameItemElement>(matches.Select(item => item.Item)), active);
            FlatUI.ActiveButton(clear, active);
        }

        private void FocusNext()
        {
            if (matches.Count == 0) { owner.Notice(ModText.Get("ui.no_matches")); return; }
            selectedMatch = (selectedMatch + 1) % matches.Count;
            owner.Focus(matches[selectedMatch]);
        }

        private string Signature() => string.Join("|", groups.Select(group => group.Source.Id + ":" + group.Source.Label));

        private void OpenNavigation()
        {
            CloseDialog(ref preview);
            if (navigation != null) { CloseDialog(ref navigation); return; }
            navigation = FlatUI.Window("navigation", ModText.Get("ui.navigate"), () => navigation = null);
            FlatUI.FitScreen(navigation, .65f);
            BuildNavigation();
        }

        private void BuildNavigation()
        {
            navigationSignature = Signature();
            CustomUIBuilder.ClearContainer(navigation.ContentRoot);
            var builder = new CustomUIBuilder(CustomUIManager.Instance, navigation);
            if (groups.Count == 0) builder.AddLabel(ModText.Get("ui.empty_groups"));
            builder.BeginScrollFlexible(80);
            foreach (var group in groups)
            {
                var destination = group;
                builder.AddButton(group.Source.Label, () => owner.Run(() =>
                { owner.Focus(destination.Bounds); CloseDialog(ref navigation); }));
            }
            builder.End();
            builder.AddButton(ModText.Get("ui.close"), () => CloseDialog(ref navigation));
            CustomUIBuilder.RebuildLayout(navigation.ContentRoot);
        }

        internal void UpdateView(Box view) => miniMap.UpdateView(view);

        private void OpenPreview()
        {
            NativeAccess.CancelDrag();
            CloseDialog(ref navigation); CloseDialog(ref preview);
            plan = AutoStorage.Preview(StorageDiscovery.Collect());
            preview = FlatUI.Window("storage", ModText.Get("ui.store_preview"), () => { preview = null; plan = null; });
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
        }

        public void Dispose()
        {
            category.dropdown.Hide();
            CloseDialog(ref navigation); CloseDialog(ref preview);
            miniMap.Dispose(); highlights.Dispose();
        }
    }
}
