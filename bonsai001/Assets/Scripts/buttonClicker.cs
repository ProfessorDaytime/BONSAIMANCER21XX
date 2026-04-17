using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

public class ButtonClicker : MonoBehaviour
{
    [SerializeField] TreeSkeleton skeleton;

    // ── Load Menu ─────────────────────────────────────────────────────────────
    VisualElement loadMenuOverlay;
    VisualElement loadMenuCardContainer;
    Button        loadMenuNewGameButton;
    Button        loadMenuBackButton;
    bool          loadMenuHasBack;   // true when opened from pause menu (back returns to pause)
    string        pendingDeleteSlotId;   // set on first DELETE click; cleared on confirm/cancel

    // ── Save Name Prompt ──────────────────────────────────────────────────────
    VisualElement           saveNamePromptOverlay;
    UnityEngine.UIElements.TextField saveNameField;
    Button                  saveNameConfirmButton;
    Button                  saveNameCancelButton;

    // ── Air Layer Sever ───────────────────────────────────────────────────────
    VisualElement  airLayerSeverOverlay;
    Label          airLayerSeverBanner;
    Button         severConfirmButton;
    Button         severCancelButton;
    GameState      preSeverState;

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
    SpeciesSortMode currentSort    = SpeciesSortMode.Care;
    bool            sortDescending = true;
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
    Button graftButton;
    Button placeRockButton;
    Button confirmOrientButton;
    Button cancelOrientButton;
    VisualElement orientButtonContainer;
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
    Button        potSizeXSButton;
    Button        potSizeSButton;
    Button        potSizeMButton;
    Button        potSizeLButton;
    Button        potSizeXLButton;
    Button        potSizeSlabButton;
    Label         potSizeLabel;
    PotSoil.PotSize pendingPotSize = PotSoil.PotSize.M;

    // ── Root Rake mini-game ───────────────────────────────────────────────────
    VisualElement rootRakePanel;
    Label         rakeStatusLabel;
    Button        confirmRepotButton;
    Button        cancelRakeButton;

    // ── First-use tooltips ────────────────────────────────────────────────────
    VisualElement tooltipOverlay;
    Label         tooltipTitleLabel;
    Label         tooltipBodyLabel;
    Button        tooltipDismissButton;
    // IDs of tooltips already shown — persisted in PlayerPrefs.
    static HashSet<string> shownTooltips;
    const string TooltipPrefsKey = "ShownTooltips_v1";

    // ── Undo indicator ───────────────────────────────────────────────────────
    Label         undoLabel;

    // ── Speed toggle ──────────────────────────────────────────────────────────
    Button speedToggleButton;

    // ── UI cycle toggle (tools → stats → neither → …) ────────────────────────
    enum UIToggleState { Tools, Stats, Neither }
    UIToggleState uiToggleState = UIToggleState.Tools;
    Button        uiToggleButton;

    // ── Tree stats panel ──────────────────────────────────────────────────────
    Button        statsToggleButton;   // kept for back-compat query; unused after refactor
    VisualElement treeStatsPanel;
    Label         statMoisture;
    Label         statNutrients;
    Label         statAvgHealth;
    Label         statEnergy;
    Label         statCompaction;
    Label         statFungal;
    Label         statWounds;
    Label         statRepot;

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
    Toggle        toggleScaleCubes;

    // Sliders + value labels
    Slider sliderTimescale;      Label labelTimescale;
    Toggle toggleQuickWinter;
    Label  labelSelectionRadius;
    Button saveButton;
    Button loadButton;
    Button loadOriginalButton;
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
        // Anchor root to fill the full UIDocument panel. Without this, the root height
        // collapses to its flow-content height when HUD buttons are hidden (display:none),
        // causing full-screen overlays (top:0; bottom:0) to offset from actual screen bounds.
        root.style.position = Position.Absolute;
        root.style.top      = 0;
        root.style.left     = 0;
        root.style.right    = 0;
        root.style.bottom   = 0;
        root.style.overflow = new StyleEnum<Overflow>(Overflow.Visible);

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
        graftButton         = root.Q("GraftButton")         as Button;
        placeRockButton     = root.Q("PlaceRockButton")     as Button;
        confirmOrientButton    = root.Q("ConfirmOrientButton")    as Button;
        cancelOrientButton     = root.Q("CancelOrientButton")     as Button;
        orientButtonContainer  = root.Q("OrientButtonContainer");
        selectionRadiusSlider  = root.Q("SelectionRadiusSlider")  as Slider;
        labelSelectionRadius   = root.Q("LabelSelectionRadius")  as Label;

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
        potSizeXSButton       = root.Q("PotSizeXSButton")      as Button;
        potSizeSButton        = root.Q("PotSizeSButton")       as Button;
        potSizeMButton        = root.Q("PotSizeMButton")       as Button;
        potSizeLButton        = root.Q("PotSizeLButton")       as Button;
        potSizeXLButton       = root.Q("PotSizeXLButton")      as Button;
        potSizeSlabButton     = root.Q("PotSizeSlabButton")    as Button;
        potSizeLabel          = root.Q("PotSizeLabel")          as Label;

        rootRakePanel        = root.Q("RootRakePanel");
        rakeStatusLabel      = root.Q("RakeStatusLabel")       as Label;
        confirmRepotButton   = root.Q("ConfirmRepotButton")    as Button;
        cancelRakeButton     = root.Q("CancelRakeButton")      as Button;

        tooltipOverlay       = root.Q("TooltipOverlay");
        tooltipTitleLabel    = root.Q("TooltipTitleLabel")     as Label;
        tooltipBodyLabel     = root.Q("TooltipBodyLabel")      as Label;
        tooltipDismissButton = root.Q("TooltipDismissButton")  as Button;
        if (tooltipOverlay != null) tooltipOverlay.style.display = DisplayStyle.None;

        // Load the set of already-shown tooltip IDs from PlayerPrefs.
        if (shownTooltips == null)
        {
            shownTooltips = new HashSet<string>();
            string saved = PlayerPrefs.GetString(TooltipPrefsKey, "");
            if (!string.IsNullOrEmpty(saved))
                foreach (var id in saved.Split(','))
                    if (!string.IsNullOrEmpty(id)) shownTooltips.Add(id);
        }

        rootHealthPanel      = root.Q("RootHealthPanel");
        rootHealthScoreLabel = root.Q("RootHealthScoreLabel") as Label;
        rootHealthSectors    = root.Q("RootHealthSectors");

        // ── Scroll sensitivity ────────────────────────────────────────────────
        const float scrollSpeed = 400f;
        var speciesScroll = root.Q<UnityEngine.UIElements.ScrollView>("SpeciesScrollView");
        if (speciesScroll != null) speciesScroll.mouseWheelScrollSize = scrollSpeed;
        var loadScroll = root.Q<UnityEngine.UIElements.ScrollView>("LoadMenuScrollView");
        if (loadScroll != null) loadScroll.mouseWheelScrollSize = scrollSpeed;

        // ── Load menu ────────────────────────────────────────────────────────
        loadMenuOverlay       = root.Q("LoadMenuOverlay");
        loadMenuCardContainer = root.Q("LoadMenuCardContainer");
        loadMenuNewGameButton = root.Q("LoadMenuNewGameButton") as Button;
        loadMenuBackButton    = root.Q("LoadMenuBackButton")    as Button;
        loadMenuNewGameButton?.RegisterCallback<ClickEvent>(_ => OnLoadMenuNewGame());
        loadMenuBackButton?.RegisterCallback<ClickEvent>(_ => OnLoadMenuBack());

