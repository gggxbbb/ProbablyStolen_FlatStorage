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
            Shortcut = config.DefineEnum("shortcut", KeyCode.Backslash, "Flat Storage", () => "全局仓储快捷键");
            if (Shortcut.Value == KeyCode.F8) Shortcut.Set(KeyCode.Backslash);
            ModHelper.AddGameOptionSection(Id, "storage", () => "扁平存储");
            ModHelper.AddGameOptionKeyBind(Id, "storage", "shortcut", () => "打开全局仓储",
                () => Shortcut.Value, key => Shortcut.Set(key));
            ModHook.OnGameLoadedLate += Loaded;
            ModHook.OnAfterhourLeaveEarly += Leaving;
            ModHook.OnGoingSleepEarly += Closing;
            Log.Log("Flat Storage 已启用，默认快捷键为反斜杠键。");
        }

        public void OnDisable()
        {
            ModHook.OnGameLoadedLate -= Loaded;
            ModHook.OnAfterhourLeaveEarly -= Leaving;
            ModHook.OnGoingSleepEarly -= Closing;
            FlatStorageController.Remove();
            ModHelper.RemoveGameOptions(Id);
        }

        private static void Loaded()
        {
            FlatStorageController.Remove();
            new GameObject("Flat Storage").AddComponent<FlatStorageController>();
        }

        private static void Leaving(string location) => Closing();
        private static void Closing() => FlatStorageController.Current?.Close();
    }
}
