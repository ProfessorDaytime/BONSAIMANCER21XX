using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// UI-free, high-resolution captures of the game camera (PLAN "Release Prep" R6 — store art; also
/// the backlog's player screenshot feature).
///
/// <para><b>F12</b> (every build): one 3840×2160 shot, saved to <c>Pictures/Bonsaimancer</c>.
/// <b>Settings ▸ Debug ▸ Store art set</b> (development): the same moment rendered at every size the
/// Steam store and library ask for, into one folder — see <see cref="StoreSizes"/>. Capsules still
/// need the logo added by hand; Steam requires it on them and rejects text-free capsules.</para>
///
/// <para>Each image is a fresh render of the main camera into a render texture of the exact size,
/// not an upscaled screen grab, so it's sharp at any size and any aspect. The camera's vertical
/// field of view is kept, so a wide image shows more to the sides and a tall one less — frame the
/// tree with that in mind. The CRT/post effects are part of the camera, so they're included.</para>
///
/// <para>Hidden for the capture: every Canvas (two in Game001 draw through the main camera), and the
/// held tool, whose highlight mesh and selection circle would otherwise be drawn into the shot.
/// UI Toolkit draws to its own texture and never reaches the camera.</para>
/// </summary>
public class ScreenshotTool : MonoBehaviour
{
    public static ScreenshotTool Instance { get; private set; }

    /// <summary>Raised with the folder or file written, for the HUD toast.</summary>
    public static event Action<string> OnSaved;

    public struct Size
    {
        public string label;
        public int w, h;
        public Size(string label, int w, int h) { this.label = label; this.w = w; this.h = h; }
    }

    /// <summary>
    /// Steam's store and library art sizes (Steamworks "Store Graphical Assets" / "Library Assets",
    /// as of 2026), plus a 4K screenshot. Screenshots must be 16:9 and at least 1920×1080.
    /// </summary>
    public static readonly Size[] StoreSizes =
    {
        new Size("screenshot",       3840, 2160),
        new Size("header_capsule",    920,  430),
        new Size("small_capsule",     462,  174),
        new Size("main_capsule",     1232,  706),
        new Size("vertical_capsule",  748,  896),
        new Size("page_background",  1438,  810),
        new Size("library_capsule",   600,  900),
        new Size("library_hero",     3840, 1240),
    };

    static readonly Size PlayerShot = new Size("screenshot", 3840, 2160);

    bool busy;

    // ── Install ───────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded += (_, __) => EnsureExists();
        EnsureExists();
    }

    static void EnsureExists()
    {
        if (Instance != null || FindAnyObjectByType<GameManager>() == null) return;
        new GameObject("ScreenshotTool").AddComponent<ScreenshotTool>();
    }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame && !KeyBindings.Capturing)
            CaptureScreenshot();
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>One 4K shot into the player's Pictures folder.</summary>
    public void CaptureScreenshot()
    {
        if (busy) return;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Bonsaimancer");
        StartCoroutine(Capture(new[] { PlayerShot }, dir, reveal: false));
    }

    /// <summary>
    /// Every store size, into a new timestamped folder: <c>StoreArt/</c> beside the project in the
    /// editor (outside Assets, so Unity won't import 30 MB of PNGs), the save folder in a build.
    /// The folder is opened when done.
    /// </summary>
    public void CaptureStoreSet()
    {
        if (busy) return;
        string root = Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "StoreArt")
            : Path.Combine(Application.persistentDataPath, "StoreArt");
        StartCoroutine(Capture(StoreSizes, Path.Combine(root, Stamp()), reveal: true));
    }

    // ── Capture ───────────────────────────────────────────────────────────────

    IEnumerator Capture(Size[] sizes, string dir, bool reveal)
    {
        var cam = Camera.main;
        if (cam == null) yield break;
        busy = true;

        // Put the tool down for a frame so its highlight and selection circle are gone.
        var tools = ToolManager.Instance;
        var heldTool = tools != null ? tools.ActiveTool : ToolType.None;
        if (heldTool != ToolType.None) tools.ClearTool();
        yield return null;
        yield return new WaitForEndOfFrame();

        var canvases = new List<Canvas>();
        foreach (var c in FindObjectsByType<Canvas>())
            if (c.enabled) { c.enabled = false; canvases.Add(c); }

        string last = null;
        try
        {
            Directory.CreateDirectory(dir);
            string stamp = Stamp();
            foreach (var s in sizes)
            {
                var png = Render(cam, s.w, s.h);
                last = Path.Combine(dir, $"Bonsaimancer_{stamp}_{s.label}_{s.w}x{s.h}.png");
                File.WriteAllBytes(last, png);
            }
            Debug.Log($"[Screenshot] Saved {sizes.Length} image(s) to {dir}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Screenshot] Capture failed: {e.Message}");
            last = null;
        }
        finally
        {
            foreach (var c in canvases) if (c != null) c.enabled = true;
            if (heldTool != ToolType.None && tools != null) tools.SelectTool(heldTool);
            busy = false;
        }

        if (last != null)
        {
            OnSaved?.Invoke(sizes.Length == 1 ? last : dir);
            if (reveal) Application.OpenURL("file:///" + dir.Replace('\\', '/'));
        }
    }

    /// <summary>Renders the camera into a w×h texture (8× MSAA) and returns it as PNG bytes.</summary>
    static byte[] Render(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 8,
        };
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.aspect = (float)w / h;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
            {
                cam.targetTexture = rt;
                cam.Render();
            }

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            return png;
        }
        finally
        {
            cam.targetTexture    = prevTarget;
            cam.ResetAspect();
            RenderTexture.active = prevActive;
            rt.Release();
            Destroy(rt);
        }
    }

    static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
}
