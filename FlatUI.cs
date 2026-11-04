using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen_FlatStorage
{
    internal static class FlatUI
    {
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
