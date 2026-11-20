using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手机端新手向导：在 HOME 页点“新手引导”按钮开始，
/// 手动步骤用对话框上方的 NextStepButton 推进；
/// 检测到玩家完成操作的步骤会自动进入下一步，此时不显示该按钮。
///
/// 位置规则：
///  · 向导只在屏幕左右两侧移动，表情边缘距屏幕边框恒为 220px；
///  · 表情在以锚点为圆心、半径 60px 的圆内移动，并水平镜像“看向”屏幕中心；
///  · 对话框统一放在向导正上方 200px；
///  · 需要看左侧界面时向导移到左边，看完回到右侧。
/// </summary>
public class SandboxGuide : MonoBehaviour {
    private const float EdgeMargin = 220f;
    private const float FaceRadius = 60f;
    private const float BubbleAbove = 275f;
    // 第一次进入“创建宇宙”时自动弹一次教程；之后只从 HOME 页按钮手动进入。
    private const string AutoIntroKey = "UPB_GuideAutoIntroShown_v3";

    public static bool IsRunning { get; private set; }

    /// <summary>向导当前是否显示在屏幕上（教学/提示都算）。</summary>
    public static bool IsVisible { get; private set; }

    /// <summary>向导从“显示”变为“隐藏”时回调，用来补弹被压住的场景介绍。</summary>
    public Action OnHidden;

    private enum Side { Right, Left }
    private enum Mood { Neutral, Happy, Curious, Serious, Excited, Angry }

    private class Step {
        public string Text;
        public Func<bool> Done;
        public float AutoSeconds;
        public Side Side = Side.Right;
        public Mood Mood = Mood.Neutral;
        public Action OnEnter;
        public bool HomeOnly;   // 只在 HOME 页手动启动时出现
        public bool AutoOnly;   // 只在第一次进入创建宇宙自动弹出时出现
    }

    private SandboxSceneController _controller;
    private RectTransform _root;
    private RectTransform _face;
    private RawImage _faceImage;
    private RectTransform _bubble;
    private TMP_Text _bubbleText;
    private RawImage _bodyImage;
    private Button _nextStepButton;
    private TMP_Text _nextStepLabel;
    private Button _quitTutorialButton;

    private readonly System.Collections.Generic.List<Step> _steps = new();
    private readonly System.Collections.Generic.List<RectTransform> _panels = new();
    private readonly System.Collections.Generic.List<Transform> _modulePanels = new();
    private readonly System.Collections.Generic.Dictionary<string, Transform> _panelCache = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _expressions = new();
    private readonly System.Collections.Generic.HashSet<string> _visitedModules = new();

    private bool _tutorial;
    private bool _fromHome;
    private int _index = -1;
    private int _bodyBaseline;
    private int _renderBaseline;
    private bool _renderBaselineCaptured;
    private double _speedBaseline;
    private double _speedPeak;
    private int _scenarioMark;
    private int _homeLoadMark;
    private Vector2 _anchor;
    private Vector2 _faceOffset;
    private Side _side = Side.Right;
    private Mood _mood = Mood.Neutral;

    // ~~~ 初始化 ~~~

    public void Initialize(SandboxSceneController controller) {
        _controller = controller;

        _root = GetComponent<RectTransform>();
        _face = FindChild(_root, "RawImage");
        _faceImage = _face != null ? _face.GetComponent<RawImage>() : null;
        _bodyImage = GetComponent<RawImage>();
        _bubble = FindChild(_root, "ConversationPop");
        _bubbleText = _bubble != null ? _bubble.GetComponentInChildren<TMP_Text>(true) : null;

        _nextStepButton = _bubble != null
            ? _bubble.Find("NextStepButton")?.GetComponent<Button>()
            : null;
        _nextStepLabel = _nextStepButton != null
            ? _nextStepButton.GetComponentInChildren<TMP_Text>(true)
            : null;
        if (_nextStepButton != null) {
            _nextStepButton.onClick.RemoveAllListeners();
            _nextStepButton.onClick.AddListener(OnNextStepClicked);
        }
        SetNextStepVisible(false);
        TuneMobileUi();

        SetUpStartGuideButton();
        SetUpQuitTutorialButton();
        CachePanels();
        LoadExpressions();
        BuildSteps();

        // 一直保持物体激活（只是隐藏画面），否则 Update 不跑。
        SetVisible(false);
        Debug.Log("[Guide] init, steps=" + _steps.Count + " controller=" + (_controller != null));

        // 第一次进入“创建宇宙”时自动弹出教程（只弹一次）。
        if (PlayerPrefs.GetInt(AutoIntroKey, 0) == 0) {
            PlayerPrefs.SetInt(AutoIntroKey, 1);
            PlayerPrefs.Save();
            StartAutoFirstTime();
        }
    }

