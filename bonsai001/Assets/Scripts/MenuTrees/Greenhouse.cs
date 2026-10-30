using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The greenhouse (2026-10-29): every saved menu tree on display in one place, reached from the
/// main menu's GREENHOUSE button.
///
/// <para>Built entirely at runtime inside the main-menu scene, a long way from the menu's own set,
/// so it needs no scene of its own; BACK reloads the menu scene, which resets the lighting and the
/// showcase tree. The building is a placeholder made of primitives (floor, white frame, faint glass)
/// in <see cref="BuildHouse"/> — swap it for real art there without touching the rest.</para>
///
/// <para>Trees stand on their own tables in two rows either side of a central aisle, each turned so
/// the side the player last looked at faces the aisle. The camera glides from tree to tree (click
/// one, or ◀ / ▶, A / D, the arrow keys) and orbits the one in focus (drag, scroll to zoom). The
/// card at the bottom shows the tree's details and its actions: play its save, set it as the menu
/// tree, mark it a favourite, export it as OBJ, delete it.</para>
/// </summary>
public class Greenhouse : MonoBehaviour
{
    static readonly Vector3 Origin = new Vector3(2000f, 0f, 0f);
    const float AisleHalf = 1.1f;

    class Entry
    {
        public MenuTreeLibrary.Meta meta;
        public GameObject holder, tree;
        public Vector3    frontLocal;   // tree-root space, towards where the player stood
        public Vector3    pivotLocal;   // snapshot pivot, tree-root space
        public Vector3    pivot;        // world, after placement
        public float      radius;
        public float      height;
        public bool       hasSave;
        public Collider   pick;
        public List<(MeshRenderer r, Color summer)> leaves = new List<(MeshRenderer, Color)>();
    }

    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor     = Shader.PropertyToID("_Color");
    const string SeasonPrefKey = "GreenhouseSeason";
    const string SyncPrefKey   = "GreenhouseSeasonSync";
    // 0 = summer green, AutumnFull = full autumn colour, past BareFrom = leaves off (every tree).
    const float AutumnFull = 0.9f, BareFrom = 0.97f;
    float season;
    bool  syncToDate;
    Slider seasonSlider;
    VisualElement seasonBox;
    MaterialPropertyBlock seasonMpb;   // made in Start: Unity forbids creating one in a field initializer

    readonly List<Entry> entries = new List<Entry>();
    readonly Dictionary<Collider, Entry> byCollider = new Dictionary<Collider, Entry>();

    VisualElement uiRoot, hud, card, actions, confirm;
    Label loadingLabel, nameLabel, detailLabel, badgeLabel, statusLabel, countLabel;
    Button playBtn, menuBtn, favBtn;
    System.Action<string> onPlay;

    Camera cam;
    int    focus = -1;
    float  yaw, pitch = 14f, dist = 4f, targetDist = 4f;
    Vector3 pivot, targetPivot;
    bool   ready, confirming;
    Vector2 pressPos;
    bool   pressed, dragging;
    float  spacing = 3f;

    /// <summary>Opens the greenhouse over the main menu. <paramref name="play"/> loads a save slot.</summary>
    public static Greenhouse Open(VisualElement root, System.Action<string> play)
    {
        var go = new GameObject("Greenhouse");
        var g  = go.AddComponent<Greenhouse>();
        g.uiRoot = root;
        g.onPlay = play;
        return g;
    }

    void Start()
    {
        foreach (var s in FindObjectsByType<MenuTreeShowcase>()) Destroy(s.gameObject);
        foreach (var l in FindObjectsByType<Light>())
            if (l.type == LightType.Directional) l.enabled = false;

        seasonMpb = new MaterialPropertyBlock();
        cam = Camera.main;
        SetUpLighting();
        PlaceCameraOverview();
        BuildHud();
        StartCoroutine(LoadTrees());
    }

    void OnDestroy()
    {
        hud?.RemoveFromHierarchy();
    }

    // ── Loading and layout ────────────────────────────────────────────────────

