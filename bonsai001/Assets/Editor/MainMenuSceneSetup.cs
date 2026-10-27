using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

/// <summary>
/// One-click wiring for the rebuilt main menu (PLAN "Release Prep" R1).
///
/// <para><see cref="MainMenu"/> bootstraps itself at runtime, so the scene works without this.
/// What this does is make the setup <i>explicit and serialized</i> rather than resolved every
/// launch: it adds the UIDocument, assigns the shared PanelSettings, points the component at the
/// legacy Canvas, and deactivates that Canvas — then saves the scene.</para>
///
/// <para>Written as an editor tool rather than applied by hand to the scene's YAML, because
/// hand-editing a .unity file risks silent corruption and conflicts with whatever the editor has
/// open.</para>
/// </summary>
public static class MainMenuSceneSetup
{
    const string SCENE_PATH  = "Assets/Scenes/MainMenu.unity";
    const string PANEL_PATH  = "Assets/UI/MainPanelSettings.asset";

    [MenuItem("Bonsai/Set Up Main Menu Scene", priority = 100)]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != SCENE_PATH)
        {
            if (!EditorUtility.DisplayDialog(
                    "Open MainMenu scene?",
                    $"This tool operates on {SCENE_PATH}.\n\nOpen it now? Unsaved changes in the " +
                    "current scene will prompt first.",
                    "Open it", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(SCENE_PATH);
        }

        var menu = Object.FindAnyObjectByType<MainMenu>();
        if (menu == null)
        {
            EditorUtility.DisplayDialog(
                "MainMenu component not found",
                "No MainMenu component exists in this scene. It is normally on the Main Camera.\n\n" +
                "Add it to a GameObject and run this again.",
                "OK");
            return;
        }

        int changes = 0;

        // ── UIDocument ──
        var doc = menu.GetComponent<UIDocument>();
        if (doc == null)
        {
            doc = Undo.AddComponent<UIDocument>(menu.gameObject);
            changes++;
            Debug.Log($"[Setup] Added UIDocument to '{menu.gameObject.name}'.");
        }

        // ── PanelSettings ──
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_PATH);
        if (panel == null)
            Debug.LogWarning($"[Setup] {PANEL_PATH} not found — MainMenu will fall back to a " +
                             "runtime-created PanelSettings.");
        else
        {
            if (doc.panelSettings != panel)
            {
                Undo.RecordObject(doc, "Assign PanelSettings");
                doc.panelSettings = panel;
                changes++;
                Debug.Log("[Setup] Assigned MainPanelSettings to the UIDocument.");
            }

            // Serialize it on the component too, so a build doesn't rely on the editor-only
            // AssetDatabase path inside MainMenu.ResolvePanelSettings.
            var so = new SerializedObject(menu);
            var prop = so.FindProperty("panelSettings");
            if (prop != null && prop.objectReferenceValue != panel)
            {
                prop.objectReferenceValue = panel;
                so.ApplyModifiedProperties();
                changes++;
                Debug.Log("[Setup] Serialized PanelSettings onto the MainMenu component.");
            }
        }

        // ── Legacy canvas ──
        GameObject canvasGo = null;
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            if (c.transform.parent == null) { canvasGo = c.gameObject; break; }

        if (canvasGo != null)
        {
            var so = new SerializedObject(menu);
            var prop = so.FindProperty("legacyCanvas");
            if (prop != null && prop.objectReferenceValue != canvasGo)
            {
                prop.objectReferenceValue = canvasGo;
                so.ApplyModifiedProperties();
                changes++;
                Debug.Log($"[Setup] Pointed MainMenu at legacy canvas '{canvasGo.name}'.");
            }

            if (canvasGo.activeSelf)
            {
                Undo.RecordObject(canvasGo, "Deactivate legacy canvas");
                canvasGo.SetActive(false);
                changes++;
                Debug.Log($"[Setup] Deactivated '{canvasGo.name}'. It is kept, not deleted — " +
                          "reverting to the old menu is a matter of re-enabling it.");
            }
        }
        else
            Debug.Log("[Setup] No root-level Canvas found; nothing to deactivate.");

        // ── Input module ──
        // The legacy StandaloneInputModule throws every frame under Input-System-only handling,
        // and blocks UI Toolkit clicks while it does. Replace it in the scene.
        var es = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (es != null)
        {
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                Undo.DestroyObjectImmediate(legacy);
                changes++;
                Debug.Log("[Setup] Removed legacy StandaloneInputModule from the EventSystem.");
            }
            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                Undo.AddComponent<InputSystemUIInputModule>(es.gameObject);
                changes++;
                Debug.Log("[Setup] Added InputSystemUIInputModule to the EventSystem.");
            }
        }

        if (changes == 0)
        {
            Debug.Log("[Setup] Main menu scene already set up — nothing to change.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Setup] Main menu scene set up ({changes} change(s)) and saved.");
    }
}
