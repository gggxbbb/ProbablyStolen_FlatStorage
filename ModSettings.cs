using System;
using System.Globalization;

namespace ProbablyStolen_FlatStorage
{
    internal static class ModSettings
    {
        private static ConfigEntry<float> zoom, refresh, edge;
        private static ConfigEntry<bool> automatic;

        internal static float DefaultZoom => Valid(zoom, .25f, 2);
        internal static float RefreshInterval => Valid(refresh, .1f, 2);
        internal static float EdgePanSpeed => Valid(edge, 0, 30);
        internal static bool DefaultAutoArrange => (automatic ?? throw new InvalidOperationException("Flat Storage settings are not initialized.")).Value;

        internal static void Register(ModConfig config)
        {
            zoom = config.Define("default_zoom", 1f, "Flat Storage", () => ModText.Get("settings.zoom"), .25f, 2f);
            refresh = config.Define("refresh_interval", .25f, "Flat Storage", () => ModText.Get("settings.refresh"), .1f, 2f);
            edge = config.Define("edge_pan_speed", 12f, "Flat Storage", () => ModText.Get("settings.edge"), 0f, 30f);
            automatic = config.Define("automatic_default", true, "Flat Storage", () => ModText.Get("settings.automatic"));
            ModHelper.AddGameOptionSlider(ModEntry.Id, "storage", "default_zoom", () => ModText.Get("settings.zoom"),
                () => DefaultZoom, value => zoom.Set(value), .25f, 2f, false, null,
                value => Math.Round(value * 100).ToString(CultureInfo.CurrentCulture) + "%");
            ModHelper.AddGameOptionSlider(ModEntry.Id, "storage", "refresh_interval", () => ModText.Get("settings.refresh"),
                () => RefreshInterval, value => refresh.Set(value), .1f, 2f, false, null,
                value => ModText.Get("settings.seconds", value.ToString("0.00", CultureInfo.CurrentCulture)));
            ModHelper.AddGameOptionSlider(ModEntry.Id, "storage", "edge_pan_speed", () => ModText.Get("settings.edge"),
                () => EdgePanSpeed, value => edge.Set(value), 0f, 30f, false, null,
                value => value.ToString("0.0", CultureInfo.CurrentCulture));
            ModHelper.AddGameOptionToggle(ModEntry.Id, "storage", "automatic_default", () => ModText.Get("settings.automatic"),
                () => DefaultAutoArrange, value => automatic.Set(value));
            Validate();
        }

        internal static CanvasState CreateCanvasState(string json)
        {
            Validate();
            return CanvasState.Create(json, DefaultZoom, DefaultAutoArrange);
        }

        private static float Valid(ConfigEntry<float> entry, float minimum, float maximum)
        {
            if (entry == null || !CanvasMath.Finite(entry.Value) || entry.Value < minimum || entry.Value > maximum)
                throw new InvalidOperationException("Invalid Flat Storage setting: " + entry?.Key);
            return entry.Value;
        }

        private static void Validate()
        {
            _ = DefaultZoom;
            _ = RefreshInterval;
            _ = EdgePanSpeed;
            _ = DefaultAutoArrange;
        }
    }
}