    /// <summary>手机端触控尺寸：按钮和气泡文字放大，避免小按钮不好点。</summary>
    private void TuneMobileUi() {
        if (_nextStepButton != null) {
            RectTransform rt = _nextStepButton.GetComponent<RectTransform>();
            if (rt != null) {
                rt.sizeDelta = new Vector2(180f, 64f);
                rt.anchoredPosition = new Vector2(0f, 135f);
            }
            if (_nextStepLabel != null) _nextStepLabel.fontSize = 28f;
        }
        if (_bubbleText != null) {
            _bubbleText.enableAutoSizing = true;
            _bubbleText.fontSizeMin = 18f;
            _bubbleText.fontSizeMax = 28f;
            _bubbleText.fontSize = 26f;
        }
    }

    /// <summary>在 HOME 页创建/接线“新手引导”按钮（Button_StartNewGuide）。</summary>
    private void SetUpStartGuideButton() {
        Transform home = FindScene("HomePagePanel");
        if (home == null) return;

        Transform existing = FindDeep(home, "Button_StartNewGuide");
        GameObject go = existing != null ? existing.gameObject : CreateStartGuideButton(home);
        if (go == null) return;

        go.transform.SetAsLastSibling();
        go.SetActive(true);
        Button button = go.GetComponent<Button>();
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(StartNewGuide);
    }

