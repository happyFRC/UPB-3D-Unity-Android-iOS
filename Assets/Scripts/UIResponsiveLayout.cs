using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
/// <summary>
/// 横屏手机 UI 自适应：不改场景、不用逐个拖拽。
///
/// 每个场景加载后（等所有 Start 跑完），把 Canvas 下“按 1920×1080 绝对坐标摆放”的
/// 顶层 UI 元素自动换算成贴角 / 贴边锚点，并保留它在参考分辨率下的边距：
///   - 距离某条边小于画布 20% → 贴到那条边（边距不变）；
///   - 否则保持居中；
///   - 横向拉伸且 sizeDelta.x &lt; 0 的侧面板（参数面板 / 创建面板）→ 改成固定宽度贴左或贴右。
///
/// 已经用贴边 / 拉伸锚点做好的元素不会被动；只跑一次，重复执行结果不变。
/// </summary>
public class UIResponsiveLayout : MonoBehaviour {
    /// <summary>离边缘多近（占画布比例）才认为是“贴边元素”。</summary>
    private const float EdgeTolerance = 0.2f;

    /// <summary>中心偏离画布中心超过这个比例，才认为它属于某一侧。</summary>
    private const float CenterBand = 0.15f;

    private static readonly Vector2 FallbackReference = new(1920f, 1080f);
    private readonly List<int> _adapted = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() {
        GameObject host = new("UIResponsiveLayout");
        DontDestroyOnLoad(host);
        host.AddComponent<UIResponsiveLayout>();
    }

