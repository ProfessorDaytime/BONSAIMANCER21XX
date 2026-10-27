using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Camera-locked, layered rain, shown only while <see cref="WeatherManager"/> says it is raining
/// (2026-10-13).
///
/// <para><b>Setup:</b> on the "Rain" object, next to its VideoPlayer, with the renderer's material
/// set to <c>RainLumaKey</c> (shader "Bonsai/Luma Key Video"). That object becomes a hidden
/// <i>template</i>: its VideoPlayer plays once into a shared render texture, and its material
/// supplies the look. Where it sits in the scene no longer matters.</para>
///
/// <para><b>Layers.</b> Vertical quads parented to the camera, so they turn and move with it and
/// the rain always reads the same on screen. Some sit <b>in front of the tree</b> and some
/// <b>behind it</b>; the in-front/behind split is measured from the tree's actual size (canopy, not
/// just pot), so it holds at any zoom and as the tree grows. When the camera is too close to fit
/// every front layer, the extras switch off — down to one, never zero.</para>
///
/// <para><b>No edges, ever.</b> Each layer works out the minimum size that covers the whole screen
/// at its depth — including its own sideways offset — and adds a margin. Every layer shows the
/// whole clip at its own aspect ratio (no cropping or stretching); about half are mirrored and each
/// is offset sideways, so one clip doesn't read as repeated. Far layers are fainter.</para>
///
/// <para><b>Staggered playback.</b> Each layer can play its own copy of the clip from a different
/// point, so no two layers are on the same frame. The original VideoPlayer is stream 0 (and the
/// only one with audio); extra streams are created at runtime and start evenly spread through the
/// clip. Fewer streams than layers is allowed — layers then share streams round-robin — to save
/// decoding.</para>
///
/// <para><b>Live tuning:</b> Tint, Opacity and the key thresholds are read from the RainLumaKey
/// material every frame, so editing the material during Play updates every layer.</para>
///
/// <para>Fully faded out, the layers are disabled and the video paused, so a clear day costs
/// nothing. If the clip has audio, it follows the fade and Settings ▸ Audio.</para>
/// </summary>
[RequireComponent(typeof(Renderer))]
public class RainVisual : MonoBehaviour
{
    [Header("Weather response")]
    [Tooltip("Seconds to fade in or out (real time, so it looks the same at any game speed).")]
    [SerializeField] float fadeSeconds = 1.5f;

    [Tooltip("Strength during a storm, relative to ordinary rain.")]
    [SerializeField, Range(0.5f, 2f)] float stormStrength = 1.5f;

    [Tooltip("Video playback speed during a storm, relative to ordinary rain.")]
    [SerializeField, Range(0.5f, 2f)] float stormPlaybackSpeed = 1.3f;

    [Header("Layers")]
    [Tooltip("Layers between the camera and the tree. Drops to fewer (never below one) when the " +
             "camera is too close for them all to fit.")]
    [SerializeField, Range(1, 4)] int frontLayers = 2;

    [Tooltip("Layers behind the tree.")]
    [SerializeField, Range(1, 6)] int backLayers = 3;

    [Tooltip("Minimum gap between front layers, in world units. Closer than this and front layers " +
             "are dropped.")]
    [SerializeField] float minFrontSpacing = 0.2f;

    [Tooltip("Gap between back layers, as a fraction of the camera–tree distance.")]
    [SerializeField, Range(0.1f, 1f)] float backSpacing = 0.4f;

    [Tooltip("Treat the tree as this wide relative to its height, for keeping front layers in front " +
             "of the canopy and back layers behind it.")]
    [SerializeField, Range(0.3f, 1.5f)] float canopyWidthToHeight = 0.6f;

    [Tooltip("How far layers spread sideways, as a fraction of the view width. Layers grow to cover " +
             "their own offset, so this never exposes an edge.")]
    [SerializeField, Range(0f, 1f)] float sidewaysSpread = 0.35f;

    [Tooltip("Extra size beyond what is needed to cover the screen.")]
    [SerializeField, Range(1.02f, 2f)] float edgeMargin = 1.2f;

    [Tooltip("Opacity of the farthest layer relative to the nearest.")]
    [SerializeField, Range(0f, 1f)] float farLayerOpacity = 0.4f;

