using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// TreeSkeleton — Growth partial. Debug GL overlay, the per-frame Update/grow tick, StartNewGrowingSeason,
/// back-budding, root containment + drainage-hole escape.
/// Split from the 6,373-line TreeSkeleton.cs (F5, 2026-07-03) with zero
/// behaviour change; all serialized fields remain in TreeSkeleton.cs.
/// </summary>
public partial class TreeSkeleton : MonoBehaviour
{
    // ── Soil debug GL overlay — renders into both Game View and Scene View ────
    void OnRenderObject()
    {
        if (!_soilDbgActive) return;
        if (Time.realtimeSinceStartup > _soilDbgEndTime) { _soilDbgActive = false; return; }

        if (_soilDbgMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            _soilDbgMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _soilDbgMat.SetInt("_ZWrite", 0);
            _soilDbgMat.SetInt("_Cull",   0);
            _soilDbgMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        _soilDbgMat.SetPass(0);
        GL.PushMatrix();
        GL.Begin(GL.LINES);

        Vector3 c = _soilDbgCenter;
        float   r = _soilDbgR;

        // GREEN cross + 4 tall pillars = soilY (where roots stop)
        GL.Color(Color.green);
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z);     GL.Vertex3(c.x + r, _soilDbgSoilY, c.z);
        GL.Vertex3(c.x, _soilDbgSoilY, c.z - r);     GL.Vertex3(c.x, _soilDbgSoilY, c.z + r);
        // Vertical pillars so they're visible edge-on
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z - r); GL.Vertex3(c.x - r, _soilDbgSoilY + 3f, c.z - r);
        GL.Vertex3(c.x + r, _soilDbgSoilY, c.z - r); GL.Vertex3(c.x + r, _soilDbgSoilY + 3f, c.z - r);
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z + r); GL.Vertex3(c.x - r, _soilDbgSoilY + 3f, c.z + r);
        GL.Vertex3(c.x + r, _soilDbgSoilY, c.z + r); GL.Vertex3(c.x + r, _soilDbgSoilY + 3f, c.z + r);

        // RED cross = rock bottom bound (old wrong soilY target)
        GL.Color(Color.red);
        GL.Vertex3(c.x - r, _soilDbgRockBot, c.z);   GL.Vertex3(c.x + r, _soilDbgRockBot, c.z);
        GL.Vertex3(c.x, _soilDbgRockBot, c.z - r);   GL.Vertex3(c.x, _soilDbgRockBot, c.z + r);

        // CYAN cross = rock top bound
        GL.Color(Color.cyan);
        GL.Vertex3(c.x - r, _soilDbgRockTop, c.z);   GL.Vertex3(c.x + r, _soilDbgRockTop, c.z);
        GL.Vertex3(c.x, _soilDbgRockTop, c.z - r);   GL.Vertex3(c.x, _soilDbgRockTop, c.z + r);

        GL.End();
        GL.PopMatrix();
    }

    void Update()
    {
        // Debug: press 1-9 to instantly simulate that many years of growth
        if (Keyboard.current != null)
        {
            Key[] digitKeys = {
                Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
            };
            for (int k = 0; k < digitKeys.Length; k++)
            {
                if (Keyboard.current[digitKeys[k]].wasPressedThisFrame)
                {
                    for (int y = 0; y <= k; y++) SimulateYear();
                    break;
                }
            }
        }

        // Root lift animation -- runs regardless of grow state
        if (!Mathf.Approximately(currentLift, liftTarget))
        {
            currentLift = Mathf.MoveTowards(currentLift, liftTarget, rootLiftSpeed * Time.deltaTime);
            var p = transform.position;
            transform.position = new Vector3(p.x, initY + currentLift, p.z);
        }

        // Hide the seed after ~1 in-game month OR once the sprout is tall enough.
        if (seedObject != null && seedObject.activeSelf && root != null)
        {
            bool sproutVisible = root.length >= seedHideLength;
            bool monthPassed   = startMonth >= 0 &&
                                 (GameManager.year * 12 + GameManager.month) >
                                 (startYear  * 12 + startMonth);
            if (sproutVisible || monthPassed)
            {
                seedObject.SetActive(false);
                Debug.Log("[Tree] Seed hidden");
            }
        }

        // Keep air layer wraps sized to the trunk every frame so they can't be swallowed.
        if (airLayers.Count > 0)
            foreach (var layer in airLayers)
                SetAirLayerWrapTransform(layer);

        // Keep air layer root bases anchored to parent tip as the trunk grows.
        UpdateAirLayerRootPositions();

        // ── F9: fixed-timestep simulation pump ────────────────────────────────
        // ALL sim state advances in fixed SIM-day quanta, never raw frame quanta.
        // Before this, one frame at high timescale spanned 4–12 in-game days, so any
        // threshold crossed mid-frame (water trigger, drought bank, wire gold) resolved
        // at the frame boundary with a full-frame magnitude — the root of the whole
        // 2026-07-02 drought-death family. Steps per frame are capped: on a hitch the
        // sim briefly lags the calendar and catches up over the following frames
        // instead of taking one giant step. Purely visual motion (root lift above,
        // wind, falling debris) deliberately stays frame-based.
        simDayAccumulator += Time.deltaTime * GameManager.TIMESCALE / 24f;
        float maxBank = simStepDays * maxSimStepsPerFrame * 2f;
        if (simDayAccumulator > maxBank) simDayAccumulator = maxBank;   // menus/no-tree: don't hoard a backlog

        int simSteps = 0;
        while (simDayAccumulator >= simStepDays && simSteps < maxSimStepsPerFrame)
        {
            simDayAccumulator -= simStepDays;
            simSteps++;
            SimStep(simStepDays);
        }
    }

    /// <summary>
    /// One fixed simulation step of <paramref name="stepDays"/> in-game days: auto-care,
    /// moisture drain + drought, elongation/branching, node aging, wire progress.
    /// Everything in here MUST scale by stepDays — never Time.deltaTime — so the tree
    /// is identical at any play speed and frame rate (F9, 2026-07-03). The body moved
    /// verbatim from the old per-frame Update; `inGameDays` is the step quantum now.
    /// </summary>
    void SimStep(float stepDays)
    {
        float inGameDays = stepDays;

        // Auto-care: runs regardless of grow state (dormancy, leaf fall, TimeGo, etc.)
        // Only requires root to exist so we don't fire before the tree is initialised.
        wateredThisFrame = false;
        if (root != null)
        {
            autoWaterCooldownDays += inGameDays;
            // Normal band keeps the 1-day cooldown so we don't water every frame.
            // EMERGENCY band: at high timescale one frame is ~0.35–0.7 in-game days, so
            // an XS pot can plunge from the 0.5 trigger to bone dry inside the cooldown
            // gap — Quick-Start trees died of "drought" while being watered hundreds of
            // times (2026-07-02). Below the drought threshold, water immediately.
            bool warmBand  = soilMoisture < 0.5f && autoWaterCooldownDays >= 1.0f;
            bool emergency = soilMoisture < droughtThreshold;
            if (autoWaterEnabled && (warmBand || emergency))
            {
                Water();
                autoWaterJustFired    = true;
                autoWaterCooldownDays = 0f;
                wateredThisFrame      = true;
            }
            if (autoFertilizeEnabled && nutrientReserve < 0.6f)
            {
                if (Fertilize())
                    autoFertilizeJustFired = true;
            }

            // Auto-herbicide: count up days while weeds are present; fire after delay
            if (autoHerbicideEnabled)
            {
                var wm = GetComponent<WeedManager>();
                if (wm != null && wm.ActiveWeedCount > 0)
                {
                    autoHerbicidePendingDays += inGameDays;
                    if (autoHerbicidePendingDays >= autoHerbicideDelayDays)
                    {
                        wm.HerbicideAll();
                        autoHerbicideJustFired   = true;
                        autoHerbicidePendingDays = 0f;
                    }
                }
                else
                {
                    autoHerbicidePendingDays = 0f;  // player pulled them — reset countdown
                }
            }

            // Auto-fungicide: fire after delay once any node exceeds the threshold
            if (autoFungicideEnabled)
            {
                bool infected = false;
                foreach (var n in allNodes)
                    if (n.fungalLoad > autoFungicideThreshold) { infected = true; break; }

                if (infected)
                {
                    autoFungicidePendingDays += inGameDays;
                    if (autoFungicidePendingDays >= autoFungicideDelayDays)
                    {
                        ApplyFungicide();
                        autoFungicideJustFired   = true;
                        autoFungicidePendingDays = 0f;
                    }
                }
                else
                {
                    autoFungicidePendingDays = 0f;
                }
            }
        }

        if (!isGrowing || root == null) return;

        float rate = GameManager.SeasonalGrowthRate;
        if (rate <= 0f) return;

        // Tick down branch activation delays regardless of rate (calendar time, not growth rate)
        if (branchSpawnMaxDelay > 0f)
            foreach (var node in allNodes)
                if (node.growthStartDelay > 0f)
                    node.growthStartDelay = Mathf.Max(0f, node.growthStartDelay - inGameDays);

        bool structureChanged = false;
        bool anyGrew          = false;

        // Soil moisture drain — modified by PotSoil water retention if present
        var potSoil = GetComponent<PotSoil>();
        float soilDrainMult = potSoil != null ? potSoil.DrainRateMultiplier : 1f;
        soilMoisture = Mathf.Max(0f, soilMoisture - drainRatePerDay * soilDrainMult * inGameDays * rate);
        if (soilMoisture < droughtThreshold)
        {
            if (autoWaterEnabled)
            {
                // Post-drain emergency water — drought mechanism #4 (2026-07-02): the
                // auto-care check runs BEFORE this drain, so at high timescale a single
                // frame could carry moisture from above the 0.5 trigger straight through
                // the drought threshold and bank 10+ phantom dry days that the waterer
                // never had a chance to prevent (one-session log: every 40-yr Quick-Start
                // died of "drought" while watering fired 2–4×/yr at TS=6000). If the
                // auto-waterer is on, dryness the same frame is by definition not neglect.
                Water();
                autoWaterJustFired = true;
                wateredThisFrame   = true;
            }
            else if (!wateredThisFrame)
            {
                droughtDaysAccumulated += inGameDays;
            }
        }
        else if (droughtDaysAccumulated > 0f)
        {
            // Recovery unwinds the counter at 2× — droughtDeathDays means SUSTAINED
            // dryness (per its tooltip: "consecutive days"), not the season's sum of
            // brief dips. At high timescale the old cumulative counter banked hundreds
            // of frame-quantized dips and killed well-watered Quick-Start trees
            // (2026-07-02: "died from drought — 82 dry days" while auto-water fired).
            droughtDaysAccumulated = Mathf.Max(0f, droughtDaysAccumulated - inGameDays * 2f);
        }

        // Drought death: extended time at zero moisture kills immediately.
        // (No second += here — the block above already counted this frame; the old
        // double-count made every zero-moisture day worth two.)
        if (treeDeathEnabled && soilMoisture <= 0f)
        {
            if (droughtDaysAccumulated >= droughtDeathDays)
            {
                Debug.Log($"[Death] Tree died from drought — {droughtDaysAccumulated:F0} dry days | year={GameManager.year}");
                KillTree("drought");
                return;
            }
        }

        // Snapshot growing nodes -- we may add new ones during this loop
        var snapshot = new List<TreeNode>(allNodes.Count);
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed && node.growthStartDelay <= 0f)
                snapshot.Add(node);
        }

        // Log every 5 in-game days — filter console by [Tree5]
        int absDay = GameManager.year * 365 + GameManager.dayOfYear;
        bool doLog = absDay - lastSnapshotAbsDay >= snapshotLogIntervalDays;
        if (doLog)
        {
            lastSnapshotAbsDay = absDay;
            int maxNodeDepth = 0;
            int rootNodes = 0, branchNodes = 0;
            foreach (var n in allNodes)
            {
                if (n.depth > maxNodeDepth) maxNodeDepth = n.depth;
                if (n.isRoot) rootNodes++; else branchNodes++;
            }
            long avgGrowth = growthFrames > 0 ? totalGrowthMs / growthFrames : 0;
            Debug.Log($"[Tree5] {GameManager.month}/{GameManager.day}/{GameManager.year} | " +
                      $"rate={rate:F2} growing={snapshot.Count} " +
                      $"total={allNodes.Count} (branches={branchNodes} roots={rootNodes}) maxDepth={maxNodeDepth} depthCap={SeasonDepthCap} | " +
                      $"growthLoop avg={avgGrowth}ms over {growthFrames} steps");

            totalGrowthMs = 0;
            growthFrames  = 0;
        }

        growthTimer.Restart();
        foreach (var node in snapshot)
        {
            // Dead or deadwood nodes never grow
            if (node.isDead || node.isDeadwood) continue;

            // Dormant from poor health -- skip this node entirely
            if (node.health < 0.25f) continue;

            // Health below 0.75 proportionally slows growth
            float healthMult = node.health >= 0.75f ? 1f : node.health;

            // nutrientReserve 0→2 maps to 0.6→1.4× growth; 1.0 = neutral (no effect)
            float nutrientMult = Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(nutrientReserve / 2f));

            float speed = baseGrowSpeed
                             * rate
                             * Mathf.Pow(depthSpeedDecay, node.depth)
                             * healthMult
                             * treeEnergy
                             * nutrientMult
                             * GrowthSeasonMult();

            // Pot-bound roots near the tray wall grow slower
            if (node.isRoot && node.boundaryPressure >= boundaryPressureThreshold)
                speed *= boundaryGrowthScale;

            node.length += speed * inGameDays;
            node.age    += inGameDays * rate;
            anyGrew      = true;

            if (node.length >= node.targetLength)
            {
                bool belowCap;
                if (node.isRoot && isIshitsukiMode)
                {
                    Vector3 tipW = transform.TransformPoint(node.tipPosition);
                    if (node.isTrainingWire)
                    {
                        // Training-wire cables stop at soil; PreGrowRootsToSoil manages them.
                        belowCap = tipW.y > plantingSurfacePoint.y;
                    }
                    else
                    {
                        // Underground roots (continuation from training wire):
                        // only grow once the tip is actually below soil.
                        // This prevents accidentally-planted or stray roots from growing in air.
                        belowCap = tipW.y <= plantingSurfacePoint.y && node.depth < maxRootDepth;
                    }
                }
                else
                {
                    belowCap = node.isRoot
                        ? node.depth < maxRootDepth
                        : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
                }

                // Always stop at targetLength — never grow past it.
                // Previously belowCap=false nodes grew indefinitely, creating
                // visually long "zombie" segments at the season depth boundary.
                node.length    = node.targetLength;
                node.isGrowing = false;
                // Finalize radius and lock minRadius so RecalculateRadii can't collapse
                // this node to 0 when its new children (starting at radius=0) are summed.
                float finalR   = (node.isRoot ? rootTerminalRadius : terminalRadius) * globalSegmentScale;
                node.radius    = finalR;
                node.minRadius = finalR;

                // Sub-chain continuation (subdivisionsLeft > 0) uses the same depth as
                // the parent, so it never consumes the season depth cap. Always allow it.
                // Real depth+1 branching (subdivisionsLeft == 0) requires belowCap.
                // Depth-frontier tips stop here and become eligible terminals next season
                // when SeasonDepthCap increases (depthsPerYear new depths per year).
                bool canSpawn = belowCap || (!node.isRoot && node.subdivisionsLeft > 0);
                if (canSpawn && !(isIshitsukiMode && node.isTrainingWire))
                    SpawnChildren(node);
                structureChanged = true;
            }
            else
            {
                // Ramp radius proportional to growth progress so parent thickening
                // spreads across the season rather than spiking at spawn time.
                // Floor at 10% of target so new segments never pop to zero-radius
                // on their first frame (which causes the visible "shrink" animation).
                float targetR = (node.isRoot ? rootTerminalRadius : terminalRadius) * globalSegmentScale;
                float rampedR = targetR * Mathf.Clamp01(node.length / node.targetLength);
                node.radius = Mathf.Max(rampedR, targetR * 0.1f);
            }
        }

        // Age accumulation — all non-trimmed nodes age each growing tick,
        // not just the ones currently growing. This drives the new-growth-to-bark
        // material fade in TreeMeshBuilder even after a segment stops elongating.
        // Roots are included so they transition from white → bark over seasons.
        // Above-ground roots bark faster (handled in GrowthColor via isExposed).
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isGrowing) continue;
            node.age += inGameDays * rate;
        }

        // Wire progress accumulation
        foreach (var node in allNodes)
        {
            if (!node.hasWire || node.isTrimmed) continue;

            node.wireAgeDays += inGameDays * rate;

            if (node.wireSetProgress < 1f)
            {
                float prev = node.wireSetProgress;
                node.wireSetProgress = Mathf.Min(1f,
                    node.wireSetProgress + inGameDays * rate * Mathf.Max(0.01f, node.wireSetSpeedMult) / wireDaysToSet);
                // Fire once when a wire first turns gold
                if (prev < 1f && node.wireSetProgress >= 1f)
                    OnWireSetGold?.Invoke();
            }
            else if (node.wireDamageProgress < 1f)
            {
                float dmgDelta = inGameDays * rate / wireDaysToSet;
                node.wireDamageProgress = Mathf.Min(1f, node.wireDamageProgress + dmgDelta);
                ApplyDamage(node, DamageType.WireDamage, dmgDelta * 0.5f);
            }
        }

        growthTimer.Stop();
        totalGrowthMs += growthTimer.ElapsedMilliseconds;
        growthFrames++;

        // RecalculateRadii once per in-game day. Structural events (trim, spring start)
        // still trigger it immediately via their own direct calls.
        // Mid-season, the daily cadence picks up the ramping radii of growing nodes
        // and propagates them up the tree gradually rather than all at once.
        bool newDay = GameManager.day != lastRecalcDay;
        if (structureChanged || newDay)
        {
            RecalculateRadii(root);
            if (newDay)
            {
                lastRecalcDay  = GameManager.day;
                cachedTreeHeight = CalculateTreeHeight();  // keep cinematic zoom current during spring
                ApplyDailySag();
            }
        }

        if (anyGrew || structureChanged)
            meshBuilder.SetDirty();
    }

    // Game State

    void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Water && root == null)
            InitTree();

        bool wasGrowing = isGrowing;
        isGrowing = (state == GameState.BranchGrow);
        Debug.Log($"[TreeState] State -> {state} | isGrowing={isGrowing} | year={GameManager.year} lastGrownYear={lastGrownYear}");

        // Season just ended — freeze all still-growing segments at their current length.
        // Only on TimeGo (the true season end), not on temporary tool states like Wiring.
        if (state == GameState.TimeGo && wasGrowing && root != null)
        {
            foreach (var node in allNodes)
                if (node.isGrowing) node.isGrowing = false;
            SetBuds();
            meshBuilder.SetDirty();

            // Narrative before the autosave so the summary lands in the save file.
            LogSeasonNarrative();

            // Auto-save at the end of each growing season (after bud set).
            SaveManager.AutoSave(this, GetComponent<LeafManager>());
        }

        // TreeOrient lowers the tree so you orient at working height, not suspended in the air.
        // RootPrune, RockPlace and RootRake still lift. Everything else grounds the tree.
        bool inRootMode = state == GameState.RootPrune || state == GameState.RockPlace ||
                          state == GameState.RootRake;
        float liftHeight = state == GameState.RootRake ? rootLiftHeight * rakeLiftMult : rootLiftHeight;
        liftTarget = inRootMode ? liftHeight : 0f;
        if (meshBuilder.renderRoots != GameManager.IsRootLiftActive(state))
        {
            meshBuilder.renderRoots = inRootMode;
            meshBuilder.SetDirty();
        }

        if (state == GameState.RockPlace)
        {
            prePlacementPosition     = transform.position;
            prePlacementRotation     = transform.rotation;
            prePlacementSurfacePoint = plantingSurfacePoint;
            prePlacementNormal       = plantingNormal;
        }

        if (state == GameState.RootPrune)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            Debug.Log($"[Root] Entering RootPrune | rootNodes={rootCount} | liftTarget={liftTarget} | plantingNormal={plantingNormal} plantingPoint={plantingSurfacePoint}");
            RecalculateRootHealthScore();
        }

        if (state == GameState.BranchGrow && root != null && GameManager.year > lastGrownYear)
        {
            lastGrownYear = GameManager.year;
            StartNewGrowingSeason();
        }
    }

    /// <summary>
    /// Plain-English season summary (PLAN item D): overall condition plus the most
    /// impactful positive and negative factors, logged to the CareLog at season end.
    /// </summary>
    void LogSeasonNarrative()
    {
        int   living = 0, wounds = 0, fungal = 0;
        float healthSum = 0f;
        foreach (var n in allNodes)
        {
            if (n.isRoot || n.isTrimmed || n.isDead) continue;
            living++; healthSum += n.health;
            if (n.hasWound && !n.pasteApplied) wounds++;
            if (n.fungalLoad > 0.3f) fungal++;
        }
        float avgHealth = living > 0 ? healthSum / living : 1f;

        string mood = avgHealth >= 0.85f ? "The tree is thriving"
                    : avgHealth >= 0.60f ? "The tree is doing well"
                    : avgHealth >= 0.40f ? "The tree is under stress"
                    :                      "The tree is struggling";

        var parts = new List<string> { mood };

        // One positive, then the worst negatives (capped so it stays 2–3 sentences)
        if      (nutrientReserve >= 1.0f) parts.Add("it heads into autumn well fed");
        else if (soilMoisture    >= 0.45f) parts.Add($"moisture has stayed comfortable at {soilMoisture * 100f:F0}%");

        if (treeInDanger)                      parts.Add("it is in DANGER — another critical season could kill it");
        if (wounds > 0)                        parts.Add($"{wounds} open wound{(wounds == 1 ? " is" : "s are")} draining health — paste would help");
        if (fungal > 0 && parts.Count < 4)     parts.Add($"{fungal} branch{(fungal == 1 ? " carries" : "es carry")} a fungal load worth watching");
        if (soilMoisture < 0.3f && parts.Count < 4) parts.Add("the soil has been running dry");
        if (IsPotBound() && parts.Count < 4)   parts.Add("roots are pressing the pot walls — a repot is due");

        string narrative = parts.Count > 1
            ? parts[0] + " — " + string.Join("; ", parts.GetRange(1, parts.Count - 1)) + "."
            : parts[0] + ".";
        CareLog.Add("Season", narrative);
    }

    /// <summary>Taper multiplier (0–1) based on day of year vs species slow/stop window.</summary>
    float GrowthSeasonMult()
    {
        float baseMult = 1f;
        if (species != null)
        {
            int d = GameManager.dayOfYear;
            if (d >= species.growthStopDay) return 0f;
            if (d >= species.growthSlowDay)
                baseMult = 1f - Mathf.InverseLerp(species.growthSlowDay, species.growthStopDay, d);
        }

        // Suggestion 3: reserve depletion — one spring of reduced growth after heavy pruning.
        if (heavyPruneRecoveryActive)
        {
            heavyPruneRecoveryActive = false; // consume; lasts exactly one growing season
            return baseMult * heavyPruneRecoveryScale;
        }

        return baseMult;
    }

    public void RestorePrePlacementSnapshot()
    {
        transform.position   = prePlacementPosition;
        transform.rotation   = prePlacementRotation;
        plantingSurfacePoint = prePlacementSurfacePoint;
        plantingNormal       = prePlacementNormal;
        meshBuilder.SetDirty();
    }

    void StartNewGrowingSeason()
    {
        // ── Scale debug (once per year) — filter console by [SCALEDEBUG] ───
        {
            // Use transform.position.y as fallback when plantingSurfacePoint hasn't been set
            float soilY    = (plantingSurfacePoint.y != 0f) ? plantingSurfacePoint.y : transform.position.y;
            float tallestY = soilY;
            float longestSeg = 0f;
            foreach (var n in allNodes)
            {
                if (n.isRoot) continue;
                Vector3 wp = transform.TransformPoint(n.tipPosition);
                if (wp.y > tallestY)    tallestY    = wp.y;
                if (n.targetLength > longestSeg) longestSeg = n.targetLength;
            }
            if (verboseLog) Debug.Log($"[SCALEDEBUG] year={GameManager.year} branchSegLen={branchSegmentLength:F4} " +
                                      $"depthCap={SeasonDepthCap} heightAboveSoil={tallestY - soilY:F3} " +
                                      $"longestSeg={longestSeg:F4} nodes={allNodes.Count}");
        }

        // Season tick: undo window expires — the tree has started responding to cuts
        pendingUndo = null;

        // Drought damage: apply accumulated stress from the previous season
        if (droughtDaysAccumulated > 0f)
        {
            float totalDamage = droughtDamagePerDay * droughtDaysAccumulated;
            foreach (var node in allNodes)
                if (!node.isRoot && !node.isTrimmed)
                    ApplyDamage(node, DamageType.Drought, totalDamage);
            Debug.Log($"[Water] Drought damage={totalDamage:F3} over {droughtDaysAccumulated:F1} dry days | year={GameManager.year}");
            droughtDaysAccumulated = 0f;
        }

        // Weeds: drain nutrients/moisture from existing weeds, maybe spawn a new one.
        // Use transform.position as soil center — always valid. plantingSurfacePoint stays
        // at Vector3.zero for non-Ishitsuki trees so it can't be used reliably here.
        var weedMgr = GetComponent<WeedManager>();
        if (weedMgr != null)
        {
            // Soil-surface Y, not the tree's Y — in Ishitsuki the tree sits up on the rock,
            // so weeds must use the tray soil level (plantingSurfacePoint) or they spawn above the rock.
            weedMgr.SetPotBounds(new Vector3(transform.position.x, plantingSurfacePoint.y, transform.position.z), weedSpawnRadius);
            weedMgr.SetTrunkRadius(root != null ? root.radius : 0f);
            weedMgr.ApplySeasonAndSpawn(this);
        }

        // Soil: degrade, check waterlogging, apply species mismatch penalties
        var soil = GetComponent<PotSoil>();
        soil?.SeasonTick(this);
        if (soil != null)
            nutrientReserve = Mathf.Min(nutrientReserve, soil.nutrientCapacity);

        // Fertilizer: burn if over-applied, then drain for this season
        if (nutrientReserve > fertilizerBurnThreshold)
        {
            float excess = nutrientReserve - fertilizerBurnThreshold;
            float burnDmg = fertilizerBurnDamage * excess;
            foreach (var node in allNodes)
                if (node.isRoot && !node.isTrimmed)
                    ApplyDamage(node, DamageType.FertilizerBurn, burnDmg);
            Debug.Log($"[Fert] Over-fertilize burn={burnDmg:F3} (reserve={nutrientReserve:F2} > threshold={fertilizerBurnThreshold:F2}) | year={GameManager.year}");
        }
        // Fungal: update infection and mycorrhizal networks before nutrient drain
        // (mycorrhizal nodes reduce effective drain)
        UpdateFungalInfection();
        UpdateMycorrhizal();
        GetComponent<LeafManager>()?.RefreshFungalTint(this);

        // Nutrient drain — mycorrhizal root coverage reduces drain proportionally
        int rootNodeCount = 0, mycoNodeCount = 0;
        foreach (var n in allNodes)
        {
            if (!n.isRoot || n.isTrimmed) continue;
            rootNodeCount++;
            if (n.isMycorrhizal) mycoNodeCount++;
        }
        float mycoFraction    = rootNodeCount > 0 ? (float)mycoNodeCount / rootNodeCount : 0f;
        float effectiveDrain  = nutrientDrainPerSeason * (1f - mycoFraction * mycorrhizalNutrientBonus);
        nutrientReserve = Mathf.Max(0f, nutrientReserve - effectiveDrain);
        Debug.Log($"[Fert] Season drain={effectiveDrain:F3} (myco={mycoFraction:P0} bonus) → nutrientReserve={nutrientReserve:F2} | year={GameManager.year}");

        // Suggestion 2: growth window lock — only advance recovery during growing months (Mar–Aug).
        // Dormant-season ticks don't count toward cut-point recovery.
        bool isGrowingSeason = GameManager.month >= 3 && GameManager.month <= 8;
        if (isGrowingSeason)
        {
            foreach (var node in allNodes)
            {
                if (!node.isTrimCutPoint) continue;
                node.regrowthSeasonCount++;
                if (node.trimCutDepth + node.regrowthSeasonCount * EffectiveDepthsPerYear >= SeasonDepthCap)
                    node.isTrimCutPoint = false;
            }
            CheckCutCapAbsorption();
        }

        // Suggestion 3: reserve depletion — heavy pruning this season penalises next spring's growth.
        if (cutDepthAccumulatedThisSeason >= heavyPruneThreshold)
        {
            heavyPruneRecoveryActive = true;
            Debug.Log($"[Prune] Heavy prune season: cutDepth={cutDepthAccumulatedThisSeason} >= threshold={heavyPruneThreshold}. Growth penalty next spring.");
        }
        cutDepthAccumulatedThisSeason = 0;

        // Refresh cached tree height and compute spread radius for this season
        cachedTreeHeight = CalculateTreeHeight();
        if (rootAreaTransform != null)
            Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} rootArea={rootAreaTransform.lossyScale.x:F2}×{rootAreaTransform.lossyScale.z:F2}");
        else
        {
            float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
            Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} spreadRadius={spreadRadius:F2}");
        }

        // Auto-plant trunk roots each spring until targetTrunkRoots is reached.
        // Counts only direct children of root that are roots (depth-1 trunk strands).
        int trunkRootCount = 0;
        int totalRootCount = 0;
        foreach (var n in allNodes)
        {
            if (!n.isRoot) continue;
            totalRootCount++;
            if (n.parent == root) trunkRootCount++;
        }
        int rootsToAdd = Mathf.Min(targetTrunkRoots - trunkRootCount, Mathf.Min(trunkRootsPerYear, maxTotalRootNodes - totalRootCount));
        if (rootsToAdd > 0)
        {
            for (int i = 0; i < rootsToAdd; i++)
            {
                // Spread new roots evenly in the gaps between existing ones
                float angle   = (trunkRootCount + i) * (Mathf.PI * 2f / targetTrunkRoots);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Ishitsuki roots drape down a rock face — start steep so the first visible
                // segment flows downward rather than shooting radially outward.
                Vector3 dir = isIshitsukiMode
                    ? (outward * 0.35f + Vector3.down).normalized
                    : (outward + Vector3.down * rootInitialPitch).normalized;
                float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

                // In Ishitsuki mode, place the startNode on the bark surface rather than
                // the trunk center so all cables have distinct, spread-out origins and
                // don't bunch into a dense white cluster at the root base.
                Vector3 localOutward = isIshitsukiMode
                    ? transform.InverseTransformDirection(outward).normalized
                    : Vector3.zero;
                float   barkOffset   = isIshitsukiMode ? Mathf.Max(root.radius, 0.04f) : 0f;
                Vector3 startPos     = root.worldPosition + localOutward * barkOffset;

                var r           = CreateNode(startPos, dir, rootTerminalRadius, len, root);
                r.isRoot        = true;
            }
            Debug.Log($"[GRoot] Auto-planted {rootsToAdd} trunk roots | trunkRoots={trunkRootCount + rootsToAdd}/{targetTrunkRoots} year={GameManager.year}");
        }

        // In Ishitsuki mode, pre-grow ALL trunk root cables toward soil before the
        // terminals list is built below.  This does two things:
        //   (a) Newly auto-planted roots get their chains draped over the rock immediately.
        //   (b) Existing mid-rock terminals from prior years get extended further.
        // Both cases prevent ContinuationDirection() from spawning children in random
        // air directions off the mid-rock positions.
        if (verboseLog) Debug.Log($"[PreGrow] year={GameManager.year} StartNewGrowingSeason: isIshitsukiMode={isIshitsukiMode} rockCollider={(rockCollider != null ? rockCollider.name : "NULL")}");
        if (isIshitsukiMode)
            PreGrowRootsToSoil(animated: true);

        // Elongate existing segments — lower-depth segments grow longer each year
        if (baseElongation > 0f)
        {
            foreach (var node in allNodes)
            {
                if (node.isTrimmed || node.isRoot || !node.isTerminal) continue;
                // Skip mid-chain sub-segments — their length was fixed when the chain started.
                // Elongating them would cause all remaining sub-segments to inherit the stretched
                // targetLength, producing one long segment per branch instead of N short ones.
                if (node.subdivisionsLeft > 0) continue;
                float delta = node.targetLength * baseElongation * Mathf.Pow(elongationDepthDecay, node.depth);
                node.targetLength += delta;
                node.length       += delta;  // advance length directly — avoids re-triggering SpawnChildren
            }
        }

        // Lateral bud visuals are only shown over winter; destroy them all at spring start.
        foreach (var go in lateralBudObjects)
            if (go != null) Destroy(go);
        lateralBudObjects.Clear();

        // Flush any growth-start delays carried over from the previous season.
        // Nodes that were waiting to activate in autumn get their chance at spring start.
        foreach (var node in allNodes)
            node.growthStartDelay = 0f;

        // Resume any non-root segments that were stopped mid-chord in autumn
        // (stopped while isGrowing but before reaching targetLength, no children yet).
        // This covers both mid-subdivision chains and any segment that didn't finish in time.
        // They resume rather than being re-spawned from a bud break.
        int resumed = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot || !node.isTerminal) continue;
            if (node.isGrowing) continue;                   // already active
            if (node.hasBud) continue;                      // will be handled by bud break below
            if (node.length >= node.targetLength) continue; // fully grown, not mid-chord

            node.isGrowing = true;
            resumed++;
        }
        if (resumed > 0)
            Debug.Log($"[Bud] Resumed mid-chord segments={resumed} year={GameManager.year}");

        // Bud system: if any branch nodes have hasBud set (year 2+), use those as
        // the terminal list. Year 1 (fresh tree, no prior season) falls back to
        // the original isTerminal scan.
        bool budSystemActive = allNodes.Exists(n => !n.isRoot && n.hasBud);

        var terminals = new List<TreeNode>();
        int resuming  = 0;
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed) resuming++;

            bool belowCap;
            if (node.isRoot && isIshitsukiMode)
            {
                if (node.isTrainingWire)
                {
                    // Training-wire cables: eligible while tip is at or above soil so they
                    // can spawn an underground continuation node the season they hit soil.
                    Vector3 tipW = transform.TransformPoint(node.tipPosition);
                    belowCap = tipW.y >= plantingSurfacePoint.y - 0.15f;
                }
                else
                {
                    // Underground roots branching off the training wire: use normal depth cap.
                    belowCap = node.depth < maxRootDepth;
                }
            }
            else
            {
                belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
            }

            if (node.isRoot)
            {
                if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                    terminals.Add(node);
            }
            else
            {
                bool eligible = budSystemActive
                    ? (!node.isTrimmed && node.hasBud && belowCap)
                    : (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap);
                if (eligible) terminals.Add(node);
            }
        }

        int rootTerminals = 0;
        int branchTerminals = 0;
        foreach (var t in terminals) { if (t.isRoot) rootTerminals++; else branchTerminals++; }
        Debug.Log($"[Tree] StartNewGrowingSeason year={GameManager.year} | depthCap={SeasonDepthCap} | terminals={terminals.Count} (roots={rootTerminals} branches={branchTerminals}) resuming={resuming} total={allNodes.Count}");

        // Count current root nodes once so the cap check is O(1) per terminal
        int currentRootCount = 0;
        int currentBranchCount = 0;
        foreach (var n in allNodes)
        {
            if (n.isRoot) currentRootCount++;
            else currentBranchCount++;
        }

        // Vigor: lateral chances scale down as the tree approaches the branch node cap.
        // Floors at 0.05 so old-wood / back-bud growth never stops completely on a living tree.
        float vigorFactor = Mathf.Max(0.05f, 1f - (float)currentBranchCount / maxBranchNodes);
        Debug.Log($"[Tree] branchNodes={currentBranchCount}/{maxBranchNodes} vigorFactor={vigorFactor:F2}");

        // Drainage-hole escape: cache the pot + pot-bound pressure once for this growth pass.
        _holePotSoil        = GetComponent<PotSoil>();
        _holeEscapePressure = RootPressureFactor();

        foreach (var terminal in terminals)
        {
            float baseSegLen  = (terminal.isRoot ? rootSegmentLength : branchSegmentLength) * globalSegmentScale;
            float decay       = terminal.isRoot ? rootSegmentLengthDecay : segmentLengthDecay;
            float chordLength = baseSegLen * Mathf.Pow(decay, terminal.depth + 1);

            // Refinement shortens internodes: each level applies ×refinementTaper (default 0.82).
            if (!terminal.isRoot && terminal.refinementLevel > 0f)
                chordLength *= Mathf.Pow(refinementTaper, terminal.refinementLevel);

            // Per-branch vigor scales segment length: high-vigor shoots grow longer each season.
            if (!terminal.isRoot)
                chordLength *= terminal.branchVigor;

            // Divide the chord into sub-segments so each is independently wireable.
            // Clamp per-segment (not the chord) so tip segments stay wireable regardless of N.
            // Also enforce maxSegmentLength: use whichever subdivision count gives shorter segments.
            int   subdivs     = !terminal.isRoot ? SubdivsForChord(chordLength) : 1;
            float childLength = (subdivs > 1) ? chordLength / subdivs : chordLength;
            childLength = Mathf.Max(childLength, terminal.isRoot ? 0.025f : minSegmentLength);

            float nodeRadius = terminal.isRoot ? rootTerminalRadius : terminalRadius;

            if (terminal.isRoot)
            {
                if (currentRootCount >= maxTotalRootNodes) continue;  // hard cap reached

                bool isIshitsuki = isIshitsukiMode;
                float distRatio  = RootDistRatio(terminal);
                // Escaped roots may poke a bit further (down past the floor through a hole) than the
                // normal outer boundary before they too stop.
                float boundary   = terminal.escapedRoot ? 1.9f : 1.3f;
                if (!isIshitsuki && distRatio >= boundary) continue;  // beyond hard outer boundary — stop

                // Terminal clamp: if this root tip has already escaped the side or bottom
                // of the pot box, stop it permanently rather than letting it grow further out.
                // Top-face escape (surface roots) is left alone — it looks realistic.
                // Exception: a pot-bound root reaching the floor over a drainage hole grows out
                // through it (escapedRoot) — a visible "needs repotting" tell.
                if (!isIshitsuki && rootAreaTransform != null)
                {
                    Vector3 tipW  = transform.TransformPoint(terminal.tipPosition);
                    Vector3 local = rootAreaTransform.InverseTransformPoint(tipW);
                    bool outsideSide   = Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f;
                    bool outsideBottom = local.y < -0.5f;
                    bool outsideTop    = local.y >  0.5f;

                    // Containment clips must REMOVE the offending segment, not mark it
                    // isTrimmed — trimmed nodes keep their mesh (by design, for player
                    // cuts), so every boundary-clipped root stub stayed visible forever
                    // and seasons of them accumulated into the towering rim mats seen on
                    // every species (2026-07-03 screenshots). Containment is simulation
                    // bookkeeping, not a cut the player made.
                    void ClipRoot(TreeNode t)
                    {
                        t.parent?.children.Remove(t);
                        var clipped = new List<TreeNode>();
                        RemoveSubtree(t, clipped);
                    }

                    if (outsideSide)
                    {
                        ClipRoot(terminal);
                        continue;
                    }
                    // Top face: roots may only surface NEAR THE TRUNK (nebari zone) — real
                    // surface roots flare at the base, they don't carpet the pot. The first
                    // attempt gated on depth ≤ 2, but depth is PER-CHORD here (one physical
                    // root = a chain of same-depth segments, see memory
                    // project_depth_chain_structure), so entire unlimited-length primary
                    // chains were exempt and the juniper still wove a fat mat over the rim
                    // (2026-07-02). Radius is the honest rule: inside 35% of the box
                    // half-extent the root may breach (visible nebari); farther out it
                    // stays underground or is trimmed. F7's mesh flare covers the base look.
                    if (outsideTop)
                    {
                        float rimDistSq = local.x * local.x + local.z * local.z;
                        // Surfaced roots HUG the soil: cap spread (the nebari ring) AND
                        // height — the ring alone let roots pile a tower straight up
                        // beside the trunk, burying the canopy (White Pine 2084: root
                        // tower + shading near-death). 0.65 local ≈ 15% of the box
                        // height above the rim: enough for a knuckle of exposed root,
                        // not a chimney.
                        if (rimDistSq > 0.35f * 0.35f || local.y > 0.65f)
                        {
                            ClipRoot(terminal);
                            continue;
                        }
                    }
                    if (outsideBottom)
                    {
                        // Escape only through a hole, and only once the tree is genuinely pot-bound
                        // (or this root already started escaping last season — keep it going).
                        bool overHole  = _holePotSoil != null && _holePotSoil.IsOverHole(local.x, local.z);
                        bool canEscape = overHole && (terminal.escapedRoot || _holeEscapePressure > 0.4f);
                        if (canEscape) terminal.escapedRoot = true;
                        else { ClipRoot(terminal); continue; }
                    }
                }
                // In Ishitsuki mode, training-wire cable growth is handled exclusively by
                // PreGrowRootsToSoil.  Air layer roots keep growing downward each season.
                // Training-wire tips that have reached soil spawn one underground continuation.
                if (isIshitsuki && !terminal.isAirLayerRoot)
                {
                    if (!terminal.isTrainingWire) continue;
                    // Allow soil-level training wires through; skip mid-rock ones.
                    Vector3 twTipW = transform.TransformPoint(terminal.tipPosition);
                    if (twTipW.y > plantingSurfacePoint.y + 0.1f) continue;
                }

                if (!isIshitsuki && distRatio >= 0.8f) childLength *= wallSegmentScale;

                // Training-wire tips at soil spawn underground continuation (not training wire).
                bool isTrainingWireTransition = isIshitsuki && terminal.isTrainingWire;

                var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                cont.isRoot         = true;
                cont.escapedRoot    = terminal.escapedRoot;   // a root growing out a hole stays escaped
                // Air-layer roots transition to normal underground roots once they reach soil.
                Vector3 terminalTipW = transform.TransformPoint(terminal.tipPosition);
                bool stillAboveSoil  = terminal.isAirLayerRoot &&
                                       terminalTipW.y > plantingSurfacePoint.y + 0.05f;
                cont.isAirLayerRoot  = stillAboveSoil;
                // Underground continuation is a normal root, not a training-wire cable.
                // (isTrainingWire already defaults to false on new nodes.)
                currentRootCount++;

                // Training-wire→underground transitions get laterals; mid-rock Ishitsuki cables don't.
                float lateralScale  = (isIshitsuki && !isTrainingWireTransition) ? 0f : Mathf.Clamp01(1f - distRatio);
                float lateralChance = rootLateralChance * lateralScale;
                if (currentRootCount < maxTotalRootNodes && Random.value < lateralChance)
                {
                    var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, childLength * 0.85f, terminal);
                    lat.isRoot         = true;
                    lat.isAirLayerRoot = stillAboveSoil;
                    currentRootCount++;
                }
            }
            else
            {
                // Hard cap bounds the TWIG count — the leader must always extend. A heavy
                // ramifier like Elm (springLateralChance 0.88, 0.9-len segments vs Ficus's
                // 0.08 / 2.0) exhausts the whole node budget as low twigs within a few
                // years, and the cap then froze apical growth entirely: a flat pancake of
                // twigs with no trunk (2026-07-02). Depth ≤ 1 terminals (trunk tip +
                // scaffold leaders) are few, so the overshoot is bounded.
                if (currentBranchCount >= maxBranchNodes && terminal.depth > 1) continue;

                // Bud break — hand the dormant bud GameObject to LeafManager, which keeps
                // it visible (swelling) until its leaf cluster finishes unfurling. Falls
                // back to immediate destroy when no LeafManager is present.
                if (terminal.hasBud)
                {
                    terminal.hasBud = false;
                    // The broken bud leaves a lasting bark mark at this spot (rendered by the
                    // bark shader). Mark once; budScarAge ages it from here.
                    if (!terminal.hadBudScar) { terminal.hadBudScar = true; terminal.budScarAge = 0f; }
                    if (budObjects.TryGetValue(terminal.id, out var budGo))
                    {
                        var lm = GetComponent<LeafManager>();
                        if (lm != null && budGo != null) lm.BeginBudOpen(terminal.id, budGo);
                        else if (budGo != null)          Destroy(budGo);
                        budObjects.Remove(terminal.id);
                    }
                }

                if (budType == BudType.Opposite)
                {
                    var forkNeeded = currentBranchCount + 2 <= maxBranchNodes;
                    var (dirA, dirB) = OppositeForkDirections(terminal);
                    var forkA = CreateNode(terminal.tipPosition, dirA, nodeRadius, childLength, terminal);
                    forkA.isRoot = false;
                    if (subdivs > 1) forkA.subdivisionsLeft = subdivs - 1;
                    currentBranchCount++;
                    if (forkNeeded)
                    {
                        var forkB = CreateNode(terminal.tipPosition, dirB, nodeRadius, childLength, terminal);
                        forkB.isRoot = false;
                        if (subdivs > 1) forkB.subdivisionsLeft = subdivs - 1;
                        currentBranchCount++;
                    }
                    GameManager.branches++;
                }
                else
                {
                    var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                    cont.isRoot = false;
                    if (subdivs > 1)
                        cont.subdivisionsLeft = subdivs - 1;
                    cont.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay * 0.3f);
                    currentBranchCount++;

                    if (currentBranchCount < maxBranchNodes && Random.value < springLateralChance * vigorFactor * treeEnergy * terminal.branchVigor)
                    {
                        float latLength = childLength * 0.85f * Mathf.Max(0.1f, 1f - apicalDominance);
                        var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, latLength, terminal);
                        lat.isRoot = false;
                        if (subdivs > 1)
                            lat.subdivisionsLeft = subdivs - 1;
                        lat.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay);
                        currentBranchCount++;
                        GameManager.branches++;
                    }
                }
            }
        }

        // Fill-in laterals: non-terminal root nodes inside the spread radius
        // continue sprouting new side roots each season, densifying the root mat.
        // Snapshot allNodes first — CreateNode appends to allNodes during iteration.
        int fillCount = 0;
        var fillCandidates = new List<TreeNode>(allNodes);
        foreach (var node in fillCandidates)
        {
            if (currentRootCount >= maxTotalRootNodes) break;  // hard cap reached

            if (!node.isRoot || node.isTrimmed || node.isTerminal) continue;
            if (node.depth >= maxRootDepth - 1) continue;

            float distRatio = RootDistRatio(node);
            if (distRatio >= 1f) continue;  // only fill inside the target radius

            // Chance: high near trunk, fades toward the spread edge, decays with depth
            float chance = rootFillLateralChance * (1f - distRatio) * Mathf.Pow(0.6f, node.depth);
            if (Random.value < chance)
            {
                float segLen = rootSegmentLength * Mathf.Pow(rootSegmentLengthDecay, node.depth + 1);
                segLen = Mathf.Max(segLen, 0.3f);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), rootTerminalRadius, segLen, node);
                lat.isRoot = true;
                fillCount++;
                currentRootCount++;
            }
        }

        if (fillCount > 0)
            Debug.Log($"[GRoot] Fill-in laterals sprouted={fillCount} year={GameManager.year}");

        // Boundary pressure: roots near walls thicken, slow down, and stimulate inner fills.
        // Track which low-depth ancestors need a lateral boost this season.
        var potBoundInnerCandidates = new HashSet<TreeNode>();
        int potBoundCount = 0;
        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;
            // Ishitsuki roots are outside the pot by design — do not treat as pot-bound.
            if (isIshitsukiMode) continue;

            float distRatio = RootDistRatio(node);
            if (distRatio >= 0.85f)
            {
                node.boundaryPressure++;
                if (node.boundaryPressure >= boundaryPressureThreshold)
                {
                    // Thicken — propagates up the pipe model to parent roots
                    node.radius    += boundaryThickenRate;
                    node.minRadius  = Mathf.Max(node.minRadius, node.radius);
                    potBoundCount++;

                    // Walk up toward trunk to collect low-depth ancestors for inner fill
                    TreeNode anc = node.parent;
                    while (anc != null && anc.isRoot)
                    {
                        if (anc.depth <= 2 && !anc.isTerminal)
                            potBoundInnerCandidates.Add(anc);
                        anc = anc.parent;
                    }
                }
            }
            else
            {
                // Pressure decays when the root is no longer crowded against a wall
                node.boundaryPressure = Mathf.Max(0, node.boundaryPressure - 1);
            }
        }

        // Spawn extra fill-in laterals from low-depth ancestors of pot-bound terminals.
        // Simulates the tree pushing new root mass back toward the trunk when walled in.
        int potBoundFillCount = 0;
        // Use a slightly higher ceiling so inner fill still works when outer cap is full,
        // but cap total roots at 1.5× maxTotalRootNodes to prevent unbounded growth.
        int potBoundRootCap = Mathf.RoundToInt(maxTotalRootNodes * 1.5f);
        foreach (var node in potBoundInnerCandidates)
        {
            if (potBoundFillCount >= potBoundMaxFillPerYear) break;
            if (currentRootCount >= potBoundRootCap) break;
            if (node.depth >= maxRootDepth - 1) continue;

            float distRatio = RootDistRatio(node);
            float chance = rootFillLateralChance * potBoundInnerBoost * Mathf.Pow(0.6f, node.depth);
            if (Random.value < chance)
            {
                float segLen = rootSegmentLength * Mathf.Pow(rootSegmentLengthDecay, node.depth + 1);
                segLen = Mathf.Max(segLen, 0.3f);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), rootTerminalRadius, segLen, node);
                lat.isRoot = true;
                currentRootCount++;
                potBoundFillCount++;
            }
        }
        if (potBoundCount > 0)
            Debug.Log($"[GRoot] Pot-bound terminals={potBoundCount} innerFill={potBoundFillCount} year={GameManager.year}");

        // Pot-bound first trigger: notify RootRakeManager to nudge tree + spawn surface roots
        bool nowPotBound = IsPotBound();
        if (nowPotBound && !wasPotBound)
            GetComponent<RootRakeManager>()?.OnBecamePotBound();
        wasPotBound = nowPotBound;

        // Back-budding: nodes whose tip ancestry was trimmed get a boosted chance
        // to sprout a new lateral from dormant axillary buds.
        // Snapshot allNodes first — CreateNode appends to allNodes during iteration.
        int backBudCount = 0;
        int forcedBudCount = 0;
        var backBudCandidates = new List<TreeNode>(allNodes);
        foreach (var node in backBudCandidates)
        {
            if (!node.backBudStimulated || node.isTrimmed || node.isRoot) continue;
            node.backBudStimulated = false;  // consume — only fires once per trim event

            // Styler-directed slot fill: the AutoStyler's February stimulation set a target
            // azimuth on this trunk node (preferredLateralAzimuth). That's a deliberate
            // cultivation action — forcing an epicormic bud on old wood — so it must not
            // starve behind canopy ramification. A mature tree sits AT maxBranchNodes with
            // vigorFactor floored at 0.05, which made the probability path below effectively
            // dead (~0.7%/yr) and pinned the style match at whatever scaffolds existed
            // (measured 2026-07-02: 5/14 slots, flat for 20+ years). Forced buds bypass the
            // cap + roll but are earned: energy-gated, capped per spring, and bounded overall
            // by the style's empty-slot count.
            bool styleDirected = node.preferredLateralAzimuth >= 0f && node.depth == 0;
            if (styleDirected && forcedBudCount < maxForcedBudsPerSpring && treeEnergy >= forcedBudMinEnergy)
            {
                float fChord   = branchSegmentLength * globalSegmentScale * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                int   fSubdivs = SubdivsForChord(fChord);
                float fSegLen  = fSubdivs > 1 ? fChord / fSubdivs : fChord;
                fSegLen = Mathf.Max(fSegLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                // No refinementLevel inherit — a new scaffold wants long structural extension,
                // not the short internodes of a ramified twig.
                var fLat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, fSegLen, node);
                fLat.isRoot = false;
                if (fSubdivs > 1) fLat.subdivisionsLeft = fSubdivs - 1;
                currentBranchCount++;
                forcedBudCount++;
                backBudCount++;
                continue;
            }

            if (currentBranchCount >= maxBranchNodes) continue;  // hard cap

            bool belowCap = node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
            if (!belowCap) continue;

            float chance = backBudBaseChance * backBudActivationBoost * vigorFactor
                           * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < chance)
            {
                float chordLen    = branchSegmentLength * globalSegmentScale * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                if (node.refinementLevel > 0f)
                    chordLen *= Mathf.Pow(refinementTaper, node.refinementLevel);
                int   bbSubdivs   = SubdivsForChord(chordLen);
                float segLen      = bbSubdivs > 1 ? chordLen / bbSubdivs : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
                lat.refinementLevel = Mathf.Min(node.refinementLevel + refinementOnBackBud, refinementCap);
                if (bbSubdivs > 1) lat.subdivisionsLeft = bbSubdivs - 1;
                currentBranchCount++;
                backBudCount++;
            }
        }
        if (backBudCount > 0)
            Debug.Log($"[Bud] Back-buds activated={backBudCount} (forced slot-fill={forcedBudCount}) year={GameManager.year}");

        // Old-wood budding: dormant axillary buds on interior junction nodes can break
        // spontaneously each spring without requiring a trim event. Rate is low on most trees;
        // higher on Japanese maple and other freely back-budding species.
        if (oldWoodBudChance > 0f)
        {
            int oldWoodCount = 0;
            var junctionCandidates = new List<TreeNode>(allNodes);
            foreach (var node in junctionCandidates)
            {
                if (node.isTrimmed || node.isRoot || node.isTerminal) continue;
                // Skip sub-segment junctions — not real branching forks
                bool isSubJunction = node.children.Count == 1 && node.children[0].depth == node.depth;
                if (isSubJunction) continue;

                if (currentBranchCount >= maxBranchNodes) break;  // hard cap

                bool belowCap = node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
                if (!belowCap) continue;

                if (Random.value >= oldWoodBudChance * vigorFactor) continue;

                float chordLen    = branchSegmentLength * globalSegmentScale * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                if (node.refinementLevel > 0f)
                    chordLen *= Mathf.Pow(refinementTaper, node.refinementLevel);
                int   owSubdivs   = SubdivsForChord(chordLen);
                float segLen      = owSubdivs > 1 ? chordLen / owSubdivs : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
                if (owSubdivs > 1) lat.subdivisionsLeft = owSubdivs - 1;
                currentBranchCount++;
                oldWoodCount++;
            }
            if (oldWoodCount > 0)
                Debug.Log($"[Bud] Old-wood buds broke={oldWoodCount} year={GameManager.year}");
        }

        // Bud-scar aging: each spring the mark settles a little as the wood thickens over it.
        foreach (var node in allNodes)
            if (node.hadBudScar) node.budScarAge += 1f;

        // Wound aging: drain health while the wound is open, then keep aging the SCAR so the
        // callus visually rolls over and occludes it across later seasons (WoundHealProgress).
        bool anyWoundAged = false;
        foreach (var node in allNodes)
        {
            if (!node.hasWound && !node.hadWoundScar) continue;

            anyWoundAged = true;
            node.woundAge++;   // advances both the health-heal check and the visual occlusion

            if (node.hasWound)
            {
                if (!node.pasteApplied)
                {
                    float drain = woundDrainRate;
                    if (useBevelCut && Vector3.Angle(node.woundFaceNormal, node.growDirection) > 10f)
                        drain *= bevelCutDrainMult;
                    ApplyDamage(node, DamageType.WoundDrain, drain);
                }

                float seasonsToHeal = Mathf.Max(1f, node.woundRadius * seasonsToHealPerUnit);
                if (node.woundAge >= seasonsToHeal)   // health wound closed (stops draining)
                {
                    node.hasWound = false;
                    if (woundObjects.TryGetValue(node.id, out var wGo))
                    {
                        Destroy(wGo);
                        woundObjects.Remove(node.id);
                    }
                }
            }
            // Visual occlusion (callus roll-in + stub engulfment) is driven by WoundHealProgress
            // in TreeMeshBuilder from the still-advancing woundAge — no per-frame work here.
        }
        // Force a rebuild so the occlusion advances even on a mature tree that added no geometry.
        if (anyWoundAged) meshBuilder?.SetDirty();

        // Trim trauma recovery: all damaged non-root nodes heal a small amount each spring.
        // Wound drain (woundDrainRate = 0.05) slightly exceeds this (0.04) so unprotected
        // wounds still net-worsen, while paste-protected and trauma-only nodes recover cleanly.
        float recoveryThisSeason = trimTraumaRecoveryPerSeason * treeEnergy;
        if (recoveryThisSeason > 0f)
        {
            foreach (var node in allNodes)
            {
                if (node.isRoot || node.isTrimmed || node.health >= 1f) continue;
                node.health = Mathf.Min(1f, node.health + recoveryThisSeason);
            }
        }

        // Per-branch vigor update:
        //   1. Apical nudge — shallow/apex nodes accumulate vigor naturally each season
        //      (simulates the hormonal advantage of the apex and outer tips).
        //   2. Decay toward 1.0 — without trimming, most nodes regress to neutral over time.
        //   3. Clamp to [vigorMin, vigorMax].
        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed) continue;
            float depthFactor = node.depth == 0 ? 1f : 1f / node.depth;
            node.branchVigor += apicalVigorBonus * depthFactor;
            node.branchVigor  = Mathf.Lerp(node.branchVigor, 1f, vigorDecayRate);
            node.branchVigor  = Mathf.Clamp(node.branchVigor, vigorMin, vigorMax);
        }

        if (terminals.Count > 0 || fillCount > 0 || backBudCount > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }

        UpdateAirLayers();
        AdvanceGrafts();
        DetectNewFusions();
        AdvanceFusions();
        RecalculateRootHealthScore();

        // Branch weight: compute loads bottom-up, sag directions, apply junction stress
        if (branchWeightEnabled)
            BranchWeightPass();

        // Dieback: mark dead branches, remove dropped ones, apply shading damage
        if (branchDiebackEnabled)
            DiebackPass();

        // Death evaluation — runs after all seasonal damage is applied
        if (treeDeathEnabled)
            EvaluateTreeDeath();
    }

}
