using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class NativeLayoutSizes
    {
        private static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>
        {
            [typeof(PixelWindow)] = new[] { AccessTools.Field(typeof(PixelWindow), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(PixelWindow), "<heightPixels>k__BackingField") },
            [typeof(MachineryPanelRow)] = new[] { AccessTools.Field(typeof(MachineryPanelRow), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryPanelRow), "<heightPixels>k__BackingField") },
            [typeof(MachineryTabs)] = new[] { AccessTools.Field(typeof(MachineryTabs), "requestedWidth"), AccessTools.Field(typeof(MachineryTabs), "requestedHeight"), AccessTools.Field(typeof(MachineryTabs), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryTabs), "<heightPixels>k__BackingField") },
            [typeof(MachineryPanel)] = new[] { AccessTools.Field(typeof(MachineryPanel), "requestedWidth"), AccessTools.Field(typeof(MachineryPanel), "requestedHeight"), AccessTools.Field(typeof(MachineryPanel), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryPanel), "<heightPixels>k__BackingField") },
            [typeof(MachineryIntake)] = new[] { AccessTools.Field(typeof(MachineryIntake), "requestedWidth"), AccessTools.Field(typeof(MachineryIntake), "requestedHeight"), AccessTools.Field(typeof(MachineryIntake), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryIntake), "<heightPixels>k__BackingField") },
            [typeof(MachineryStack)] = new[] { AccessTools.Field(typeof(MachineryStack), "requestedWidth"), AccessTools.Field(typeof(MachineryStack), "requestedHeight"), AccessTools.Field(typeof(MachineryStack), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryStack), "<heightPixels>k__BackingField") },
            [typeof(MachineryProgressBar)] = new[] { AccessTools.Field(typeof(MachineryProgressBar), "requestedWidth"), AccessTools.Field(typeof(MachineryProgressBar), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryProgressBar), "<heightPixels>k__BackingField") },
            [typeof(MachineryRangeSlider)] = new[] { AccessTools.Field(typeof(MachineryRangeSlider), "requestedWidth"), AccessTools.Field(typeof(MachineryRangeSlider), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryRangeSlider), "<heightPixels>k__BackingField") },
            [typeof(MachinerySlider)] = new[] { AccessTools.Field(typeof(MachinerySlider), "requestedWidth"), AccessTools.Field(typeof(MachinerySlider), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachinerySlider), "<heightPixels>k__BackingField") },
            [typeof(MachineryText)] = new[] { AccessTools.Field(typeof(MachineryText), "requestedWidth"), AccessTools.Field(typeof(MachineryText), "<widthPixels>k__BackingField"), AccessTools.Field(typeof(MachineryText), "<heightPixels>k__BackingField") }
        };
        private readonly Dictionary<PixelElement, int[]> values = new Dictionary<PixelElement, int[]>();

        internal static void Check()
        {
            foreach (var type in fields.Keys)
            {
                if (fields[type].Any(f => f == null || f.FieldType != typeof(int)))
                    throw new MissingMemberException("无法保存原生控件尺寸：" + type.Name);
            }
        }

        internal void Capture(PixelElement element)
        {
            if (element == null || values.ContainsKey(element) || !fields.TryGetValue(element.GetType(), out var members)) return;
            values.Add(element, members.Select(f => (int)f.GetValue(element)).ToArray());
        }

        internal void Restore(PixelElement subtree = null)
        {
            // 全部尺寸先恢复，再允许原生控件相互触发排版。
            foreach (var pair in values.ToArray())
            {
                if (pair.Key.handler == null) continue;
                if (subtree != null && !pair.Key.handler.transform.IsChildOf(subtree.handler.transform)) continue;
                var members = fields[pair.Key.GetType()];
                for (int i = 0; i < members.Length; i++) members[i].SetValue(pair.Key, pair.Value[i]);
                if (subtree != null) values.Remove(pair.Key);
            }
        }
    }
}