    [Tooltip("Separately-timed copies of the video, so layers show different frames. Capped at " +
             "the layer count. Each extra stream is another 720p decode; lower this if it costs " +
             "too much, and layers will share streams.")]
    [SerializeField, Range(1, 10)] int videoStreams = 10;

    [Tooltip("Changes the random sideways positions, mirroring and stream start points.")]
    [SerializeField] int seed = 7;

    [Tooltip("What the depths are measured against. Leave empty to use the tree.")]
    [SerializeField] Transform focus;

    static readonly int FadeId    = Shader.PropertyToID("_Fade");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int TintId    = Shader.PropertyToID("_Tint");
    static readonly int KeyLowId  = Shader.PropertyToID("_KeyLow");
    static readonly int KeyHighId = Shader.PropertyToID("_KeyHigh");

    class Layer
    {
        public Transform    tf;
        public MeshRenderer mr;
        public Material     mat;
        public bool         front;
        public int          index;      // within its group, nearest first
        public float        xFrac;      // sideways position, fraction of view width
        public bool         mirrored;
    }

    readonly List<Layer> layers = new List<Layer>();

    Renderer      templateRenderer;
    Material      template;         // the RainLumaKey asset — read every frame for live tuning
    VideoPlayer   video;            // stream 0: the original player (keeps the audio)
    RenderTexture rt;               // stream 0's texture
    readonly List<VideoPlayer>   streams   = new List<VideoPlayer>();
    readonly List<RenderTexture> streamRTs = new List<RenderTexture>();
    Camera        cam;
    TreeSkeleton  tree;
    Transform     layerRoot;
    Mesh          quad;
    float         fade;             // 0 hidden .. 1 shown (above 1 in a storm)
    bool          shown;

    void Awake()
    {
        templateRenderer = GetComponent<Renderer>();
        template = templateRenderer.sharedMaterial;
        video = GetComponent<VideoPlayer>();

        if (template == null || !template.HasProperty(FadeId))
            Debug.LogWarning($"[Rain] '{name}' isn't using the Bonsai/Luma Key Video shader — assign " +
                             "the RainLumaKey material, or black won't be keyed out.");

        // The template plane is never drawn, and its collider (a 10×10 plane at the tree) must not
        // catch tool clicks.
        templateRenderer.enabled = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (video != null)
        {
            int w = video.clip != null ? (int)video.clip.width  : 1280;
            int h = video.clip != null ? (int)video.clip.height : 720;
            rt = new RenderTexture(Mathf.Max(16, w), Mathf.Max(16, h), 0)
            {
                name = "RainVideoRT",
                wrapMode = TextureWrapMode.Clamp,
            };
            video.renderMode    = VideoRenderMode.RenderTexture;
            video.targetTexture = rt;
            video.isLooping     = true;
            video.Pause();
        }

        quad = BuildQuad();
    }

    void Start()
    {
        cam  = Camera.main;
        tree = FindAnyObjectByType<TreeSkeleton>();
        if (focus == null && tree != null) focus = tree.transform;
        BuildLayers();
        BuildStreams();
        SetShown(false);
    }

    void OnDestroy()
    {
        foreach (var l in layers) if (l.mat != null) Destroy(l.mat);
        if (layerRoot != null) Destroy(layerRoot.gameObject);
        if (quad != null) Destroy(quad);
        if (rt != null) { rt.Release(); Destroy(rt); }
        for (int i = 1; i < streamRTs.Count; i++)
            if (streamRTs[i] != null) { streamRTs[i].Release(); Destroy(streamRTs[i]); }
    }

    // ── Build ──