    private void OnEnable() {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable() {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start() {
        StartCoroutine(AdaptNextFrame());
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) {
        _adapted.Clear();
        StartCoroutine(AdaptNextFrame());
    }

    private IEnumerator AdaptNextFrame() {
        // 等所有脚本的 Start 执行完（它们可能还在摆 UI），再统一换算锚点。
        yield return null;
        yield return new WaitForEndOfFrame();

        int adapted = 0;
        foreach (Canvas canvas in FindObjectsOfType<Canvas>()) {
            if (canvas == null || !canvas.isRootCanvas) continue;
            int id = canvas.GetInstanceID();
            if (_adapted.Contains(id)) continue;
            _adapted.Add(id);
            adapted += AdaptCanvas(canvas);
        }

        if (adapted > 0) {
            Debug.Log($"[UI] 横屏自适应：调整了 {adapted} 个 UI 元素");
        }
    }

    private static int AdaptCanvas(Canvas canvas) {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        // 只处理“按分辨率缩放”的屏幕空间 Canvas；
        // 星点、漂浮文字这类临时 Canvas 没有 CanvasScaler，不能动。
        if (scaler == null) return 0;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 0;
        if (canvas.renderMode == RenderMode.WorldSpace) return 0;

        Vector2 reference = scaler.referenceResolution;
        if (reference.x < 1f || reference.y < 1f) reference = FallbackReference;

        // 横屏按高度缩放：设计高度 1080 一定放得下，多出来的宽度分给两侧。
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        RectTransform root = canvas.transform as RectTransform;
        if (root == null) return 0;

        int count = 0;
        for (int i = 0; i < root.childCount; i++) {
            if (root.GetChild(i) is not RectTransform child) continue;
            // 向导和“退出教程”按钮自己按 Canvas 中心坐标移动，不能被贴边换算改掉锚点。
            if (child.name == "Aquarius_One_Guider" || child.name == "Button_QuitTutorial") {
                continue;
            }
            if (AdaptElement(child, reference)) count++;
        }
        return count;
    }

    private static bool AdaptElement(RectTransform rect, Vector2 reference) {
        Vector2 anchorMin = rect.anchorMin;
        Vector2 anchorMax = rect.anchorMax;
        Vector2 pivot = rect.pivot;
        Vector2 position = rect.anchoredPosition;
        Vector2 sizeDelta = rect.sizeDelta;
        Vector2 size = rect.rect.size;

        bool stretchX = !Mathf.Approximately(anchorMin.x, anchorMax.x);
        bool stretchY = !Mathf.Approximately(anchorMin.y, anchorMax.y);

        // 1) 横向拉伸 + 负 sizeDelta + 整体偏移 → 贴左 / 贴右的固定宽度侧面板。
        //    原来宽度 = 画布宽 + sizeDelta，屏幕一宽面板就被拉宽、内部控件错位。
        if (stretchX && sizeDelta.x < -1f && Mathf.Abs(position.x) > reference.x * 0.05f) {
            float fixedWidth = Mathf.Max(1f, reference.x + sizeDelta.x);
            bool dockRight = position.x > 0f;

            rect.anchorMin = new Vector2(dockRight ? 1f : 0f, anchorMin.y);
            rect.anchorMax = new Vector2(dockRight ? 1f : 0f, anchorMax.y);
            rect.pivot = new Vector2(dockRight ? 1f : 0f, pivot.y);
            rect.sizeDelta = new Vector2(fixedWidth, sizeDelta.y);
            rect.anchoredPosition = new Vector2(0f, position.y);
            return true;
        }

        // 2) 只换算“居中锚点 + 居中轴心”的元素；标题那种用左/上轴心的保持原样，
        //    否则会把宽标题误判成贴右。
        if (stretchX || stretchY) return false;
        if (Mathf.Abs(pivot.x - 0.5f) > 0.01f || Mathf.Abs(pivot.y - 0.5f) > 0.01f) {
            return false;
        }

        float width = size.x;
        float height = size.y;

        // 换算到“参考分辨率画布中心为原点”的坐标。
        float centerX = (anchorMin.x - 0.5f) * reference.x + position.x;
        float centerY = (anchorMin.y - 0.5f) * reference.y + position.y;

        float leftMargin = centerX - width * 0.5f + reference.x * 0.5f;
        float rightMargin = reference.x * 0.5f - centerX - width * 0.5f;
        float bottomMargin = centerY - height * 0.5f + reference.y * 0.5f;
        float topMargin = reference.y * 0.5f - centerY - height * 0.5f;

        float toleranceX = reference.x * EdgeTolerance;
        float toleranceY = reference.y * EdgeTolerance;
        float centerBandX = reference.x * CenterBand;
        float centerBandY = reference.y * CenterBand;
        float maxDockedWidth = reference.x * 0.9f;
        float maxDockedHeight = reference.y * 0.9f;

        // 水平：中心明显偏左/偏右 + 宽度不过大 + 边距合理，才贴边；否则保持居中。
        float anchorX = 0.5f;
        float outX = centerX;
        if (centerX <= -centerBandX && width <= maxDockedWidth &&
            leftMargin >= -1f && leftMargin <= toleranceX) {
            anchorX = 0f;
            outX = leftMargin;
        } else if (centerX >= centerBandX && width <= maxDockedWidth &&
                   rightMargin >= -1f && rightMargin <= toleranceX) {
            anchorX = 1f;
            outX = -rightMargin;
        }

        // 垂直：同理。
        float anchorY = 0.5f;
        float outY = centerY;
        if (centerY <= -centerBandY && height <= maxDockedHeight &&
            bottomMargin >= -1f && bottomMargin <= toleranceY) {
            anchorY = 0f;
            outY = bottomMargin;
        } else if (centerY >= centerBandY && height <= maxDockedHeight &&
                   topMargin >= -1f && topMargin <= toleranceY) {
            anchorY = 1f;
            outY = -topMargin;
        }

        bool changed =
            !Mathf.Approximately(anchorMin.x, anchorX) ||
            !Mathf.Approximately(anchorMin.y, anchorY);
        if (!changed) return false;

        rect.anchorMin = new Vector2(anchorX, anchorY);
        rect.anchorMax = new Vector2(anchorX, anchorY);
        rect.pivot = new Vector2(anchorX == 0.5f ? 0.5f : anchorX,
                                 anchorY == 0.5f ? 0.5f : anchorY);
        rect.anchoredPosition = new Vector2(outX, outY);
        return true;
    }
}