    IEnumerator LoadTrees()
    {
        var metas = MenuTreeLibrary.List();
        var saves = new HashSet<string>();
        foreach (var s in SaveManager.ListAllSaves()) saves.Add(s.slotId);

        for (int i = 0; i < metas.Count; i++)
        {
            loadingLabel.text = $"Bringing in the trees…  {i + 1} / {metas.Count}";
            yield return null;

            var snap = MenuTreeLibrary.Load(metas[i].slotId);
            if (snap == null) continue;
            var tree = MenuTreeBuilder.Build(snap, MenuTreeBuilder.BuildMaterials(snap), "GreenhouseTree");
            if (tree == null) continue;

            var front = snap.cameraLocalPosition - snap.pivotLocal;
            front.y = 0f;
            entries.Add(new Entry
            {
                meta       = metas[i],
                tree       = tree,
                frontLocal = front.sqrMagnitude > 1e-4f ? front.normalized : Vector3.forward,
                pivotLocal = snap.pivotLocal,
                hasSave    = saves.Contains(metas[i].slotId),
            });
            CollectLeaves(entries[entries.Count - 1]);
        }

        loadingLabel.style.display = DisplayStyle.None;
        if (entries.Count == 0)
        {
            BuildHouse(1, 3f);
            PlaceCameraOverview();
            loadingLabel.text = "No trees here yet.\nQuit a game from the pause menu, and its tree is brought here.";
            loadingLabel.style.display = DisplayStyle.Flex;
            countLabel.text = "";
            yield break;
        }

        // Spacing from the widest tree (table included), so nothing overlaps.
        float widest = 0f, tallest = 0f;
        foreach (var e in entries)
        {
            var b = Bounds(e.tree);
            widest  = Mathf.Max(widest, Mathf.Max(b.size.x, b.size.z));
            tallest = Mathf.Max(tallest, b.size.y);
        }
        spacing = Mathf.Max(2.5f, widest + 1.2f);
        Layout();
        ApplySeason();
        BuildHouse((entries.Count + 1) / 2, tallest);

        ready = true;
        Focus(0, snap: true);
    }