    static Mesh BuildQuad()
    {
        // 1×1, centred, in the XY plane facing the camera (−Z). No collider, unlike a primitive.
        var m = new Mesh { name = "RainLayerQuad" };
        m.vertices  = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) };
        m.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void BuildLayers()
    {
        if (cam == null || template == null) return;

        layerRoot = new GameObject("Rain Layers").transform;
        layerRoot.SetParent(cam.transform, false);

        var rng = new System.Random(seed);
        int n = 0;
        for (int i = 0; i < frontLayers; i++) AddLayer(true,  i, n++, rng);
        for (int i = 0; i < backLayers;  i++) AddLayer(false, i, n++, rng);
    }

    void AddLayer(bool front, int index, int overall, System.Random rng)
    {
        var go = new GameObject($"Rain Layer {(front ? "front" : "back")} {index}");
        go.transform.SetParent(layerRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        var mat = new Material(template) { name = go.name + " (instance)" };
        if (rt != null) mat.SetTexture(BaseMapId, rt);
        mr.sharedMaterial = mat;

        // Golden-ratio stepping spreads layers evenly but irregularly sideways, so neighbouring
        // depths don't line up.
        float spread = ((overall * 0.618034f + (float)rng.NextDouble() * 0.15f) % 1f) - 0.5f;

        layers.Add(new Layer
        {
            tf = go.transform, mr = mr, mat = mat, front = front, index = index,
            xFrac = spread * sidewaysSpread,
            mirrored = rng.NextDouble() < 0.5,
        });
    }

    /// <summary>
    /// Creates the extra video streams and hands each layer one. Stream 0 is the original player;
    /// stream k starts k/count of the way through the clip (plus a small random nudge), so the
    /// layers are spread evenly through the loop rather than clustered.
    /// </summary>
    void BuildStreams()
    {
        streams.Clear();
        streamRTs.Clear();
        if (video == null || rt == null) return;

        streams.Add(video);
        streamRTs.Add(rt);

        int count = Mathf.Clamp(videoStreams, 1, Mathf.Max(1, layers.Count));
        double length = video.clip != null ? video.clip.length : 0d;
        var rng = new System.Random(seed * 31 + 5);

        for (int k = 1; k < count; k++)
        {
            var go = new GameObject($"Rain Video Stream {k}");
            go.transform.SetParent(layerRoot != null ? layerRoot : transform, false);

            var vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake      = false;
            vp.source           = video.source;
            vp.clip             = video.clip;
            vp.url              = video.url;
            vp.isLooping        = true;
            vp.skipOnDrop       = true;
            vp.waitForFirstFrame = true;
            vp.timeUpdateMode   = video.timeUpdateMode;
            vp.audioOutputMode  = VideoAudioOutputMode.None;   // one audio source is enough

            var srt = new RenderTexture(rt.width, rt.height, 0)
            {
                name = $"RainVideoRT {k}",
                wrapMode = TextureWrapMode.Clamp,
            };
            vp.renderMode    = VideoRenderMode.RenderTexture;
            vp.targetTexture = srt;

            double start = length > 0
                ? length * ((k / (double)count) + (rng.NextDouble() - 0.5) * 0.1 / count)
                : 0d;
            start = length > 0 ? ((start % length) + length) % length : 0d;

            // Seek once the clip is ready, then follow whatever the rain is doing right now.
            vp.prepareCompleted += p =>
            {
                p.time = start;
                if (shown) p.Play(); else p.Pause();
            };
            vp.Prepare();

            streams.Add(vp);
            streamRTs.Add(srt);
        }

        for (int i = 0; i < layers.Count; i++)
            layers[i].mat.SetTexture(BaseMapId, streamRTs[i % streamRTs.Count]);
    }

    // ── Update ──

    void Update()
    {
        var weather = WeatherManager.Instance;
        bool raining = weather != null && weather.IsRaining;
        bool storm   = weather != null && weather.state == WeatherState.Storm;

        float target = raining ? (storm ? stormStrength : 1f) : 0f;
        float step   = fadeSeconds > 0f ? Time.unscaledDeltaTime / fadeSeconds : 1f;
        fade = Mathf.MoveTowards(fade, target, step * Mathf.Max(1f, target));

        float speed = storm ? stormPlaybackSpeed : 1f;
        foreach (var vp in streams) if (vp != null) vp.playbackSpeed = speed;

        bool shouldShow = fade > 0.001f;
        if (shouldShow != shown) SetShown(shouldShow);

        if (video != null && video.audioOutputMode == VideoAudioOutputMode.Direct && video.clip != null)
        {
            float vol = (GameSettings.MuteAll ? 0f : GameSettings.MasterVolume * GameSettings.SfxVolume)
                        * Mathf.Clamp01(fade);
            for (ushort t = 0; t < video.clip.audioTrackCount; t++)
                video.SetDirectAudioVolume(t, vol);
        }
    }

    // LateUpdate so the layers are placed after the camera has moved this frame.
    void LateUpdate()
    {
        if (!shown || cam == null || template == null) return;

        var camTf = cam.transform;

        // Distance to the tree along the view direction.
        float focusDepth = focus != null
            ? Mathf.Max(0.3f, Vector3.Dot(focus.position - camTf.position, camTf.forward))
            : 5f;

        // Half the tree's depth extent: front layers stay nearer than this, back layers farther.
        float treeHeight   = tree != null ? tree.CachedTreeHeight : 0.5f;
        float canopyRadius = Mathf.Max(0.3f, treeHeight * canopyWidthToHeight * 0.5f);

        // ── Front depths: evenly between the near limit and the front of the canopy. When there is
        //    no room for all of them, keep as many as fit — at least one.
        float nearLimit  = Mathf.Max(cam.nearClipPlane * 2f, 0.15f);
        float frontLimit = Mathf.Max(nearLimit, focusDepth - canopyRadius);
        float frontSpan  = frontLimit - nearLimit;
        int   frontFit   = Mathf.Clamp(Mathf.FloorToInt(frontSpan / Mathf.Max(0.01f, minFrontSpacing)) + 1,
                                       1, frontLayers);

        // ── Back depths: start just behind the canopy, then step back.
        float backStart = focusDepth + canopyRadius + focusDepth * 0.1f;
        float backStep  = Mathf.Max(0.1f, focusDepth * backSpacing);

        float tanHalf     = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float videoAspect = rt != null ? rt.width / (float)rt.height : 16f / 9f;

        float baseOpacity = template.GetFloat(OpacityId);
        Color tint        = template.GetColor(TintId);
        float keyLow      = template.GetFloat(KeyLowId);
        float keyHigh     = template.GetFloat(KeyHighId);

        float farthest = backStart + backStep * Mathf.Max(0, backLayers - 1);

        foreach (var l in layers)
        {
            float depth;
            if (l.front)
            {
                bool active = l.index < frontFit;
                if (l.mr.enabled != active) l.mr.enabled = active;
                if (!active) continue;

                // Spread the active front layers across the span; a single one sits midway.
                float t = frontFit > 1 ? l.index / (float)(frontFit - 1) : 0.5f;
                depth = Mathf.Lerp(nearLimit, frontLimit, frontFit > 1 ? Mathf.Lerp(0.1f, 0.9f, t) : 0.5f);
            }
            else
            {
                if (!l.mr.enabled) l.mr.enabled = true;
                depth = backStart + backStep * l.index;
            }

            float viewH = 2f * depth * tanHalf;
            float viewW = viewH * cam.aspect;

            // Smallest clip-shaped rectangle that still covers the screen with this layer's
            // sideways offset, then a margin. Height follows the clip, so it must also be at least
            // the view height.
            float needW = viewW * (1f + 2f * Mathf.Abs(l.xFrac));
            float w = Mathf.Max(needW, viewH * videoAspect) * edgeMargin;
            float h = w / videoAspect;

            l.tf.localPosition = new Vector3(l.xFrac * viewW, 0f, depth);
            l.tf.localRotation = Quaternion.identity;
            l.tf.localScale    = new Vector3(w, h, 1f);

            // Whole frame; mirrored layers flip horizontally (u -> 1 - u).
            var st = l.mirrored ? new Vector4(-1f, 1f, 1f, 0f) : new Vector4(1f, 1f, 0f, 0f);

            float depthT = Mathf.InverseLerp(nearLimit, Mathf.Max(nearLimit + 0.01f, farthest), depth);

            l.mat.SetVector(BaseMapST, st);
            l.mat.SetColor(TintId, tint);
            l.mat.SetFloat(KeyLowId, keyLow);
            l.mat.SetFloat(KeyHighId, keyHigh);
            l.mat.SetFloat(OpacityId, baseOpacity * Mathf.Lerp(1f, farLayerOpacity, depthT));
            l.mat.SetFloat(FadeId, fade);
        }
    }

    void SetShown(bool v)
    {
        shown = v;
        foreach (var l in layers) if (l.mr != null) l.mr.enabled = v;

        // Pause rather than stop, so each stream resumes where it left off and the stagger between
        // them is preserved. Streams still preparing start themselves in prepareCompleted.
        var all = streams.Count > 0 ? streams : (video != null ? new List<VideoPlayer> { video } : null);
        if (all == null) return;
        foreach (var vp in all)
        {
            if (vp == null) continue;
            if (v && !vp.isPlaying && (vp == video || vp.isPrepared)) vp.Play();
            else if (!v && vp.isPlaying) vp.Pause();
        }
    }
}
