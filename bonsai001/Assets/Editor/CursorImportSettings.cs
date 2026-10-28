using UnityEditor;

/// <summary>
/// Imports everything in <c>Assets/Resources/Cursors/</c> as a hardware-cursor texture (PLAN R4):
/// Cursor type (readable, which Cursor.SetCursor needs), point filtering so the pixel art stays
/// crisp, no mipmaps, no compression. Applies to any frame dropped in that folder later, so new
/// cursor art needs no manual Inspector setup.
/// </summary>
public class CursorImportSettings : AssetPostprocessor
{
    const string FOLDER = "Assets/Resources/Cursors/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(FOLDER)) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType         = TextureImporterType.Cursor;
        ti.filterMode          = UnityEngine.FilterMode.Point;
        ti.mipmapEnabled       = false;
        ti.textureCompression  = TextureImporterCompression.Uncompressed;
        ti.alphaIsTransparency = true;
        ti.wrapMode            = UnityEngine.TextureWrapMode.Clamp;
        ti.npotScale           = TextureImporterNPOTScale.None;
    }
}
