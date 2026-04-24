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
    Button promoteButton;
    Button placeRockButton;
    Button confirmOrientButton;
    Button cancelOrientButton;
    VisualElement orientButtonContainer;
    Slider selectionRadiusSlider;

    VisualElement rootHealthPanel;
    Label         rootHealthScoreLabel;
    VisualElement rootHealthSectors;

    Label         debugStateLabel;
    bool          debugStateVisible = false;

    // ── Calendar ──────────────────────────────────────────────────────────────
    Button        calendarButton;
    VisualElement calendarOverlay;
    Label         calMonthYearLabel;
    Button        calPrevMonth, calNextMonth, calCloseButton;
    VisualElement calGrid;
    VisualElement calMonthView, calDayView, calEventView;
    // Day detail
    Label         calDayTitleLabel;
    VisualElement calDayEventList;
    Button        calAddWaterButton, calAddFertButton;
    // Event detail
    Label         calEventTitleLabel;
    VisualElement calRepeatOptions;
    Toggle        calRepeatToggle;
    Label         calRepeatNLabel;
    Button        calRepeatNDec, calRepeatNInc;
    VisualElement calFertTypeRow;
    Button        calDayBackButton, calEventBackButton;
    Button        calEventConfirmButton, calEventCancelButton;
    // State
    int           calViewMonth, calViewYear;
    int           calSelectedDay;
    ScheduledEventType calEditType;
    ScheduledEventAmount calEditAmount = ScheduledEventAmount.Light;
    int           calEditFertType = 0;
    TimeOfDay     calEditTimeOfDay = TimeOfDay.Morning;
    Season        calEditSeason = Season.AllYear;
    RepeatMode    calEditRepeat = RepeatMode.Once;
    int           calEditRepeatN = 1;

    // Calendar tab strip
    Button        calTabSchedule, calTabModes, calTabSpeed;
    VisualElement calScheduleTab, calModesTab, calSpeedTab;

    // Modes tab
    VisualElement calModeChips, calRulesList, calAddRulePanel, calModeSettings;
    Button        calAddRuleButton, calModeResetButton, calRuleConfirmButton, calRuleCancelButton;
    Toggle        calModeAutoWater, calModeAutoFert, calModeIdleOrbit, calRuleIdleToggle;
    UnityEngine.UIElements.IntegerField calModeOrbitDelay;
    UnityEngine.UIElements.DropdownField calRuleTriggerDropdown;
    VisualElement calRuleParamRow, calRuleSpeedChips;
    Slider        calRuleParamSlider;
    Label         calRuleParamLabel, calRuleParamValueLabel;
    UnityEngine.UIElements.FloatField calRuleIdleReal, calRuleIdleDays;
    GameManager.SpeedMode calEditRuleSpeed = GameManager.SpeedMode.Slow;
    VisualElement calModeSpeedChips;

    // Speed tab
    Slider calSpeedSlowSlider, calSpeedMedSlider, calSpeedFastSlider;
    Label  calSpeedSlowValue, calSpeedMedValue, calSpeedFastValue;
    Label  calSpeedSlowHint,  calSpeedMedHint,  calSpeedFastHint;
    Button calSpeedResetButton;

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

    // ── Rock size selection ───────────────────────────────────────────────────
    VisualElement rockSizePanel;
    Button        rockSizeSButton, rockSizeMButton, rockSizeLButton, rockSizeXLButton;

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
    // ID of the tooltip currently on screen — saved to PlayerPrefs only when dismissed.
    string pendingTooltipId;
    // Seasonal care tips repeat every year — track which season was last shown this session.
    // When true, dismissing the current tooltip opens the calendar instead of resuming.
    bool pendingCalendarAfterTip;

    // ── Undo indicator ───────────────────────────────────────────────────────
    Label         undoLabel;

    // ── Speed toggle ──────────────────────────────────────────────────────────
    Button speedToggleButton;

    // ── UI cycle toggle (tools → stats → neither → …) ────────────────────────
    enum UIToggleState { Tools, Stats, Neither }
    UIToggleState uiToggleState = UIToggleState.Tools;
    Button        uiToggleButton;

    // ── Tree stats panel ──────────────────────────────────────────────────────
    VisualElement treeStatsPanel;
    VisualElement promotionAdvisorPanel;
    Label         promotionTargetLabel;
    VisualElement promotionListContainer;
    TreeInteraction treeInteraction;
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
    Toggle        toggleScaleCubes;

    // Set to true to re-enable first-use tooltips across the whole UI.
    public static bool tooltipsEnabled = false;

    // Sliders + value labels
    Slider sliderTimescale;      Label labelTimescale;
    Toggle toggleQuickWinter;
    Label  labelSelectionRadius;
    Button saveButton;
    Button loadButton;
    Button loadOriginalButton;
    Label  saveStatusLabel;
    float  saveStatusClearTime = 0f;
    Label  saveToastLabel;
    float  saveToastFadeEndTime = 0f;
    const float TOAST_HOLD = 2f;
    const float TOAST_FADE = 0.4f;
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
        promoteButton       = root.Q("PromoteButton")       as Button;
        promotionAdvisorPanel  = root.Q("PromotionAdvisorPanel");
        promotionTargetLabel   = root.Q("PromotionTargetLabel")   as Label;
        promotionListContainer = root.Q("PromotionListContainer");
        treeInteraction = skeleton != null ? skeleton.GetComponent<TreeInteraction>() : null;
        placeRockButton     = root.Q("PlaceRockButton")     as Button;
        debugStateLabel     = root.Q("DebugStateLabel")     as Label;

        // Calendar
        calendarButton      = root.Q("CalendarButton")      as Button;
        calendarOverlay     = root.Q("CalendarOverlay");
        calMonthYearLabel   = root.Q("CalMonthYearLabel")   as Label;
        calPrevMonth        = root.Q("CalPrevMonth")        as Button;
        calNextMonth        = root.Q("CalNextMonth")        as Button;
        calCloseButton      = root.Q("CalCloseButton")      as Button;
        calGrid             = root.Q("CalGrid");
        calMonthView        = root.Q("CalMonthView");
        calDayView          = root.Q("CalDayView");
        calEventView        = root.Q("CalEventView");
        calDayTitleLabel    = root.Q("CalDayTitleLabel")    as Label;
        calDayEventList     = root.Q("CalDayEventList");
        calAddWaterButton   = root.Q("CalAddWaterButton")   as Button;
        calAddFertButton    = root.Q("CalAddFertButton")    as Button;
        calEventTitleLabel  = root.Q("CalEventTitleLabel")  as Label;
        calRepeatToggle     = root.Q("CalRepeatToggle")     as Toggle;
        calRepeatOptions    = root.Q("CalRepeatOptions");
        calRepeatNLabel     = root.Q("CalRepeatNLabel")     as Label;
        calRepeatNDec       = root.Q("CalRepeatNDec")       as Button;
        calRepeatNInc       = root.Q("CalRepeatNInc")       as Button;
        calFertTypeRow          = root.Q("CalFertTypeRow");
        calDayBackButton        = root.Q("CalDayBackButton")        as Button;
        calEventBackButton      = root.Q("CalEventBackButton")      as Button;
        calEventConfirmButton   = root.Q("CalEventConfirmButton")   as Button;
        calEventCancelButton    = root.Q("CalEventCancelButton")    as Button;

        // Calendar tabs
        calTabSchedule  = root.Q("CalTabSchedule")  as Button;
        calTabModes     = root.Q("CalTabModes")     as Button;
        calTabSpeed     = root.Q("CalTabSpeed")     as Button;
        calScheduleTab  = root.Q("CalScheduleTab");
        calModesTab     = root.Q("CalModesTab");
        calSpeedTab     = root.Q("CalSpeedTab");

        // Modes tab
        calModeChips        = root.Q("CalModeChips");
        calModeSettings     = root.Q("CalModeSettings");
        calModeSpeedChips   = root.Q("CalModeSpeedChips");
        calModeAutoWater    = root.Q("CalModeAutoWater")    as Toggle;
        calModeAutoFert     = root.Q("CalModeAutoFert")     as Toggle;
        calModeIdleOrbit    = root.Q("CalModeIdleOrbit")    as Toggle;
        calModeOrbitDelay   = root.Q("CalModeOrbitDelay")   as UnityEngine.UIElements.IntegerField;
        calRulesList        = root.Q("CalRulesList");
        calAddRuleButton    = root.Q("CalAddRuleButton")    as Button;
        calAddRulePanel     = root.Q("CalAddRulePanel");
        calModeResetButton  = root.Q("CalModeResetButton")  as Button;
        calRuleTriggerDropdown  = root.Q("CalRuleTriggerDropdown")  as UnityEngine.UIElements.DropdownField;
        calRuleParamRow         = root.Q("CalRuleParamRow");
        calRuleParamSlider      = root.Q("CalRuleParamSlider")      as Slider;
        calRuleParamLabel       = root.Q("CalRuleParamLabel")       as Label;
        calRuleParamValueLabel  = root.Q("CalRuleParamValueLabel")  as Label;
        calRuleSpeedChips       = root.Q("CalRuleSpeedChips");
        calRuleIdleToggle       = root.Q("CalRuleIdleToggle")       as Toggle;
        calRuleIdleReal         = root.Q("CalRuleIdleReal")         as UnityEngine.UIElements.FloatField;
        calRuleIdleDays         = root.Q("CalRuleIdleDays")         as UnityEngine.UIElements.FloatField;
        calRuleConfirmButton    = root.Q("CalRuleConfirmButton")    as Button;
        calRuleCancelButton     = root.Q("CalRuleCancelButton")     as Button;

        // Speed tab
        calSpeedSlowSlider  = root.Q("CalSpeedSlowSlider")  as Slider;
        calSpeedMedSlider   = root.Q("CalSpeedMedSlider")   as Slider;
        calSpeedFastSlider  = root.Q("CalSpeedFastSlider")  as Slider;
        calSpeedSlowValue   = root.Q("CalSpeedSlowValue")   as Label;
        calSpeedMedValue    = root.Q("CalSpeedMedValue")    as Label;
        calSpeedFastValue   = root.Q("CalSpeedFastValue")   as Label;
        calSpeedSlowHint    = root.Q("CalSpeedSlowHint")    as Label;
        calSpeedMedHint     = root.Q("CalSpeedMedHint")     as Label;
        calSpeedFastHint    = root.Q("CalSpeedFastHint")    as Label;
        calSpeedResetButton = root.Q("CalSpeedResetButton") as Button;

        calendarButton?.RegisterCallback<ClickEvent>(_ => OpenCalendar());
        calCloseButton?.RegisterCallback<ClickEvent>(_ => CloseCalendar());
        calPrevMonth?.RegisterCallback<ClickEvent>(_ => { calViewMonth--; if (calViewMonth < 1) { calViewMonth = 12; calViewYear--; } BuildCalendarGrid(); });
        calNextMonth?.RegisterCallback<ClickEvent>(_ => { calViewMonth++; if (calViewMonth > 12) { calViewMonth = 1; calViewYear++; } BuildCalendarGrid(); });
        calDayBackButton?.RegisterCallback<ClickEvent>(_ => CalShowView(calMonthView));
        calAddWaterButton?.RegisterCallback<ClickEvent>(_ => OpenEventDetail(ScheduledEventType.Water));
        calAddFertButton?.RegisterCallback<ClickEvent>(_ => OpenEventDetail(ScheduledEventType.Fertilize));
        calEventBackButton?.RegisterCallback<ClickEvent>(_ => CalShowView(calDayView));
        calRepeatToggle?.RegisterValueChangedCallback(evt => { if (calRepeatOptions != null) calRepeatOptions.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None; });

        WireCalendarChips();

        calEventConfirmButton?.RegisterCallback<ClickEvent>(_ => ConfirmCalendarEvent());
        calEventCancelButton?.RegisterCallback<ClickEvent>(_ => CalShowView(calDayView));

        // Calendar tab strip
        calTabSchedule?.RegisterCallback<ClickEvent>(_ => SwitchCalTab(0));
        calTabModes?.RegisterCallback<ClickEvent>(_ =>    SwitchCalTab(1));
        calTabSpeed?.RegisterCallback<ClickEvent>(_ =>    SwitchCalTab(2));

        // Modes tab wiring
        calModeAutoWater?.RegisterValueChangedCallback(evt  => { var m = PlayModeManager.Instance?.ActiveMode; if (m != null) { m.autoWater    = evt.newValue; PlayModeManager.Instance.SaveModes(); } });
        calModeAutoFert?.RegisterValueChangedCallback(evt   => { var m = PlayModeManager.Instance?.ActiveMode; if (m != null) { m.autoFertilize = evt.newValue; PlayModeManager.Instance.SaveModes(); } });
        calModeIdleOrbit?.RegisterValueChangedCallback(evt  => { var m = PlayModeManager.Instance?.ActiveMode; if (m != null) { m.idleOrbit    = evt.newValue; PlayModeManager.Instance.SaveModes(); } });
        calModeOrbitDelay?.RegisterValueChangedCallback(evt => { var m = PlayModeManager.Instance?.ActiveMode; if (m != null) { m.idleOrbitDelaySecs = evt.newValue; PlayModeManager.Instance.SaveModes(); } });
        calAddRuleButton?.RegisterCallback<ClickEvent>(_ =>    { if (calAddRulePanel != null) calAddRulePanel.style.display = DisplayStyle.Flex; });
        calRuleCancelButton?.RegisterCallback<ClickEvent>(_ => { if (calAddRulePanel != null) calAddRulePanel.style.display = DisplayStyle.None; });
        calRuleConfirmButton?.RegisterCallback<ClickEvent>(_ => ConfirmAddRule());
        calModeResetButton?.RegisterCallback<ClickEvent>(_ =>  { PlayModeManager.Instance?.ResetBuiltInModes(); RefreshModesTab(); });

        // Rule trigger dropdown choices
        if (calRuleTriggerDropdown != null)
        {
            calRuleTriggerDropdown.choices = new System.Collections.Generic.List<string>
            {
                "Month", "Season",
                "Moisture below", "Health below", "Nutrient below",
                "Fungal load above", "Weed count above",
                "Wire set gold", "Tree in danger"
            };
            calRuleTriggerDropdown.index = 0;
            calRuleTriggerDropdown.RegisterValueChangedCallback(_ => RefreshRuleParamRow());
        }
        if (calRuleParamSlider != null)
            calRuleParamSlider.RegisterValueChangedCallback(evt =>
            {
                if (calRuleParamValueLabel != null)
                    calRuleParamValueLabel.text = FormatRuleParam(calRuleTriggerDropdown?.index ?? 0, evt.newValue);
            });

        // Rule speed chips
        WireChipGroup(calRuleSpeedChips, new[]{"CalRuleSpeedSlow","CalRuleSpeedMed","CalRuleSpeedFast"}, root, i =>
        {
            calEditRuleSpeed = i == 0 ? GameManager.SpeedMode.Slow : i == 1 ? GameManager.SpeedMode.Med : GameManager.SpeedMode.Fast;
        });

        // Mode default speed chips
        WireChipGroup(calModeSpeedChips, new[]{"CalModeSpeedSlow","CalModeSpeedMed","CalModeSpeedFast"}, root, i =>
        {
            var m = PlayModeManager.Instance?.ActiveMode;
            if (m == null) return;
            m.defaultSpeed = i == 0 ? GameManager.SpeedMode.Slow : i == 1 ? GameManager.SpeedMode.Med : GameManager.SpeedMode.Fast;
            PlayModeManager.Instance.SaveModes();
        });

        // Speed tab wiring
        if (calSpeedSlowSlider != null) calSpeedSlowSlider.RegisterValueChangedCallback(evt => OnSpeedSliderChanged());
        if (calSpeedMedSlider  != null) calSpeedMedSlider.RegisterValueChangedCallback(evt  => OnSpeedSliderChanged());
        if (calSpeedFastSlider != null) calSpeedFastSlider.RegisterValueChangedCallback(evt => OnSpeedSliderChanged());
        calSpeedResetButton?.RegisterCallback<ClickEvent>(_ =>
        {
            GameManager.TIMESCALE_SLOW = 0.5f;
            GameManager.TIMESCALE_MED  = 10f;
            GameManager.TIMESCALE_FAST = 200f;
            GameManager.SaveTimescalePrefs();
            if (calSpeedSlowSlider != null) calSpeedSlowSlider.SetValueWithoutNotify(0.5f);
            if (calSpeedMedSlider  != null) calSpeedMedSlider.SetValueWithoutNotify(10f);
            if (calSpeedFastSlider != null) calSpeedFastSlider.SetValueWithoutNotify(200f);
            RefreshSpeedTab();
        });
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
        rockSizePanel         = root.Q("RockSizePanel");
        rockSizeSButton       = root.Q("RockSizeSButton")      as Button;
        rockSizeMButton       = root.Q("RockSizeMButton")      as Button;
        rockSizeLButton       = root.Q("RockSizeLButton")      as Button;
        rockSizeXLButton      = root.Q("RockSizeXLButton")     as Button;

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
            // Scrub any weed_first IDs burned by the old save-on-show bug.
            if (shownTooltips.RemoveWhere(id => id.StartsWith("weed_first")) > 0)
                SaveShownTooltips();
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
        saveToastLabel       = root.Q("SaveToastLabel")       as Label;

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
        promoteButton?.RegisterCallback<ClickEvent>(_ => OnPromoteButtonClick());
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

        rockSizeSButton?.RegisterCallback<ClickEvent>(_ => SelectRockSize(RockPlacer.RockSize.S));
        rockSizeMButton?.RegisterCallback<ClickEvent>(_ => SelectRockSize(RockPlacer.RockSize.M));
        rockSizeLButton?.RegisterCallback<ClickEvent>(_ => SelectRockSize(RockPlacer.RockSize.L));
        rockSizeXLButton?.RegisterCallback<ClickEvent>(_ => SelectRockSize(RockPlacer.RockSize.XL));

        confirmRepotButton?.RegisterCallback<ClickEvent>(_ => OnConfirmRepotClick());
        cancelRakeButton?.RegisterCallback<ClickEvent>(_ => OnCancelRakeClick());

        tooltipDismissButton?.RegisterCallback<ClickEvent>(_ =>
        {
            MarkPendingTooltipSeen();
            GameManager.Instance?.ExitTipPause();
            if (pendingCalendarAfterTip) { pendingCalendarAfterTip = false; OpenCalendar(); }
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

        GameManager.OnGameStateChanged   += OnGameStateChanged;
        GameManager.OnMonthChanged       += OnMonthChanged;
        if (skeleton != null) skeleton.OnWireSetGold += OnWireSetGold;
        WeedManager.OnFirstWeedSpawned   += OnFirstWeedSpawned;
        SaveManager.OnSaved              += ShowSaveToast;

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
        GameManager.OnGameStateChanged   -= OnGameStateChanged;
        GameManager.OnMonthChanged       -= OnMonthChanged;
        if (skeleton != null) skeleton.OnWireSetGold -= OnWireSetGold;
        WeedManager.OnFirstWeedSpawned   -= OnFirstWeedSpawned;
        SaveManager.OnSaved              -= ShowSaveToast;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            debugStateVisible = !debugStateVisible;
            if (debugStateLabel != null)
                debugStateLabel.style.display = debugStateVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (debugStateVisible && debugStateLabel != null)
            debugStateLabel.text = GameManager.Instance != null ? GameManager.Instance.state.ToString() : "—";

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            var s = GameManager.Instance?.state ?? GameState.Idle;
            if (s == GameState.CalendarOpen)
                CloseCalendar();
            else if (s == GameState.LoadMenu && loadMenuHasBack)
                OnLoadMenuBack();
            else if (s == GameState.TipPause && !string.IsNullOrEmpty(GameManager.TooltipTitle))
            {
                MarkPendingTooltipSeen();
                GameManager.Instance.ExitTipPause();
                if (pendingCalendarAfterTip) { pendingCalendarAfterTip = false; OpenCalendar(); }
            }
            else if (s != GameState.LoadMenu && s != GameState.SpeciesSelect &&
                     s != GameState.TipPause && s != GameState.TreeDead && s != GameState.AirLayerSever)
                TogglePauseMenu();
        }

        // Save toast fade
        if (saveToastLabel != null && saveToastFadeEndTime > 0f)
        {
            float t = saveToastFadeEndTime - Time.realtimeSinceStartup;
            if (t <= 0f)
            {
                saveToastLabel.style.display = DisplayStyle.None;
                saveToastLabel.style.opacity = 0f;
                saveToastFadeEndTime = 0f;
            }
            else if (t < TOAST_FADE)
                saveToastLabel.style.opacity = new StyleFloat(t / TOAST_FADE);
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

                // Sync rock size chips to the current rock
                var placer = FindFirstObjectByType<RockPlacer>();
                if (placer != null) RefreshRockSizeButtons(placer.rockSize);

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

        // Promotion advisor panel: show while promote tool is active and a target is locked
        if (promotionAdvisorPanel != null)
        {
            bool hasTarget = GameManager.canPromote && treeInteraction != null && treeInteraction.LockedPromoteTarget != null;
            promotionAdvisorPanel.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasTarget) RefreshPromotionPanel();
        }

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
    bool MaybeShowTooltip(string id, string title, string body)
    {
        if (!tooltipsEnabled) return false;
        if (shownTooltips == null || shownTooltips.Contains(id)) return false;
        if (GameManager.Instance == null) return false;
        // Don't clobber an active tooltip — ShowTooltip would return early anyway,
        // and setting pendingTooltipId here would cause the wrong ID to be burned
        // when the visible tooltip is dismissed.
        if (GameManager.Instance.state == GameState.TipPause) return false;
        pendingTooltipId = id;
        GameManager.Instance.ShowTooltip(title, body);
        return true;
    }

    void MarkPendingTooltipSeen()
    {
        if (string.IsNullOrEmpty(pendingTooltipId)) return;
        shownTooltips?.Add(pendingTooltipId);
        SaveShownTooltips();
        pendingTooltipId = null;
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
        Set(pasteButton); Set(rootPruneButton); Set(airLayerButton); Set(graftButton); Set(promoteButton);

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

        // One-time moisture tutorial: fires the first time moisture drops below 50%.
        if (skeleton != null && skeleton.soilMoisture < 0.5f)
        {
            MaybeShowTooltip("moisture_low_v1",
                "Soil Getting Dry — Use the Calendar",
                "Your soil moisture has dropped below 50%. The tree will stress if it falls much further.\n\n" +
                "WATERING GUIDE (spring):\n" +
                "• Water when moisture drops below 40–50%.\n" +
                "• In spring, that's roughly every 3–5 days.\n" +
                "• Aim to keep moisture in the 50–80% range.\n" +
                "• Amount setting: Medium.\n\n" +
                "FERTILIZING GUIDE (spring):\n" +
                "• Fertilize once every 3–4 weeks during active growth.\n" +
                "• Spring is the most important feeding window — buds are pushing and roots are hungry.\n" +
                "• Use a balanced or nitrogen-forward feed. Don't over-fertilize: too much nitrogen grows weak, oversized leaves.\n" +
                "• Amount setting: Light to Medium.\n\n" +
                "USE THE CALENDAR:\n" +
                "Open the calendar (📅 button) to schedule automatic watering and fertilizing. Set a watering event to repeat every 4 days and a fertilizer event every 3 weeks — then you won't have to think about it.");
        }

        // Seasonal care tips — show only the first time each season is entered (ever).
        if (month == 3 || month == 6 || month == 9 || month == 12)
        {
            string seasonId, seasonTitle, seasonBody;
            switch (month)
            {
                case 3:
                    seasonId    = "season_spring";
                    seasonTitle = "Spring Care";
                    seasonBody  =
                        "Spring is the most active growth period. The tree is hungry and thirsty.\n\n" +
                        "WATERING: Water every 3–5 days. Keep moisture in the 50–80% range. " +
                        "The soil should dry slightly between waterings — never let it stay soggy.\n" +
                        "Amount setting: Medium.\n\n" +
                        "FERTILIZING: Feed every 3–4 weeks with a balanced or nitrogen-forward fertilizer. " +
                        "This fuels new shoots and root expansion. Don't skip spring feeding.\n" +
                        "Amount setting: Light to Medium.";
                    break;
                case 6:
                    seasonId    = "season_summer";
                    seasonTitle = "Summer Care";
                    seasonBody  =
                        "Heat accelerates evaporation. The tree may need water more often now.\n\n" +
                        "WATERING: Check moisture every 2–3 days. In hot weather the pot can dry out fast — " +
                        "increase your calendar schedule if moisture is dropping below 40% regularly.\n" +
                        "Amount setting: Medium to Heavy if drying fast.\n\n" +
                        "FERTILIZING: Reduce to a half-strength dose or switch to a low-nitrogen feed. " +
                        "Heavy feeding in peak summer pushes soft growth that burns easily.\n" +
                        "Amount setting: Light.";
                    break;
                case 9:
                    seasonId    = "season_autumn";
                    seasonTitle = "Autumn Care";
                    seasonBody  =
                        "Growth is slowing. The tree is hardening off and moving energy to the roots.\n\n" +
                        "WATERING: Reduce frequency. Water every 5–7 days or when moisture drops below 40%. " +
                        "Cooler temps mean slower evaporation — over-watering in autumn encourages root rot.\n" +
                        "Amount setting: Light.\n\n" +
                        "FERTILIZING: One last feed with a low-nitrogen, high-phosphorus fertilizer. " +
                        "This strengthens roots and wood rather than pushing soft leafy growth. " +
                        "Stop fertilizing entirely by late October.\n" +
                        "Amount setting: Light.";
                    break;
                default: // 12
                    seasonId    = "season_winter";
                    seasonTitle = "Winter Care";
                    seasonBody  =
                        "The tree is dormant. Metabolism is at its lowest — it barely needs anything.\n\n" +
                        "WATERING: Water sparingly — only when moisture drops below 30%. " +
                        "Cold wet soil is the most common cause of winter root rot. " +
                        "Check every 1–2 weeks; most species only need water once a week or less.\n" +
                        "Amount setting: Light.\n\n" +
                        "FERTILIZING: Do not fertilize in winter. The tree cannot process nutrients while dormant — " +
                        "salts will build up in the soil and burn roots. Resume feeding when buds start to swell in early spring.";
                    break;
            }

            if (MaybeShowTooltip(seasonId, seasonTitle, seasonBody))
                pendingCalendarAfterTip = true;
        }
    }

    // ── Button swap logic ────────────────────────────────────────────────────

    static bool IsNormalGameplayState(GameState s) =>
        s == GameState.Idle      || s == GameState.BranchGrow ||
        s == GameState.TimeGo    || s == GameState.LeafGrow   ||
        s == GameState.LeafFall  || s == GameState.Pruning    ||
        s == GameState.Shaping   || s == GameState.Wiring;

    void OnGameStateChanged(GameState state)
    {
        // Each time the game settles into a normal gameplay state, check for weeds.
        // MaybeShowTooltip's shownTooltips guard ensures the tooltip only ever shows once.
        if (IsNormalGameplayState(state) &&
            WeedManager.Instance != null && WeedManager.Instance.ActiveWeedCount > 0)
            OnFirstWeedSpawned();

        // First-use tooltip overlay: visible in TipPause when a tooltip title is set.
        // Use the ACTUAL current state, not the event arg — a nested UpdateGameState(TipPause)
        // call (e.g. from a weed spawn) may have already advanced the state before this
        // listener fires, and we must not hide the overlay based on a stale event arg.
        if (tooltipOverlay != null)
        {
            var actualState = GameManager.Instance != null ? GameManager.Instance.state : state;
            bool showTip = actualState == GameState.TipPause && !string.IsNullOrEmpty(GameManager.TooltipTitle);
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
            airLayerButton, graftButton, promoteButton, placeRockButton,
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
            bool showTools = uiToggleState == UIToggleState.Tools;
            if (airLayerButton  != null) airLayerButton.style.display  = (inRootLift || !showTools) ? DisplayStyle.None : DisplayStyle.Flex;
            if (graftButton     != null) graftButton.style.display     = (inRootLift || !showTools) ? DisplayStyle.None : DisplayStyle.Flex;
            if (promoteButton   != null) promoteButton.style.display   = (inRootLift || !showTools) ? DisplayStyle.None : DisplayStyle.Flex;
            if (placeRockButton != null) placeRockButton.style.display = inRootPrune ? DisplayStyle.Flex : DisplayStyle.None;
            if (rockSizePanel   != null) rockSizePanel.style.display   = inRootPrune ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Confirm / Cancel orient buttons — visible only during rock placement / tree orient
        if (orientButtonContainer != null)
            orientButtonContainer.style.display = inPlacement ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── Save toast ────────────────────────────────────────────────────────────

    void ShowSaveToast(string message)
    {
        if (saveToastLabel == null) return;
        saveToastLabel.text                = message;
        saveToastLabel.style.display       = DisplayStyle.Flex;
        saveToastLabel.style.opacity       = new StyleFloat(1f);
        saveToastFadeEndTime               = Time.realtimeSinceStartup + TOAST_HOLD + TOAST_FADE;
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

    void SelectRockSize(RockPlacer.RockSize size)
    {
        var placer = FindFirstObjectByType<RockPlacer>();
        if (placer == null) return;
        placer.rockSize = size;
        placer.ApplyRockSize();
        RefreshRockSizeButtons(size);
    }

    void RefreshRockSizeButtons(RockPlacer.RockSize active)
    {
        var chips = new[] {
            (rockSizeSButton,  RockPlacer.RockSize.S),
            (rockSizeMButton,  RockPlacer.RockSize.M),
            (rockSizeLButton,  RockPlacer.RockSize.L),
            (rockSizeXLButton, RockPlacer.RockSize.XL),
        };
        foreach (var (btn, sz) in chips)
        {
            if (btn == null) continue;
            bool isActive = sz == active;
            btn.style.backgroundColor = isActive
                ? new UnityEngine.UIElements.StyleColor(new Color(0.40f, 0.55f, 0.35f))
                : new UnityEngine.UIElements.StyleColor(new Color(0.25f, 0.25f, 0.25f));
            btn.style.color = isActive
                ? new UnityEngine.UIElements.StyleColor(new Color(0.90f, 0.70f, 0.07f))
                : new UnityEngine.UIElements.StyleColor(new Color(0.78f, 0.78f, 0.78f));
        }
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
            // Remove dead tree mesh and all node state so InitTree fires fresh on next Water.
            skeleton.ClearForRestart();
            skeleton.GetComponent<TreeMeshBuilder>()?.SetDeadTint(false);
            skeleton.treeInDanger              = false;
            skeleton.consecutiveCriticalSeasons = 0;
            // Reset drought state so KillTree can't re-fire before species is chosen.
            skeleton.soilMoisture              = 0.5f;
            skeleton.droughtDaysAccumulated    = 0f;
        }
        // Reset waterings so the first Water click re-enters the new-tree InitTree path.
        GameManager.waterings = -1;
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

    void OnFirstWeedSpawned()
    {
        Debug.Log($"[WeedTip] OnFirstWeedSpawned — state={GameManager.Instance?.state} activeWeeds={WeedManager.Instance?.ActiveWeedCount}");
        MaybeShowTooltip("weed_first",
            "Weeds Appeared!",
            "Weeds compete with your tree for water and nutrients — remove them promptly.\n\n" +
            "Click and drag a weed upward to pull it out. Pull slowly and steadily: yanking too hard rips the stem and leaves the root behind, making it harder to fully remove.\n\n" +
            "Herbicide button: kills all weeds instantly but damages beneficial soil fungi and dries out the soil. Use it as a last resort when weeds are out of control, not as routine maintenance.");
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

    void OnPromoteButtonClick()
    {
        ToolManager.Instance.SelectTool(ToolType.Promote);
        MaybeShowTooltip("promote",
            "Branch Promotion Advisor",
            "Click any terminal branch tip to set it as your promotion target.\n\n" +
            "The advisor scores all competing branches and marks them:\n" +
            "  Cyan circle  — your target tip\n" +
            "  Red diamond  — Remove (strongest competitor)\n" +
            "  Gold diamond — Trim back (shorten, don't remove)\n" +
            "  Lime diamond — Pinch (mild tip suppression)\n\n" +
            "Click a different tip to change the target. Right-click or ESC to clear.");
    }

    void RefreshPromotionPanel()
    {
        if (treeInteraction == null || promotionListContainer == null) return;
        var target = treeInteraction.LockedPromoteTarget;
        var scores = treeInteraction.PromotionScores;
        if (target == null) return;

        if (promotionTargetLabel != null)
            promotionTargetLabel.text = $"Target: branch #{target.id}  (depth {target.depth})";

        promotionListContainer.Clear();
        int shown = 0;
        foreach (var (node, score) in scores)
        {
            if (shown >= 8) break;
            string action = TreeSkeleton.PromotionAction(node, score);
            string season = TreeSkeleton.BestPromotionSeason(node, action);
            UnityEngine.Color rowColor = action == "Remove"    ? new UnityEngine.Color(0.9f, 0.35f, 0.35f)
                                       : action == "Pinch"     ? new UnityEngine.Color(0.55f, 1.0f, 0.15f)
                                       : /* Trim back */         new UnityEngine.Color(0.9f, 0.65f, 0.1f);
            var row = new UnityEngine.UIElements.VisualElement();
            row.style.flexDirection  = UnityEngine.UIElements.FlexDirection.Row;
            row.style.justifyContent = UnityEngine.UIElements.Justify.SpaceBetween;
            row.style.marginBottom   = 3;
            var lbl = new UnityEngine.UIElements.Label($"{action}  #{node.id}  d{node.depth}  {score:F2}");
            lbl.style.color    = new UnityEngine.UIElements.StyleColor(rowColor);
            lbl.style.fontSize = 10;
            var seasonLbl = new UnityEngine.UIElements.Label(season);
            seasonLbl.style.color    = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(0.6f, 0.6f, 0.6f));
            seasonLbl.style.fontSize = 9;
            row.Add(lbl);
            row.Add(seasonLbl);
            promotionListContainer.Add(row);
            shown++;
        }
        if (shown == 0)
        {
            var empty = new UnityEngine.UIElements.Label("No competing branches found.");
            empty.style.color    = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(0.5f, 0.5f, 0.5f));
            empty.style.fontSize = 10;
            promotionListContainer.Add(empty);
        }
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

    // ── Calendar ──────────────────────────────────────────────────────────────

    static readonly string[] MonthNames =
        { "", "January", "February", "March", "April", "May", "June",
          "July", "August", "September", "October", "November", "December" };

    // ── Calendar tab switching ────────────────────────────────────────────────

    void SwitchCalTab(int tab)
    {
        if (calScheduleTab != null) calScheduleTab.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (calModesTab    != null) calModesTab.style.display    = tab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        if (calSpeedTab    != null) calSpeedTab.style.display    = tab == 2 ? DisplayStyle.Flex : DisplayStyle.None;

        SetCalTabActive(calTabSchedule, tab == 0);
        SetCalTabActive(calTabModes,    tab == 1);
        SetCalTabActive(calTabSpeed,    tab == 2);

        // Also hide prev/next month buttons outside schedule tab
        var showNav = tab == 0;
        if (calPrevMonth != null) calPrevMonth.style.display = showNav ? DisplayStyle.Flex : DisplayStyle.None;
        if (calNextMonth != null) calNextMonth.style.display = showNav ? DisplayStyle.Flex : DisplayStyle.None;

        if (tab == 1) RefreshModesTab();
        if (tab == 2) RefreshSpeedTab();
    }

    void SetCalTabActive(Button btn, bool active)
    {
        if (btn == null) return;
        btn.style.backgroundColor = active
            ? new StyleColor(new UnityEngine.Color(60/255f, 80/255f, 40/255f))
            : new StyleColor(new UnityEngine.Color(30/255f, 30/255f, 30/255f));
        btn.style.color = active
            ? new StyleColor(new UnityEngine.Color(220/255f, 220/255f, 160/255f))
            : new StyleColor(new UnityEngine.Color(140/255f, 140/255f, 140/255f));
    }

    // ── Modes tab ─────────────────────────────────────────────────────────────

    void RefreshModesTab()
    {
        var pm = PlayModeManager.Instance;
        if (pm == null || calModeChips == null) return;

        // Rebuild mode chips
        calModeChips.Clear();
        for (int i = 0; i < pm.modes.Count; i++)
        {
            int idx = i;
            var mode = pm.modes[i];
            bool active = idx == pm.activeModeIndex;
            var btn = new Button(() => { pm.SetActiveMode(idx); RefreshModesTab(); }) { text = mode.name };
            btn.style.height = 28;
            btn.style.paddingLeft  = btn.style.paddingRight = 10;
            btn.style.marginRight  = 4;
            btn.style.marginBottom = 4;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
            btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 4;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
            btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.backgroundColor = active
                ? new StyleColor(new UnityEngine.Color(229/255f, 179/255f, 16/255f))
                : new StyleColor(new UnityEngine.Color(40/255f, 40/255f, 40/255f));
            btn.style.color = active
                ? new StyleColor(new UnityEngine.Color(10/255f, 10/255f, 10/255f))
                : new StyleColor(new UnityEngine.Color(160/255f, 160/255f, 160/255f));
            btn.style.fontSize = 11;
            calModeChips.Add(btn);
        }

        // Populate mode settings from active mode
        var m = pm.ActiveMode;
        if (m == null) return;

        if (calModeAutoWater  != null) calModeAutoWater.SetValueWithoutNotify(m.autoWater);
        if (calModeAutoFert   != null) calModeAutoFert.SetValueWithoutNotify(m.autoFertilize);
        if (calModeIdleOrbit  != null) calModeIdleOrbit.SetValueWithoutNotify(m.idleOrbit);
        if (calModeOrbitDelay != null) calModeOrbitDelay.SetValueWithoutNotify((int)m.idleOrbitDelaySecs);

        // Default speed chips
        SetChipActive(calModeSpeedChips, "CalModeSpeedSlow", m.defaultSpeed == GameManager.SpeedMode.Slow);
        SetChipActive(calModeSpeedChips, "CalModeSpeedMed",  m.defaultSpeed == GameManager.SpeedMode.Med);
        SetChipActive(calModeSpeedChips, "CalModeSpeedFast", m.defaultSpeed == GameManager.SpeedMode.Fast);

        // Rebuild rules list
        if (calRulesList != null)
        {
            calRulesList.Clear();
            for (int i = 0; i < m.rules.Count; i++)
            {
                int rIdx = i;
                var rule = m.rules[i];
                var row = BuildRuleRow(rule, () => { m.rules.RemoveAt(rIdx); pm.SaveModes(); RefreshModesTab(); });
                calRulesList.Add(row);
            }
        }

        // Hide add-rule panel
        if (calAddRulePanel != null) calAddRulePanel.style.display = DisplayStyle.None;
    }

    VisualElement BuildRuleRow(SpeedRule rule, System.Action onDelete)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems    = Align.Center;
        row.style.marginBottom  = 4;

        var toggle = new Toggle { value = rule.enabled };
        toggle.style.marginRight = 6;
        toggle.RegisterValueChangedCallback(evt => { rule.enabled = evt.newValue; PlayModeManager.Instance?.SaveModes(); });
        row.Add(toggle);

        string triggerStr = FormatTrigger(rule);
        string speedStr   = rule.targetSpeed == GameManager.SpeedMode.Slow ? "→ Slow" :
                            rule.targetSpeed == GameManager.SpeedMode.Med  ? "→ Med"  : "→ Fast";
        string idleStr    = rule.idleResumeEnabled ? $" (idle {rule.idleResumeRealSeconds}s/{rule.idleResumeInGameDays}d)" : "";

        var lbl = new Label($"{triggerStr}  {speedStr}{idleStr}");
        lbl.style.flexGrow = 1;
        lbl.style.fontSize = 10;
        lbl.style.color    = new StyleColor(new UnityEngine.Color(180/255f, 200/255f, 180/255f));
        lbl.style.overflow = Overflow.Hidden;
        row.Add(lbl);

        var del = new Button(onDelete) { text = "✕" };
        del.style.width  = 22; del.style.height = 22;
        del.style.fontSize = 10;
        del.style.borderTopWidth = del.style.borderBottomWidth =
        del.style.borderLeftWidth = del.style.borderRightWidth = 0;
        del.style.borderTopLeftRadius = del.style.borderTopRightRadius =
        del.style.borderBottomLeftRadius = del.style.borderBottomRightRadius = 3;
        del.style.backgroundColor = new StyleColor(new UnityEngine.Color(60/255f, 20/255f, 20/255f));
        del.style.color = new StyleColor(new UnityEngine.Color(200/255f, 100/255f, 100/255f));
        del.style.paddingTop = del.style.paddingBottom =
        del.style.paddingLeft = del.style.paddingRight = new StyleLength(0f);
        row.Add(del);

        return row;
    }

    string FormatTrigger(SpeedRule rule)
    {
        return rule.trigger switch
        {
            SpeedRuleTrigger.Month           => $"Month = {MonthNames[(int)rule.triggerParam]}",
            SpeedRuleTrigger.Season          => $"Season = {(Season)(int)rule.triggerParam}",
            SpeedRuleTrigger.MoistureBelow   => $"Moisture < {rule.triggerParam:P0}",
            SpeedRuleTrigger.HealthBelow     => $"Health < {rule.triggerParam:P0}",
            SpeedRuleTrigger.NutrientBelow   => $"Nutrient < {rule.triggerParam:F1}",
            SpeedRuleTrigger.FungalLoadAbove => $"Fungal > {rule.triggerParam:P0}",
            SpeedRuleTrigger.WeedCountAbove  => $"Weeds > {(int)rule.triggerParam}",
            SpeedRuleTrigger.WireSetGold     => "Wire set gold",
            SpeedRuleTrigger.TreeInDanger    => "Tree in danger",
            _ => rule.trigger.ToString()
        };
    }

    void RefreshRuleParamRow()
    {
        if (calRuleTriggerDropdown == null || calRuleParamRow == null) return;
        int idx = calRuleTriggerDropdown.index;
        // Triggers that need a param: 0=Month, 1=Season, 2-6=numeric, 7-8=no param
        bool needsParam = idx <= 6;
        calRuleParamRow.style.display = needsParam ? DisplayStyle.Flex : DisplayStyle.None;
        if (!needsParam) return;

        switch (idx)
        {
            case 0: // Month
                if (calRuleParamLabel     != null) calRuleParamLabel.text = "Month:";
                if (calRuleParamSlider    != null) { calRuleParamSlider.lowValue = 1; calRuleParamSlider.highValue = 12; calRuleParamSlider.SetValueWithoutNotify(1); }
                if (calRuleParamValueLabel != null) calRuleParamValueLabel.text = MonthNames[1];
                break;
            case 1: // Season
                if (calRuleParamLabel     != null) calRuleParamLabel.text = "Season:";
                if (calRuleParamSlider    != null) { calRuleParamSlider.lowValue = 0; calRuleParamSlider.highValue = 3; calRuleParamSlider.SetValueWithoutNotify(0); }
                if (calRuleParamValueLabel != null) calRuleParamValueLabel.text = "Spring";
                break;
            case 2: case 3: // Moisture/Health 0–1
                if (calRuleParamLabel     != null) calRuleParamLabel.text = "Threshold:";
                if (calRuleParamSlider    != null) { calRuleParamSlider.lowValue = 0; calRuleParamSlider.highValue = 1; calRuleParamSlider.SetValueWithoutNotify(0.3f); }
                if (calRuleParamValueLabel != null) calRuleParamValueLabel.text = "30%";
                break;
            case 4: // Nutrient 0–2
                if (calRuleParamLabel     != null) calRuleParamLabel.text = "Below:";
                if (calRuleParamSlider    != null) { calRuleParamSlider.lowValue = 0; calRuleParamSlider.highValue = 2; calRuleParamSlider.SetValueWithoutNotify(0.5f); }
                if (calRuleParamValueLabel != null) calRuleParamValueLabel.text = "0.5";
                break;
            case 5: // Fungal 0–1
                if (calRuleParamLabel     != null) calRuleParamLabel.text = "Above:";
                if (calRuleParamSlider    != null) { calRuleParamSlider.lowValue = 0; calRuleParamSlider.highValue = 1; calRuleParamSlider.SetValueWithoutNotify(0.4f); }
                if (calRuleParamValueLabel != null) calRuleParamValueLabel.text = "40%";
                break;
            case 6: // Weed count
                if (calRuleParamLabel     != null) calRuleParamLabel.text = "Count >:";
                if (calRuleParamSlider    != null) { calRuleParamSlider.lowValue = 0; calRuleParamSlider.highValue = 10; calRuleParamSlider.SetValueWithoutNotify(0); }
                if (calRuleParamValueLabel != null) calRuleParamValueLabel.text = "0";
                break;
        }
    }

    string FormatRuleParam(int triggerIdx, float val)
    {
        return triggerIdx switch
        {
            0 => MonthNames[Mathf.Clamp(Mathf.RoundToInt(val), 1, 12)],
            1 => ((Season)Mathf.RoundToInt(val)).ToString(),
            2 => $"{val:P0}",
            3 => $"{val:P0}",
            4 => $"{val:F1}",
            5 => $"{val:P0}",
            6 => $"{Mathf.RoundToInt(val)}",
            _ => val.ToString("F2")
        };
    }

    void ConfirmAddRule()
    {
        var pm = PlayModeManager.Instance;
        var m  = pm?.ActiveMode;
        if (m == null) return;

        int  triggerIdx = calRuleTriggerDropdown?.index ?? 0;
        float param     = calRuleParamSlider?.value ?? 0f;
        if (triggerIdx == 0) param = Mathf.RoundToInt(param);   // month int
        if (triggerIdx == 1) param = Mathf.RoundToInt(param);   // season enum int

        var rule = new SpeedRule
        {
            enabled             = true,
            trigger             = (SpeedRuleTrigger)triggerIdx,
            triggerParam        = param,
            targetSpeed         = calEditRuleSpeed,
            idleResumeEnabled   = calRuleIdleToggle?.value ?? false,
            idleResumeRealSeconds = calRuleIdleReal?.value ?? 0f,
            idleResumeInGameDays  = calRuleIdleDays?.value ?? 0f,
        };

        m.rules.Add(rule);
        pm.SaveModes();
        if (calAddRulePanel != null) calAddRulePanel.style.display = DisplayStyle.None;
        RefreshModesTab();
    }

    // ── Speed tab ─────────────────────────────────────────────────────────────

    void RefreshSpeedTab()
    {
        if (calSpeedSlowSlider != null) calSpeedSlowSlider.SetValueWithoutNotify(GameManager.TIMESCALE_SLOW);
        if (calSpeedMedSlider  != null) calSpeedMedSlider.SetValueWithoutNotify(GameManager.TIMESCALE_MED);
        if (calSpeedFastSlider != null) calSpeedFastSlider.SetValueWithoutNotify(GameManager.TIMESCALE_FAST);
        UpdateSpeedHints();
    }

    void OnSpeedSliderChanged()
    {
        float s = calSpeedSlowSlider?.value ?? GameManager.TIMESCALE_SLOW;
        float m = calSpeedMedSlider?.value  ?? GameManager.TIMESCALE_MED;
        float f = calSpeedFastSlider?.value ?? GameManager.TIMESCALE_FAST;

        // Enforce Slow < Med < Fast with small gaps
        s = Mathf.Min(s, m - 0.1f);
        m = Mathf.Clamp(m, s + 0.1f, f - 1f);
        f = Mathf.Max(f, m + 1f);

        GameManager.TIMESCALE_SLOW = s;
        GameManager.TIMESCALE_MED  = m;
        GameManager.TIMESCALE_FAST = f;
        GameManager.SaveTimescalePrefs();

        // Sync current TIMESCALE if mode matches
        GameManager.Instance?.SetSpeedMode(GameManager.CurrentSpeed);

        UpdateSpeedHints();
    }

    void UpdateSpeedHints()
    {
        if (calSpeedSlowValue != null) calSpeedSlowValue.text = $"{GameManager.TIMESCALE_SLOW:F1}×";
        if (calSpeedMedValue  != null) calSpeedMedValue.text  = $"{GameManager.TIMESCALE_MED:F1}×";
        if (calSpeedFastValue != null) calSpeedFastValue.text = $"{GameManager.TIMESCALE_FAST:F0}×";
        if (calSpeedSlowHint  != null) calSpeedSlowHint.text  = FormatDayDuration(GameManager.TIMESCALE_SLOW);
        if (calSpeedMedHint   != null) calSpeedMedHint.text   = FormatDayDuration(GameManager.TIMESCALE_MED);
        if (calSpeedFastHint  != null) calSpeedFastHint.text  = FormatDayDuration(GameManager.TIMESCALE_FAST);
    }

    static string FormatDayDuration(float timescale)
    {
        float secs = 24f / timescale;
        if (secs >= 60f)  return $"1 in-game day = {secs / 60f:F1} real minutes";
        if (secs >= 1f)   return $"1 in-game day = {secs:F0} real seconds";
        // Faster than 1 day per second — invert so we never show "0 real seconds"
        float daysPerSec = timescale / 24f;
        return $"{daysPerSec:F1} in-game days = 1 real second";
    }

    // ── Chip helpers ──────────────────────────────────────────────────────────

    void SetChipActive(VisualElement container, string name, bool active)
    {
        if (container == null) return;
        var btn = container.Q<Button>(name);
        if (btn == null) return;
        btn.style.backgroundColor = active
            ? new StyleColor(new UnityEngine.Color(229/255f, 179/255f, 16/255f))
            : new StyleColor(new UnityEngine.Color(40/255f, 40/255f, 40/255f));
        btn.style.color = active
            ? new StyleColor(new UnityEngine.Color(10/255f, 10/255f, 10/255f))
            : new StyleColor(new UnityEngine.Color(160/255f, 160/255f, 160/255f));
    }

    void WireChipGroup(VisualElement container, string[] names, VisualElement root, System.Action<int> onSelect)
    {
        if (container == null) return;
        for (int i = 0; i < names.Length; i++)
        {
            int idx  = i;
            var btn  = root.Q<Button>(names[idx]);
            if (btn == null) continue;
            btn.RegisterCallback<ClickEvent>(_ =>
            {
                for (int j = 0; j < names.Length; j++)
                    SetChipActive(container, names[j], j == idx);
                onSelect(idx);
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    void OpenCalendar()
    {
        if (calendarOverlay == null) return;
        ToolManager.Instance?.ClearTool();
        calViewMonth = GameManager.month;
        calViewYear  = GameManager.year;
        BuildCalendarGrid();
        CalShowView(calMonthView);
        SwitchCalTab(0);   // always open on Schedule tab
        calendarOverlay.style.display = DisplayStyle.Flex;
        GameManager.Instance?.UpdateGameState(GameState.CalendarOpen);

        MaybeShowTooltip("calendar_schedule_v3",
            "Care Scheduling",
            "Use the calendar to set up a watering and fertilizing routine.\n\n" +
            "Watering: Ficus prefer soil that dries slightly between waterings. " +
            "In spring and summer, water every 1–2 days. In autumn, every 2–3 days. " +
            "In winter, water sparingly — every 4–5 days is usually enough.\n\n" +
            "Fertilizing: Feed every 1–2 weeks during the growing season (spring–summer) " +
            "with a balanced fertilizer. Switch to low-nitrogen in late summer to harden " +
            "growth. Do not fertilize in winter — the tree is dormant and can't use it.\n\n" +
            "Tap any day to schedule an event. Tick 'Repeating' to set it on a cycle, " +
            "and pick a season so it only fires when appropriate.");
    }

    void CloseCalendar()
    {
        if (calendarOverlay == null) return;
        calendarOverlay.style.display = DisplayStyle.None;
        var gm = GameManager.Instance;
        if (gm != null)
        {
            // State may have drifted from CalendarOpen (e.g. TreeInteraction transiently
            // set Wiring while the calendar was open), so check both.
            if (gm.state == GameState.CalendarOpen || gm.state == GameState.Wiring ||
                gm.state == GameState.Shaping || gm.state == GameState.Pruning)
                gm.UpdateGameState(gm.StateForMonth(GameManager.month));
            // Always restore — CalendarOpen froze time and nothing else restores it.
            Time.timeScale = 1f;
            // Return to medium speed; PlayModeManager will re-evaluate rules next frame.
            gm.SetSpeedMode(GameManager.SpeedMode.Med);
        }
    }

    void CalShowView(VisualElement view)
    {
        if (calMonthView != null) calMonthView.style.display = DisplayStyle.None;
        if (calDayView   != null) calDayView.style.display   = DisplayStyle.None;
        if (calEventView != null) calEventView.style.display = DisplayStyle.None;
        if (view != null) view.style.display = DisplayStyle.Flex;
    }

    void BuildCalendarGrid()
    {
        if (calGrid == null || calMonthYearLabel == null) return;
        calGrid.Clear();
        calMonthYearLabel.text = $"{MonthNames[calViewMonth]} {calViewYear}";

        int daysInMonth = GameManager.DaysInMonth(calViewMonth, calViewYear);
        // Day of week for the 1st (0=Mon … 6=Sun using ISO)
        int firstDow = ((int)new System.DateTime(calViewYear, calViewMonth, 1).DayOfWeek + 6) % 7;

        // Empty cells before the 1st
        for (int i = 0; i < firstDow; i++)
            calGrid.Add(MakeDayCell(-1, false, false, false));

        for (int d = 1; d <= daysInMonth; d++)
        {
            int dayCapture = d;
            bool isToday  = calViewMonth == GameManager.month && calViewYear == GameManager.year && d == GameManager.day;
            bool hasWater = GameManager.schedule.Exists(e => e.type == ScheduledEventType.Water    && GameManager.EventFiresOnDate(e, calViewMonth, d, calViewYear));
            bool hasFert  = GameManager.schedule.Exists(e => e.type == ScheduledEventType.Fertilize && GameManager.EventFiresOnDate(e, calViewMonth, d, calViewYear));
            var cell = MakeDayCell(d, isToday, hasWater, hasFert);
            cell.RegisterCallback<ClickEvent>(_ => OpenDayDetail(dayCapture));
            calGrid.Add(cell);
        }
    }

    VisualElement MakeDayCell(int day, bool isToday, bool hasWater, bool hasFert)
    {
        var cell = new VisualElement();
        cell.style.width  = new StyleLength(new Length(100f / 7f, LengthUnit.Percent));
        cell.style.height = 46;
        cell.style.alignItems     = Align.Center;
        cell.style.justifyContent = Justify.Center;
        cell.style.borderTopLeftRadius     = new StyleLength(4);
        cell.style.borderTopRightRadius    = new StyleLength(4);
        cell.style.borderBottomLeftRadius  = new StyleLength(4);
        cell.style.borderBottomRightRadius = new StyleLength(4);
        cell.style.marginBottom  = 2;

        if (day <= 0) return cell;  // spacer

        if (isToday)
            cell.style.backgroundColor = new StyleColor(new Color(0.90f, 0.70f, 0.06f, 0.25f));

        cell.style.cursor = new StyleCursor(new UnityEngine.UIElements.Cursor());

        var lbl = new Label(day.ToString());
        lbl.style.color = isToday
            ? new StyleColor(new Color(1f, 0.88f, 0.2f))
            : new StyleColor(new Color(0.80f, 0.85f, 0.80f));
        lbl.style.fontSize = 13;
        cell.Add(lbl);

        if (hasWater || hasFert)
        {
            var dotRow = new VisualElement();
            dotRow.style.flexDirection = FlexDirection.Row;
            dotRow.style.marginTop     = 2;

            if (hasWater) dotRow.Add(MakeDot(new Color(0.25f, 0.55f, 1.00f)));  // blue
            if (hasFert)  dotRow.Add(MakeDot(new Color(0.25f, 0.80f, 0.35f)));  // green

            cell.Add(dotRow);
        }

        return cell;
    }

    VisualElement MakeDot(Color color)
    {
        var dot = new VisualElement();
        dot.style.width  = 5;
        dot.style.height = 5;
        dot.style.borderTopLeftRadius     = new StyleLength(3);
        dot.style.borderTopRightRadius    = new StyleLength(3);
        dot.style.borderBottomLeftRadius  = new StyleLength(3);
        dot.style.borderBottomRightRadius = new StyleLength(3);
        dot.style.backgroundColor = new StyleColor(color);
        dot.style.marginLeft      = 1;
        dot.style.marginRight     = 1;
        return dot;
    }

    void OpenDayDetail(int day)
    {
        calSelectedDay = day;
        if (calDayTitleLabel != null)
        {
            var dt = new System.DateTime(calViewYear, calViewMonth, day);
            calDayTitleLabel.text = $"{dt.DayOfWeek}, {MonthNames[calViewMonth]} {day}";
        }
        RebuildDayEventList();
        CalShowView(calDayView);
    }

    void RebuildDayEventList()
    {
        if (calDayEventList == null) return;
        calDayEventList.Clear();

        var events = GameManager.schedule.FindAll(e => e.month == calViewMonth && e.day == calSelectedDay);
        if (events.Count == 0)
        {
            var empty = new Label("No events scheduled.");
            empty.style.color    = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            empty.style.fontSize = 11;
            empty.style.marginBottom = 4;
            calDayEventList.Add(empty);
            return;
        }

        foreach (var ev in events)
        {
            var evCopy = ev;
            var row    = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4;

            string icon   = ev.type == ScheduledEventType.Water ? "💧" : "🌿";
            string repeat = ev.repeat == RepeatMode.Once ? "Once"
                : ev.repeat == RepeatMode.EveryNDays  ? $"Every {ev.repeatInterval}d"
                : $"Every {ev.repeatInterval}w";
            string season = ev.season == Season.AllYear ? "" : $" · {ev.season}";
            string tod    = ev.timeOfDay.ToString();

            var lbl = new Label($"{icon} {ev.type} · {ev.amount} · {tod} · {repeat}{season}");
            lbl.style.flexGrow   = 1;
            lbl.style.fontSize   = 11;
            lbl.style.color      = new StyleColor(new Color(0.75f, 0.85f, 0.75f));
            lbl.style.whiteSpace = new StyleEnum<WhiteSpace>(WhiteSpace.Normal);

            var toggle = new Toggle();
            toggle.value = ev.enabled;
            toggle.RegisterValueChangedCallback(e => { evCopy.enabled = e.newValue; });

            var del = new Button(() => { GameManager.schedule.Remove(evCopy); RebuildDayEventList(); BuildCalendarGrid(); });
            del.text = "✕";
            del.style.width           = 24;
            del.style.height          = 24;
            del.style.backgroundColor = new StyleColor(new Color(0.3f, 0.1f, 0.1f));
            del.style.color           = new StyleColor(new Color(0.8f, 0.4f, 0.4f));
            del.style.borderLeftWidth         = 0;
            del.style.borderRightWidth        = 0;
            del.style.borderTopWidth          = 0;
            del.style.borderBottomWidth       = 0;
            del.style.borderTopLeftRadius     = new StyleLength(4);
            del.style.borderTopRightRadius    = new StyleLength(4);
            del.style.borderBottomLeftRadius  = new StyleLength(4);
            del.style.borderBottomRightRadius = new StyleLength(4);
            del.style.cursor          = new StyleCursor(new UnityEngine.UIElements.Cursor());

            row.Add(lbl);
            row.Add(toggle);
            row.Add(del);
            calDayEventList.Add(row);
        }
    }

    void OpenEventDetail(ScheduledEventType type)
    {
        calEditType     = type;
        calEditAmount   = ScheduledEventAmount.Light;
        calEditFertType = 0;
        calEditTimeOfDay = TimeOfDay.Morning;
        calEditSeason   = Season.AllYear;
        calEditRepeat   = RepeatMode.Once;
        calEditRepeatN  = 2;

        if (calEventTitleLabel != null)
            calEventTitleLabel.text = type == ScheduledEventType.Water ? "Schedule Watering" : "Schedule Fertilizer";

        if (calFertTypeRow != null)
            calFertTypeRow.style.display = type == ScheduledEventType.Fertilize ? DisplayStyle.Flex : DisplayStyle.None;

        if (calRepeatToggle != null) calRepeatToggle.value = false;
        if (calRepeatOptions != null) calRepeatOptions.style.display = DisplayStyle.None;
        calEditRepeatN = 1;
        if (calRepeatNLabel != null) calRepeatNLabel.text = "1";

        RefreshCalendarChips();
        CalShowView(calEventView);
    }

    void WireCalendarChips()
    {
        WireChip("CalAmtLight",      () => { calEditAmount = ScheduledEventAmount.Light;  RefreshCalendarChips(); });
        WireChip("CalAmtMedium",     () => { calEditAmount = ScheduledEventAmount.Medium; RefreshCalendarChips(); });
        WireChip("CalAmtHeavy",      () => { calEditAmount = ScheduledEventAmount.Heavy;  RefreshCalendarChips(); });
        WireChip("CalFertBalanced",  () => { calEditFertType = 0; RefreshCalendarChips(); });
        WireChip("CalFertHighN",     () => { calEditFertType = 1; RefreshCalendarChips(); });
        WireChip("CalFertHighP",     () => { calEditFertType = 2; RefreshCalendarChips(); });
        WireChip("CalFertLowN",      () => { calEditFertType = 3; RefreshCalendarChips(); });
        WireChip("CalTimeMorning",   () => { calEditTimeOfDay = TimeOfDay.Morning; RefreshCalendarChips(); });
        WireChip("CalTimeMidday",    () => { calEditTimeOfDay = TimeOfDay.Midday;  RefreshCalendarChips(); });
        WireChip("CalTimeNight",     () => { calEditTimeOfDay = TimeOfDay.Night;   RefreshCalendarChips(); });
        WireChip("CalSeasonAll",     () => { calEditSeason = Season.AllYear; RefreshCalendarChips(); });
        WireChip("CalSeasonSpring",  () => { calEditSeason = Season.Spring;  RefreshCalendarChips(); });
        WireChip("CalSeasonSummer",  () => { calEditSeason = Season.Summer;  RefreshCalendarChips(); });
        WireChip("CalSeasonAutumn",  () => { calEditSeason = Season.Autumn;  RefreshCalendarChips(); });
        WireChip("CalSeasonWinter",  () => { calEditSeason = Season.Winter;  RefreshCalendarChips(); });
        WireChip("CalCadenceDays",   () => { calEditRepeat = RepeatMode.EveryNDays;  RefreshCalendarChips(); });
        WireChip("CalCadenceWeeks",  () => { calEditRepeat = RepeatMode.EveryNWeeks; RefreshCalendarChips(); });

        calRepeatNDec?.RegisterCallback<ClickEvent>(_ => {
            calEditRepeatN = Mathf.Max(1, calEditRepeatN - 1);
            if (calRepeatNLabel != null) calRepeatNLabel.text = calEditRepeatN.ToString();
        });
        calRepeatNInc?.RegisterCallback<ClickEvent>(_ => {
            calEditRepeatN = Mathf.Min(99, calEditRepeatN + 1);
            if (calRepeatNLabel != null) calRepeatNLabel.text = calEditRepeatN.ToString();
        });
    }

    void WireChip(string name, System.Action onClick)
    {
        var btn = buttonDocument?.rootVisualElement.Q(name) as Button;
        btn?.RegisterCallback<ClickEvent>(_ => onClick());
    }

    void RefreshCalendarChips()
    {
        SetChipActive("CalAmtLight",     calEditAmount == ScheduledEventAmount.Light);
        SetChipActive("CalAmtMedium",    calEditAmount == ScheduledEventAmount.Medium);
        SetChipActive("CalAmtHeavy",     calEditAmount == ScheduledEventAmount.Heavy);
        SetChipActive("CalFertBalanced", calEditFertType == 0);
        SetChipActive("CalFertHighN",    calEditFertType == 1);
        SetChipActive("CalFertHighP",    calEditFertType == 2);
        SetChipActive("CalFertLowN",     calEditFertType == 3);
        SetChipActive("CalTimeMorning",  calEditTimeOfDay == TimeOfDay.Morning);
        SetChipActive("CalTimeMidday",   calEditTimeOfDay == TimeOfDay.Midday);
        SetChipActive("CalTimeNight",    calEditTimeOfDay == TimeOfDay.Night);
        SetChipActive("CalSeasonAll",    calEditSeason == Season.AllYear);
        SetChipActive("CalSeasonSpring", calEditSeason == Season.Spring);
        SetChipActive("CalSeasonSummer", calEditSeason == Season.Summer);
        SetChipActive("CalSeasonAutumn", calEditSeason == Season.Autumn);
        SetChipActive("CalSeasonWinter", calEditSeason == Season.Winter);

        bool repeating = calRepeatToggle?.value ?? false;
        SetChipActive("CalCadenceDays",  repeating && calEditRepeat == RepeatMode.EveryNDays);
        SetChipActive("CalCadenceWeeks", repeating && calEditRepeat == RepeatMode.EveryNWeeks);
    }

    void SetChipActive(string name, bool active)
    {
        var btn = buttonDocument?.rootVisualElement.Q(name) as Button;
        if (btn == null) return;
        btn.style.backgroundColor = active
            ? new StyleColor(new Color(0.90f, 0.70f, 0.06f))
            : new StyleColor(new Color(0.16f, 0.16f, 0.16f));
        btn.style.color = active
            ? new StyleColor(new Color(0.04f, 0.04f, 0.04f))
            : new StyleColor(new Color(0.63f, 0.63f, 0.63f));
    }

    void ConfirmCalendarEvent()
    {
        bool repeating = calRepeatToggle?.value ?? false;
        var ev = new ScheduledEvent
        {
            id             = System.Guid.NewGuid().ToString(),
            type           = calEditType,
            amount         = calEditAmount,
            fertType       = calEditFertType,
            month          = calViewMonth,
            day            = calSelectedDay,
            repeat         = repeating ? calEditRepeat : RepeatMode.Once,
            repeatInterval = calEditRepeatN,
            season         = repeating ? calEditSeason : Season.AllYear,
            timeOfDay      = calEditTimeOfDay,
            enabled        = true,
        };
        GameManager.schedule.Add(ev);
        OpenDayDetail(calSelectedDay);   // return to day view, list rebuilds
        BuildCalendarGrid();             // refresh dots
    }

    static string SoilLabel(float retention) =>
        retention < 0.35f ? "Dry Soil" : retention < 0.55f ? "Balanced" : "Moist Soil";

    static Color SoilColor(float retention) =>
        retention < 0.35f ? new Color(0.82f, 0.68f, 0.42f) :   // sand  — dry
        retention < 0.55f ? new Color(0.58f, 0.72f, 0.52f) :   // sage  — balanced
                            new Color(0.42f, 0.62f, 0.82f);    // slate — moist
}
