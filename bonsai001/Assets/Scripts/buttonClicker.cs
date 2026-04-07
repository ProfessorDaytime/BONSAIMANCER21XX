using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    [SerializeField] TreeSkeleton skeleton;

    // ── Tree Death ────────────────────────────────────────────────────────────
    VisualElement treeDeadOverlay;
    Label         treeDeadCauseLabel;
    Button        deadLoadButton;
    Button        deadRestartButton;
    Label         treeDangerBanner;

    // ── Species Selection ────────────────────────────────────────────────────
    // Populate this array in the Inspector by dragging in every .asset from
    // Assets/Scripts/Tree/Species/  (order = order shown in the picker).
    [SerializeField] TreeSpecies[] availableSpecies;

    VisualElement speciesSelectOverlay;
    VisualElement speciesListContainer;
    VisualElement speciesSortBar;
    Button        speciesConfirmButton;
    TreeSpecies   selectedSpecies;
    VisualElement selectedSpeciesCard;

    enum SpeciesSortMode { None, Growth, Water, Care, Soil }
    SpeciesSortMode currentSort    = SpeciesSortMode.None;
    bool            sortDescending = false;
    Button          activeSortBtn;

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

    // ── Fertilizer ───────────────────────────────────────────────────────────
    Button        fertilizeButton;
    VisualElement nutrientBarFill;

    // ── Weeds / Fungal ────────────────────────────────────────────────────────
    Button        herbicideButton;
    Button        fungicideButton;

    // ── Soil / Repot ──────────────────────────────────────────────────────────
    VisualElement repotPanel;
    Label         soilDegradationLabel;
    VisualElement degradationBarFill;
    Label         soilSaturationLabel;
    VisualElement saturationBarFill;
    Button        repotClassicButton;
    Button        repotFreeDrainButton;
    Button        repotMoistButton;
    Button        repotAcidicButton;

    // ── Undo indicator ───────────────────────────────────────────────────────
    Label         undoLabel;

    // ── Pause / Settings menu ────────────────────────────────────────────────
    Label         speciesLabel;
    Button        pauseButton;
    VisualElement pauseMenuOverlay;
    Button        resumeButton;

    // Tab buttons & content panels
    Button        tabBtnTime;
    Button        tabBtnGrowth;
    Button        tabBtnIshitsuki;
    Button        tabBtnDebug;
    VisualElement tabContentTime;
    VisualElement tabContentGrowth;
    VisualElement tabContentIshitsuki;
    VisualElement tabContentDebug;

    // Debug tab controls
    Toggle        toggleAutoWater;
    Toggle        toggleAutoFertilize;

    // Sliders + value labels
    Slider sliderTimescale;      Label labelTimescale;
    Toggle toggleQuickWinter;
    Button saveButton;
    Button loadButton;
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
        fertilizeButton      = root.Q("FertilizeButton")    as Button;
        nutrientBarFill      = root.Q("NutrientBarFill");
        herbicideButton      = root.Q("HerbicideButton")    as Button;
        fungicideButton      = root.Q("FungicideButton")    as Button;

        repotPanel            = root.Q("RepotPanel");
        soilDegradationLabel  = root.Q("SoilDegradationLabel") as Label;
        degradationBarFill    = root.Q("DegradationBarFill");
        soilSaturationLabel   = root.Q("SoilSaturationLabel")  as Label;
        saturationBarFill     = root.Q("SaturationBarFill");
        repotClassicButton    = root.Q("RepotClassicButton")   as Button;
        repotFreeDrainButton  = root.Q("RepotFreeDrainButton") as Button;
        repotMoistButton      = root.Q("RepotMoistButton")     as Button;
        repotAcidicButton     = root.Q("RepotAcidicButton")    as Button;

        rootHealthPanel      = root.Q("RootHealthPanel");
        rootHealthScoreLabel = root.Q("RootHealthScoreLabel") as Label;
        rootHealthSectors    = root.Q("RootHealthSectors");

        // ── Tree death ───────────────────────────────────────────────────────
        treeDeadOverlay    = root.Q("TreeDeadOverlay");
        treeDeadCauseLabel = root.Q("TreeDeadCauseLabel") as Label;
        deadLoadButton     = root.Q("DeadLoadButton")     as Button;
        deadRestartButton  = root.Q("DeadRestartButton")  as Button;
        treeDangerBanner   = root.Q("TreeDangerBanner")   as Label;
        deadLoadButton?.RegisterCallback<ClickEvent>(_ => OnDeadLoadClick());
        deadRestartButton?.RegisterCallback<ClickEvent>(_ => OnDeadRestartClick());

        // ── Species selection ────────────────────────────────────────────────
        speciesSelectOverlay  = root.Q("SpeciesSelectOverlay");
        speciesListContainer  = root.Q("SpeciesListContainer");
        speciesSortBar        = root.Q("SpeciesSortBar");
        speciesConfirmButton  = root.Q("SpeciesConfirmButton") as Button;
        speciesConfirmButton?.RegisterCallback<ClickEvent>(_ => OnSpeciesConfirmClick());
        BuildSortBar();
        BuildSpeciesCards();

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
        speciesLabel     = root.Q("SpeciesLabel")      as Label;
        pauseButton      = root.Q("PauseButton")      as Button;
        pauseMenuOverlay = root.Q("PauseMenuOverlay");
        resumeButton     = root.Q("ResumeButton")     as Button;

        tabBtnTime      = root.Q("TabBtnTime")      as Button;
        tabBtnGrowth    = root.Q("TabBtnGrowth")    as Button;
        tabBtnIshitsuki = root.Q("TabBtnIshitsuki") as Button;
        tabBtnDebug     = root.Q("TabBtnDebug")     as Button;
        tabContentTime      = root.Q("TabContentTime");
        tabContentGrowth    = root.Q("TabContentGrowth");
        tabContentIshitsuki = root.Q("TabContentIshitsuki");
        tabContentDebug     = root.Q("TabContentDebug");

        toggleAutoWater     = root.Q("ToggleAutoWater")     as Toggle;
        toggleAutoFertilize = root.Q("ToggleAutoFertilize") as Toggle;

        saveButton           = root.Q("SaveButton")           as Button;
        loadButton           = root.Q("LoadButton")           as Button;
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
        loadButton?.RegisterCallback<ClickEvent>(_ => OnLoadButtonClick());

        // Wire up pause menu
        pauseButton?.RegisterCallback<ClickEvent>(_ => TogglePauseMenu());
        resumeButton?.RegisterCallback<ClickEvent>(_ => TogglePauseMenu());
        tabBtnTime?.RegisterCallback<ClickEvent>(_ => SelectTab(0));
        tabBtnGrowth?.RegisterCallback<ClickEvent>(_ => SelectTab(1));
        tabBtnIshitsuki?.RegisterCallback<ClickEvent>(_ => SelectTab(2));
        tabBtnDebug?.RegisterCallback<ClickEvent>(_ => SelectTab(3));

        fertilizeButton?.RegisterCallback<ClickEvent>(_ => OnFertilizeButtonClick());
        herbicideButton?.RegisterCallback<ClickEvent>(_ => OnHerbicideButtonClick());
        fungicideButton?.RegisterCallback<ClickEvent>(_ => OnFungicideButtonClick());

        repotClassicButton?.RegisterCallback<ClickEvent>(_ => OnRepotButtonClick(PotSoil.SoilPreset.ClassicBonsai));
        repotFreeDrainButton?.RegisterCallback<ClickEvent>(_ => OnRepotButtonClick(PotSoil.SoilPreset.FreeDraining));
        repotMoistButton?.RegisterCallback<ClickEvent>(_ => OnRepotButtonClick(PotSoil.SoilPreset.MoistureRetaining));
        repotAcidicButton?.RegisterCallback<ClickEvent>(_ => OnRepotButtonClick(PotSoil.SoilPreset.Acidic));

        toggleAutoWater?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.autoWaterEnabled = evt.newValue;
        });

        toggleAutoFertilize?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.autoFertilizeEnabled = evt.newValue;
        });

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

        // Sync overlay visibility to whatever state was set before OnEnable ran
        if (speciesSelectOverlay != null)
            speciesSelectOverlay.style.display =
                GameManager.Instance != null && GameManager.Instance.state == GameState.SpeciesSelect
                    ? DisplayStyle.Flex : DisplayStyle.None;
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

        // Nutrient bar: 0–2 maps to 0–100%; green → amber → red as reserves drop, orange-red when over-fertilized
        if (skeleton != null && nutrientBarFill != null)
        {
            float n   = skeleton.nutrientReserve;
            float pct = Mathf.Clamp01(n / 2f);
            nutrientBarFill.style.width = new StyleLength(new Length(pct * 100f, LengthUnit.Percent));
            Color nCol;
            if (n > 1.5f)
                nCol = new Color(0.9f, 0.4f, 0.1f);       // orange — over-fertilized
            else if (n > 0.6f)
                nCol = new Color(0.25f, 0.75f, 0.25f);    // green — healthy
            else if (n > 0.2f)
                nCol = new Color(0.85f, 0.65f, 0.1f);     // amber — getting low
            else
                nCol = new Color(0.75f, 0.1f, 0.1f);      // red — depleted
            nutrientBarFill.style.backgroundColor = new StyleColor(nCol);
        }

        // Fertilize button: dim in winter months (fertilizing dormant trees burns roots)
        if (skeleton != null && fertilizeButton != null)
        {
            int m2 = GameManager.month;
            bool isWinter = m2 == 11 || m2 == 12 || m2 == 1 || m2 == 2;
            SetButtonEnabled(fertilizeButton, !isWinter);
        }

        // Herbicide button: dim when no weeds present
        if (herbicideButton != null)
        {
            bool hasWeeds = WeedManager.Instance != null && WeedManager.Instance.ActiveWeedCount > 0;
            SetButtonEnabled(herbicideButton, hasWeeds);
        }

        // Fungicide button: dim when no infected nodes exist
        if (fungicideButton != null && skeleton != null)
        {
            bool hasInfection = false;
            foreach (var n in skeleton.allNodes) { if (n.fungalLoad > 0.05f) { hasInfection = true; break; } }
            SetButtonEnabled(fungicideButton, hasInfection);
        }

        // Soil / Repot panel: show in RootPrune mode, update bars
        bool inRootPrune = GameManager.Instance.state == GameState.RootPrune;
        if (repotPanel != null)
            repotPanel.style.display = inRootPrune ? DisplayStyle.Flex : DisplayStyle.None;

        if (inRootPrune && skeleton != null)
        {
            var potSoil = skeleton.GetComponent<PotSoil>();
            if (potSoil != null)
            {
                float deg = potSoil.soilDegradation;
                float sat = potSoil.saturationLevel;

                if (soilDegradationLabel != null)
                    soilDegradationLabel.text = $"Compaction: {deg * 100f:F0}%";
                if (degradationBarFill != null)
                {
                    degradationBarFill.style.width = new StyleLength(new Length(deg * 100f, LengthUnit.Percent));
                    // Colour: light amber → deep orange as it degrades
                    var col = Color.Lerp(new Color(0.4f, 0.7f, 0.3f), new Color(0.75f, 0.35f, 0.1f), deg);
                    degradationBarFill.style.backgroundColor = new StyleColor(col);
                }

                if (soilSaturationLabel != null)
                    soilSaturationLabel.text = $"Saturation: {sat * 100f:F0}%";
                if (saturationBarFill != null)
                {
                    saturationBarFill.style.width = new StyleLength(new Length(sat * 100f, LengthUnit.Percent));
                    var col = sat > 0.5f
                        ? Color.Lerp(new Color(0.2f, 0.5f, 0.9f), new Color(0.6f, 0.1f, 0.8f), (sat - 0.5f) * 2f)
                        : Color.Lerp(new Color(0.3f, 0.6f, 1.0f), new Color(0.2f, 0.5f, 0.9f), sat * 2f);
                    saturationBarFill.style.backgroundColor = new StyleColor(col);
                }
            }
        }

        // Tree danger banner: show when tree is stressed but not yet dead
        if (treeDangerBanner != null && skeleton != null)
            treeDangerBanner.style.display = skeleton.treeInDanger && skeleton.treeDeathEnabled
                ? DisplayStyle.Flex : DisplayStyle.None;

        // Auto-water flash: pulse water button background when auto-water fires
        if (skeleton != null && skeleton.autoWaterJustFired)
        {
            skeleton.autoWaterJustFired = false;
            StartCoroutine(FlashWaterButton());
        }

        // Auto-fertilize flash: pulse fertilize button when auto-fertilize fires
        if (skeleton != null && skeleton.autoFertilizeJustFired)
        {
            skeleton.autoFertilizeJustFired = false;
            StartCoroutine(FlashFertilizeButton());
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
        if (tabContentDebug     != null) tabContentDebug.style.display     = index == 3 ? DisplayStyle.Flex : DisplayStyle.None;

        // Tab button highlight (active = gold bg + dark text, inactive = dark bg + grey text)
        SetTabStyle(tabBtnTime,      index == 0);
        SetTabStyle(tabBtnGrowth,    index == 1);
        SetTabStyle(tabBtnIshitsuki, index == 2);
        SetTabStyle(tabBtnDebug,     index == 3);
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
        if (speciesLabel != null && skeleton != null)
            speciesLabel.text = skeleton.SpeciesName;

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
        // Species select overlay: shown only during species picker state
        if (speciesSelectOverlay != null)
            speciesSelectOverlay.style.display = state == GameState.SpeciesSelect
                ? DisplayStyle.Flex : DisplayStyle.None;

        // Tree dead overlay
        if (treeDeadOverlay != null)
        {
            bool dead = state == GameState.TreeDead;
            treeDeadOverlay.style.display = dead ? DisplayStyle.Flex : DisplayStyle.None;
            if (dead && treeDeadCauseLabel != null)
                treeDeadCauseLabel.text = string.IsNullOrEmpty(TreeSkeleton.LastDeathCause)
                    ? "" : $"Cause: {TreeSkeleton.LastDeathCause}  |  Year {GameManager.year}";
        }

        bool inRootLift   = GameManager.IsRootLiftActive(state);
        bool inRockPlace  = state == GameState.RockPlace;
        bool inTreeOrient = state == GameState.TreeOrient;
        bool inRootPrune  = state == GameState.RootPrune;

        // Clear any active tool when entering rock placement or tree orientation —
        // clicking the tree in those states should grab/rotate, not trim/wire/paste.
        if (inRockPlace || inTreeOrient)
            ToolManager.Instance.ClearTool();

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

    void OnLoadButtonClick()
    {
        var leafMgr = skeleton != null ? skeleton.GetComponent<LeafManager>() : null;
        bool ok = SaveManager.Load(skeleton, leafMgr);
        if (saveStatusLabel != null)
        {
            saveStatusLabel.text = ok ? $"Loaded  ({System.DateTime.Now:HH:mm:ss})" : "No save file found.";
            saveStatusClearTime  = Time.realtimeSinceStartup + 4f;
        }
        if (ok) TogglePauseMenu(); // close menu so the player sees the loaded tree
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

    IEnumerator FlashWaterButton()
    {
        if (waterButton == null) yield break;
        // Pulse: dark → light → dark over 0.15 s real-time to show auto-water fired
        Color dark   = new Color(0.25f, 0.25f, 0.25f);
        Color light  = new Color(0.75f, 0.75f, 0.75f);
        Color normal = new Color(0.17f, 0.17f, 0.17f);
        float t = 0f;
        while (t < 0.15f)
        {
            float p = Mathf.PingPong(t * (1f / 0.075f), 1f);
            waterButton.style.backgroundColor = new StyleColor(Color.Lerp(dark, light, p));
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        waterButton.style.backgroundColor = new StyleColor(normal);
    }

    IEnumerator FlashFertilizeButton()
    {
        if (fertilizeButton == null) yield break;
        Color dark   = new Color(0.1f, 0.35f, 0.1f);
        Color light  = new Color(0.35f, 0.75f, 0.35f);
        Color normal = new Color(0.17f, 0.17f, 0.17f);
        float t = 0f;
        while (t < 0.15f)
        {
            float p = Mathf.PingPong(t * (1f / 0.075f), 1f);
            fertilizeButton.style.backgroundColor = new StyleColor(Color.Lerp(dark, light, p));
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        fertilizeButton.style.backgroundColor = new StyleColor(normal);
    }

    /// <summary>Visually dims a button and blocks pointer events when disabled.</summary>
    static void SetButtonEnabled(Button btn, bool enabled)
    {
        if (btn == null) return;
        btn.style.opacity = enabled ? 1f : 0.35f;
        btn.pickingMode   = enabled ? PickingMode.Position : PickingMode.Ignore;
    }

    void OnFertilizeButtonClick()
    {
        if (skeleton == null) return;
        var s = GameManager.Instance.state;
        if (s == GameState.Idle || s == GameState.BranchGrow || s == GameState.TimeGo ||
            s == GameState.LeafFall || s == GameState.LeafGrow)
        {
            skeleton.Fertilize();
        }
    }

    void OnHerbicideButtonClick()
    {
        WeedManager.Instance?.HerbicideAll();
    }

    void OnRepotButtonClick(PotSoil.SoilPreset preset)
    {
        if (skeleton == null) return;
        var potSoil = skeleton.GetComponent<PotSoil>();
        if (potSoil == null) return;
        potSoil.Repot(skeleton, preset);
        // Refresh leaf tint — repot stress may have changed node health
        skeleton.GetComponent<LeafManager>()?.RefreshFungalTint(skeleton);
    }

    void OnDeadLoadClick()
    {
        Time.timeScale = 1f;
        var leafMgr = skeleton != null ? skeleton.GetComponent<LeafManager>() : null;
        bool ok = SaveManager.Load(skeleton, leafMgr);
        if (ok)
        {
            skeleton?.GetComponent<TreeMeshBuilder>()?.SetDeadTint(false);
            skeleton.treeInDanger = false;
            skeleton.consecutiveCriticalSeasons = 0;
            if (GameManager.Instance.state == GameState.TreeDead)
                GameManager.Instance.UpdateGameState(GameState.Idle);
        }
    }

    void OnDeadRestartClick()
    {
        Time.timeScale = 1f;
        if (skeleton != null)
        {
            skeleton.GetComponent<TreeMeshBuilder>()?.SetDeadTint(false);
            skeleton.treeInDanger = false;
            skeleton.consecutiveCriticalSeasons = 0;
        }
        GameManager.Instance.UpdateGameState(GameState.SpeciesSelect);
    }

    void OnFungicideButtonClick()
    {
        if (skeleton == null) return;
        var s = GameManager.Instance.state;
        if (s == GameState.Idle || s == GameState.BranchGrow || s == GameState.TimeGo ||
            s == GameState.LeafFall || s == GameState.LeafGrow)
        {
            skeleton.ApplyFungicide();
            // Refresh leaf tint immediately so colour clears without waiting for next season
            skeleton.GetComponent<LeafManager>()?.RefreshFungalTint(skeleton);
        }
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

    // ── Species Selection ────────────────────────────────────────────────────

    void BuildSortBar()
    {
        if (speciesSortBar == null) return;
        speciesSortBar.Clear();

        var label = new Label("Sort: ");
        label.style.color    = new StyleColor(new Color(0.55f, 0.65f, 0.55f));
        label.style.fontSize = 11;
        label.style.alignSelf = Align.Center;
        label.style.marginRight = 6;
        speciesSortBar.Add(label);

        foreach (SpeciesSortMode mode in System.Enum.GetValues(typeof(SpeciesSortMode)))
        {
            if (mode == SpeciesSortMode.None) continue;
            var m = mode; // capture for lambda
            var sortBtn = new Button();
            sortBtn.text = mode.ToString();
            sortBtn.style.height       = 26;
            sortBtn.style.paddingLeft  = sortBtn.style.paddingRight = 10;
            sortBtn.style.marginRight  = 5;
            sortBtn.style.fontSize     = 11;
            sortBtn.style.borderTopLeftRadius    = sortBtn.style.borderTopRightRadius    = 4;
            sortBtn.style.borderBottomLeftRadius = sortBtn.style.borderBottomRightRadius = 4;
            sortBtn.style.borderTopWidth = sortBtn.style.borderBottomWidth =
                sortBtn.style.borderLeftWidth = sortBtn.style.borderRightWidth = 1;
            SetSortBtnStyle(sortBtn, false, false);
            sortBtn.RegisterCallback<ClickEvent>(_ => OnSortButtonClick(m, sortBtn));
            speciesSortBar.Add(sortBtn);
        }
    }

    void OnSortButtonClick(SpeciesSortMode mode, Button btn)
    {
        if (currentSort == mode)
            sortDescending = !sortDescending;   // toggle direction on second click
        else
        {
            if (activeSortBtn != null) SetSortBtnStyle(activeSortBtn, false, false);
            currentSort = mode;
            sortDescending = false;
        }
        activeSortBtn = btn;
        SetSortBtnStyle(btn, true, sortDescending);
        BuildSpeciesCards();
    }

    // Maps sort mode → its button's base label text (set once in BuildSortBar)
    readonly System.Collections.Generic.Dictionary<SpeciesSortMode, (Button btn, string label)>
        sortBtnMap = new System.Collections.Generic.Dictionary<SpeciesSortMode, (Button, string)>();

    void SetSortBtnStyle(Button btn, bool active, bool descending)
    {
        if (btn == null) return;
        btn.style.backgroundColor = active
            ? new StyleColor(new Color(0.898f, 0.702f, 0.086f))
            : new StyleColor(new Color(0.15f, 0.20f, 0.15f));
        btn.style.color = active
            ? new StyleColor(new Color(0.06f, 0.06f, 0.06f))
            : new StyleColor(new Color(0.60f, 0.70f, 0.60f));
        var borderCol = active
            ? new StyleColor(new Color(0.898f, 0.702f, 0.086f))
            : new StyleColor(new Color(0.30f, 0.38f, 0.30f));
        btn.style.borderTopColor = btn.style.borderBottomColor =
            btn.style.borderLeftColor = btn.style.borderRightColor = borderCol;
    }

    TreeSpecies[] SortedSpecies()
    {
        if (availableSpecies == null) return new TreeSpecies[0];
        var list = new System.Collections.Generic.List<TreeSpecies>(availableSpecies);
        list.RemoveAll(s => s == null);

        System.Comparison<TreeSpecies> cmp = currentSort switch
        {
            SpeciesSortMode.Growth => (a, b) => a.baseGrowSpeed.CompareTo(b.baseGrowSpeed),
            SpeciesSortMode.Water  => (a, b) => a.drainRatePerDay.CompareTo(b.drainRatePerDay),
            SpeciesSortMode.Care   => (a, b) => CareScore(a).CompareTo(CareScore(b)),
            SpeciesSortMode.Soil   => (a, b) => a.preferredWaterRetention.CompareTo(b.preferredWaterRetention),
            _                      => null,
        };

        if (cmp != null)
        {
            list.Sort(cmp);
            if (sortDescending) list.Reverse();
        }
        return list.ToArray();
    }

    static float CareScore(TreeSpecies sp) =>
        sp.repotTolerance * 0.6f + Mathf.Clamp01(1f - sp.woundDrainRate * 10f) * 0.4f;

    void BuildSpeciesCards()
    {
        if (speciesListContainer == null || availableSpecies == null) return;
        speciesListContainer.Clear();

        // Re-select the previously selected card after rebuild
        VisualElement reselect = null;
        foreach (var sp in SortedSpecies())
        {
            var card = MakeSpeciesCard(sp);
            speciesListContainer.Add(card);
            if (sp == selectedSpecies) reselect = card;
        }

        if (reselect != null && selectedSpecies != null)
        {
            selectedSpeciesCard = reselect;
            reselect.style.backgroundColor = new StyleColor(new Color(0.14f, 0.19f, 0.11f));
            SetCardBorderColor(reselect, new Color(0.898f, 0.702f, 0.086f));
        }
    }

    VisualElement MakeSpeciesCard(TreeSpecies sp)
    {
        var card = new VisualElement();
        card.style.flexDirection   = FlexDirection.Row;
        card.style.alignItems      = Align.Center;
        card.style.paddingTop      = card.style.paddingBottom = 10;
        card.style.paddingLeft     = card.style.paddingRight  = 14;
        card.style.marginBottom    = 5;
        card.style.borderTopLeftRadius    = card.style.borderTopRightRadius    = 6;
        card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
        card.style.backgroundColor = new StyleColor(new Color(0.10f, 0.14f, 0.10f));
        card.style.borderTopWidth = card.style.borderBottomWidth =
            card.style.borderLeftWidth = card.style.borderRightWidth = 1;
        SetCardBorderColor(card, new Color(0.25f, 0.25f, 0.22f));

        // Left column: common name + scientific name
        var names = new VisualElement();
        names.style.flexGrow       = 1;
        names.style.flexDirection  = FlexDirection.Column;
        names.style.justifyContent = Justify.Center;

        var nameLabel = new Label(sp.speciesName);
        nameLabel.style.fontSize = 15;
        nameLabel.style.color    = new StyleColor(new Color(0.92f, 0.88f, 0.78f));
        nameLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);

        var sciLabel = new Label(sp.scientificName);
        sciLabel.style.fontSize = 11;
        sciLabel.style.color    = new StyleColor(new Color(0.52f, 0.65f, 0.52f));
        sciLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Italic);

        names.Add(nameLabel);
        names.Add(sciLabel);

        // Right column: stat chips
        var chips = new VisualElement();
        chips.style.flexDirection = FlexDirection.Row;
        chips.style.alignItems    = Align.Center;
        chips.style.flexShrink    = 0;

        chips.Add(MakeChip(GrowthLabel(sp.baseGrowSpeed),         GrowthColor(sp.baseGrowSpeed)));
        chips.Add(MakeChip(WaterLabel(sp.drainRatePerDay),         WaterColor(sp.drainRatePerDay)));
        chips.Add(MakeChip(CareLabel(sp.repotTolerance, sp.woundDrainRate),
                                     CareColor(sp.repotTolerance, sp.woundDrainRate)));
        chips.Add(MakeChip(SoilLabel(sp.preferredWaterRetention),  SoilColor(sp.preferredWaterRetention)));

        card.Add(names);
        card.Add(chips);

        card.RegisterCallback<ClickEvent>(_ => SelectSpeciesCard(sp, card));
        return card;
    }

    VisualElement MakeChip(string text, Color col)
    {
        var chip = new Label(text);
        chip.style.fontSize          = 10;
        chip.style.color             = new StyleColor(new Color(0.06f, 0.06f, 0.06f));
        chip.style.backgroundColor   = new StyleColor(col);
        chip.style.paddingLeft       = chip.style.paddingRight  = 6;
        chip.style.paddingTop        = chip.style.paddingBottom = 2;
        chip.style.marginLeft        = 5;
        chip.style.borderTopLeftRadius    = chip.style.borderTopRightRadius    = 3;
        chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 3;
        chip.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        return chip;
    }

    void SelectSpeciesCard(TreeSpecies sp, VisualElement card)
    {
        // Deselect previous
        if (selectedSpeciesCard != null)
        {
            selectedSpeciesCard.style.backgroundColor = new StyleColor(new Color(0.10f, 0.14f, 0.10f));
            SetCardBorderColor(selectedSpeciesCard, new Color(0.25f, 0.25f, 0.22f));
        }

        selectedSpecies     = sp;
        selectedSpeciesCard = card;

        card.style.backgroundColor = new StyleColor(new Color(0.14f, 0.19f, 0.11f));
        SetCardBorderColor(card, new Color(0.898f, 0.702f, 0.086f));  // gold

        // Enable confirm button
        if (speciesConfirmButton != null)
        {
            speciesConfirmButton.style.backgroundColor =
                new StyleColor(new Color(0.898f, 0.702f, 0.086f));
            speciesConfirmButton.style.color =
                new StyleColor(new Color(0.05f, 0.05f, 0.05f));
            speciesConfirmButton.pickingMode = PickingMode.Position;
        }
    }

    void OnSpeciesConfirmClick()
    {
        if (selectedSpecies == null) return;
        if (skeleton != null)
        {
            skeleton.species = selectedSpecies;
            skeleton.ApplySpecies();
        }
        GameManager.Instance.UpdateGameState(GameState.TipPause);
    }

    static void SetCardBorderColor(VisualElement el, Color col)
    {
        var sc = new StyleColor(col);
        el.style.borderTopColor    = sc;
        el.style.borderBottomColor = sc;
        el.style.borderLeftColor   = sc;
        el.style.borderRightColor  = sc;
    }

    // ── Stat chip helpers ────────────────────────────────────────────────────

    static string GrowthLabel(float speed) =>
        speed < 0.12f ? "Slow" : speed < 0.20f ? "Medium" : speed < 0.28f ? "Fast" : "Very Fast";

    static Color GrowthColor(float speed) =>
        speed < 0.12f ? new Color(0.52f, 0.62f, 0.85f) :   // blue  — slow
        speed < 0.20f ? new Color(0.55f, 0.78f, 0.52f) :   // green — medium
        speed < 0.28f ? new Color(0.88f, 0.75f, 0.28f) :   // amber — fast
                        new Color(0.88f, 0.42f, 0.22f);    // orange-red — very fast

    static string WaterLabel(float drain) =>
        drain < 0.08f ? "Low Water" : drain < 0.14f ? "Med Water" : "High Water";

    static Color WaterColor(float drain) =>
        drain < 0.08f ? new Color(0.72f, 0.88f, 0.60f) :   // light green — drought tolerant
        drain < 0.14f ? new Color(0.40f, 0.70f, 0.95f) :   // sky blue    — moderate
                        new Color(0.28f, 0.48f, 0.88f);    // deep blue   — thirsty

    static string CareLabel(float repotTol, float woundDrain)
    {
        float score = repotTol * 0.6f + Mathf.Clamp01(1f - woundDrain * 10f) * 0.4f;
        return score > 0.58f ? "Beginner" : score > 0.38f ? "Moderate" : "Expert";
    }

    static Color CareColor(float repotTol, float woundDrain)
    {
        float score = repotTol * 0.6f + Mathf.Clamp01(1f - woundDrain * 10f) * 0.4f;
        return score > 0.58f ? new Color(0.55f, 0.85f, 0.55f) :   // green  — beginner
               score > 0.38f ? new Color(0.88f, 0.72f, 0.28f) :   // amber  — moderate
                               new Color(0.88f, 0.38f, 0.38f);    // red    — expert
    }

    static string SoilLabel(float retention) =>
        retention < 0.35f ? "Dry Soil" : retention < 0.55f ? "Balanced" : "Moist Soil";

    static Color SoilColor(float retention) =>
        retention < 0.35f ? new Color(0.82f, 0.68f, 0.42f) :   // sand  — dry
        retention < 0.55f ? new Color(0.58f, 0.72f, 0.52f) :   // sage  — balanced
                            new Color(0.42f, 0.62f, 0.82f);    // slate — moist
}