        // ── Save name prompt ─────────────────────────────────────────────────
        saveNamePromptOverlay  = root.Q("SaveNamePromptOverlay");
        saveNameField          = root.Q("SaveNameField") as UnityEngine.UIElements.TextField;
        saveNameConfirmButton  = root.Q("SaveNameConfirmButton") as Button;
        saveNameCancelButton   = root.Q("SaveNameCancelButton")  as Button;
        saveNameConfirmButton?.RegisterCallback<ClickEvent>(_ => OnSaveNameConfirm());
        saveNameCancelButton?.RegisterCallback<ClickEvent>(_ => OnSaveNameCancel());

        // ── Air layer sever ──────────────────────────────────────────────────
        airLayerSeverOverlay = root.Q("AirLayerSeverOverlay");
        airLayerSeverBanner  = root.Q("AirLayerSeverBanner") as Label;
        severConfirmButton   = root.Q("SeverConfirmButton")  as Button;
        severCancelButton    = root.Q("SeverCancelButton")   as Button;
        severConfirmButton?.RegisterCallback<ClickEvent>(_ => OnSeverConfirmClick());
        severCancelButton?.RegisterCallback<ClickEvent>(_ => OnSeverCancelClick());

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

        // ── Speed toggle ─────────────────────────────────────────────────────
        speedToggleButton = root.Q("SpeedToggleButton") as Button;

        // ── HUD cycle toggle + stats panel ──────────────────────────────────
        uiToggleButton   = root.Q("UIToggleButton")    as Button;
        statsToggleButton = null;   // removed from UXML; was StatsToggleButton
        treeStatsPanel   = root.Q("TreeStatsPanel");
        statMoisture     = root.Q("StatMoisture")      as Label;
        statNutrients    = root.Q("StatNutrients")     as Label;
        statAvgHealth    = root.Q("StatAvgHealth")     as Label;
        statEnergy       = root.Q("StatEnergy")        as Label;
        statCompaction   = root.Q("StatCompaction")    as Label;
        statFungal       = root.Q("StatFungal")        as Label;
        statWounds       = root.Q("StatWounds")        as Label;
        statRepot        = root.Q("StatRepot")         as Label;

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
        toggleScaleCubes    = root.Q("ToggleScaleCubes")    as Toggle;

        saveButton           = root.Q("SaveButton")           as Button;
        loadButton           = root.Q("LoadButton")           as Button;
        loadOriginalButton   = root.Q("LoadOriginalButton")   as Button;
        var browseSavesButton = root.Q("BrowseSavesButton")  as Button;
        browseSavesButton?.RegisterCallback<ClickEvent>(_ => {
            TogglePauseMenu(); // close pause menu first
            loadMenuHasBack = true;
            GameManager.Instance.UpdateGameState(GameState.LoadMenu);
        });
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
        graftButton?.RegisterCallback<ClickEvent>(_ => OnGraftButtonClick());
        placeRockButton?.RegisterCallback<ClickEvent>(OnPlaceRockButtonClick);
        confirmOrientButton?.RegisterCallback<ClickEvent>(OnConfirmOrientButtonClick);
        cancelOrientButton?.RegisterCallback<ClickEvent>(_ => OnCancelOrientButtonClick());
        selectionRadiusSlider?.RegisterValueChangedCallback(evt => {
            GameManager.selectionRadius = evt.newValue;
            if (labelSelectionRadius != null) labelSelectionRadius.text = evt.newValue.ToString("F2");
        });

        saveButton?.RegisterCallback<ClickEvent>(_ => OnSaveButtonClick());
        loadButton?.RegisterCallback<ClickEvent>(_ => OnLoadButtonClick());
        loadOriginalButton?.RegisterCallback<ClickEvent>(_ => OnLoadOriginalButtonClick());
        // Show the load-original button only when a backup exists.
        if (loadOriginalButton != null)
            loadOriginalButton.style.display = SaveManager.OriginalExists()
                ? DisplayStyle.Flex : DisplayStyle.None;

        // Wire up pause menu
        pauseButton?.RegisterCallback<ClickEvent>(_ => TogglePauseMenu());
        speedToggleButton?.RegisterCallback<ClickEvent>(_ => OnSpeedToggleClick());
        uiToggleButton?.RegisterCallback<ClickEvent>(_ => OnUIToggleClick());
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

        potSizeXSButton?.RegisterCallback<ClickEvent>(_ => SelectPotSize(PotSoil.PotSize.XS));
        potSizeSButton?.RegisterCallback<ClickEvent>(_ => SelectPotSize(PotSoil.PotSize.S));
        potSizeMButton?.RegisterCallback<ClickEvent>(_ => SelectPotSize(PotSoil.PotSize.M));
        potSizeLButton?.RegisterCallback<ClickEvent>(_ => SelectPotSize(PotSoil.PotSize.L));
        potSizeXLButton?.RegisterCallback<ClickEvent>(_ => SelectPotSize(PotSoil.PotSize.XL));
        potSizeSlabButton?.RegisterCallback<ClickEvent>(_ => SelectPotSize(PotSoil.PotSize.Slab));

        confirmRepotButton?.RegisterCallback<ClickEvent>(_ => OnConfirmRepotClick());
        cancelRakeButton?.RegisterCallback<ClickEvent>(_ => OnCancelRakeClick());

        tooltipDismissButton?.RegisterCallback<ClickEvent>(_ => GameManager.Instance?.ExitTipPause());

