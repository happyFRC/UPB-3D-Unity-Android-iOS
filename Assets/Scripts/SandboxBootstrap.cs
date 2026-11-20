using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 进入 SandboxScene 时自动创建 SandboxSceneController，
/// 并禁用从演化场景复制过来的旧 StellarEvolutionScene 组件（它需要一堆沙盘里没有的 UI 引用）。
/// 这样 SandboxScene 不需要手动挂任何脚本或拖任何引用。
/// </summary>
public static class SandboxBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init() {
#if UNITY_ANDROID && !UNITY_EDITOR
        // 手机端固定 60 帧目标；关闭 vSync 让目标帧率真正生效。
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
#endif
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 有些编辑器设置会关闭 Scene Reload；这时 sceneLoaded 不一定触发，
    // 所以在首个场景加载完成后再兜底检查一次。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureForActiveScene() {
        if (SceneManager.GetActiveScene().name == "SandboxScene") {
            EnsureController();
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (scene.name == "SandboxScene") EnsureController();
    }

    private static void EnsureController() {
        StellarEvolutionScene old = UnityEngine.Object.FindObjectOfType<StellarEvolutionScene>();
        if (old != null) old.enabled = false;

        if (UnityEngine.Object.FindObjectOfType<SandboxSceneController>() == null) {
            GameObject go = new("SandboxSceneController");
            go.AddComponent<SandboxSceneController>();
        }

        EnsureTimeManager();
    }

    private static void EnsureTimeManager() {
        if (UnityEngine.Object.FindObjectOfType<SandboxTimeManager>() != null) return;
        GameObject go = new("SandboxTimeManager");
        go.AddComponent<SandboxTimeManager>();
    }
}