    private GameObject CreateStartGuideButton(Transform home) {
        Transform template = _bubble != null ? _bubble.Find("NextStepButton") : null;
        if (template == null) return null;

        GameObject go = Instantiate(template.gameObject, home);
        go.name = "Button_StartNewGuide";
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null) {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(810f, 490f);
            rt.sizeDelta = new Vector2(300f, 100f);
        }
        TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) {
            label.text = "新手引导";
            label.fontSize = 36f;
        }
        // 模板按钮在向导初始化时已经被隐藏，克隆出来默认也是隐藏的，必须主动打开。
        go.SetActive(true);
        return go;
    }

    /// <summary>接线屏幕上方的“退出教程”按钮（Button_QuitTutorial）。</summary>
    private void SetUpQuitTutorialButton() {
        Transform canvas = transform.parent;
        if (canvas == null) return;
        Transform existing = FindDeep(canvas, "Button_QuitTutorial");
        if (existing == null) existing = FindScene("Button_QuitTutorial");
        GameObject go = existing != null ? existing.gameObject : CreateQuitTutorialButton(canvas);
        if (go == null) return;

        // 不管这个按钮原来挂在哪个面板下，都移到 Canvas 根节点并放到屏幕上方的水平正中：
        // 否则它可能随 HOME 页隐藏、跑到屏幕外，或者挡住右上角的其它 UI。
        go.transform.SetParent(canvas, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null) {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -30f);
            rt.sizeDelta = new Vector2(220f, 64f);
        }
        TMP_Text quitLabel = go.GetComponentInChildren<TMP_Text>(true);
        if (quitLabel != null) quitLabel.fontSize = 28f;

        _quitTutorialButton = go.GetComponent<Button>();
        if (_quitTutorialButton == null) return;
        _quitTutorialButton.onClick.RemoveAllListeners();
        _quitTutorialButton.onClick.AddListener(OnQuitTutorialClicked);
        go.SetActive(false);
    }

    private GameObject CreateQuitTutorialButton(Transform canvas) {
        Transform template = _bubble != null ? _bubble.Find("NextStepButton") : null;
        if (template == null) return null;

        GameObject go = Instantiate(template.gameObject, canvas);
        go.name = "Button_QuitTutorial";
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null) {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(780f, 780f);
            rt.sizeDelta = new Vector2(200f, 50f);
        }
        TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) {
            label.text = "退出教程";
            label.fontSize = 24f;
        }
        go.SetActive(true);
        return go;
    }

    private void OnQuitTutorialClicked() {
        if (!_tutorial) return;
        Finish(completed: false);
    }

    private static RectTransform FindChild(Transform root, string name) {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++) {
            Transform c = root.GetChild(i);
            if (c.name == name) return c as RectTransform;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static Transform FindScene(string name) {
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>()) {
            if (t == null || t.name != name) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            return t;
        }
        return null;
    }

    private Transform GetPanel(string name) {
        if (_panelCache.TryGetValue(name, out Transform cached) && cached != null) return cached;
        Transform found = FindScene(name);
        _panelCache[name] = found;
        return found;
    }

    private bool IsPanelActive(string name) {
        Transform t = GetPanel(name);
        return t != null && t.gameObject.activeInHierarchy;
    }

    private void SetPanelActive(string name, bool active) {
        Transform t = GetPanel(name);
        if (t != null) t.gameObject.SetActive(active);
    }

    private void CachePanels() {
        _panels.Clear();
        _modulePanels.Clear();
        // 只把“会挡住操作的大面板”算进重叠检测。
        foreach (string n in new[] {
            "CelestialBodyCreatePanel", "StarParameterPanel", "PlanetParameterPanel",
            "SettingsPanelSandbox", "HomePagePanel" }) {
            Transform t = GetPanel(n);
            if (t is RectTransform rt) _panels.Add(rt);
        }
        // 模块面板有多个同名副本，先一次性收集，避免每帧扫描整个场景。
        foreach (string n in new[] { "GeneralPanel", "MechanicsPanel", "Energy&StructureParameter" }) {
            FindAllScene(n, _modulePanels);
        }
    }

    private static void FindAllScene(string name, System.Collections.Generic.List<Transform> results) {
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>()) {
            if (t == null || t.name != name) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            results.Add(t);
        }
    }

    private void LoadExpressions() {
        _expressions.Clear();
        foreach (string name in new[] {
            "Normal", "smile", "smile_eyes_opend", "wink", "bored", "angry",
            "eyes_closed", "left_confused", "right_confused" }) {
            Texture2D tex = Resources.Load<Texture2D>("Guider_Expressions/" + name);
            if (tex != null) _expressions[name] = tex;
        }
    }

    // ~~~ 步骤表 ~~~

    private void Add(string text, Func<bool> done, float auto, Side side, Mood mood,
            Action onEnter = null, bool homeOnly = false, bool autoOnly = false) {
        _steps.Add(new Step {
            Text = text, Done = done, AutoSeconds = auto, Side = side, Mood = mood,
            OnEnter = onEnter, HomeOnly = homeOnly, AutoOnly = autoOnly
        });
    }

    private void BuildSteps() {
        _steps.Clear();

        // 第一次进入创建宇宙自动弹出：先简单介绍，不询问。
        Add("你好，我是向导水瓶座一，我来带你做新手教程。\n" +
            "如果不想学了，随时按屏幕上方的“退出教程”按键即可。",
            null, 0f, Side.Right, Mood.Happy, null, autoOnly: true);

        // 从 HOME 页手动启动：第一个提示是双击打开水瓶座一系。
        Add("请双击水瓶座一系，开始新手引导。如果当前玩的存档对你很重要，请先保存存档。",
            () => _controller != null && _controller.IsAquariusScenario &&
                  SandboxSceneController.ScenarioLoadCount > _scenarioMark,
            -1f, Side.Right, Mood.Happy, null, homeOnly: true);

        Add("点击左上角的 + 按钮，打开创建天体面板。",
            () => IsPanelActive("CelestialBodyCreatePanel"), -1f, Side.Right, Mood.Curious,
            CaptureBaselines);
        Add("在面板里选一颗行星，进入放置模式。",
            () => IsPanelActive("SandboxPlacementPreview"), -1f, Side.Right, Mood.Curious);
        Add("在场景里点一下，把它放下来。",
            () => SandboxPhysicsSystem.Bodies.Count > _bodyBaseline, -1f, Side.Right, Mood.Excited);
        Add("放下之后先把这个创建面板关掉。",
            () => !IsPanelActive("CelestialBodyCreatePanel"), -1f, Side.Right, Mood.Neutral);

        // 手机端新增：长按空白处取消选择；这一步用“下一步”手动继续。
        Add("长按场景空白处，可以取消当前的选择。", null, 0f, Side.Right, Mood.Happy);

        Add("点击刚放下的天体，打开它的参数面板。",
            () => IsPanelActive("StarParameterPanel") || IsPanelActive("PlanetParameterPanel"),
            -1f, Side.Left, Mood.Curious, () => _visitedModules.Clear());
        Add("依次看看通用、力学、能量三个模块。", ModulesVisited,
            -1f, Side.Left, Mood.Curious, () => ModulesVisited());
        Add("看完之后，把参数面板关掉。",
            () => !IsPanelActive("StarParameterPanel") && !IsPanelActive("PlanetParameterPanel"),
            -1f, Side.Left, Mood.Neutral);

        Add("点齿轮打开设置面板。", () => IsPanelActive("SettingsPanelSandbox"),
            -1f, Side.Right, Mood.Happy);
        Add("设置分通用、引力、热学三个模块，先点通用，试试“恒星曝光度”。",
            () => _controller != null && _renderBaselineCaptured
                && _controller.StarRenderMode != _renderBaseline,
            -1f, Side.Right, Mood.Excited, CaptureRenderBaseline);
        Add("点击画面看效果，然后关掉设置。", () => !IsPanelActive("SettingsPanelSandbox"),
            -1f, Side.Right, Mood.Happy);

        Add("再打开设置，切到引力模块。", () => IsPanelActive("SettingsPanelSandbox"),
            -1f, Side.Right, Mood.Curious);
        Add("把“全局引力”关掉试试。", () => !SandboxPhysicsSystem.GravityEnabled,
            -1f, Side.Right, Mood.Angry);
        Add("我来帮你关掉面板、把时间调到 7 天/秒，并把镜头拉到 5 AU 之外，注意看轨道变化。", null, 1.2f,
            Side.Right, Mood.Serious, ApplyGravityDemo);
        Add("看到了吗？关掉引力后，天体不再互相吸引，会沿直线漂移。", null, 4.0f,
            Side.Right, Mood.Neutral);

        Add("现在重新加载一遍水瓶座一星系，看看不同的场景。", null, 1.0f,
            Side.Right, Mood.Happy, ReloadAquarius);
        Add("加载好了。再次打开设置。", () => IsPanelActive("SettingsPanelSandbox"),
            -1f, Side.Right, Mood.Happy);
        Add("切到第三个模块：热学。", () => IsPanelActive("ThermalModelPanel"),
            -1f, Side.Right, Mood.Curious);
        Add("把“热辐射散热”关掉，看行星会怎样。", () => !SandboxPhysicsSystem.ThermalRadiationLoss,
            -1f, Side.Right, Mood.Angry);
        Add("然后关掉设置面板。", () => !IsPanelActive("SettingsPanelSandbox"),
            -1f, Side.Right, Mood.Neutral);

        Add("打开参数面板，查看“热辐射”功率那一行。",
            () => IsPanelActive("PlanetParameterPanel") || IsPanelActive("StarParameterPanel"),
            -1f, Side.Left, Mood.Curious);
        Add("看完把它关掉。",
            () => !IsPanelActive("PlanetParameterPanel") && !IsPanelActive("StarParameterPanel"),
            -1f, Side.Left, Mood.Neutral);

        // HOME 面板教学：打开场景库，双击一个存档进入。
        Add("点左上角的 HOME 按钮，打开场景库。",
            () => IsPanelActive("HomePagePanel"), -1f, Side.Left, Mood.Happy);
        Add("双击一个你想进入的存档，就能加载它。",
            () => !IsPanelActive("HomePagePanel") &&
                  SandboxSceneController.ScenarioLoadCount > _homeLoadMark,
            -1f, Side.Left, Mood.Excited,
            () => _homeLoadMark = SandboxSceneController.ScenarioLoadCount);
        Add("回到这边来，接着讲时间面板。", null, 1.5f, Side.Right, Mood.Happy);

        Add("下面这个是时间面板：可以改时间流速。", null, 3.0f, Side.Right, Mood.Curious);
        Add("点一下“加速”，时间会跑得更快。",
            () => SpeedAbove(_speedBaseline), -1f, Side.Right, Mood.Excited,
            () => _speedBaseline = CurrentSpeed());
        Add("再点一下“减速”，把速度降回来。",
            () => SpeedBelow(_speedPeak), -1f, Side.Right, Mood.Happy,
            () => _speedPeak = CurrentSpeed());
        Add("点暂停按钮，时间会停下来。", () => {
            SandboxTimeManager t = SandboxTimeManager.Instance;
            return t != null && t.IsPaused;
        }, -1f, Side.Right, Mood.Curious);
        Add("再点一次继续运行。", () => {
            SandboxTimeManager t = SandboxTimeManager.Instance;
            return t != null && !t.IsPaused;
        }, -1f, Side.Right, Mood.Happy);

        Add("就到这里啦，剩下的交给你自己探索！\n想再看一遍，可以在 HOME 页面点“新手引导”。",
            null, 0f, Side.Right, Mood.Happy);
    }

    private static double CurrentSpeed() {
        return SandboxTimeManager.Instance != null ? SandboxTimeManager.Instance.SpeedValue : 0.0;
    }

    private static bool SpeedAbove(double value) => CurrentSpeed() > value + 1e-6;
    private static bool SpeedBelow(double value) => CurrentSpeed() < value - 1e-6;

    private bool ModulesVisited() {
        foreach (Transform t in _modulePanels) {
            if (t == null) continue;
            if (t.gameObject.activeInHierarchy) _visitedModules.Add(t.name);
        }
        return _visitedModules.Count >= 3;
    }

    private void CaptureRenderBaseline() {
        if (_controller == null) return;
        _renderBaseline = _controller.StarRenderMode;
        _renderBaselineCaptured = true;
    }

    /// <summary>第一步检测到场景加载后，重新记录天体数量等基线。</summary>
    private void CaptureBaselines() {
        _bodyBaseline = SandboxPhysicsSystem.Bodies.Count;
        _scenarioMark = SandboxSceneController.ScenarioLoadCount;
    }

    private void ApplyGravityDemo() {
        SetPanelActive("SettingsPanelSandbox", false);
        SandboxTimeManager t = SandboxTimeManager.Instance;
        if (t != null) {
            t.SetUnit(SandboxTimeUnit.DaysPerSecond);
            t.SetSpeed(7.0);
        }
        PullCameraToAu(6.0);
    }

    /// <summary>把相机沿视线方向拉远到指定 AU 之外。</summary>
    private void PullCameraToAu(double au) {
        Camera cam = Camera.main;
        if (cam == null) return;
        double units = au * CelestialScale.AuKm / CelestialScale.KmPerUnit;
        UPB3DCamera orbit = cam.GetComponent<UPB3DCamera>();
        if (orbit != null) {
            orbit.SetDistance((float)units);
            orbit.SyncNow();
        } else {
            cam.transform.position -= cam.transform.forward * (float)units;
        }
    }

    private void ReloadAquarius() {
        // 重新加载场景的同时把引力模拟打开。
        SandboxPhysicsSystem.GravityEnabled = true;
        _controller?.LoadDefaultScenario(SandboxSaveSystem.BuiltinAquarius, true);
        _bodyBaseline = SandboxPhysicsSystem.Bodies.Count;
    }

    // ~~~ 流程 ~~~

    /// <summary>HOME 页“新手引导”按钮：从第一步开始。</summary>
    public void StartNewGuide() {
        BeginTutorial(fromHome: true);
    }

    /// <summary>第一次进入创建宇宙时自动触发：先介绍，再直接进入操作流程。</summary>
    private void StartAutoFirstTime() {
        BeginTutorial(fromHome: false);
    }

    private void BeginTutorial(bool fromHome) {
        if (_controller == null) return;
        _fromHome = fromHome;
        _tutorial = true;
        IsRunning = true;
        _scenarioMark = SandboxSceneController.ScenarioLoadCount;
        _bodyBaseline = SandboxPhysicsSystem.Bodies.Count;
        if (_quitTutorialButton != null) _quitTutorialButton.gameObject.SetActive(true);
        SetVisible(true);
        BringToFront();
        GoToStep(0);
    }

    /// <summary>向导永远压在最上层：HOME 页等面板打开后立刻抢回最后一位。</summary>
    public void BringToFront() {
        if (transform.parent == null) return;
        int last = transform.parent.childCount - 1;
        // “退出教程”按钮在向导显示期间也要一直在最上层，避免被面板挡住。
        if (_quitTutorialButton != null && _quitTutorialButton.gameObject.activeSelf) {
            if (_quitTutorialButton.transform.GetSiblingIndex() < last - 1) {
                _quitTutorialButton.transform.SetAsLastSibling();
            }
        }
        if (transform.GetSiblingIndex() != transform.parent.childCount - 1) {
            transform.SetAsLastSibling();
        }
    }

    private void GoToStep(int index) {
        // 自动首次教程跳过“双击水瓶座一系”，HOME 手动教程跳过开场介绍。
        while (index < _steps.Count) {
            Step candidate = _steps[index];
            if ((candidate.HomeOnly && !_fromHome) ||
                (candidate.AutoOnly && _fromHome)) {
                index++;
                continue;
            }
            break;
        }

        _index = index;
        if (index >= _steps.Count) { Finish(completed: true); return; }

        Step step = _steps[index];
        _side = step.Side;
        _mood = step.Mood;
        SetText(step.Text);

        bool manual = step.Done == null;
        SetNextStepVisible(manual);
        if (_nextStepLabel != null) {
            _nextStepLabel.text = index == _steps.Count - 1 ? "完成" : "下一步";
        }

        BringToFront();
        PlaceGuide(force: true);
        step.OnEnter?.Invoke();
    }

    private void OnNextStepClicked() {
        if (!_tutorial) return;
        if (_index < 0 || _index >= _steps.Count) return;
        // 自动步骤不显示按钮，也不允许用按钮跳过。
        if (_steps[_index].Done != null) return;
        GoToStep(_index + 1);
    }

    private void SetNextStepVisible(bool visible) {
        if (_nextStepButton != null) _nextStepButton.gameObject.SetActive(visible);
    }

    /// <summary>参数面板打开时暂时收起“退出教程”，避免挡住参数操作。</summary>
    private void UpdateQuitButtonVisibility() {
        if (_quitTutorialButton == null) return;
        bool parameterOpen = IsPanelActive("StarParameterPanel") || IsPanelActive("PlanetParameterPanel");
        bool shouldShow = !parameterOpen;
        if (_quitTutorialButton.gameObject.activeSelf != shouldShow) {
            _quitTutorialButton.gameObject.SetActive(shouldShow);
        }
    }

    private void Finish(bool completed = false) {
        _tutorial = false;
        IsRunning = false;
        SetNextStepVisible(false);
        if (_quitTutorialButton != null) _quitTutorialButton.gameObject.SetActive(false);
        Debug.Log("[Guide] finish completed=" + completed);
        SetVisible(false);
    }

    /// <summary>只隐藏画面，不关物体，保证 Update 一直有效。</summary>
    public void SetVisible(bool visible) {
        if (visible && !gameObject.activeSelf) gameObject.SetActive(true);
        bool wasVisible = IsVisible;
        IsVisible = visible;
        if (wasVisible && !visible) OnHidden?.Invoke();
        if (_bodyImage != null) _bodyImage.enabled = visible;
        if (_face != null) _face.gameObject.SetActive(visible);
        if (_bubble != null) _bubble.gameObject.SetActive(visible);
    }

    public bool IsOpen => _bodyImage != null && _bodyImage.enabled;

    private void SetText(string text) {
        if (_bubbleText != null) _bubbleText.text = text;
    }

    private void Update() {
        if (!_tutorial) return;
        UpdateQuitButtonVisibility();
        if (Input.GetKeyDown(KeyCode.I)) { Finish(completed: false); return; }
        if (_index < 0 || _index >= _steps.Count) return;

        // 向导永远浮在最上层。
        BringToFront();

        // 用户在教学里切了新场景（比如重新加载水瓶座一系）时，把热学模拟恢复回去。
        // 第 1 步（双击水瓶座一系）本身要靠 _scenarioMark 判断是否刚加载完，不能在这里清掉。
        if (_index > 1 && SandboxSceneController.ScenarioLoadCount != _scenarioMark) {
            _scenarioMark = SandboxSceneController.ScenarioLoadCount;
            SandboxPhysicsSystem.ThermalEnabled = true;
            SandboxPhysicsSystem.ThermalRadiationLoss = true;
        }

        Step step = _steps[_index];
        if (step.Done != null && step.Done()) {
            GoToStep(_index + 1);
            return;
        }

        UpdateFace();
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) UpdateBubble((canvas.transform as RectTransform).rect.size);
        if (OverlapsPanel(_anchor)) PlaceGuide(force: true);
    }

    // ~~~ 位置 ~~~

    private void PlaceGuide(bool force) {
        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null) return;

        // PlaceGuide 的坐标是按“Canvas 中心为原点”算的；UIResponsiveLayout 可能给
        // 顶层元素换过贴边锚点，这里强制恢复中心锚点，否则向导会被算到屏幕外。
        _root.anchorMin = new Vector2(0.5f, 0.5f);
        _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);

        Vector2 size = canvasRect.rect.size;
        Vector2 half = _face != null && _face.rect.size.sqrMagnitude > 1f
            ? _face.rect.size * 0.5f
            : _root.rect.size * 0.5f;

        Vector2 right = new(size.x * 0.5f - (EdgeMargin + half.x),
                            -size.y * 0.5f + (EdgeMargin + half.y));
        Vector2 left = new(-size.x * 0.5f + (EdgeMargin + half.x),
                           -size.y * 0.5f + (EdgeMargin + half.y));

        Vector2 target = _side == Side.Left ? left : right;
        if (OverlapsPanel(target)) {
            Vector2 other = _side == Side.Left ? right : left;
            if (!OverlapsPanel(other)) target = other;
        }
        _anchor = target;

        Vector2 toCenter = -_anchor.normalized;
        _faceOffset = toCenter * (FaceRadius * 0.6f);
        _root.anchoredPosition = _anchor - _faceOffset;
        UpdateFace();
        UpdateBubble(size);
    }

    private bool OverlapsPanel(Vector2 anchored) {
        Vector2 extent = _face != null && _face.rect.size.sqrMagnitude > 1f
            ? _face.rect.size : _root.rect.size;
        Rect guide = new(anchored - extent * 0.5f, extent);
        foreach (RectTransform panel in _panels) {
            if (panel == null || !panel.gameObject.activeInHierarchy) continue;
            Rect r = new(panel.anchoredPosition - panel.rect.size * 0.5f, panel.rect.size);
            if (guide.Overlaps(r)) return true;
        }
        return false;
    }

    private void UpdateFace() {
        if (_face == null) return;

        float bobAmp, bobSpeed;
        switch (_mood) {
            case Mood.Happy: bobAmp = 12f; bobSpeed = 2.4f; break;
            case Mood.Excited: bobAmp = 18f; bobSpeed = 4.2f; break;
            case Mood.Curious: bobAmp = 10f; bobSpeed = 1.6f; break;
            case Mood.Serious: bobAmp = 4f; bobSpeed = 1.0f; break;
            case Mood.Angry: bobAmp = 16f; bobSpeed = 5.0f; break;
            default: bobAmp = 7f; bobSpeed = 1.6f; break;
        }

        float wobble = Mathf.Sin(Time.unscaledTime * bobSpeed) * bobAmp;
        Vector2 offset = _faceOffset + new Vector2(0f, wobble);
        if (offset.magnitude > FaceRadius) offset = offset.normalized * FaceRadius;
        _face.anchoredPosition = offset;

        bool faceRight = _anchor.x >= 0f;
        _face.localRotation = Quaternion.identity;
        _face.localScale = new Vector3(faceRight ? -1f : 1f, 1f, 1f);

        if (_faceImage != null) {
            // 只换表情贴图，保持原色（不要把眼睛染色）。
            _faceImage.color = Color.white;
            Texture2D tex = ExpressionTexture();
            if (tex != null && _faceImage.texture != tex) _faceImage.texture = tex;
        }
    }

    private Texture2D ExpressionTexture() {
        string name = _mood switch {
            Mood.Happy => "smile",
            Mood.Excited => "smile_eyes_opend",
            Mood.Curious => _anchor.x >= 0f ? "left_confused" : "right_confused",
            Mood.Serious => "bored",
            Mood.Angry => "angry",
            _ => "Normal",
        };
        if (_expressions.TryGetValue(name, out Texture2D tex)) return tex;
        _expressions.TryGetValue("Normal", out Texture2D fallback);
        return fallback;
    }

    private void UpdateBubble(Vector2 canvasSize) {
        if (_bubble == null) return;
        // 统一放在向导正上方 200px（相对根节点）。
        _bubble.anchoredPosition = new Vector2(0f, BubbleAbove);
    }
}
