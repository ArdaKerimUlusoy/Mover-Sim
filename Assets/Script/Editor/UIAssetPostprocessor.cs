using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Resources/UI/{Panels,Buttons,Frames,Icons,Backgrounds,Textures} altındaki arayüz dokularını OnGUI için
/// doğru ayarlarla içe aktarır: mipmap kapalı, sıkıştırmasız (ince çizgiler bozulmasın), clamp, bilinear,
/// alphaIsTransparency, textureType Default, NPOT ölçekleme yok. 9 dilim GUIStyle.border ile koddan yapılır,
/// bu yüzden sprite kenarı gerekmez. Doku/eski kök klasördeki (Resources/UI/*.png) elle ayarlı dosyalara dokunmaz.
/// Döşenen dokular (Textures/tex_*) Repeat sarmalıdır.
/// </summary>
public class UIAssetPostprocessor : AssetPostprocessor
{
    private const string Root = "Assets/Resources/UI/";

    public override uint GetVersion() => 2;

    private void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith(Root)) return;
        // Yalnızca alt klasörler (Panels/, Buttons/ ...): kökteki eski elle ayarlı dosyalar olduğu gibi kalsın
        if (p.IndexOf('/', Root.Length) < 0) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.textureShape = TextureImporterShape.Texture2D;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.isReadable = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = p.Contains("/Textures/tex_") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 1024;
    }

    [MenuItem("Tools/UI/Reimport UI Textures")]
    private static void ReimportAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI" });
        foreach (string g in guids)
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
        Debug.Log($"[UI] {guids.Length} doku yeniden içe aktarıldı.");
    }
}