        toggleAutoWater?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.autoWaterEnabled = evt.newValue;
        });

        toggleAutoFertilize?.RegisterValueChangedCallback(evt => {
            if (skeleton != null) skeleton.autoFertilizeEnabled = evt.newValue;
        });

        toggleScaleCubes?.RegisterValueChangedCallback(evt => {
            var dbg = skeleton != null ? skeleton.GetComponent<ScaleDebugger>() : null;
            if (dbg != null)
                dbg.Visible = evt.newValue;
            else
                Debug.LogWarning("[ScaleDebugger] Component not found on skeleton GameObject. Add ScaleDebugger to the tree.");
        });

        if (sliderTimescale != null)
        {
            sliderTimescale.lowValue = 0f;
            sliderTimescale.highValue = 1f;
        }
        sliderTimescale?.RegisterValueChangedCallback(evt => {
            float ts = SliderToTimescale(evt.newValue);
            // Snap to nearest notch if within ±3% of slider range
            foreach (float notch in TimescaleNotches)
            {
                float notchPos = TimescaleToSlider(notch);
                if (Mathf.Abs(evt.newValue - notchPos) < 0.03f)
                {
                    ts = notch;
                    sliderTimescale.SetValueWithoutNotify(notchPos);
                    break;
                }
            }
            GameManager.TIMESCALE = ts;
            if (labelTimescale != null) labelTimescale.text = FormatTimescaleLabel(ts);
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
        GameManager.OnMonthChanged     += OnMonthChanged;
        if (skeleton != null) skeleton.OnWireSetGold += OnWireSetGold;

        // Sync overlay visibility to whatever state was set before OnEnable ran
        var initState = GameManager.Instance?.state ?? GameState.SpeciesSelect;
        if (speciesSelectOverlay != null)
            speciesSelectOverlay.style.display =
                initState == GameState.SpeciesSelect ? DisplayStyle.Flex : DisplayStyle.None;
        if (loadMenuOverlay != null)
        {
            bool showLoad = initState == GameState.LoadMenu;
            loadMenuOverlay.style.display = showLoad ? DisplayStyle.Flex : DisplayStyle.None;
            if (showLoad)
            {
                BuildLoadMenuCards();
                // At startup, loadMenuHasBack is false — back button stays hidden.
                if (loadMenuBackButton != null)
                    loadMenuBackButton.style.display = DisplayStyle.None;
            }
        }
    }

    void OnDisable()
    {
        GameManager.OnGameStateChanged -= OnGameStateChanged;
        GameManager.OnMonthChanged     -= OnMonthChanged;
        if (skeleton != null) skeleton.OnWireSetGold -= OnWireSetGold;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            var s = GameManager.Instance?.state ?? GameState.Idle;
            if (s == GameState.LoadMenu && loadMenuHasBack)
                OnLoadMenuBack();
            else if (s == GameState.TipPause && !string.IsNullOrEmpty(GameManager.TooltipTitle))
                GameManager.Instance.ExitTipPause();  // dismiss first-use tooltip with ESC
            else if (s != GameState.LoadMenu && s != GameState.SpeciesSelect &&
                     s != GameState.TipPause && s != GameState.TreeDead && s != GameState.AirLayerSever)
                TogglePauseMenu();
        }

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
                // Sync pending pot size from actual pot on first open
                if (pendingPotSize != potSoil.potSize)
                {
                    pendingPotSize = potSoil.potSize;
                    if (potSizeLabel != null) potSizeLabel.text = $"Current: {potSoil.potSize}";
                    RefreshPotSizeButtons();
                }

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

        // Root rake panel: show in RootRake state, update status label
        bool inRootRake = GameManager.Instance.state == GameState.RootRake;
        if (rootRakePanel != null)
            rootRakePanel.style.display = inRootRake ? DisplayStyle.Flex : DisplayStyle.None;

        if (inRootRake && skeleton != null)
        {
            var rakeManager = skeleton.GetComponent<RootRakeManager>();
            if (rakeManager != null && rakeStatusLabel != null)
            {
                float removed = rakeManager.SoilRemovedFraction() * 100f;
                int strands = rakeManager.RootStrandCount();
                rakeStatusLabel.text = $"Soil removed: {removed:F0}%\nRoot strands: {strands}";
            }
        }

        // Stats panel: refresh every frame while open
        if (uiToggleState == UIToggleState.Stats)
            RefreshStatsPanel();

        // Speed toggle button: keep in sync with GameManager (catches auto-slow in January)
        RefreshSpeedToggleButton();

        // Tree danger banner: show when tree is stressed but not yet dead
        if (treeDangerBanner != null && skeleton != null)
            treeDangerBanner.style.display = skeleton.treeInDanger && skeleton.treeDeathEnabled
                ? DisplayStyle.Flex : DisplayStyle.None;

        // Air layer sever banner: show when any layer is ready to sever
        if (airLayerSeverBanner != null && skeleton != null)
            airLayerSeverBanner.style.display = skeleton.HasSeverableLayer
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

            if (canUndo && Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed && Keyboard.current.zKey.wasPressedThisFrame)
                skeleton.UndoLastTrim();
        }
    }

    // ── First-use tooltips ────────────────────────────────────────────────────

    /// <summary>
    /// Show a first-use tooltip for the given id if it hasn't been shown before.
    /// Call this AFTER the underlying action so the tool is already active when the tip appears.
    /// </summary>
    void MaybeShowTooltip(string id, string title, string body)
    {
        if (shownTooltips == null || shownTooltips.Contains(id)) return;
        shownTooltips.Add(id);
        SaveShownTooltips();
        GameManager.Instance?.ShowTooltip(title, body);
    }

    void SaveShownTooltips()
    {
        PlayerPrefs.SetString(TooltipPrefsKey, string.Join(",", shownTooltips));
        PlayerPrefs.Save();
    }

    // ── Speed toggle ─────────────────────────────────────────────────────────

    void OnSpeedToggleClick()
    {
        GameManager.Instance.ToggleSpeed();
        RefreshSpeedToggleButton();
    }

    void RefreshSpeedToggleButton()
    {
        if (speedToggleButton == null) return;
        var mode = GameManager.CurrentSpeed;
        speedToggleButton.text = mode switch
        {
            GameManager.SpeedMode.Slow => "▶",
            GameManager.SpeedMode.Med  => "▶▶",
            _                          => "▶▶▶",
        };
        speedToggleButton.style.color = mode switch
        {
            GameManager.SpeedMode.Slow => new StyleColor(new Color(0.9f, 0.7f, 0.2f)),   // amber
            GameManager.SpeedMode.Med  => new StyleColor(new Color(0.63f, 0.63f, 0.63f)),// grey
            _                          => new StyleColor(new Color(0.55f, 0.85f, 0.55f)),// green
        };
    }

    // ── UI cycle toggle ───────────────────────────────────────────────────────

    void OnUIToggleClick()
    {
        // Advance: Tools → Stats → Neither → Tools …
        uiToggleState = uiToggleState switch
        {
            UIToggleState.Tools   => UIToggleState.Stats,
            UIToggleState.Stats   => UIToggleState.Neither,
            _                     => UIToggleState.Tools,
        };
        ApplyUIToggleState();
    }

    void ApplyUIToggleState()
    {
        bool showTools = uiToggleState == UIToggleState.Tools;
        bool showStats = uiToggleState == UIToggleState.Stats;

        SetHideableButtonsVisible(showTools);

        if (treeStatsPanel != null)
            treeStatsPanel.style.display = showStats ? DisplayStyle.Flex : DisplayStyle.None;
        if (rootHealthPanel != null)
            rootHealthPanel.style.display = showStats ? DisplayStyle.Flex : DisplayStyle.None;

        if (uiToggleButton != null)
            uiToggleButton.style.color = uiToggleState switch
            {
                UIToggleState.Tools   => new StyleColor(new Color(0.63f, 0.63f, 0.63f)), // grey  — default
                UIToggleState.Stats   => new StyleColor(new Color(0.55f, 0.85f, 0.55f)), // green — stats open
                _                     => new StyleColor(new Color(0.9f, 0.7f, 0.2f)),    // amber — all hidden
            };

        if (showStats) RefreshStatsPanel();
    }

    /// <summary>
    /// Elements toggled by the cycle button.
    /// Excludes: UIToggleButton, PauseButton, and always-visible overlays.
    /// </summary>
    void SetHideableButtonsVisible(bool visible)
    {
        var ds = visible ? DisplayStyle.Flex : DisplayStyle.None;
        void Set(VisualElement e) { if (e != null) e.style.display = ds; }

        // Left-column tool buttons
        Set(trimButton); Set(waterButton);
        Set(pinchButton); Set(defoliateButton); Set(defoliateAllButton);
        Set(wireButton); Set(removeWireButton);

        // Right-column tool buttons (absolute-positioned; only the ones not context-controlled)
        Set(pasteButton); Set(rootPruneButton); Set(airLayerButton); Set(graftButton);

        // Right-column action buttons
        Set(fertilizeButton); Set(herbicideButton); Set(fungicideButton);

        // Moisture and nutrient bars sit beside their buttons — hide together
        var mbar = moistureBarFill?.parent; if (mbar != null) mbar.style.display = ds;
        if (nutrientBarFill != null)
        {
            var nbar = nutrientBarFill.parent;
            if (nbar != null) nbar.style.display = ds;
        }
    }

    void RefreshStatsPanel()
    {
        if (skeleton == null || treeStatsPanel == null) return;
        UpdateRootHealthDisplay();

        // Moisture
        float moist = skeleton.soilMoisture;
        if (statMoisture != null)
        {
            statMoisture.text = $"{moist * 100f:F0}%";
            statMoisture.style.color = new StyleColor(moist < 0.25f
                ? new Color(0.9f, 0.4f, 0.3f)
                : moist < 0.5f ? new Color(0.9f, 0.7f, 0.3f)
                : new Color(0.55f, 0.85f, 0.55f));
        }

        // Nutrients
        float nutr = skeleton.nutrientReserve;
        if (statNutrients != null)
        {
            statNutrients.text = $"{nutr:F2}";
            statNutrients.style.color = new StyleColor(nutr < 0.3f
                ? new Color(0.9f, 0.4f, 0.3f)
                : nutr > 1.5f ? new Color(0.9f, 0.6f, 0.2f)
                : new Color(0.55f, 0.85f, 0.55f));
        }

        // Average health
        float avgH = 0f; int hc = 0;
        foreach (var n in skeleton.allNodes) if (!n.isTrimmed) { avgH += n.health; hc++; }
        if (hc > 0) avgH /= hc;
        if (statAvgHealth != null)
        {
            statAvgHealth.text = $"{avgH * 100f:F0}%";
            statAvgHealth.style.color = new StyleColor(avgH < 0.4f
                ? new Color(0.9f, 0.3f, 0.3f)
                : avgH < 0.65f ? new Color(0.9f, 0.7f, 0.3f)
                : new Color(0.55f, 0.85f, 0.55f));
        }

        // Energy
        if (statEnergy != null)
            statEnergy.text = $"{skeleton.treeEnergy * 100f:F0}%";

        // Soil
        var potSoil = skeleton.GetComponent<PotSoil>();
        if (statCompaction != null)
            statCompaction.text = potSoil != null ? $"{potSoil.soilDegradation * 100f:F0}%" : "—";

        // Fungal load — average across all nodes
        float fungSum = 0f; int fc = 0;
        foreach (var n in skeleton.allNodes) if (!n.isTrimmed) { fungSum += n.fungalLoad; fc++; }
        float fungAvg = fc > 0 ? fungSum / fc : 0f;
        if (statFungal != null)
        {
            statFungal.text = $"{fungAvg * 100f:F0}%";
            statFungal.style.color = new StyleColor(fungAvg > 0.3f
                ? new Color(0.9f, 0.4f, 0.3f)
                : fungAvg > 0.1f ? new Color(0.9f, 0.7f, 0.3f)
                : new Color(0.55f, 0.85f, 0.55f));
        }

        // Wound count
        int woundCount = 0;
        foreach (var n in skeleton.allNodes) if (n.hasWound && !n.isTrimmed) woundCount++;
        if (statWounds != null)
            statWounds.text = woundCount == 0 ? "None" : woundCount.ToString();

        // Seasons since repot
        if (statRepot != null)
            statRepot.text = potSoil != null ? $"{potSoil.seasonsSinceRepot} seasons" : "—";

        // Root health score (nebari quality) — only shown in root panel; omitted here to avoid confusion
        // with the separate "root vitality" (avg health %) which is a different metric.
    }

    // ── Pause menu helpers ───────────────────────────────────────────────────

    void TogglePauseMenu()
    {
        if (pauseMenuOverlay == null) return;
        bool opening = pauseMenuOverlay.style.display == DisplayStyle.None;
        // Don't open the pause menu from non-gameplay states.
        if (opening)
        {
            var s = GameManager.Instance?.state ?? GameState.Idle;
            if (s == GameState.LoadMenu   || s == GameState.SpeciesSelect ||
                s == GameState.TipPause   || s == GameState.TreeDead      ||
                s == GameState.AirLayerSever) return;
        }
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
            sliderTimescale.lowValue = 0f;
            sliderTimescale.highValue = 1f;
            sliderTimescale.SetValueWithoutNotify(TimescaleToSlider(GameManager.TIMESCALE));
            if (labelTimescale != null) labelTimescale.text = FormatTimescaleLabel(GameManager.TIMESCALE);
        }
        if (toggleQuickWinter != null)
            toggleQuickWinter.SetValueWithoutNotify(GameManager.quickWinter);

        if (selectionRadiusSlider != null)
        {
            selectionRadiusSlider.SetValueWithoutNotify(GameManager.selectionRadius);
            if (labelSelectionRadius != null)
                labelSelectionRadius.text = GameManager.selectionRadius.ToString("F2");
        }

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

    // ── Month-triggered tutorials ─────────────────────────────────────────────

    void OnWireSetGold()
    {
        MaybeShowTooltip("removewire",
            "Wire Is Set — Remove Now",
            "A wire has turned gold. The branch has held its new direction.\n\n" +
            "Remove the wire before it bites into the bark — wire left too long cuts into the cambium and leaves scars that take years to callus over.\n\n" +
            "Use the Unwire tool (click the gold wire) to remove it safely.");
    }

    void OnMonthChanged(int month)
    {
        if (month == 4)
        {
            MaybeShowTooltip("april_ramification",
                "April — Pinching & Ramification",
                "New shoots are extending right now. This is the most important window of the year.\n\n" +
                "PINCH any shoot that has extended 2–3 leaves. Removing the growing tip stops auxin production — the hormone that suppresses side buds. Within a week, 2–4 new buds will break from the nodes below the cut.\n\n" +
                "This is how ramification works: each pinch turns 1 tip into 2–4. Do this every spring and your branch count doubles each year.\n\n" +
                "TRIM vs PINCH:\n" +
                "• Pinch = soft tip only, now, while it's green. Short internodes, maximum back-budding.\n" +
                "• Trim = any time on hardened wood. For structure — removing whole branches.\n\n" +
                "Heavy trimming in January forced energy back into the old wood. Now those backbuds are extending. Pinch them before they run — don't let any one shoot dominate.");
        }
    }

    // ── Button swap logic ────────────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        // First-use tooltip overlay: visible in TipPause when a tooltip title is set.
        if (tooltipOverlay != null)
        {
            bool showTip = state == GameState.TipPause && !string.IsNullOrEmpty(GameManager.TooltipTitle);
            tooltipOverlay.style.display = showTip ? DisplayStyle.Flex : DisplayStyle.None;
            if (showTip)
            {
                if (tooltipTitleLabel != null) tooltipTitleLabel.text = GameManager.TooltipTitle;
                if (tooltipBodyLabel  != null) tooltipBodyLabel.text  = GameManager.TooltipBody;
            }
        }

        // Species select overlay: shown only during species picker state
        if (speciesSelectOverlay != null)
            speciesSelectOverlay.style.display = state == GameState.SpeciesSelect
                ? DisplayStyle.Flex : DisplayStyle.None;

        // Load menu overlay
        if (loadMenuOverlay != null)
        {
            bool show = state == GameState.LoadMenu;
            loadMenuOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                BuildLoadMenuCards();
                if (loadMenuBackButton != null)
                    loadMenuBackButton.style.display = loadMenuHasBack
                        ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                loadMenuHasBack = false;  // reset for next open
            }
        }

        // Save name prompt overlay — shown/hidden by explicit calls, not state
        if (saveNamePromptOverlay != null && state != GameState.LoadMenu)
            saveNamePromptOverlay.style.display = DisplayStyle.None;

        // Air layer sever overlay
        if (airLayerSeverOverlay != null)
            airLayerSeverOverlay.style.display = state == GameState.AirLayerSever
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

        bool inPlacement = inRockPlace || inTreeOrient;

        // Dim and disable all tool buttons during rock placement / tree orient
        Button[] toolButtons = {
            trimButton, waterButton, pinchButton, defoliateButton, defoliateAllButton,
            wireButton, removeWireButton, rootPruneButton, quickWinterButton,
            pasteButton, fertilizeButton, herbicideButton, fungicideButton,
            airLayerButton, graftButton, placeRockButton,
            repotClassicButton, repotFreeDrainButton, repotMoistButton, repotAcidicButton,
        };
        foreach (var btn in toolButtons)
        {
            if (btn == null) continue;
            btn.pickingMode   = inPlacement ? PickingMode.Ignore : PickingMode.Position;
            btn.style.opacity = inPlacement ? 0.25f : 1f;
        }

        // Air Layer / Place Rock slot visibility (only when not in placement)
        if (!inPlacement)
        {
            if (airLayerButton  != null) airLayerButton.style.display  = inRootLift  ? DisplayStyle.None : DisplayStyle.Flex;
            if (graftButton     != null) graftButton.style.display     = inRootLift  ? DisplayStyle.None : DisplayStyle.Flex;
            if (placeRockButton != null) placeRockButton.style.display = inRootPrune ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Confirm / Cancel orient buttons — visible only during rock placement / tree orient
        if (orientButtonContainer != null)
            orientButtonContainer.style.display = inPlacement ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    void OnSaveButtonClick()
    {
        if (skeleton == null || skeleton.root == null) return;
        if (string.IsNullOrEmpty(SaveManager.ActiveSlotId))
        {
            // No slot yet — show the name prompt.
            ShowSaveNamePrompt();
            return;
        }
        var leafMgr = skeleton.GetComponent<LeafManager>();
        bool ok = SaveManager.Save(skeleton, leafMgr);
        if (saveStatusLabel != null)
        {
            saveStatusLabel.text = ok ? $"Saved  ({System.DateTime.Now:HH:mm:ss})" : "Save failed.";
            saveStatusClearTime  = Time.realtimeSinceStartup + 4f;
        }
        if (ok) StartCoroutine(TakeScreenshotForSlot(SaveManager.ActiveSlotId));
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
        if (ok)
        {
            TogglePauseMenu();
            Time.timeScale = 1f;
            GameManager.Instance.UpdateGameState(GameManager.Instance.StateForMonth(GameManager.month));
        }
    }

    // ── Load Menu ─────────────────────────────────────────────────────────────

    void OnLoadMenuNewGame()
    {
        SaveManager.ActiveSlotId = null;
        GameManager.Instance.UpdateGameState(GameState.SpeciesSelect);
    }

    void OnLoadMenuBack()
    {
        if (!loadMenuHasBack) return;
        // Re-open the pause menu: show the overlay, set state to GamePause.
        // prePauseState in GameManager is still set from when the menu was open,
        // so Resume will correctly restore the game state.
        if (pauseMenuOverlay != null) pauseMenuOverlay.style.display = DisplayStyle.Flex;
        SyncSlidersFromGame();
        SelectTab(0);
        GameManager.Instance.UpdateGameState(GameState.GamePause);
    }

    void BuildLoadMenuCards()
    {
        if (loadMenuCardContainer == null) return;
        loadMenuCardContainer.Clear();
        pendingDeleteSlotId = null;

        var saves = SaveManager.ListAllSaves();
        if (saves.Count == 0)
        {
            var empty = new UnityEngine.UIElements.Label { text = "No saved games found." };
            empty.style.color    = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            empty.style.fontSize = 13;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.marginTop = 40;
            loadMenuCardContainer.Add(empty);
            return;
        }

        foreach (var meta in saves)
            loadMenuCardContainer.Add(MakeSaveCard(meta));
    }

    VisualElement MakeSaveCard(SaveMeta meta)
    {
        // Card container
        var card = new VisualElement();
        card.style.flexDirection        = FlexDirection.Row;
        card.style.alignItems           = Align.Center;
        card.style.backgroundColor      = new StyleColor(new Color(0.08f, 0.12f, 0.08f));
        card.style.borderTopLeftRadius  = card.style.borderTopRightRadius  = 6;
        card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
        card.style.marginBottom         = 8;
        card.style.paddingTop = card.style.paddingBottom = 10;
        card.style.paddingLeft = card.style.paddingRight = 14;

        // Thumbnail (if available)
        string thumbPath = ThumbPath(meta.slotId);
        if (!string.IsNullOrEmpty(meta.slotId) && System.IO.File.Exists(thumbPath))
        {
            try
            {
                var thumbData = System.IO.File.ReadAllBytes(thumbPath);
                var thumbTex  = new Texture2D(2, 2);
                if (thumbTex.LoadImage(thumbData))
                {
                    var thumbEl = new VisualElement();
                    thumbEl.style.width           = 80;
                    thumbEl.style.minWidth        = 80;
                    thumbEl.style.height          = 45;
                    thumbEl.style.minHeight       = 45;
                    thumbEl.style.marginRight     = 12;
                    thumbEl.style.borderTopLeftRadius    = thumbEl.style.borderBottomLeftRadius = 4;
                    thumbEl.style.borderTopRightRadius   = thumbEl.style.borderBottomRightRadius = 4;
                    thumbEl.style.backgroundImage = new StyleBackground(thumbTex);
                    card.Add(thumbEl);
                }
                else
                    Destroy(thumbTex);
            }
            catch { /* skip broken thumb */ }
        }

        // Left info column
        var info = new VisualElement();
        info.style.flexGrow    = 1;
        info.style.flexShrink  = 1;
        info.style.marginRight = 12;

        // Save name
        var nameLabel = new UnityEngine.UIElements.Label { text = meta.saveName ?? "Unnamed" };
        nameLabel.style.fontSize     = 16;
        nameLabel.style.color        = new StyleColor(new Color(0.9f, 0.88f, 0.78f));
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.marginBottom = 4;
        info.Add(nameLabel);

        // Row 2: origin badge + species
        var row2 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 3 } };
        var origin = (TreeOrigin)meta.treeOrigin;
        var (badgeText, badgeCol) = origin switch
        {
            TreeOrigin.AirLayer => ("✦ Air Layer", new Color(0.3f, 0.85f, 0.65f)),
            TreeOrigin.Cutting  => ("✂ Cutting",   new Color(0.85f, 0.75f, 0.3f)),
            _                   => ("● Seedling",  new Color(0.5f,  0.85f, 0.4f)),
        };
        var badge = new UnityEngine.UIElements.Label { text = badgeText };
        badge.style.fontSize         = 10;
        badge.style.color            = new StyleColor(badgeCol);
        badge.style.backgroundColor  = new StyleColor(new Color(badgeCol.r * 0.15f, badgeCol.g * 0.15f, badgeCol.b * 0.15f, 0.8f));
        badge.style.paddingLeft = badge.style.paddingRight = 5;
        badge.style.paddingTop  = badge.style.paddingBottom = 2;
        badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius = 3;
        badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
        badge.style.marginRight = 7;
        row2.Add(badge);

        var specLabel = new UnityEngine.UIElements.Label { text = meta.speciesName ?? "" };
        specLabel.style.fontSize = 12;
        specLabel.style.color    = new StyleColor(new Color(0.65f, 0.78f, 0.65f));
        row2.Add(specLabel);
        info.Add(row2);

        // Row 3: date + tree age
        string monthName = meta.month switch
        {
            3 => "Mar", 4 => "Apr", 5 => "May", 6 => "Jun",
            7 => "Jul", 8 => "Aug", 9 => "Sep", 10 => "Oct",
            11 => "Nov", 12 => "Dec", 1 => "Jan", 2 => "Feb", _ => "???"
        };
        string ageTxt = meta.treeAge <= 0 ? "seedling" : meta.treeAge == 1 ? "1 yr" : $"{meta.treeAge} yrs";
        var row3 = new UnityEngine.UIElements.Label { text = $"{monthName} {meta.year}  ·  {ageTxt}" };
        row3.style.fontSize = 11;
        row3.style.color    = new StyleColor(new Color(0.45f, 0.55f, 0.45f));
        info.Add(row3);

        // Row 4: health bar
        var row4 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
        var healthLbl = new UnityEngine.UIElements.Label { text = "Health " };
        healthLbl.style.fontSize = 10;
        healthLbl.style.color    = new StyleColor(new Color(0.45f, 0.55f, 0.45f));
        row4.Add(healthLbl);
        var healthBg = new VisualElement();
        healthBg.style.width  = 80; healthBg.style.height = 5;
        healthBg.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));
        healthBg.style.borderTopLeftRadius = healthBg.style.borderTopRightRadius =
            healthBg.style.borderBottomLeftRadius = healthBg.style.borderBottomRightRadius = 2;
        float h = Mathf.Clamp01(meta.avgHealth);
        var healthFill = new VisualElement();
        healthFill.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
        healthFill.style.width  = new StyleLength(new Length(h * 100f, LengthUnit.Percent));
        Color hCol = h > 0.6f ? new Color(0.25f, 0.75f, 0.25f)
                   : h > 0.3f ? new Color(0.85f, 0.65f, 0.1f)
                               : new Color(0.75f, 0.1f,  0.1f);
        healthFill.style.backgroundColor = new StyleColor(hCol);
        healthFill.style.borderTopLeftRadius = healthFill.style.borderTopRightRadius =
            healthFill.style.borderBottomLeftRadius = healthFill.style.borderBottomRightRadius = 2;
        healthBg.Add(healthFill);
        row4.Add(healthBg);
        var healthPctLbl = new UnityEngine.UIElements.Label { text = $"  {h * 100f:F0}%" };
        healthPctLbl.style.fontSize = 10;
        healthPctLbl.style.color    = new StyleColor(hCol);
        row4.Add(healthPctLbl);
        info.Add(row4);

        card.Add(info);

        // Right button column
        var btns = new VisualElement();
        btns.style.flexDirection = FlexDirection.Column;
        btns.style.alignItems    = Align.FlexEnd;

        var slotId = meta.slotId;

        var loadBtn = new Button { text = "LOAD" };
        StyleButton(loadBtn, new Color(0.15f, 0.35f, 0.55f), new Color(0.7f, 0.85f, 1f));
        loadBtn.style.marginBottom = 5;
        loadBtn.RegisterCallback<ClickEvent>(_ => OnLoadMenuCardLoad(slotId));
        btns.Add(loadBtn);

        var delBtn = new Button { text = "DELETE" };
        StyleButton(delBtn, new Color(0.35f, 0.1f, 0.1f), new Color(0.9f, 0.55f, 0.55f));
        delBtn.style.fontSize = 11;
        delBtn.RegisterCallback<ClickEvent>(_ => OnLoadMenuCardDelete(slotId, delBtn, card));
        btns.Add(delBtn);

        card.Add(btns);
        return card;
    }

    static void StyleButton(Button btn, Color bg, Color fg)
    {
        btn.style.backgroundColor = new StyleColor(bg);
        btn.style.color           = new StyleColor(fg);
        btn.style.borderTopWidth = btn.style.borderBottomWidth =
        btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
        btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
        btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 5;
        btn.style.paddingLeft = btn.style.paddingRight = 10;
        btn.style.paddingTop  = btn.style.paddingBottom = 5;
    }

    // ── Screenshot thumbnail ─────────────────────────────────────────────────

    static string ThumbPath(string slotId) =>
        System.IO.Path.Combine(SaveManager.SavesRoot, slotId, "thumb.png");

    IEnumerator TakeScreenshotForSlot(string slotId)
    {
        if (string.IsNullOrEmpty(slotId)) yield break;
        // Hide UI for a clean screenshot, wait for the frame to render, capture.
        var uiRoot = buttonDocument?.rootVisualElement;
        if (uiRoot != null) uiRoot.style.display = DisplayStyle.None;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (uiRoot != null) uiRoot.style.display = DisplayStyle.Flex;

        var thumb = ScaleTexture(tex, 256, 144);
        Destroy(tex);

        byte[] bytes = thumb.EncodeToPNG();
        Destroy(thumb);

        try { System.IO.File.WriteAllBytes(ThumbPath(slotId), bytes); }
        catch (System.Exception e) { Debug.LogWarning($"[Screenshot] Failed to write thumb: {e.Message}"); }
    }

    static Texture2D ScaleTexture(Texture2D src, int w, int h)
    {
        var rt   = RenderTexture.GetTemporary(w, h, 0);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var result = new Texture2D(w, h, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    void OnLoadMenuCardLoad(string slotId)
    {
        var leafMgr = skeleton?.GetComponent<LeafManager>();
        bool ok = SaveManager.LoadSlot(slotId, skeleton, leafMgr);
        if (ok)
        {
            skeleton?.GetComponent<TreeMeshBuilder>()?.SetDeadTint(false);
            if (skeleton != null) { skeleton.treeInDanger = false; skeleton.consecutiveCriticalSeasons = 0; }
            Time.timeScale = 1f;
            GameManager.Instance.UpdateGameState(GameManager.Instance.StateForMonth(GameManager.month));
        }
    }

    void OnLoadMenuCardDelete(string slotId, Button delBtn, VisualElement card)
    {
        if (pendingDeleteSlotId == slotId)
        {
            // Second click — confirmed.
            SaveManager.DeleteSlot(slotId);
            loadMenuCardContainer?.Remove(card);
            pendingDeleteSlotId = null;
            // If no saves left, go straight to new game.
            if (!SaveManager.HasAnySave())
                OnLoadMenuNewGame();
        }
        else
        {
            // First click — arm delete.
            pendingDeleteSlotId = slotId;
            delBtn.text = "SURE?";
            delBtn.style.backgroundColor = new StyleColor(new Color(0.7f, 0.15f, 0.15f));
        }
    }

    // ── Save Name Prompt ──────────────────────────────────────────────────────

    void ShowSaveNamePrompt()
    {
        if (saveNamePromptOverlay == null) return;
        string defaultName = (skeleton?.SpeciesName ?? "Bonsai") + " " + GameManager.year;
        if (saveNameField != null) saveNameField.value = defaultName;
        saveNamePromptOverlay.style.display = DisplayStyle.Flex;
    }

    void OnSaveNameConfirm()
    {
        if (skeleton == null || skeleton.root == null) return;
        string name = saveNameField?.value ?? "";
        if (string.IsNullOrWhiteSpace(name))
            name = (skeleton.SpeciesName ?? "Bonsai") + " " + GameManager.year;
        name = name.Trim();

        string slotId = SaveManager.NewSlotId();
        var leafMgr   = skeleton.GetComponent<LeafManager>();
        var meta = new SaveMeta
        {
            slotId            = slotId,
            saveName          = name,
            treeOrigin        = (int)skeleton.treeOrigin,
            speciesName       = skeleton.SpeciesName,
            year              = GameManager.year,
            month             = GameManager.month,
            saveTimestamp     = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"),
            nodeCount         = skeleton.allNodes?.Count ?? 0,
            seasonsSinceRepot = skeleton.GetComponent<PotSoil>()?.seasonsSinceRepot ?? 0,
            treeAge           = GameManager.year - skeleton.SaveStartYear,
            avgHealth         = SaveManager.CalcAvgHealth(skeleton),
        };
        SaveManager.SaveSlot(slotId, skeleton, leafMgr, meta);
        StartCoroutine(TakeScreenshotForSlot(slotId));

        if (saveNamePromptOverlay != null) saveNamePromptOverlay.style.display = DisplayStyle.None;
        if (saveStatusLabel != null)
        {
            saveStatusLabel.text = $"Saved as '{name}'";
            saveStatusClearTime  = Time.realtimeSinceStartup + 4f;
        }

        // Show load-original button if a backup now exists.
        if (loadOriginalButton != null)
            loadOriginalButton.style.display = SaveManager.OriginalExists()
                ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void OnSaveNameCancel()
    {
        if (saveNamePromptOverlay != null) saveNamePromptOverlay.style.display = DisplayStyle.None;
    }

    void OnLoadOriginalButtonClick()
    {
        var leafMgr = skeleton != null ? skeleton.GetComponent<LeafManager>() : null;
        bool ok = SaveManager.LoadOriginal(skeleton, leafMgr);
        if (saveStatusLabel != null)
        {
            saveStatusLabel.text = ok ? $"Original tree restored  ({System.DateTime.Now:HH:mm:ss})" : "No original backup found.";
            saveStatusClearTime  = Time.realtimeSinceStartup + 4f;
        }
        if (ok)
        {
            if (loadOriginalButton != null)
                loadOriginalButton.style.display = DisplayStyle.None;
            TogglePauseMenu();
            Time.timeScale = 1f;
            GameManager.Instance.UpdateGameState(GameManager.Instance.StateForMonth(GameManager.month));
        }
    }

    public void OnTreeButtonClick(ClickEvent evt){
        ToolManager.Instance.SelectTool(ToolType.SmallClippers);
        AudioManager.Instance.PlaySFX("Trim");
        MaybeShowTooltip("trim",
            "Trimming",
            "Click a branch tip to cut it. The parent node becomes a cut point — growth continues from there the following spring.\n\nTip: trimming in winter (Jan–Feb) is standard bonsai practice and gives the best recovery.");
    }

    public void OnPinchButtonClick(ClickEvent evt){
        ToolManager.Instance.SelectTool(ToolType.Pinch);
        AudioManager.Instance.PlaySFX("Trim");
        MaybeShowTooltip("pinch",
            "Pinching",
            "Click a soft growing tip to pinch it. This stops tip elongation and stimulates back-budding nearby, creating denser branching.\n\nBest used in active growth season (spring–summer).");
    }

    public void OnDefoliateButtonClick(ClickEvent evt){
        if (!IsDefoliationAllowed()) return;
        ToolManager.Instance.SelectTool(ToolType.Defoliate);
        MaybeShowTooltip("defoliate",
            "Defoliation",
            "Remove leaves to shock the tree into producing smaller replacement foliage and stronger ramification.\n\nOnly effective in June–July. Weakens the tree — do not defoliate if the tree is stressed.");
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
            MaybeShowTooltip("fertilize",
                "Fertilizing",
                "Fertilizer replenishes nutrient reserves that fuel growth and thickening.\n\n" +
                "A balanced fertilizer in spring and summer promotes strong growth. Switch to a low-nitrogen fertilizer in late summer to harden the wood before winter.\n\n" +
                "Do not fertilize in winter — dormant roots cannot absorb nutrients and excess salts will burn them. The fertilize button dims in Nov–Feb as a reminder.");
        }
    }

    void OnHerbicideButtonClick()
    {
        WeedManager.Instance?.HerbicideAll();
        MaybeShowTooltip("herbicide",
            "Weeding",
            "Weeds compete directly for nutrients and moisture in the small soil volume of a bonsai pot.\n\n" +
            "Remove them early — a weed allowed to flower and seed will spread across the pot surface. Herbicide kills all active weeds at once.\n\n" +
            "Check the soil surface after each watering. A layer of fine moss over the soil surface is the best long-term weed suppressor.");
    }

    void SelectPotSize(PotSoil.PotSize size)
    {
        pendingPotSize = size;
        if (potSizeLabel != null)
            potSizeLabel.text = $"Current: {size}";
        RefreshPotSizeButtons();
    }

    void RefreshPotSizeButtons()
    {
        var sizes = new[] { (potSizeXSButton, PotSoil.PotSize.XS), (potSizeSButton, PotSoil.PotSize.S),
                            (potSizeMButton,  PotSoil.PotSize.M),  (potSizeLButton,  PotSoil.PotSize.L),
                            (potSizeXLButton, PotSoil.PotSize.XL), (potSizeSlabButton, PotSoil.PotSize.Slab) };
        foreach (var (btn, sz) in sizes)
        {
            if (btn == null) continue;
            btn.style.backgroundColor = (sz == pendingPotSize)
                ? new UnityEngine.UIElements.StyleColor(new Color(0.40f, 0.55f, 0.35f))
                : new UnityEngine.UIElements.StyleColor(new Color(0.25f, 0.25f, 0.25f));
        }
    }

    void OnRepotButtonClick(PotSoil.SoilPreset preset)
    {
        if (skeleton == null) return;
        var potSoil = skeleton.GetComponent<PotSoil>();
        if (potSoil == null) return;

        // Pot-bound trees get the rake mini-game before new soil is applied
        if (skeleton.IsPotBound())
        {
            var rakeManager = skeleton.GetComponent<RootRakeManager>();
            if (rakeManager != null)
            {
                bool sizeChanged = pendingPotSize != potSoil.potSize;
                rakeManager.EnterRakeMode(preset, pendingPotSize, sizeChanged);
                return;
            }
        }

        // Normal (non-pot-bound) repot — apply immediately
        bool changed = pendingPotSize != potSoil.potSize;
        potSoil.Repot(skeleton, preset, pendingPotSize, changed);
        // Refresh leaf tint — repot stress may have changed node health
        skeleton.GetComponent<LeafManager>()?.RefreshFungalTint(skeleton);
    }

    void OnConfirmRepotClick()
    {
        if (skeleton == null) return;
        skeleton.GetComponent<RootRakeManager>()?.ConfirmRepot();
    }

    void OnCancelRakeClick()
    {
        if (skeleton == null) return;
        skeleton.GetComponent<RootRakeManager>()?.CancelRakeMode();
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
            GameManager.Instance.UpdateGameState(GameManager.Instance.StateForMonth(GameManager.month));
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
        // Clear active slot so the new tree won't overwrite the dead tree's save.
        SaveManager.ActiveSlotId = null;
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
        MaybeShowTooltip("water",
            "Watering",
            "Keep soil moisture in the healthy range (shown on the moisture bar). Too dry and the tree stresses; too wet and roots rot.\n\nTip: water less in winter when the tree is dormant — cold wet soil encourages root rot.");
    }

    public void OnWireButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.Wire);
        MaybeShowTooltip("wire",
            "Wiring",
            "Apply wire to guide a branch's direction over time. Click the base anchor, then click a tip to wrap the wire along the branch.\n\nCheck in spring — remove before the wire bites into bark.");
    }

    public void OnRemoveWireButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.RemoveWire);
    }

    public void OnRootPruneButtonClick(ClickEvent evt)
    {
        GameManager.Instance.ToggleRootPrune();
        MaybeShowTooltip("repot",
            "Repotting",
            "Lift the tree to manage its root system and repot into fresh soil.\n\nBest done in late winter (Jan–Feb). When roots are pot-bound, rake away the soil ball before repotting to straighten and refresh the roots.");
    }

    void UpdateRootHealthDisplay()
    {
        if (skeleton == null || rootHealthScoreLabel == null || rootHealthSectors == null) return;

        // Only show a score once the tree has root nodes; "—" avoids garbage values at startup.
        bool hasRoots = false;
        foreach (var n in skeleton.allNodes) if (n.isRoot && !n.isTrimmed) { hasRoots = true; break; }

        if (!hasRoots)
        {
            rootHealthScoreLabel.text = "—";
            return;
        }

        float raw = skeleton.RootHealthScore;
        rootHealthScoreLabel.text = float.IsNaN(raw) || float.IsInfinity(raw)
            ? "—"
            : Mathf.RoundToInt(raw).ToString();

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
        MaybeShowTooltip("paste",
            "Wound Paste",
            "Apply paste to open wounds (shown as coloured marks on cut surfaces) to prevent fungal infection and accelerate callus formation.\n\nAlways paste large cuts, especially heading cuts made in wet or cold seasons.");
    }

    public void OnAirLayerButtonClick(ClickEvent evt)
    {
        // If any layer is ready to sever, open the sever prompt first.
        if (skeleton != null && skeleton.HasSeverableLayer)
        {
            preSeverState = GameManager.Instance.state;
            GameManager.Instance.UpdateGameState(GameState.AirLayerSever);
            return;
        }
        ToolManager.Instance.SelectTool(ToolType.AirLayer);
        MaybeShowTooltip("airlayer",
            "Air Layering",
            "Mark a healthy branch to grow roots at a midpoint along its length. After several seasons of rooting, sever it to propagate a new tree.\n\nRequires patience — the layer needs to build a strong root mass before severing.");
    }

    void OnGraftButtonClick()
    {
        ToolManager.Instance.SelectTool(ToolType.Graft);
        MaybeShowTooltip("graft",
            "Approach Grafting",
            "Select a living branch tip as the source, then click a nearby node on a different branch as the target.\n\n" +
            "Over 2 growing seasons the source tip will grow toward the target and fuse, creating a living bridge between the two branches.\n\n" +
            "Tip: keep both nodes healthy during the fusion period — if either dies, the graft fails.");
    }

    void OnSeverConfirmClick()
    {
        var layer = skeleton?.GetFirstSeverableLayer();
        if (layer == null) { GameManager.Instance.UpdateGameState(preSeverState); return; }
        // SeverAirLayer saves original, loads new tree, transitions to Idle.
        skeleton.SeverAirLayer(layer);
    }

    void OnSeverCancelClick()
    {
        // Dismiss overlay, return to previous state (Idle, BranchGrow, etc.)
        GameManager.Instance.UpdateGameState(
            preSeverState == GameState.AirLayerSever ? GameState.Idle : preSeverState);
    }

    public void OnPlaceRockButtonClick(ClickEvent evt)
    {
        GameManager.Instance.UpdateGameState(GameState.RockPlace);
    }

    public void OnConfirmOrientButtonClick(ClickEvent evt)
    {
        GameManager.Instance.ConfirmRockOrient();
    }

    void OnCancelOrientButtonClick()
    {
        skeleton?.RestorePrePlacementSnapshot();
        GameManager.Instance.ToggleRootPrune();
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
            bool isDefault = (m == currentSort);
            SetSortBtnStyle(sortBtn, isDefault, sortDescending);
            if (isDefault) activeSortBtn = sortBtn;
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

        // Re-select the previously selected card after rebuild; auto-select first on first open
        VisualElement reselect  = null;
        VisualElement firstCard = null;
        TreeSpecies   firstSp   = null;
        foreach (var sp in SortedSpecies())
        {
            var card = MakeSpeciesCard(sp);
            speciesListContainer.Add(card);
            if (firstCard == null) { firstCard = card; firstSp = sp; }
            if (sp == selectedSpecies) reselect = card;
        }

        if (reselect != null && selectedSpecies != null)
        {
            selectedSpeciesCard = reselect;
            reselect.style.backgroundColor = new StyleColor(new Color(0.14f, 0.19f, 0.11f));
            SetCardBorderColor(reselect, new Color(0.898f, 0.702f, 0.086f));
        }
        else if (firstCard != null)
        {
            // Nothing previously selected — auto-select the top card
            SelectSpeciesCard(firstSp, firstCard);
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
        Debug.Log($"[Species] ConfirmClick | selectedSpecies={selectedSpecies?.speciesName ?? "NULL"} overlay={speciesSelectOverlay != null}");
        if (selectedSpecies == null) return;
        if (skeleton != null)
        {
            skeleton.species    = selectedSpecies;
            skeleton.treeOrigin = TreeOrigin.Seedling;
            skeleton.ApplySpecies();
        }
        string speciesName = selectedSpecies?.speciesName ?? "your tree";
        GameManager.Instance.ShowTooltip(
            $"Your {speciesName} is planted.",
            "Use the tool buttons on the left to water, trim, and train your tree as the seasons pass.\n\n" +
            "Time auto-slows in January — your pruning window. Use ▶▶/▶ in the corner to control speed.\n\n" +
            "Press the ◉ button to hide the tools or view health stats.");
    }

    const float TIMESCALE_MAX = 400f;
    static readonly float[] TimescaleNotches =
    {
        GameManager.TIMESCALE_MIN, // 1 game-min / real-sec
        0.5f,                      // 1 hr / 2 real-sec
        1f,                        // 1 hr / real-sec
        50f, 100f, 200f,
        TIMESCALE_MAX,
    };

    static float SliderToTimescale(float s) =>
        Mathf.Exp(Mathf.Lerp(Mathf.Log(GameManager.TIMESCALE_MIN), Mathf.Log(TIMESCALE_MAX), s));

    static float TimescaleToSlider(float ts) =>
        Mathf.InverseLerp(Mathf.Log(GameManager.TIMESCALE_MIN), Mathf.Log(TIMESCALE_MAX),
                          Mathf.Log(Mathf.Max(ts, GameManager.TIMESCALE_MIN)));

    static string FormatTimescaleLabel(float ts)
    {
        if (ts <= GameManager.TIMESCALE_MIN + 0.001f) return "1 min/s";
        if (Mathf.Abs(ts - 0.5f)  < 0.01f) return "1 hr/2s";
        if (Mathf.Abs(ts - 1f)    < 0.01f) return "1 hr/s";
        if (ts < 1f) return $"{ts:F2}×";
        return $"{Mathf.RoundToInt(ts)}×";
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
