using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal static class StorageTreeView
    {
        internal static void Build(CustomUIWindow window, IReadOnlyList<WindowLease> groups, ISet<string> collapsed,
            Action<string> focus, Action<string> toggle, Action close)
        {
            var previousScroll = window.root.GetComponentInChildren<ScrollRect>();
            Vector2 previousPosition = previousScroll == null ? Vector2.zero : previousScroll.content.anchoredPosition;
            var rows = StorageTree.Build(groups.Select(group => new StorageTreeItem(group.Source.Id,
                group.Source.ParentId, StorageLabels.Name(group.Source.OwnLabel, group.Source.Label))), collapsed);
            var byId = groups.ToDictionary(group => group.Source.Id, StringComparer.Ordinal);
            float unit = Math.Abs(RenderHandler.current.windowRoot.rectTransform.lossyScale.y / window.ContentRoot.lossyScale.y);
            float rowHeight = 18 * unit, indent = 12 * unit, arrowWidth = 18 * unit;
            CustomUIBuilder.ClearContainer(window.ContentRoot);
            var builder = new CustomUIBuilder(CustomUIManager.Instance, window);
            if (rows.Count == 0) builder.AddLabel(ModText.Get("ui.empty_groups"));
            builder.BeginScrollFlexible(80 * unit);
            var list = builder.CurrentContainer;
            var scroll = list.GetComponentInParent<ScrollRect>();
            if (scroll == null) throw new InvalidOperationException(ModText.Get("ui.component_missing", "Directory ScrollRect"));
            var column = list.GetComponent<VerticalLayoutGroup>();
            if (column == null) throw new InvalidOperationException(ModText.Get("ui.component_missing", "Directory layout"));
            column.spacing = 2 * unit;
            column.childForceExpandWidth = true;
            foreach (var row in rows)
            {
                string id = row.Id;
                builder.BeginRow(0, TextAnchor.MiddleLeft);
                var rowRect = builder.CurrentContainer;
                var layout = rowRect.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = layout.preferredHeight = rowHeight;
                layout.flexibleWidth = 1; layout.flexibleHeight = 0;
                rowRect.GetComponent<HorizontalLayoutGroup>().padding.left = Mathf.RoundToInt(row.Depth * indent);
                if (row.HasChildren)
                {
                    bool expanded = !collapsed.Contains(id);
                    builder.AddButton("", () => toggle(id));
                    var disclosure = builder.Last;
                    StyleButton(disclosure, unit, false);
                    disclosure.SetFixedWidth(arrowWidth).SetPreferredSize(-1, rowHeight).SetFlexible(0, 0);
                    disclosure.SetTooltip(ModText.Get(expanded ? "ui.tree_collapse" : "ui.tree_expand", row.Name));
                    AddArrow(disclosure, unit, expanded);
                }
                else builder.AddSpace(arrowWidth);
                builder.AddButton(row.Name, () => focus(id));
                var name = builder.Last;
                StyleButton(name, unit, row.Depth == 0);
                name.SetPreferredSize(-1, rowHeight).SetFlexible(1, 0);
                var nameLayout = name.root.GetComponent<LayoutElement>();
                nameLayout.minWidth = nameLayout.preferredWidth = 0;
                name.SetTooltip(byId[id].Source.Label);
                builder.End();
            }
            builder.End();
            builder.AddButton(ModText.Get("ui.close"), close);
            CustomUIBuilder.RebuildLayout(window.ContentRoot);
            window.ContentRoot.ForceUpdateRectTransforms();
            float requiredWidth = (rows.Count == 0 ? 0 : rows.Max(row => row.Depth) * indent) + arrowWidth + 90 * unit;
            float availableWidth = scroll.viewport.rect.width;
            bool overflow = requiredWidth > availableWidth;
            if (overflow) AddHorizontalScrollbar(scroll, unit);
            scroll.horizontal = overflow;
            var fitter = list.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            list.anchorMin = list.anchorMax = new Vector2(0, 1);
            list.pivot = new Vector2(0, 1);
            list.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Math.Max(requiredWidth, availableWidth));
            CustomUIBuilder.RebuildLayout(window.ContentRoot);
            window.ContentRoot.ForceUpdateRectTransforms();
            float maxX = Math.Max(0, list.rect.width - scroll.viewport.rect.width);
            float maxY = Math.Max(0, list.rect.height - scroll.viewport.rect.height);
            scroll.StopMovement();
            list.anchoredPosition = new Vector2(Mathf.Clamp(previousPosition.x, -maxX, 0), Mathf.Clamp(previousPosition.y, 0, maxY));
        }

        private static void StyleButton(CustomUIElement element, float unit, bool root)
        {
            if (element.button == null || element.text == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Directory button"));
            var layout = element.root.GetComponent<LayoutElement>();
            if (layout == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Directory button layout"));
            layout.minHeight = layout.preferredHeight = 18 * unit;
            layout.flexibleHeight = 0;
            FlatUI.ManualLayout(element.Rect);
            FlatUI.SetColors(element.button, root ? FlatUI.Field : FlatUI.Surface);
            var text = element.text;
            text.enableAutoSizing = false; text.fontSize = 7.5f * unit;
            text.fontStyle = FontStyles.Normal; text.richText = false;
            text.color = root ? FlatUI.Accent : FlatUI.Foreground;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = Vector4.zero; text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4 * unit, 0);
            text.rectTransform.offsetMax = new Vector2(-4 * unit, 0);
        }

        private static void AddArrow(CustomUIElement element, float unit, bool expanded)
        {
            var prefab = CustomUIManager.Instance.GetPrefab(CustomUIManager.ElementKind.Dropdown, false);
            var original = prefab == null ? null : prefab.transform.Find("Arrow")?.GetComponent<Image>();
            if (original == null || original.sprite == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Directory arrow"));
            var arrow = new GameObject("Disclosure", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(element.Rect, false);
            var rect = (RectTransform)arrow.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(7 * unit, 7 * unit);
            rect.localEulerAngles = new Vector3(0, 0, expanded ? 0 : 90);
            var image = arrow.GetComponent<Image>();
            image.sprite = original.sprite; image.preserveAspect = true;
            image.color = FlatUI.Muted; image.raycastTarget = false;
        }

        private static void AddHorizontalScrollbar(ScrollRect scroll, float unit)
        {
            if (scroll.verticalScrollbar == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Directory scrollbar"));
            var bar = UnityEngine.Object.Instantiate(scroll.verticalScrollbar, scroll.transform);
            bar.name = "Directory horizontal scrollbar";
            bar.direction = Scrollbar.Direction.LeftToRight;
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1, 0);
            rect.pivot = Vector2.zero;
            rect.offsetMin = Vector2.zero; rect.offsetMax = new Vector2(-8 * unit, 4 * unit);
            var track = (RectTransform)bar.handleRect.parent;
            track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
            track.offsetMin = track.offsetMax = Vector2.zero;
            bar.handleRect.offsetMin = bar.handleRect.offsetMax = Vector2.zero;
            FlatUI.SetColors(bar, FlatUI.Muted);
            scroll.horizontalScrollbar = bar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.viewport.offsetMin += new Vector2(0, 6 * unit);
        }
    }
}
