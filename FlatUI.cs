using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal static class FlatUI
    {
        internal static readonly Color CanvasColor = new Color(.065f, .077f, .09f, 1);
        internal static readonly Color Surface = new Color(.11f, .13f, .15f, 1);
        internal static readonly Color Field = new Color(.16f, .19f, .21f, 1);
        internal static readonly Color Border = new Color(.25f, .29f, .31f, 1);
        internal static readonly Color Accent = new Color(.48f, .78f, .69f, 1);
        internal static readonly Color Foreground = new Color(.91f, .93f, .92f, 1);
        internal static readonly Color Muted = new Color(.61f, .67f, .68f, 1);

        internal static CustomUIWindow Window(string id, string title, Action close, bool inventoryCanvas = false)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) throw new InvalidOperationException(ModText.Get("ui.manager_unavailable"));
            var window = manager.CreateWindow(ModEntry.Id + ":" + id, title, "front", null, CustomUIManager.Style.Machinery)
                .SetDraggable(false).SetCloseOnEscape(false).OnClose(close).Show();
            var storeUI = window.root.GetComponent<StoreUI>();
            // 原生物品拖动要求仓储节点没有 StoreUI 祖先；普通对话窗口保留面板命中。
            if (inventoryCanvas)
            {
                if (storeUI != null) UnityEngine.Object.Destroy(storeUI);
                if (window.frame.closeButton != null) window.frame.closeButton.gameObject.SetActive(false);
                if (window.frame.titleText != null) window.frame.titleText.gameObject.SetActive(false);
                ManualLayout(window.Rect);
                var content = window.ContentRoot;
                content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
                content.offsetMin = content.offsetMax = Vector2.zero;
                content.SetAsLastSibling();
            }
            else if (storeUI == null) window.root.AddComponent<StoreUI>();
            return window;
        }

        internal static void Rect(RectTransform rect, float x, float y, float width, float height)
        {
            var anchor = new Vector2(0, 1);
            if (rect.anchorMin != anchor) rect.anchorMin = anchor;
            if (rect.anchorMax != anchor) rect.anchorMax = anchor;
            if (rect.pivot != anchor) rect.pivot = anchor;
            var position = new Vector2(x, -y);
            var size = new Vector2(width, height);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }

        internal static Canvas IsolateCanvas(TreeNodeRender node, bool boundPointer = false)
        {
            var parent = node.GetComponentInParent<Canvas>();
            var canvas = node.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = false;
            // 平移与缩放使用已有图形；容器内容变化时仍由 Unity 更新几何数据。
            canvas.overridePixelPerfect = true;
            canvas.pixelPerfect = false;
            if (parent != null) canvas.additionalShaderChannels = parent.additionalShaderChannels;
            var raycaster = boundPointer ? node.gameObject.AddComponent<StorageRaycaster>() : node.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            return canvas;
        }

        internal static void ManualLayout(RectTransform rect)
        {
            foreach (var layout in rect.GetComponents<LayoutGroup>()) layout.enabled = false;
            foreach (var fitter in rect.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
        }

        internal static void FitScreen(CustomUIWindow window, float fraction = 1)
        {
            var parent = (RectTransform)window.Rect.parent;
            var top = (Vector2)parent.InverseTransformPoint(new Vector3(0, Screen.height));
            var bottom = (Vector2)parent.InverseTransformPoint(new Vector3(Screen.width, 0));
            window.Rect.anchorMin = window.Rect.anchorMax = window.Rect.pivot = new Vector2(.5f, .5f);
            window.SetPosition((top + bottom) / 2 - parent.rect.center);
            window.SetSize(Math.Abs(bottom.x - top.x) * fraction, Math.Abs(top.y - bottom.y) * fraction);
            window.Rect.ForceUpdateRectTransforms();
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.Rect);
        }

        internal static CustomUIElement Control(CustomUIWindow window, TreeNodeRender parent,
            CustomUIManager.ElementKind kind, string name)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) throw new InvalidOperationException(ModText.Get("ui.manager_unavailable"));
            GameObject go;
            if (kind == CustomUIManager.ElementKind.Input || kind == CustomUIManager.ElementKind.Dropdown)
            {
                var prefab = manager.GetPrefab(kind, false);
                if (prefab == null) throw new InvalidOperationException(ModText.Get("ui.prefab_missing", kind));
                go = UnityEngine.Object.Instantiate(prefab, parent.transform);
                go.name = name;
            }
            else go = manager.SpawnElement(kind, parent.transform, window, name);
            if (go == null) throw new InvalidOperationException(ModText.Get("ui.prefab_missing", kind));
            var control = CustomUIElement.Bind(go, name);
            control.Rect.localScale = Vector3.one;
            ManualLayout(control.Rect);
            foreach (var label in go.GetComponentsInChildren<TMP_Text>(true))
            {
                label.enableAutoSizing = false;
                label.fontSize = 7.5f;
                label.fontStyle = FontStyles.Normal;
                label.color = Foreground;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (kind == CustomUIManager.ElementKind.Input && control.input == null ||
                kind == CustomUIManager.ElementKind.Dropdown && control.dropdown == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", kind));
            if (control.input != null) StyleInput(control.input);
            if (control.dropdown != null) StyleDropdown(control.dropdown);
            var layout = go.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            return control;
        }

        private static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = minimum; rect.offsetMax = maximum;
        }

        private static void StyleInput(TMP_InputField input)
        {
            if (input.textViewport == null || input.textComponent == null || !(input.placeholder is TMP_Text placeholder))
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Input text"));
            ManualLayout(input.textViewport);
            Stretch(input.textViewport, new Vector2(5, 1), new Vector2(-5, -1));
            Stretch(input.textComponent.rectTransform, Vector2.zero, Vector2.zero);
            Stretch(placeholder.rectTransform, Vector2.zero, Vector2.zero);
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.pointSize = 7.5f;
            input.textComponent.alignment = placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            input.textComponent.margin = placeholder.margin = Vector4.zero;
            input.textComponent.overflowMode = TextOverflowModes.Masking;
            placeholder.color = Muted;
            input.customCaretColor = true;
            input.caretColor = Accent;
            input.caretWidth = 1;
            input.selectionColor = new Color(.25f, .55f, .47f, .6f);
            SetColors(input, Field);
        }

        private static void StyleDropdown(TMP_Dropdown dropdown)
        {
            var template = dropdown.template;
            if (template == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Dropdown template"));
            var toggle = template.GetComponentInChildren<Toggle>(true);
            var scroll = template.GetComponentInChildren<ScrollRect>(true);
            var arrowTransform = dropdown.transform.Find("Arrow");
            var arrow = arrowTransform == null ? null : arrowTransform.GetComponent<Image>();
            if (toggle == null || scroll == null || scroll.viewport == null || scroll.content == null ||
                dropdown.itemText == null || dropdown.captionText == null || arrow == null ||
                toggle.transform.parent != scroll.content || !dropdown.itemText.transform.IsChildOf(toggle.transform))
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Dropdown template"));
            ManualLayout(template);
            template.localScale = Vector3.one;
            template.anchorMin = new Vector2(0, 1); template.anchorMax = Vector2.one;
            template.pivot = new Vector2(.5f, 0);
            template.anchoredPosition = new Vector2(0, 3);
            template.sizeDelta = new Vector2(0, 124);
            var backdrop = template.GetComponent<Image>();
            if (backdrop != null) Solid(backdrop, Surface);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            ManualLayout(scroll.viewport);
            Stretch(scroll.viewport, new Vector2(2, 0), new Vector2(-8, 0));
            var viewportImage = scroll.viewport.GetComponent<Image>();
            if (viewportImage != null) Solid(viewportImage, Color.white);
            var mask = scroll.viewport.GetComponent<Mask>();
            if (mask != null) mask.showMaskGraphic = false;
            ManualLayout(scroll.content);
            scroll.content.localScale = Vector3.one;
            scroll.content.anchorMin = new Vector2(0, 1); scroll.content.anchorMax = Vector2.one;
            scroll.content.pivot = new Vector2(.5f, 1);
            scroll.content.anchoredPosition = Vector2.zero;
            scroll.content.sizeDelta = new Vector2(0, 24);
            var item = (RectTransform)toggle.transform;
            ManualLayout(item);
            item.localScale = Vector3.one;
            item.anchorMin = new Vector2(0, 1); item.anchorMax = Vector2.one;
            item.pivot = new Vector2(.5f, 1);
            // TMP 根据模板项相对 Content 的位置保留上下各 2 的间距。
            item.anchoredPosition = new Vector2(0, -2); item.sizeDelta = new Vector2(0, 20);
            var itemLayout = item.GetComponent<LayoutElement>();
            if (itemLayout != null) { itemLayout.minHeight = itemLayout.preferredHeight = 20; itemLayout.flexibleHeight = 0; }
            if (toggle.targetGraphic != null && toggle.targetGraphic.rectTransform != item)
                Stretch(toggle.targetGraphic.rectTransform, Vector2.zero, Vector2.zero);
            if (toggle.graphic != null)
            {
                var mark = toggle.graphic.rectTransform;
                mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(0, .5f);
                mark.anchoredPosition = new Vector2(4, 0); mark.sizeDelta = new Vector2(6, 6);
                toggle.graphic.color = Accent;
                toggle.graphic.raycastTarget = false;
            }
            Stretch(dropdown.itemText.rectTransform, new Vector2(13, 1), new Vector2(-4, -1));
            Stretch(dropdown.captionText.rectTransform, new Vector2(6, 1), new Vector2(-14, -1));
            dropdown.itemText.margin = dropdown.captionText.margin = Vector4.zero;
            dropdown.itemText.alignment = dropdown.captionText.alignment = TextAlignmentOptions.MidlineLeft;
            dropdown.itemText.raycastTarget = dropdown.captionText.raycastTarget = false;
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1, .5f);
            arrow.rectTransform.pivot = new Vector2(.5f, .5f);
            arrow.rectTransform.anchoredPosition = new Vector2(-7, 0);
            arrow.rectTransform.sizeDelta = new Vector2(8, 8);
            arrow.rectTransform.localScale = Vector3.one;
            arrow.color = Muted;
            arrow.preserveAspect = true;
            arrow.raycastTarget = false;
            StyleScrollbar(scroll.verticalScrollbar);
            SetColors(toggle, Surface);
            SetColors(dropdown, Field);
        }

        private static void StyleScrollbar(Scrollbar scrollbar)
        {
            if (scrollbar == null || scrollbar.handleRect == null || !(scrollbar.handleRect.parent is RectTransform track))
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Dropdown scrollbar"));
            var bar = (RectTransform)scrollbar.transform;
            ManualLayout(bar);
            bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = Vector2.one;
            bar.offsetMin = new Vector2(-6, 2); bar.offsetMax = new Vector2(-2, -2);
            ManualLayout(track);
            Stretch(track, Vector2.zero, Vector2.zero);
            scrollbar.handleRect.offsetMin = scrollbar.handleRect.offsetMax = Vector2.zero;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var background = scrollbar.GetComponent<Image>();
            if (background != null && background != scrollbar.targetGraphic) Solid(background, Field);
            SetColors(scrollbar, Muted);
        }

        private static void Solid(Image image, Color color)
        {
            image.sprite = image.overrideSprite = null;
            image.type = Image.Type.Simple;
            image.color = color;
        }

        internal static void SetColors(Selectable control, Color normal)
        {
            if (control == null || control.targetGraphic == null)
                throw new InvalidOperationException(ModText.Get("ui.component_missing", "Selectable graphic"));
            if (control.targetGraphic is Image image) Solid(image, Color.white);
            else control.targetGraphic.color = Color.white;
            control.transition = Selectable.Transition.ColorTint;
            var colors = control.colors;
            colors.normalColor = normal;
            colors.highlightedColor = new Color(.24f, .32f, .33f, 1);
            colors.pressedColor = new Color(.18f, .40f, .34f, 1);
            colors.selectedColor = normal;
            colors.disabledColor = new Color(.12f, .14f, .16f, .6f);
            colors.colorMultiplier = 1; colors.fadeDuration = .08f;
            control.colors = colors;
            control.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        internal static void SizeButton(TreeNodeRender button, Box box)
        {
            NativeAccess.Place(button, box.X, box.Y, box.Width, box.Height);
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) Stretch(label.rectTransform, new Vector2(4, 0), new Vector2(-4, 0));
        }

        internal static void ActiveButton(TreeNodeRender button, bool active)
        {
            SetColors(button.GetComponent<Button>(), active ? new Color(.16f, .32f, .28f, 1) : Field);
            button.GetComponentInChildren<TMP_Text>().color = active ? Accent : Foreground;
        }

        internal static TreeNodeRender Panel(TreeNodeRender parent, string name, Color color)
        {
            var node = TreeNodeRender.Instantiate().SetParent(parent);
            node.gameObject.name = name;
            node.image.sprite = null;
            node.image.color = color;
            node.image.enabled = true;
            node.image.raycastTarget = true;
            if (node.mask != null) node.mask.enabled = false;
            node.rectTransform.localScale = Vector3.one;
            return node;
        }

        internal static TreeNodeRender Text(TreeNodeRender parent, string text, float size)
        {
            var node = TreeNodeRender.InstantiateText().SetParent(parent);
            node.text.enabled = true;
            node.rectTransform.localScale = Vector3.one;
            node.text.text = text;
            node.text.fontSize = size;
            node.text.color = Foreground;
            node.text.fontStyle = FontStyles.Normal;
            node.text.enableAutoSizing = false;
            node.text.alignment = TextAlignmentOptions.MidlineLeft;
            node.text.enableWordWrapping = false;
            node.text.overflowMode = TextOverflowModes.Ellipsis;
            node.text.raycastTarget = false;
            return node;
        }

        internal static TreeNodeRender Button(TreeNodeRender parent, string label, float x, float width, Action action)
        {
            var button = Panel(parent, label, Color.white);
            NativeAccess.Place(button, x, 3, width, 18);
            var text = Text(button, label, 7);
            NativeAccess.Place(text, 4, 0, width - 8, 18);
            text.text.alignment = TextAlignmentOptions.Center;
            var selectable = button.gameObject.AddComponent<Button>();
            selectable.targetGraphic = button.image;
            SetColors(selectable, Field);
            button.InitEventHandler();
            button.eventHandler.mouseClickFunc.Add(e =>
            {
                if (e.button == PointerEventData.InputButton.Left) action();
                return true;
            });
            return button;
        }
    }
}
