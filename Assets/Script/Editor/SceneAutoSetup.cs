using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Kod güncellendikten sonra, Unity derlemeyi bitirince BİR KEZ çalışır:
///   1) Örnek eşyaları günceller (büst / yüzük sahteleri + ItemDatabase)
///   2) Sahneyi GDD'ye göre yeniden kurar (TestMuseumBuilder) ve eski prototip nesnelerini siler
///   3) Sahneyi kaydeder
/// Yapıldığını ProjectSettings/SahteSaheserSetup.txt dosyasına yazar; bu dosya commit'lenince
/// takım arkadaşlarında tekrar çalışmaz (onlar kaydedilmiş sahneyi git'ten alır).
/// Elle yeniden kurmak için: Tools → Sahte Şaheser → Create Test Museum.
/// </summary>
[InitializeOnLoad]
public static class SceneAutoSetup
{
    private const string Version = "2026-10-11-gdd-scene-2-jail";
    private const string MarkerPath = "ProjectSettings/SahteSaheserSetup.txt";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    static SceneAutoSetup()
    {
        if (AlreadyDone()) return;
        EditorApplication.update += WaitAndRun;
    }

    private static bool AlreadyDone()
    {
        try { return File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == Version; }
        catch { return false; }
    }

    private static void WaitAndRun()
    {
        // Oyun çalışırken, derleme / içe aktarma sürerken bekle
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitAndRun;
        if (AlreadyDone()) return;

        try
        {
            Run();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Kurulum] Otomatik sahne kurulumu tamamlanamadı. Tools → Sahte Şaheser → Create Test Museum ile elle kurabilirsiniz.\n" + e);
        }
    }

    private static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty || !File.Exists(ScenePath))
            {
                Debug.LogWarning($"[Kurulum] Açık sahne {ScenePath} değil; otomatik kurulum yapılmadı. O sahneyi açıp Tools → Sahte Şaheser → Create Test Museum çalıştırın.");
                return;
            }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ItemTools.CreateSampleItems();
        TestMuseumBuilder.Build(true);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        if (!saved)
        {
            Debug.LogWarning("[Kurulum] Sahne kuruldu ama kaydedilemedi. Ctrl+S ile kaydedin.");
            return;
        }

        File.WriteAllText(MarkerPath, Version + "\n");
        Debug.Log("[Kurulum] Sahne GDD'ye göre yeniden kuruldu ve kaydedildi: kamyon atölyesi, sokak, garaj, karaborsa, müze. Play'e basıp deneyebilirsiniz.");
    }
}
