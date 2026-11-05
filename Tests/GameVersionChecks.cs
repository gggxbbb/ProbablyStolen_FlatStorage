using System.Reflection;
using Mono.Cecil;

internal static class GameVersionChecks
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Manifest(string workspace, string managed)
    {
        // 当前游戏资源 PlayerSettings.bundleVersion 的兼容版本。
        const string supportedVersion = "0.46D";
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"));
        var matcher = game.MainModule.Types.Single(type => type.Name == "ModUIManager")
            .Methods.Single(method => method.Name == "VersionMatchesGame");
        var calls = matcher.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().ToList();
        Check(calls.Any(method => method.DeclaringType.FullName == "UnityEngine.Application" && method.Name == "get_version"),
            "原生兼容判断读取 Application.version");
        Check(calls.Count(method => method.Name == "NormalizeVersion") == 2, "原生兼容判断规范化声明和游戏版本");

        var manifest = ModLoader.ParseManifestForTools(workspace, out string problem);
        Check(manifest != null && problem == null, "原生清单读取失败：" + problem);
        var normalize = typeof(ModUIManager).GetMethod("NormalizeVersion", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(ModUIManager), "NormalizeVersion");
        string actual = (string)normalize.Invoke(null, new object[] { supportedVersion });
        Check(actual.Length > 0, "游戏兼容版本包含有效数字");
        Check(manifest.GameVersions.Count > 0 && manifest.GameVersions.All(version =>
            (string)normalize.Invoke(null, new object[] { version }) == actual),
            "清单兼容版本与游戏不符：" + string.Join(", ", manifest.GameVersions) + "；游戏版本：" + supportedVersion);
        Check(manifest.GameVersions.SequenceEqual(new[] { supportedVersion }), "清单声明完整游戏兼容版本");
        Console.WriteLine("  已核验游戏兼容版本 " + supportedVersion + "，原生规范化结果 " + actual + "。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
