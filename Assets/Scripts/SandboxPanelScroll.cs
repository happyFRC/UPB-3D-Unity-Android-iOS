using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 参数面板的竖向滚动：PC 用鼠标滚轮，手机端按住面板上下拖动。
/// 不重建 UI 层级：直接移动整个面板，由父面板的 RectMask2D 负责裁剪。
/// </summary>
public class SandboxPanelScroll : MonoBehaviour {
    private RectTransform _panel;
    private RectTransform _maskRoot;
    private Vector2 _basePosition;
    private float _scroll;   // 0 = 顶部，max = 底部
    private float _maxScroll;

    // 拖动滚动（触屏 / 按住鼠标拖动）
    private bool _dragging;
    private Vector2 _lastDragPosition;
    private float _dragVelocity;

    public void Initialize(RectTransform panel, RectTransform maskRoot = null) {
        _panel = panel != null ? panel : GetComponent<RectTransform>();
        RectTransform parent = maskRoot != null ? maskRoot : _panel != null ? _panel.parent as RectTransform : null;
        if (_panel == null) return;

        // 关键：不要把 RectMask2D 直接加在 StarParameterPanel 上，
        // 否则同在参数面板下的 UnitPanel 也会被裁剪掉。
        // 只给 Mechanics 建一个独立 viewport。
        if (parent != null) {
            GameObject viewportGo = new("MechanicsViewport", typeof(RectTransform),
                typeof(RectMask2D));
            RectTransform viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(parent, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            _panel.SetParent(viewport, true);
            _maskRoot = viewport;
        }

        _basePosition = _panel.anchoredPosition;
    }

    private void Update() {
        if (_panel == null || !_panel.gameObject.activeInHierarchy) return;

        RectTransform hitArea = _maskRoot != null ? _maskRoot : _panel;

        HandleWheel(hitArea);
        HandleDrag(hitArea);
    }

    private void HandleWheel(RectTransform hitArea) {
        if (!RectTransformUtility.RectangleContainsScreenPoint(hitArea, Input.mousePosition, null)) return;

        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) < 0.001f) return;

        // 滚轮向下 -> scroll 增大 -> UI 按钮整体向上移动，露出下方。
        // 滚轮向上 -> scroll 减小 -> UI 按钮整体向下移动，回到顶部。
        RecalculateRange();
        _scroll = Mathf.Clamp(_scroll - wheel * 120f, 0f, _maxScroll);
        ApplyPosition();
    }

    /// <summary>
    /// 按住面板上下拖动：手指往上 -> 内容跟着往上 -> 露出下方（和滚轮方向一致）。
    /// 松手后带一点惯性。
    /// </summary>
    private void HandleDrag(RectTransform hitArea) {
        // 双指缩放时不要滚动。
        if (Input.touchCount > 1) {
            _dragging = false;
            _dragVelocity = 0f;
            return;
        }

        if (Input.GetMouseButtonDown(0)) {
            _dragging = RectTransformUtility.RectangleContainsScreenPoint(
                hitArea, Input.mousePosition, null);
            _lastDragPosition = Input.mousePosition;
            _dragVelocity = 0f;
        }

        if (_dragging && Input.GetMouseButton(0)) {
            Vector2 current = Input.mousePosition;
            float deltaY = current.y - _lastDragPosition.y;
            _lastDragPosition = current;

            if (Mathf.Abs(deltaY) > 0.01f) {
                float scaled = deltaY / CanvasScale();
                RecalculateRange();
                _scroll = Mathf.Clamp(_scroll + scaled, 0f, _maxScroll);
                ApplyPosition();
                _dragVelocity = scaled / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            }
            return;
        }

        if (Input.GetMouseButtonUp(0)) _dragging = false;

        // 松手后的惯性滑动。
        if (!_dragging && Mathf.Abs(_dragVelocity) > 20f) {
            RecalculateRange();
            _scroll = Mathf.Clamp(
                _scroll + _dragVelocity * Time.unscaledDeltaTime, 0f, _maxScroll);
            ApplyPosition();
            _dragVelocity *= Mathf.Pow(0.02f, Time.unscaledDeltaTime);
        }
    }

    private float CanvasScale() {
        Canvas canvas = GetComponentInParent<Canvas>();
        return canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;
    }

    private void RecalculateRange() {
        _maxScroll = Mathf.Max(0f, MeasureContentHeight() - _panel.rect.height);
    }

    private void ApplyPosition() {
        _panel.anchoredPosition = _basePosition + new Vector2(0f, _scroll);
    }

    private float MeasureContentHeight() {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (Transform child in _panel) {
            if (child is not RectTransform rect) continue;
            Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(_panel, rect);
            min = Mathf.Min(min, b.min.y);
            max = Mathf.Max(max, b.max.y);
        }
        if (min > max) return _panel.rect.height;
        return Mathf.Max(_panel.rect.height, max - min);
    }
}
