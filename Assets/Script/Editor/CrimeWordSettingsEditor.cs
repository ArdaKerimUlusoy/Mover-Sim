using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sesli kelime algılama ayar asset'i (Assets/Resources/CrimeWordSettings.asset):
///  - Editör açılınca / derleme sonrası asset yoksa kendiliğinden oluşturur.
///  - Menü: Tools/Sahte Şaheser/Voice Filter Settings → asset'i seçer (Inspector'da açılır).
///  - Inspector: Denge'den hesaplanan alanlar "elle ayarla" kapalıyken gri gösterilir.
/// </summary>
[InitializeOnLoad]
public static class CrimeWordSettingsSetup
{
    public const string AssetPath = "Assets/Resources/" + CrimeWordSettings.ResourceName + ".asset";

    static CrimeWordSettingsSetup()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess()) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Ensure();
        };
    }

    /// <summary>Asset'i döner; yoksa varsayılan değerlerle oluşturur.</summary>
    public static CrimeWordSettings Ensure()
    {
        var s = AssetDatabase.LoadAssetAtPath<CrimeWordSettings>(AssetPath);
        if (s != null) return s;
        if (File.Exists(AssetPath)) return null; // dosya var ama henüz içe aktarılmadı: dokunma

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        s = ScriptableObject.CreateInstance<CrimeWordSettings>();
        s.ApplyBalanceToFields();
        AssetDatabase.CreateAsset(s, AssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[CrimeTalk] Ses filtresi ayarları oluşturuldu: " + AssetPath + " (Tools/Sahte Şaheser/Voice Filter Settings)");
        return s;
    }

    [MenuItem("Tools/Sahte Şaheser/Voice Filter Settings")]
    private static void Select()
    {
        CrimeWordSettings s = Ensure();
        if (s == null) s = AssetDatabase.LoadAssetAtPath<CrimeWordSettings>(AssetPath);
        if (s == null) { Debug.LogWarning("[CrimeTalk] Ayar asset'i bulunamadı: " + AssetPath); return; }
        Selection.activeObject = s;
        EditorGUIUtility.PingObject(s);
    }
}

[CustomEditor(typeof(CrimeWordSettings))]
public class CrimeWordSettingsInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var s = (CrimeWordSettings)target;
        SerializedProperty manual = serializedObject.FindProperty(nameof(CrimeWordSettings.manualDetails));

        EditorGUILayout.HelpBox(
            "Denge: 0 = en hızlı / en hassas (anında, yanlış alarm riski yüksek) • 0.5 = önerilen • 1 = en güvenli / en geç (sadece cümle sonu).\n" +
            "'Ayrıntılı alanları elle ayarla' kapalıyken gri alanlar Denge'den hesaplanır. Play sırasında değişiklikler ~0.5 sn içinde uygulanır ve kalıcıdır.\n" +
            "Her alanın açıklaması için fareyi üstüne getirin.",
            MessageType.Info);

        SerializedProperty it = serializedObject.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            bool derived = System.Array.IndexOf(CrimeWordSettings.DerivedFieldNames, it.name) >= 0;
            using (new EditorGUI.DisabledScope(derived && !manual.boolValue))
                EditorGUILayout.PropertyField(it, true);
        }

        if (serializedObject.ApplyModifiedProperties() && !s.manualDetails)
        {
            s.ApplyBalanceToFields();
            EditorUtility.SetDirty(s);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Önerilen değerlere dön (Denge 0.5, otomatik)"))
        {
            Undo.RecordObject(s, "Ses filtresi varsayılanları");
            s.balance = 0.5f;
            s.manualDetails = false;
            s.ApplyBalanceToFields();
            s.languageArbitration = false;
            s.languageMargin = 0.15f;
            s.doubleCountSeconds = 0.8f;
            s.clientSendMinInterval = 0.25f;
            s.serverMinInterval = 0.5f;
            EditorUtility.SetDirty(s);
        }
    }
}
