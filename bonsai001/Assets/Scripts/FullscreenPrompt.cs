using UnityEngine;

/// <summary>
/// Shows a one-shot "Play fullscreen?" dialog at startup.
/// Attach to any GameObject in the scene. Destroys itself after the choice is made.
/// "Yes" → borderless fullscreen (FullScreenWindow — no title bar or window chrome).
/// "No"  → continue in whatever window mode Unity was already using.
/// </summary>
public class FullscreenPrompt : MonoBehaviour
{
    const float W = 260f, H = 120f;

    Texture2D bgTex, btnTex, btnHoverTex;
    GUIStyle  boxStyle, labelStyle, btnStyle;
    bool      stylesReady;

    void Awake()
    {
        bgTex       = MakeTex(new Color(0.10f, 0.10f, 0.10f, 0.96f));
        btnTex      = MakeTex(new Color(0.28f, 0.28f, 0.28f, 1f));
        btnHoverTex = MakeTex(new Color(0.48f, 0.48f, 0.48f, 1f));
    }

    void OnGUI()
    {
        if (!stylesReady) BuildStyles();

        // Dim everything behind the dialog
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Rect r = new Rect((Screen.width  - W) * 0.5f,
                          (Screen.height - H) * 0.5f,
                          W, H);

        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Label(new Rect(r.x, r.y + 16f, W, 34f), "Play fullscreen?", labelStyle);

        float bw = 88f, bh = 32f;
        float by = r.yMax - bh - 14f;

        if (GUI.Button(new Rect(r.x + 28f, by, bw, bh), "Yes", btnStyle))
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight,
                                 FullScreenMode.FullScreenWindow);
            Destroy(gameObject);
        }

        if (GUI.Button(new Rect(r.xMax - 28f - bw, by, bw, bh), "No", btnStyle))
            Destroy(gameObject);
    }

    void BuildStyles()
    {
        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = bgTex }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };

        btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 14,
            alignment = TextAnchor.MiddleCenter,
            normal    = { background = btnTex,      textColor = Color.white },
            hover     = { background = btnHoverTex, textColor = Color.white },
            active    = { background = btnTex,      textColor = new Color(0.75f, 0.75f, 0.75f) },
            focused   = { background = btnTex,      textColor = Color.white }
        };

        stylesReady = true;
    }

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
