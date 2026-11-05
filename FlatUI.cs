using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen_FlatStorage
{
    internal static class FlatUI
    {
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
            }
            else if (storeUI == null) window.root.AddComponent<StoreUI>();
            return window;
        }

        internal static void Rect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
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
            Canvas.ForceUpdateCanvases();
        }

        internal static CustomUIElement Control(CustomUIWindow window, TreeNodeRender parent,
            CustomUIManager.ElementKind kind, string name)
        {
            var go = CustomUIManager.Instance.SpawnElement(kind, parent.transform, window, name);
            if (go == null) throw new InvalidOperationException(ModText.Get("ui.prefab_missing", kind));
            var control = CustomUIElement.Bind(go, name);
            foreach (var label in go.GetComponentsInChildren<TMP_Text>(true))
            {
                label.enableAutoSizing = false;
                label.fontSize = 7;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (control.input != null && control.input.textViewport != null)
            {
                var textArea = control.input.textViewport;
                textArea.anchorMin = Vector2.zero; textArea.anchorMax = Vector2.one;
                textArea.offsetMin = new Vector2(3, 1); textArea.offsetMax = new Vector2(-3, -1);
            }
            var layout = go.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            return control;
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
            node.text.color = new Color(.90f, .92f, .87f);
            node.text.alignment = TextAlignmentOptions.MidlineLeft;
            node.text.enableWordWrapping = false;
            node.text.overflowMode = TextOverflowModes.Ellipsis;
            node.text.raycastTarget = false;
            return node;
        }

        internal static TreeNodeRender Button(TreeNodeRender parent, string label, float x, float width, Action action)
        {
            var button = Panel(parent, label, new Color(.22f, .28f, .31f));
            NativeAccess.Place(button, x, 3, width, 18);
            var text = Text(button, label, 7);
            NativeAccess.Place(text, 4, 0, width - 8, 18);
            text.text.alignment = TextAlignmentOptions.Center;
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
