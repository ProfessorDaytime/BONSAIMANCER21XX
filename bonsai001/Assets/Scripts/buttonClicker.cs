using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    [SerializeField] TreeSkeleton skeleton;

    UIDocument buttonDocument;
    Button trimButton;
    Button waterButton;
    Button pinchButton;
    Button defoliateButton;
    Button defoliateAllButton;
    Button wireButton;
    Button removeWireButton;
    Button rootPruneButton;
    Button quickWinterButton;
    Button pasteButton;
    Button airLayerButton;
    Button placeRockButton;
    Button confirmOrientButton;
    Slider selectionRadiusSlider;

    VisualElement rootHealthPanel;
    Label         rootHealthScoreLabel;
    VisualElement rootHealthSectors;

    // ── Moisture bar ─────────────────────────────────────────────────────────
    VisualElement moistureBarFill;

    // ── Undo indicator ───────────────────────────────────────────────────────
    Label         undoLabel;

    // ── Pause / Settings menu ────────────────────────────────────────────────
    Button        pauseButton;
    VisualElement pauseMenuOverlay;
    Button        resumeButton;

    // Tab buttons & content panels
    Button        tabBtnTime;
    Button        tabBtnGrowth;
    Button        tabBtnIshitsuki;
    VisualElement tabContentTime;
    VisualElement tabContentGrowth;
    VisualElement tabContentIshitsuki;

    // Sliders + value labels
    Slider sliderTimescale;      Label labelTimescale;
    Toggle toggleQuickWinter;
    Button saveButton;
    Label  saveStatusLabel;
    float  saveStatusClearTime = 0f;
    Slider sliderGrowSpeed;      Label labelGrowSpeed;
    Slider sliderSpringLaterals; Label labelSpringLaterals;
    Slider sliderDepthDecay;     Label labelDepthDecay;
    Slider sliderCableRadius;    Label labelCableRadius;
    Slider sliderMinCableAngle;  Label labelMinCableAngle;

    void OnEnable()
    {
        buttonDocument = GetComponent<UIDocument>();

        if(buttonDocument == null){
            Debug.LogError("No button document found");
        }

        var root = buttonDocument.rootVisualElement;

        trimButton          = root.Q("TrimButton")          as Button;
        waterButton         = root.Q("WaterButton")         as Button;
        pinchButton         = root.Q("PinchButton")         as Button;
        defoliateButton     = root.Q("DefoliateButton")     as Button;
        defoliateAllButton  = root.Q("DefoliateAllButton")  as Button;
        wireButton          = root.Q("WireButton")          as Button;
        removeWireButton    = root.Q("RemoveWireButton")    as Button;
        rootPruneButton     = root.Q("RootPruneButton")     as Button;
        quickWinterButton   = root.Q("QuickWinterButton")   as Button;
        pasteButton         = root.Q("PasteButton")         as Button;
        airLayerButton      = root.Q("AirLayerButton")      as Button;
        placeRockButton     = root.Q("PlaceRockButton")     as Button;
        confirmOrientButton = root.Q("ConfirmOrientButton") as Button;
        selectionRadiusSlider = root.Q("SelectionRadiusSlider") as Slider;

        undoLabel            = root.Q("UndoLabel")          as Label;
        moistureBarFill      = root.Q("MoistureBarFill");

        rootHealthPanel      = root.Q("RootHealthPanel");
        rootHealthScoreLabel = root.Q("RootHealthScoreLabel") as Label;
        rootHealthSectors    = root.Q("RootHealthSectors");

        // Build the 8 sector indicator squares
        if (rootHealthSectors != null)
        {
            for (int i = 0; i < 8; i++)
            {
                var sq = new VisualElement();
                sq.name = $"RootHealthSector{i}";
                sq.style.width           = 14;
                sq.style.height          = 14;
                sq.style.borderTopLeftRadius     = 2;
                sq.style.borderTopRightRadius    = 2;
                sq.style.borderBottomLeftRadius  = 2;
                sq.style.borderBottomRightRadius = 2;
                sq.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
                rootHealthSectors.Add(sq);
            }
        }

        // ── Pause menu elements ──────────────────────────────────────────────
        pauseButton      = root.Q("PauseButton")      as Button;
        pauseMenuOverlay = root.Q("PauseMenuOverlay");
        resumeButton     = root.Q("ResumeButton")     as Button;

        tabBtnTime      = root.Q("TabBtnTime")      as Button;
        tabBtnGrowth    = root.Q("TabBtnGrowth")    as Button;
        tabBtnIshitsuki = root.Q("TabBtnIshitsuki") as Button;
        tabContentTime      = root.Q("TabContentTime");
        tabContentGrowth    = root.Q("TabContentGrowth");
        tabContentIshitsuki = root.Q("TabContentIshitsuki");

        saveButton           = root.Q("SaveButton")           as Button;
        saveStatusLabel      = root.Q("SaveStatusLabel")      as Label;

        sliderTimescale      = root.Q("SliderTimescale")      as Slider;
        labelTimescale       = root.Q("LabelTimescale")       as Label;
        toggleQuickWinter    = root.Q("ToggleQuickWinter")    as Toggle;
        sliderGrowSpeed      = root.Q("SliderGrowSpeed")      as Slider;
        labelGrowSpeed       = root.Q("LabelGrowSpeed")       as Label;
        sliderSpringLaterals = root.Q("SliderSpringLaterals") as Slider;
        labelSpringLaterals  = root.Q("LabelSpringLaterals")  as Label;
        sliderDepthDecay     = root.Q("SliderDepthDecay")     as Slider;
        labelDepthDecay      = root.Q("LabelDepthDecay")      as Label;
        sliderCableRadius    = root.Q("SliderCableRadius")    as Slider;
        labelCableRadius     = root.Q("LabelCableRadius")     as Label;
        sliderMinCableAngle  = root.Q("SliderMinCableAngle")  as Slider;
        labelMinCableAngle   = root.Q("LabelMinCableAngle")   as Label;

        // Sync slider starting values from live skeleton/GameManager values
        SyncSlidersFromGame();

        // Wire up gameplay buttons
        trimButton?.RegisterCallback<ClickEvent>(OnTreeButtonClick);
        waterButton?.RegisterCallback<ClickEvent>(OnWaterButtonClick);
        pinchButton?.RegisterCallback<ClickEvent>(OnPinchButtonClick);
        defoliateButton?.RegisterCallback<ClickEvent>(OnDefoliateButtonClick);
        defoliateAllButton?.RegisterCallback<ClickEvent>(OnDefoliateAllButtonClick);
        wireButton?.RegisterCallback<ClickEvent>(OnWireButtonClick);
        removeWireButton?.RegisterCallback<ClickEvent>(OnRemoveWireButtonClick);
        rootPruneButton?.RegisterCallback<ClickEvent>(OnRootPruneButtonClick);
        quickWinterButton?.RegisterCallback<ClickEvent>(OnQuickWinterButtonClick);
        pasteButton?.RegisterCallback<ClickEvent>(OnPasteButtonClick);
        airLayerButton?.RegisterCallback<ClickEvent>(OnAirLayerButtonClick);
        placeRockButton?.RegisterCallback<ClickEvent>(OnPlaceRockButtonClick);
        confirmOrientButton?.RegisterCallback<ClickEvent>(OnConfirmOrientButtonClick);
        selectionRadiusSlider?.RegisterValueChangedCallback(evt => GameManager.selectionRadius = evt.newValue);

        saveButton?.RegisterCallback<ClickEvent>(_ => OnSaveButtonClick());

        // Wire up pause menu
        pauseButton?.RegisterCallback<ClickEvent>(_ => TogglePauseMenu());
        resumeButton?.RegisterCallback<ClickEvent>(_ => TogglePauseMenu());
        tabBtnTime?.RegisterCallback<ClickEvent>(_ => SelectTab(0));
        tabBtnGrowth?.RegisterCallback<ClickEvent>(_ => SelectTab(1));
        tabBtnIshitsuki?.RegisterCallback<ClickEvent>(_ => SelectTab(2));

        sliderTimescale?.RegisterValueChangedCallback(evt => {
            GameManager.TIMESCALE = evt.newValue;
            if (labelTimescale != null) labelTimescale.text = Mathf.RoundToInt(evt.newValue).ToString();
        });
        toggleQuickWinter?.RegisterValueChangedCallback(evt => {
            GameManager.quickWinter = evt.newValue;
            // Keep the HUD quick-winter button in sync
            if (quickWinterButton != null)
            {
                quickWinterButton.style.backgroundColor = evt.newValue
                    ? new StyleColor(new Color(0.75f, 0.75f, 0.75f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
                quickWinterButton.style.color = evt.newValue
                    ? new StyleColor(Color.black)
                    : new StyleColor(new Color(0.78f, 0.78f, 0.78f));
            }
        });
        sliderGrowSpeed?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.BaseGrowSpeed = evt.newValue;
            if (labelGrowSpeed != null) labelGrowSpeed.text = evt.newValue.ToString("F2");
        });
        sliderSpringLaterals?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.SpringLateralChance = evt.newValue;
            if (labelSpringLaterals != null) labelSpringLaterals.text = evt.newValue.ToString("F2");
        });
        sliderDepthDecay?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.DepthSpeedDecay = evt.newValue;
            if (labelDepthDecay != null) labelDepthDecay.text = evt.newValue.ToString("F2");
        });
        sliderCableRadius?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.IshitsukiCableRadiusMultiplier = evt.newValue;
            if (labelCableRadius != null) labelCableRadius.text = evt.newValue.ToString("F2");
        });
        sliderMinCableAngle?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.MinCableAngleDeg = evt.newValue;
            if (labelMinCableAngle != null) labelMinCableAngle.text = Mathf.RoundToInt(evt.newValue) + "°";
        });

        GameManager.OnGameStateChanged += OnGameStateChanged;
    }

    void OnDisable()
    {
        GameManager.OnGameStateChanged -= OnGameStateChanged;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePauseMenu();

        // Trim undo
        // Clear save status label after a few seconds
        if (saveStatusLabel != null && saveStatusClearTime > 0f && Time.realtimeSinceStartup > saveStatusClearTime)
        {
            saveStatusLabel.text = "";
            saveStatusClearTime  = 0f;
        }

        // Defoliation buttons: only active in June/July during growing season
        bool defoliationAllowed = (GameManager.month == 6 || GameManager.month == 7)
                                && (GameManager.Instance.state == GameState.BranchGrow
                                 || GameManager.Instance.state == GameState.TimeGo);
        SetButtonEnabled(defoliateButton,    defoliationAllowed);
        SetButtonEnabled(defoliateAllButton, defoliationAllowed);

        if (skeleton != null && moistureBarFill != null)
        {
            float m = Mathf.Clamp01(skeleton.soilMoisture);
            moistureBarFill.style.width = new StyleLength(new Length(m * 100f, LengthUnit.Percent));
            // Blue when wet, amber when low, red when dry
            Color barCol = m > 0.5f
                ? Color.Lerp(new Color(0.9f, 0.55f, 0.1f), new Color(0.31f, 0.63f, 1f), (m - 0.5f) * 2f)
                : Color.Lerp(new Color(0.75f, 0.1f, 0.1f), new Color(0.9f, 0.55f, 0.1f), m * 2f);
            moistureBarFill.style.backgroundColor = new StyleColor(barCol);
        }

        if (skeleton != null)
        {
            bool canUndo = skeleton.CanUndo;

            if (undoLabel != null)
            {
                undoLabel.style.display = canUndo ? DisplayStyle.Flex : DisplayStyle.None;
                if (canUndo)
                    undoLabel.text = $"<< UNDO  Ctrl+Z  ({skeleton.UndoTimeRemaining:F0}s)";
            }

            if (canUndo && Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
                skeleton.UndoLastTrim();
        }
    }

    // ── Pause menu helpers ───────────────────────────────────────────────────

    void TogglePauseMenu()
    {
        if (pauseMenuOverlay == null) return;
        bool opening = pauseMenuOverlay.style.display == DisplayStyle.None;
        pauseMenuOverlay.style.display = opening ? DisplayStyle.Flex : DisplayStyle.None;

        if (opening)
        {
            GameManager.Instance.TogglePause();
            SyncSlidersFromGame();  // refresh values each time menu opens
            SelectTab(0);           // always open on Time tab
        }
        else
        {
            // Only unpause if we are in GamePause (don't unpause if we were
            // already paused for another reason before opening the menu)
            if (GameManager.Instance.state == GameState.GamePause)
                GameManager.Instance.TogglePause();
        }
    }

    void SelectTab(int index)
    {
        // Content panels
        if (tabContentTime      != null) tabContentTime.style.display      = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentGrowth    != null) tabContentGrowth.style.display    = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentIshitsuki != null) tabContentIshitsuki.style.display = index == 2 ? DisplayStyle.Flex : DisplayStyle.None;

        // Tab button highlight (active = gold bg + dark text, inactive = dark bg + grey text)
        SetTabStyle(tabBtnTime,      index == 0);
        SetTabStyle(tabBtnGrowth,    index == 1);
        SetTabStyle(tabBtnIshitsuki, index == 2);
    }

    void SetTabStyle(Button btn, bool active)
    {
        if (btn == null) return;
        btn.style.backgroundColor = active
            ? new StyleColor(new Color(0.898f, 0.702f, 0.086f))   // gold
            : new StyleColor(new Color(0.157f, 0.157f, 0.157f));  // dark
        btn.style.color = active
            ? new StyleColor(new Color(0.04f, 0.04f, 0.04f))      // near-black
            : new StyleColor(new Color(0.627f, 0.627f, 0.627f));  // grey
        btn.style.unityFontStyleAndWeight = active
            ? new StyleEnum<FontStyle>(FontStyle.Bold)
            : new StyleEnum<FontStyle>(FontStyle.Normal);
    }

    /// <summary>Push current live values into the menu sliders on open.</summary>
    void SyncSlidersFromGame()
    {
        if (sliderTimescale != null)
        {
            sliderTimescale.SetValueWithoutNotify(GameManager.TIMESCALE);
            if (labelTimescale != null) labelTimescale.text = Mathf.RoundToInt(GameManager.TIMESCALE).ToString();
        }
        if (toggleQuickWinter != null)
            toggleQuickWinter.SetValueWithoutNotify(GameManager.quickWinter);

        if (skeleton == null) return;

        if (sliderGrowSpeed != null)
        {
            sliderGrowSpeed.SetValueWithoutNotify(skeleton.BaseGrowSpeed);
            if (labelGrowSpeed != null) labelGrowSpeed.text = skeleton.BaseGrowSpeed.ToString("F2");
        }
        if (sliderSpringLaterals != null)
        {
            sliderSpringLaterals.SetValueWithoutNotify(skeleton.SpringLateralChance);
            if (labelSpringLaterals != null) labelSpringLaterals.text = skeleton.SpringLateralChance.ToString("F2");
        }
        if (sliderDepthDecay != null)
        {
            sliderDepthDecay.SetValueWithoutNotify(skeleton.DepthSpeedDecay);
            if (labelDepthDecay != null) labelDepthDecay.text = skeleton.DepthSpeedDecay.ToString("F2");
        }
        if (sliderCableRadius != null)
        {
            sliderCableRadius.SetValueWithoutNotify(skeleton.IshitsukiCableRadiusMultiplier);
            if (labelCableRadius != null) labelCableRadius.text = skeleton.IshitsukiCableRadiusMultiplier.ToString("F2");
        }
        if (sliderMinCableAngle != null)
        {
            sliderMinCableAngle.SetValueWithoutNotify(skeleton.MinCableAngleDeg);
            if (labelMinCableAngle != null) labelMinCableAngle.text = Mathf.RoundToInt(skeleton.MinCableAngleDeg) + "°";
        }
    }

    // ── Button swap logic ────────────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        bool inRootLift   = GameManager.IsRootLiftActive(state);
        bool inRockPlace  = state == GameState.RockPlace;
        bool inTreeOrient = state == GameState.TreeOrient;
        bool inRootPrune  = state == GameState.RootPrune;

        // Air Layer slot → Place Rock while in any root-lift state
        if (airLayerButton   != null) airLayerButton.style.display   = inRootLift ? DisplayStyle.None : DisplayStyle.Flex;
        if (placeRockButton  != null) placeRockButton.style.display  = inRootPrune ? DisplayStyle.Flex : DisplayStyle.None;

        // Paste slot → Confirm Orientation while placing rock or orienting tree
        bool showConfirm = inRockPlace || inTreeOrient;
        if (pasteButton        != null) pasteButton.style.display        = showConfirm ? DisplayStyle.None : DisplayStyle.Flex;
        if (confirmOrientButton!= null) confirmOrientButton.style.display= showConfirm ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    void OnSaveButtonClick()
    {
        var leafMgr = skeleton != null ? skeleton.GetComponent<LeafManager>() : null;
        SaveManager.Save(skeleton, leafMgr);
        if (saveStatusLabel != null)
        {
            saveStatusLabel.text = $"Saved  ({System.DateTime.Now:HH:mm:ss})";
            saveStatusClearTime  = Time.realtimeSinceStartup + 4f;
        }
    }

    public void OnTreeButtonClick(ClickEvent evt){
        ToolManager.Instance.SelectTool(ToolType.SmallClippers);
        AudioManager.Instance.PlaySFX("Trim");
    }

    public void OnPinchButtonClick(ClickEvent evt){
        ToolManager.Instance.SelectTool(ToolType.Pinch);
        AudioManager.Instance.PlaySFX("Trim");
    }

    public void OnDefoliateButtonClick(ClickEvent evt){
        if (!IsDefoliationAllowed()) return;
        ToolManager.Instance.SelectTool(ToolType.Defoliate);
    }

    public void OnDefoliateAllButtonClick(ClickEvent evt){
        if (!IsDefoliationAllowed()) return;
        ToolManager.Instance.ClearTool();
        var leafMgr = skeleton != null ? skeleton.GetComponent<LeafManager>() : null;
        leafMgr?.DefoliateAll();
    }

    bool IsDefoliationAllowed() =>
        (GameManager.month == 6 || GameManager.month == 7)
        && (GameManager.Instance.state == GameState.BranchGrow
         || GameManager.Instance.state == GameState.TimeGo);

    /// <summary>Visually dims a button and blocks pointer events when disabled.</summary>
    static void SetButtonEnabled(Button btn, bool enabled)
    {
        if (btn == null) return;
        btn.style.opacity = enabled ? 1f : 0.35f;
        btn.pickingMode   = enabled ? PickingMode.Position : PickingMode.Ignore;
    }

    public void OnWaterButtonClick(ClickEvent evt){
        AudioManager.Instance.PlaySFX("Water");

        if (GameManager.Instance.state == GameState.Idle && GameManager.waterings == -1)
        {
            // First time: plant & start growing
            GameManager.Instance.UpdateGameState(GameState.Water);
            GameManager.Instance.UpdateGameState(GameState.BranchGrow);
            GameManager.waterings++;
            Debug.Log("The tree has been watered");
            return;
        }

        var s = GameManager.Instance.state;
        if (s == GameState.Idle || s == GameState.BranchGrow || s == GameState.TimeGo)
            skeleton?.Water();
    }

    public void OnWireButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.Wire);
    }

    public void OnRemoveWireButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.RemoveWire);
    }

    public void OnRootPruneButtonClick(ClickEvent evt)
    {
        GameManager.Instance.ToggleRootPrune();
    }

    void UpdateRootHealthDisplay()
    {
        if (skeleton == null || rootHealthScoreLabel == null || rootHealthSectors == null) return;

        int score = Mathf.RoundToInt(skeleton.RootHealthScore);
        rootHealthScoreLabel.text = score.ToString();

        float[] coverage = skeleton.RootHealthSectorCoverage;
        for (int i = 0; i < 8 && i < rootHealthSectors.childCount; i++)
        {
            float t   = i < coverage.Length ? coverage[i] : 0f;
            var   col = Color.Lerp(new Color(0.15f, 0.15f, 0.15f), new Color(0.3f, 0.75f, 0.3f), t);
            rootHealthSectors[i].style.backgroundColor = new StyleColor(col);
        }
    }

    public void OnQuickWinterButtonClick(ClickEvent evt)
    {
        GameManager.quickWinter = !GameManager.quickWinter;
        quickWinterButton.style.backgroundColor = GameManager.quickWinter
            ? new StyleColor(new UnityEngine.Color(0.75f, 0.75f, 0.75f))
            : new StyleColor(new UnityEngine.Color(0.25f, 0.25f, 0.25f));
        quickWinterButton.style.color = GameManager.quickWinter
            ? new StyleColor(UnityEngine.Color.black)
            : new StyleColor(new UnityEngine.Color(0.78f, 0.78f, 0.78f));
        // Keep menu toggle in sync
        toggleQuickWinter?.SetValueWithoutNotify(GameManager.quickWinter);
    }

    public void OnPasteButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.Paste);
    }

    public void OnAirLayerButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.AirLayer);
    }

    public void OnPlaceRockButtonClick(ClickEvent evt)
    {
        GameManager.Instance.UpdateGameState(GameState.RockPlace);
    }

    public void OnConfirmOrientButtonClick(ClickEvent evt)
    {
        GameManager.Instance.ConfirmRockOrient();
    }
}