    /// <summary>Places every tree: two rows either side of the aisle, alternating, front to the aisle.</summary>
    void Layout()
    {
        byCollider.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool left = i % 2 == 0;
            float x = left ? -(AisleHalf + spacing * 0.5f) : (AisleHalf + spacing * 0.5f);
            var slot = Origin + new Vector3(x, 0f, (i / 2) * spacing + spacing * 0.5f);

            if (e.holder == null)
            {
                e.holder = new GameObject($"Slot_{e.meta.slotId}");
                e.holder.transform.SetParent(transform, false);
                e.tree.transform.SetParent(e.holder.transform, false);
            }
            e.holder.transform.SetPositionAndRotation(slot, Quaternion.identity);
            e.tree.transform.localPosition = Vector3.zero;

            // Turn the tree so its front faces the aisle.
            var frontWorld = e.tree.transform.TransformDirection(e.frontLocal);
            frontWorld.y = 0f;
            var want = left ? Vector3.right : Vector3.left;
            float delta = Vector3.SignedAngle(frontWorld, want, Vector3.up);
            e.holder.transform.rotation = Quaternion.AngleAxis(delta, Vector3.up);

            // Stand it on the floor, centred on its slot.
            var b = Bounds(e.tree);
            e.tree.transform.position += new Vector3(slot.x - b.center.x, Origin.y - b.min.y, slot.z - b.center.z);
            b = Bounds(e.tree);
            e.pivot  = e.tree.transform.TransformPoint(e.pivotLocal);
            e.radius = Mathf.Max(0.5f, b.extents.magnitude);
            e.height = b.size.y;

            if (e.pick == null)
            {
                var pickGo = new GameObject("Pick");
                pickGo.transform.SetParent(e.holder.transform, true);
                e.pick = pickGo.AddComponent<BoxCollider>();
            }
            e.pick.transform.SetPositionAndRotation(b.center, Quaternion.identity);
            e.pick.transform.localScale = Vector3.one;
            ((BoxCollider)e.pick).size = b.size;
            byCollider[e.pick] = e;
        }
    }

    /// <summary>Remembers each leaf part's saved (summer) tint. Leaves are the parts that carry a
    /// tint; bark, pot and table don't.</summary>
    void CollectLeaves(Entry e)
    {
        foreach (var r in e.tree.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.HasPropertyBlock()) continue;
            r.GetPropertyBlock(seasonMpb);
            if (mpbHas(seasonMpb, IdBaseColor, out var c) || mpbHas(seasonMpb, IdColor, out c))
                e.leaves.Add((r, c));
        }

        static bool mpbHas(MaterialPropertyBlock b, int id, out Color c)
        {
            bool has = b.HasColor(id);
            c = has ? b.GetColor(id) : default;
            return has;
        }
    }

    /// <summary>
    /// Slider position for a real date (northern hemisphere): summer green through August,
    /// turning from September 1 to November 15, bare from then to the end of March, green again
    /// from April.
    /// </summary>
    static float SeasonForDate(System.DateTime d)
    {
        int doy = d.DayOfYear;
        int sep1 = new System.DateTime(d.Year, 9, 1).DayOfYear;
        int nov15 = new System.DateTime(d.Year, 11, 15).DayOfYear;
        int apr1 = new System.DateTime(d.Year, 4, 1).DayOfYear;
        if (doy < apr1 || doy >= nov15) return 1f;
        if (doy < sep1) return 0f;
        return AutumnFull * (doy - sep1) / (float)(nov15 - sep1);
    }

    /// <summary>Blends every deciduous tree's leaves from their saved colour toward the species'
    /// autumn colour, keeping each leaf group's lightness so the variety survives. Evergreens stay
    /// green. Past <see cref="BareFrom"/> every tree's leaves are hidden, evergreens included.
    /// Trees saved before autumn colours were stored use a generic orange-red.</summary>
    void ApplySeason()
    {
        bool bare = season >= BareFrom;
        float t = Mathf.Clamp01(season / AutumnFull);
        foreach (var e in entries)
        {
            foreach (var (r, _) in e.leaves)
                if (r != null) r.enabled = !bare;
            if (bare || e.meta.evergreen) continue;
            Color fall = e.meta.autumnColor.a > 0f ? e.meta.autumnColor : new Color(0.85f, 0.35f, 0.10f);
            foreach (var (r, summerTint) in e.leaves)
            {
                if (r == null) continue;
                float k = Mathf.Clamp(Luma(summerTint) / 0.45f, 0.6f, 1.4f);
                var target = new Color(fall.r * k, fall.g * k, fall.b * k, summerTint.a);
                var c = Color.Lerp(summerTint, target, t);
                r.GetPropertyBlock(seasonMpb);
                seasonMpb.SetColor(IdBaseColor, c);
                seasonMpb.SetColor(IdColor, c);
                r.SetPropertyBlock(seasonMpb);
            }
        }
    }

    static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ── The building (placeholder art) ────────────────────────────────────────

    /// <summary>A simple greenhouse from primitives: paved floor and gravel aisle, a white frame of
    /// posts, rails and roof rafters, and faint glass. Replace this method to use real art.</summary>
    void BuildHouse(int rows, float tallest)
    {
        var house = new GameObject("GreenhouseBuilding");
        house.transform.SetParent(transform, false);

        float halfW  = AisleHalf + spacing + 0.6f;
        float length = Mathf.Max(1, rows) * spacing;
        float wallH  = Mathf.Max(3.5f, tallest + 1.5f);
        float ridgeH = wallH + halfW * 0.5f;
        float z0 = -0.6f, z1 = length + 0.6f;

        var floorMat = Lit(new Color(0.55f, 0.52f, 0.47f));
        var aisleMat = Lit(new Color(0.42f, 0.40f, 0.36f));
        var frameMat = Lit(new Color(0.92f, 0.92f, 0.90f));
        var glassMat = Glass(new Color(0.75f, 0.9f, 0.95f, 0.08f));

        Box(house, "Floor", new Vector3(0f, -0.05f, (z0 + z1) * 0.5f), new Vector3(halfW * 2f + 1f, 0.1f, z1 - z0 + 1f), floorMat);
        Box(house, "Aisle", new Vector3(0f, 0.005f, (z0 + z1) * 0.5f), new Vector3(AisleHalf * 2f, 0.01f, z1 - z0), aisleMat);

        const float post = 0.08f;
        int bays = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 2.5f));
        float bay = (z1 - z0) / bays;
        float rafterLen = Mathf.Sqrt(halfW * halfW + (ridgeH - wallH) * (ridgeH - wallH));
        float rafterAng = Mathf.Atan2(ridgeH - wallH, halfW) * Mathf.Rad2Deg;
        for (int i = 0; i <= bays; i++)
        {
            float z = z0 + i * bay;
            foreach (float side in new[] { -1f, 1f })
            {
                Box(house, "Post", new Vector3(side * halfW, wallH * 0.5f, z), new Vector3(post, wallH, post), frameMat);
                var r = Box(house, "Rafter", new Vector3(side * halfW * 0.5f, (wallH + ridgeH) * 0.5f, z),
                            new Vector3(rafterLen, post, post), frameMat);
                r.transform.localRotation = Quaternion.Euler(0f, 0f, side < 0 ? rafterAng : -rafterAng);
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            Box(house, "Rail",     new Vector3(side * halfW, wallH, (z0 + z1) * 0.5f), new Vector3(post, post, z1 - z0), frameMat);
            Box(house, "Sill",     new Vector3(side * halfW, 0.8f,  (z0 + z1) * 0.5f), new Vector3(post * 1.5f, post, z1 - z0), frameMat);
            Box(house, "GlassWall", new Vector3(side * halfW, wallH * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.01f, wallH, z1 - z0), glassMat);
            var roof = Box(house, "GlassRoof", new Vector3(side * halfW * 0.5f, (wallH + ridgeH) * 0.5f, (z0 + z1) * 0.5f),
                           new Vector3(rafterLen, 0.01f, z1 - z0), glassMat);
            roof.transform.localRotation = Quaternion.Euler(0f, 0f, side < 0 ? rafterAng : -rafterAng);
        }
        Box(house, "Ridge", new Vector3(0f, ridgeH, (z0 + z1) * 0.5f), new Vector3(post, post, z1 - z0), frameMat);
        foreach (float z in new[] { z0, z1 })
            Box(house, "GlassEnd", new Vector3(0f, wallH * 0.5f, z), new Vector3(halfW * 2f, wallH, 0.01f), glassMat);

        house.transform.position = Origin;
    }

    static GameObject Box(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        if (name.StartsWith("Glass")) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static Material Lit(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
        return m;
    }

    /// <summary>Sprites/Default is always in a build and blends alpha, so the glass needs no
    /// transparent URP variant that might have been stripped.</summary>
    static Material Glass(Color c)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = c;
        return m;
    }

    void SetUpLighting()
    {
        var sun = new GameObject("GreenhouseSun").AddComponent<Light>();
        sun.transform.SetParent(transform, false);
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.6f;

        RenderSettings.ambientMode         = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.58f, 0.66f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.47f, 0.49f, 0.44f);
        RenderSettings.ambientGroundColor  = new Color(0.27f, 0.25f, 0.22f);
        RenderSettings.ambientIntensity    = 1f;
        RenderSettings.fog = false;

        if (cam != null)
        {
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.66f, 0.78f, 0.86f);
            cam.fieldOfView     = 50f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 300f;
        }
    }

    // ── Camera ────────────────────────────────────────────────────────────────

    void Focus(int i, bool snap = false)
    {
        if (entries.Count == 0) return;
        focus = (i % entries.Count + entries.Count) % entries.Count;
        var e = entries[focus];
        targetPivot = e.pivot;
        targetDist  = e.radius * 2.4f;
        // Look at it from the aisle.
        float side = e.holder.transform.position.x < Origin.x ? 1f : -1f;
        yaw   = side > 0 ? -90f : 90f;
        pitch = 14f;
        if (snap || GameSettings.ReducedMotion) { pivot = targetPivot; dist = targetDist; }
        CancelDelete();
        RefreshCard();
    }

    void PlaceCameraOverview()
    {
        pivot = targetPivot = Origin + new Vector3(0f, 1.5f, 1.5f);
        dist = targetDist = 6f;
        yaw = 0f; pitch = 20f;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        float k = GameSettings.ReducedMotion ? 1f : 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
        pivot = Vector3.Lerp(pivot, targetPivot, k);
        dist  = Mathf.Lerp(dist, targetDist, k);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.SetPositionAndRotation(pivot - rot * Vector3.forward * dist, rot);
    }

    void Update()
    {
        var kb    = Keyboard.current;
        var mouse = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (confirming) CancelDelete(); else Back();
            return;
        }
        if (!ready) return;

        if (kb != null)
        {
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)  Focus(focus - 1);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Focus(focus + 1);
        }
        if (mouse == null) return;

        Vector2 pos = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame && !OverUi(pos))
        {
            pressed = true; dragging = false; pressPos = pos;
        }
        if (pressed && mouse.leftButton.isPressed)
        {
            if (!dragging && (pos - pressPos).sqrMagnitude > 36f) dragging = true;
            if (dragging)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw   += d.x * 0.25f;
                pitch  = Mathf.Clamp(pitch - d.y * 0.2f, -5f, 70f);
            }
        }
        if (pressed && mouse.leftButton.wasReleasedThisFrame)
        {
            pressed = false;
            if (!dragging && cam != null && Physics.Raycast(cam.ScreenPointToRay(pos), out var hit, 500f)
                && byCollider.TryGetValue(hit.collider, out var e))
                Focus(entries.IndexOf(e));
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f && !OverUi(pos) && focus >= 0)
        {
            float r = entries[focus].radius;
            targetDist = Mathf.Clamp(targetDist * (1f - Mathf.Sign(scroll) * 0.1f), r * 0.8f, r * 8f);
        }
    }

    bool OverUi(Vector2 screenPos)
    {
        var panel = hud?.panel;
        if (panel == null) return false;
        var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
        // The menu's own full-screen root is pickable everywhere, so only count the greenhouse's
        // buttons and card as "the UI".
        for (var v = panel.Pick(p); v != null; v = v.parent)
            if (v is Button || v == card || v == seasonBox) return true;
        return false;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    void Back() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    Entry Current => focus >= 0 && focus < entries.Count ? entries[focus] : null;

    void Play()
    {
        var e = Current;
        if (e == null || !e.hasSave) return;
        onPlay?.Invoke(e.meta.slotId);
    }

    void ToggleMenuTree()
    {
        var e = Current;
        if (e == null) return;
        MenuTreeLibrary.Pinned = MenuTreeLibrary.Pinned == e.meta.slotId ? null : e.meta.slotId;
        RefreshCard();
    }

    void ToggleFavorite()
    {
        var e = Current;
        if (e == null) return;
        e.meta.favorite = !e.meta.favorite;
        MenuTreeLibrary.SetFavorite(e.meta.slotId, e.meta.favorite);
        RefreshCard();
    }

    void Export()
    {
        var e = Current;
        if (e == null) return;
        string dir = MenuTreeExport.ToObj(e.tree, DisplayName(e.meta));
        if (dir == null) { statusLabel.text = "Export failed. The log has the details."; return; }
        statusLabel.text = $"Exported to {dir}";
        Application.OpenURL("file:///" + dir.Replace('\\', '/'));
    }

    void AskDelete()
    {
        if (Current == null) return;
        confirming = true;
        actions.style.display = DisplayStyle.None;
        confirm.style.display = DisplayStyle.Flex;
    }

    void CancelDelete()
    {
        confirming = false;
        if (actions != null) actions.style.display = DisplayStyle.Flex;
        if (confirm != null) confirm.style.display = DisplayStyle.None;
    }

    void ConfirmDelete()
    {
        var e = Current;
        CancelDelete();
        if (e == null) return;
        MenuTreeLibrary.Delete(e.meta.slotId);
        byCollider.Remove(e.pick);
        Destroy(e.holder);
        entries.Remove(e);
        if (entries.Count == 0) { Back(); return; }
        Layout();
        Focus(Mathf.Min(focus, entries.Count - 1));
    }

    static string DisplayName(MenuTreeLibrary.Meta m) =>
        string.IsNullOrEmpty(m.treeName) ? m.speciesName : m.treeName;

    // ── HUD ───────────────────────────────────────────────────────────────────

    void RefreshCard()
    {
        var e = Current;
        countLabel.text = $"{entries.Count} tree{(entries.Count == 1 ? "" : "s")}";
        if (e == null) { card.style.display = DisplayStyle.None; return; }
        card.style.display = DisplayStyle.Flex;
        statusLabel.text = "";

        var m = e.meta;
        nameLabel.text = DisplayName(m);
        string when = System.DateTime.TryParse(m.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("MMM d, yyyy") : "";
        string species = string.IsNullOrEmpty(m.treeName) ? "" : m.speciesName + "  ·  ";
        detailLabel.text = $"{species}{m.treeAge} years old  ·  saved {when}  ·  {focus + 1} of {entries.Count}";

        bool pinned = MenuTreeLibrary.Pinned == m.slotId;
        var badges = new List<string>();
        if (pinned)     badges.Add("Menu tree");
        if (m.favorite) badges.Add("Favorite");
        if (!e.hasSave) badges.Add("Save deleted");
        badgeLabel.text = string.Join("  ·  ", badges);

        playBtn.SetEnabled(e.hasSave);
        menuBtn.text = pinned ? "UNSET MENU TREE" : "SET AS MENU TREE";
        favBtn.text  = m.favorite ? "UNFAVORITE" : "FAVORITE";
    }

    void BuildHud()
    {
        var th = UiTheme.Current;
        hud = new VisualElement { name = "GreenhouseHud", pickingMode = PickingMode.Ignore };
        hud.style.position = Position.Absolute;
        hud.style.left = hud.style.right = hud.style.top = hud.style.bottom = 0;

        // Top-left: back, title, count.
        var top = new VisualElement();
        top.style.position = Position.Absolute;
        top.style.left = 24; top.style.top = 20;
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;
        top.Add(Btn("BACK", th.cardBg, th.textMain, Back));
        var title = new Label("GREENHOUSE");
        title.style.fontSize = 22;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(th.accent);
        title.style.marginLeft = 16;
        top.Add(title);
        countLabel = new Label("");
        countLabel.style.color = new StyleColor(th.textSub);
        countLabel.style.marginLeft = 12;
        top.Add(countLabel);
        hud.Add(top);

        loadingLabel = new Label("");
        loadingLabel.style.position = Position.Absolute;
        loadingLabel.style.left = loadingLabel.style.right = 0;
        loadingLabel.style.top = new Length(45, LengthUnit.Percent);
        loadingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        loadingLabel.style.fontSize = 16;
        loadingLabel.style.color = new StyleColor(th.textMain);
        loadingLabel.style.whiteSpace = WhiteSpace.Normal;
        loadingLabel.pickingMode = PickingMode.Ignore;
        hud.Add(loadingLabel);

        // Bottom: ◀ card ▶
        var bottom = new VisualElement { pickingMode = PickingMode.Ignore };
        bottom.style.position = Position.Absolute;
        bottom.style.left = bottom.style.right = 0;
        bottom.style.bottom = 22;
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.justifyContent = Justify.Center;
        bottom.style.alignItems = Align.Center;
        hud.Add(bottom);

        card = new VisualElement();
        card.style.display = DisplayStyle.None;
        card.style.width = 640;
        card.style.backgroundColor = new StyleColor(th.panelBg);
        card.style.borderTopColor = card.style.borderBottomColor =
        card.style.borderLeftColor = card.style.borderRightColor = new StyleColor(th.edge);
        card.style.borderTopWidth = card.style.borderBottomWidth =
        card.style.borderLeftWidth = card.style.borderRightWidth = 1;
        card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
        card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 8;
        card.style.paddingTop = card.style.paddingBottom = 12;
        card.style.paddingLeft = card.style.paddingRight = 16;
        card.style.marginLeft = card.style.marginRight = 10;

        bottom.Add(Btn("◀", th.cardBg, th.textMain, () => Focus(focus - 1)));
        bottom.Add(card);
        bottom.Add(Btn("▶", th.cardBg, th.textMain, () => Focus(focus + 1)));

        nameLabel = new Label();
        nameLabel.style.fontSize = 20;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.color = new StyleColor(th.accent);
        card.Add(nameLabel);
        detailLabel = new Label();
        detailLabel.style.color = new StyleColor(th.textMain);
        card.Add(detailLabel);
        badgeLabel = new Label();
        badgeLabel.style.color = new StyleColor(th.textSub);
        badgeLabel.style.marginBottom = 8;
        card.Add(badgeLabel);

        actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.flexWrap = Wrap.Wrap;
        playBtn = Btn("PLAY", th.accent, th.ink, Play);
        menuBtn = Btn("SET AS MENU TREE", th.cardBg, th.textMain, ToggleMenuTree);
        favBtn  = Btn("FAVORITE", th.cardBg, th.textMain, ToggleFavorite);
        actions.Add(playBtn);
        actions.Add(menuBtn);
        actions.Add(favBtn);
        actions.Add(Btn("EXPORT 3D MODEL", th.cardBg, th.textMain, Export));
        actions.Add(Btn("DELETE", th.cardBg, th.negative, AskDelete));
        card.Add(actions);

        confirm = new VisualElement();
        confirm.style.display = DisplayStyle.None;
        confirm.style.flexDirection = FlexDirection.Row;
        confirm.style.alignItems = Align.Center;
        var q = new Label("Remove this tree from the greenhouse? Its save isn't touched.");
        q.style.color = new StyleColor(th.textMain);
        q.style.flexShrink = 1;
        q.style.whiteSpace = WhiteSpace.Normal;
        q.style.marginRight = 8;
        confirm.Add(q);
        confirm.Add(Btn("DELETE", th.negative, th.ink, ConfirmDelete));
        confirm.Add(Btn("CANCEL", th.cardBg, th.textMain, CancelDelete));
        card.Add(confirm);

        statusLabel = new Label();
        statusLabel.style.color = new StyleColor(th.textSub);
        statusLabel.style.fontSize = 11;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        card.Add(statusLabel);

        var hint = new Label("Drag to look around  ·  scroll to zoom  ·  click a tree, or A / D, to move between them  ·  Esc to go back");
        hint.style.position = Position.Absolute;
        hint.style.right = 24; hint.style.top = 26;
        hint.style.fontSize = 11;
        hint.style.color = new StyleColor(th.textSub);
        hint.pickingMode = PickingMode.Ignore;
        hud.Add(hint);

        // Season slider (2026-10-29): every tree from summer green to its species' autumn colour,
        // and at the far end, leaves off on every tree (evergreens too) for a winter view.
        try
        {
            season     = Mathf.Clamp01(PlayerPrefs.GetFloat(SeasonPrefKey, 0f));
            syncToDate = PlayerPrefs.GetInt(SyncPrefKey, 0) == 1;
        }
        catch { season = 0f; syncToDate = false; }
        if (syncToDate) season = SeasonForDate(System.DateTime.Now);
        seasonBox = new VisualElement();
        seasonBox.style.position = Position.Absolute;
        seasonBox.style.right = 24; seasonBox.style.top = 48;
        seasonBox.style.width = 380;
        seasonBox.style.flexDirection = FlexDirection.Row;
        seasonBox.style.flexWrap = Wrap.Wrap;
        seasonBox.style.alignItems = Align.Center;
        seasonBox.style.backgroundColor = new StyleColor(th.panelBg);
        seasonBox.style.borderTopLeftRadius = seasonBox.style.borderTopRightRadius =
        seasonBox.style.borderBottomLeftRadius = seasonBox.style.borderBottomRightRadius = 6;
        seasonBox.style.paddingLeft = seasonBox.style.paddingRight = 10;
        seasonBox.style.paddingTop = seasonBox.style.paddingBottom = 4;
        var summer = new Label("Summer");
        summer.style.color = new StyleColor(th.textSub);
        summer.style.fontSize = 11;
        seasonBox.Add(summer);
        seasonSlider = new Slider(0f, 1f) { value = season };
        seasonSlider.style.flexGrow = 1;
        seasonSlider.style.marginLeft = seasonSlider.style.marginRight = 8;
        seasonSlider.RegisterValueChangedCallback(e =>
        {
            if (syncToDate) return;
            season = e.newValue;
            ApplySeason();
            try { PlayerPrefs.SetFloat(SeasonPrefKey, season); } catch { }
        });
        seasonSlider.SetEnabled(!syncToDate);
        seasonBox.Add(seasonSlider);
        var winter = new Label("Autumn · Winter");
        winter.style.color = new StyleColor(th.textSub);
        winter.style.fontSize = 11;
        seasonBox.Add(winter);

        var sync = new Toggle("Match today's date") { value = syncToDate };
        sync.style.width = new Length(100, LengthUnit.Percent);
        sync.style.marginTop = 2;
        sync.labelElement.style.color = new StyleColor(th.textSub);
        sync.labelElement.style.fontSize = 11;
        sync.RegisterValueChangedCallback(e =>
        {
            syncToDate = e.newValue;
            if (syncToDate) season = SeasonForDate(System.DateTime.Now);
            else { try { season = Mathf.Clamp01(PlayerPrefs.GetFloat(SeasonPrefKey, 0f)); } catch { } }
            seasonSlider.SetEnabled(!syncToDate);
            seasonSlider.SetValueWithoutNotify(season);
            ApplySeason();
            try { PlayerPrefs.SetInt(SyncPrefKey, syncToDate ? 1 : 0); } catch { }
        });
        seasonBox.Add(sync);
        hud.Add(seasonBox);

        uiRoot.Add(hud);
    }

    static Button Btn(string text, Color bg, Color fg, System.Action onClick)
    {
        var b = new Button(onClick) { text = text };
        b.style.height = 32;
        b.style.fontSize = 12;
        b.style.paddingLeft = b.style.paddingRight = 12;
        b.style.marginRight = 6;
        b.style.marginTop = 4;
        b.style.backgroundColor = new StyleColor(bg);
        b.style.color = new StyleColor(fg);
        b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
        b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
        b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 5;
        return b;
    }
}
