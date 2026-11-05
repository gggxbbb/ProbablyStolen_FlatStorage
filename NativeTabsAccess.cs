using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen_FlatStorage
{
    internal static class NativeTabsAccess
    {
        private static readonly FieldInfo entries = AccessTools.Field(typeof(MachineryTabs), "tabs");
        private static readonly FieldInfo plate = AccessTools.Field("MachineryTabs+Tab:plate");
        private static readonly FieldInfo text = AccessTools.Field("MachineryTabs+Tab:text");
        private static readonly FieldInfo page = AccessTools.Field("MachineryTabs+Tab:page");

        internal static void Check()
        {
            if (entries == null || !typeof(IList).IsAssignableFrom(entries.FieldType) ||
                plate?.FieldType != typeof(TreeNodeRender) || text?.FieldType != typeof(TreeNodeRender) ||
                page?.FieldType != typeof(PixelElement))
                throw new MissingMemberException("Flat Storage cannot access the current game's MachineryTabs entries.");
        }

        internal static TabNodes Get(MachineryTabs tabs, int index)
        {
            var list = (IList)entries.GetValue(tabs);
            if (list.Count != tabs.count || index < 0 || index >= list.Count)
                throw new InvalidOperationException(ModText.Get("native.tabs_invalid", index));
            var entry = list[index];
            var plateNode = (TreeNodeRender)plate.GetValue(entry);
            var textNode = (TreeNodeRender)text.GetValue(entry);
            var pageElement = (PixelElement)page.GetValue(entry);
            if (plateNode == null || textNode == null || textNode.text == null || pageElement != tabs.GetPage(index))
                throw new InvalidOperationException(ModText.Get("native.tabs_invalid", index));
            return new TabNodes(plateNode, textNode, pageElement);
        }

        internal readonly struct TabNodes
        {
            internal readonly TreeNodeRender Plate, Text;
            internal readonly PixelElement Page;

            internal TabNodes(TreeNodeRender plate, TreeNodeRender text, PixelElement page)
            { Plate = plate; Text = text; Page = page; }
        }
    }
}
