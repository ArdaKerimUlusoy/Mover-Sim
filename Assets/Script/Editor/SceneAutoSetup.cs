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

    // İkinci sahte masası: tam yeniden kurulum yapmadan tek seferlik ekleme
    private const string BenchMarkerPath = "ProjectSettings/SahteSaheserBench2.txt";

    static SceneAutoSetup()
    {
        if (!AlreadyDone()) EditorApplication.update += WaitAndRun;
        else if (!File.Exists(BenchMarkerPath)) EditorApplication.update += WaitAndAddBench;
    }

    private static void WaitAndAddBench()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitAndAddBench;
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return; // başka sahne açık: menüden elle ekleyin
            if (TestMuseumBuilder.EnsureSecondWorkbench())
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Kurulum] Kamyona ikinci sahte masası eklendi ve sahne kaydedildi.");
            }
            File.WriteAllText(BenchMarkerPath, "ok\n");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Kurulum] İkinci masa eklenemedi; Tools → Sahte Şaheser → Add Second Workbench ile ekleyin.\n" + e);
        }
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
        File.WriteAllText(BenchMarkerPath, "ok\n");
        Debug.Log("[Kurulum] Sahne GDD'ye göre yeniden kuruldu ve kaydedildi: kamyon atölyesi, sokak, garaj, karaborsa, müze. Play'e basıp deneyebilirsiniz.");
    }
}
