using System;
using System.Globalization;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal static class ModText
    {
        internal static string Get(string key, params object[] args)
        {
            var mod = ModLoader.Mods.Single(m => m.Manifest.ID == ModEntry.Id);
            RegisterLocales(mod);
            if (!mod.Localization["zh"].ContainsKey(key) || !mod.Localization["en"].ContainsKey(key))
                throw new InvalidOperationException("Missing Flat Storage localization: " + key);
            string text = ModHelper.GetLocalized(ModEntry.Id, key);
            return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
        }

        internal static void RegisterLocales(LoadedMod mod)
        {
            if (!mod.Localization.TryGetValue("zh", out var chinese) || !mod.Localization.ContainsKey("en"))
                throw new InvalidOperationException("Flat Storage localization files are not loaded.");
            mod.Localization["zh-Hans"] = chinese;
        }
    }
}
