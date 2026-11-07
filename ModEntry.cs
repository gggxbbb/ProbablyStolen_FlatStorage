using UnityEngine;

namespace ProbablyStolen_FlatStorage
{
    public sealed class ModEntry : IMod
    {
        public static ModEntry Instance { get; private set; }
        public const string Id = "flat_storage";
        public ModLog Log { get; private set; }
        internal ConfigEntry<KeyCode> Shortcut { get; private set; }

        public void Init(ModManifest manifest)
        {
            Instance = this;
            Log = new ModLog(manifest);
            NativeAccess.Check();
        }

        public void OnEnable()
        {
            var config = ModHelper.GetConfig(Id);
            Shortcut = config.DefineEnum("shortcut", KeyCode.Backslash, "Flat Storage", () => ModText.Get("settings.shortcut"));
            if (Shortcut.Value == KeyCode.F8) Shortcut.Set(KeyCode.Backslash);
            ModHelper.AddGameOptionSection(Id, "storage", () => ModText.Get("app.name"));
            ModHelper.AddGameOptionKeyBind(Id, "storage", "shortcut", () => ModText.Get("settings.open"),
                () => Shortcut.Value, key => Shortcut.Set(key));
            ModSettings.Register(config);
            PerformanceMonitor.Install();
#if FLAT_STORAGE_RUNTIME_VALIDATION
            PerformanceRuntimeValidation.Install();
#endif
            ModHook.OnGameLoadedInit += Loading;
            ModHook.OnGameLoadedLate += Loaded;
            ModHook.OnAfterhourArriveEarly += Leaving;
            ModHook.OnAfterhourLeaveEarly += Leaving;
            ModHook.OnGoingSleepEarly += Closing;
        }

        public void OnDisable()
        {
            PerformanceMonitor.Shutdown();
            ModHook.OnGameLoadedInit -= Loading;
            ModHook.OnGameLoadedLate -= Loaded;
            ModHook.OnAfterhourArriveEarly -= Leaving;
            ModHook.OnAfterhourLeaveEarly -= Leaving;
            ModHook.OnGoingSleepEarly -= Closing;
            FlatStorageController.Remove();
            ModHelper.RemoveGameOptions(Id);
        }

        private static void Loaded()
        {
            FlatStorageController.Remove();
            SaveDataMaintenance.Loaded();
            new GameObject("Flat Storage").AddComponent<FlatStorageController>();
            Instance.Log.Log(ModText.Get("app.enabled", Instance.Shortcut.Value));
        }

        private static void Loading()
        {
            PerformanceMonitor.Boundary("game-loading");
            FlatStorageController.Current?.CloseWithoutSave();
        }
        private static void Leaving(string location) => Closing();
        private static void Closing()
        {
            PerformanceMonitor.Boundary("scene-transition");
            FlatStorageController.Current?.Close();
        }
    }
}
