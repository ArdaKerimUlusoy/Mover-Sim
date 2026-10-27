using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GARAJI AYRI SAHNEYE TAŞIR (Assets/Scenes/Garage.unity):
///   1) SampleScene'deki TestMuseum/Garage (duvarlar, mağaza, ödül panosu, şövale, kozmetik dolabı, ışık) yeni sahneye taşınır
///      ve ana dünyadan uzağa (x +400) konur. Garajın açık kuzey ağzı "KAMYONA" kapısıyla kapanır.
///   2) Garaja: sınır (GarageArea), 4 doğma noktası (GarageSpawnPoint), boy aynası (GarageMirror) eklenir.
///   3) Kamyonun batı duvarının içine "Garaja dön" kapısı eklenir (SampleScene).
///   4) İki sahne kaydedilir, Garage.unity Build Settings'e eklenir. Oyun açılınca sunucu garajı ağ üzerinden ek sahne olarak yükler.
/// Kod güncellenince BİR KEZ kendiliğinden çalışır (SampleScene açıksa). Elle: Tools → Sahte Şaheser → Garajı Ayrı Sahneye Taşı.
/// Karaborsa ara sokakta kalır (mekaniği değişmesin).
/// </summary>
[InitializeOnLoad]
public static class GarageSceneBuilder
{
    private const string MainPath = "Assets/Scenes/SampleScene.unity";
    private const string MarkerPath = "ProjectSettings/SahteSaheserGarageScene.txt";
    private const string MatFolder = "Assets/TestMuseum";
    public static readonly Vector3 Offset = new Vector3(400f, 0f, 0f);

    public static bool GarageSceneExists => File.Exists(GarageSceneLink.ScenePath);

    static GarageSceneBuilder()
    {
        if (!File.Exists(MarkerPath) && !GarageSceneExists) EditorApplication.update += WaitAndRun;
        else if (GarageSceneExists) EditorApplication.delayCall += EnsureBuildSettings;

        // Garage.unity tek başına açıkken Play'e basılırsa oyun SampleScene'den başlasın
        // (garajda kamera / NetworkManager yok; oyunda sunucu garajı ek sahne olarak kendisi yükler)
        EditorSceneManager.activeSceneChangedInEditMode += (a, b) => UpdatePlayStartScene();
        EditorSceneManager.sceneOpened += (s, m) => UpdatePlayStartScene();
        EditorSceneManager.sceneClosed += s => UpdatePlayStartScene();
        EditorApplication.delayCall += UpdatePlayStartScene;
        EditorApplication.delayCall += RemoveTruckDoor;
        EditorSceneManager.sceneOpened += (s, m) => RemoveTruckDoor();
    }

    private static void UpdatePlayStartScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        bool garageOnly = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;
            if (s.path == MainPath) { garageOnly = false; break; }
            if (s.path == GarageSceneLink.ScenePath) garageOnly = true;
        }
        SceneAsset want = garageOnly ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MainPath) : null;
        if (EditorSceneManager.playModeStartScene != want) EditorSceneManager.playModeStartScene = want;
    }

    private static void WaitAndRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitAndRun;
        if (SceneManager.GetActiveScene().path != MainPath) return; // başka sahne açıkken dokunma (menüden elle yapılır)
        try
        {
            if (Build(true)) File.WriteAllText(MarkerPath, "ok\n");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Garaj] Garaj ayrı sahneye taşınamadı; Tools → Sahte Şaheser → Garajı Ayrı Sahneye Taşı ile deneyin.\n" + e);
        }
    }

    [MenuItem("Tools/Sahte Şaheser/Garajı Ayrı Sahneye Taşı")]
    private static void BuildFromMenu()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Garaj", "Önce oyunu durdurun.", "Tamam"); return; }
        if (GarageSceneExists)
        {
            EnsureBuildSettings();
            EditorUtility.DisplayDialog("Garaj", "Garage.unity zaten var. Build Settings kontrol edildi.", "Tamam");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (Build(false)) File.WriteAllText(MarkerPath, "ok\n");
    }

    /// <summary>Taşımayı yapar. true: başarılı.</summary>
    public static bool Build(bool silent)
    {
        Scene main = SceneManager.GetActiveScene();
        if (main.path != MainPath) main = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);

        GameObject root = main.GetRootGameObjects().FirstOrDefault(g => g.name == "TestMuseum");
        Transform garage = root != null ? root.transform.Find("Garage") : null;
        if (garage == null)
        {
            string msg = "SampleScene'de TestMuseum/Garage bulunamadı (önce Tools → Sahte Şaheser → Create Test Museum).";
            if (silent) Debug.LogWarning("[Garaj] " + msg); else EditorUtility.DisplayDialog("Garaj", msg, "Tamam");
            return false;
        }
        Transform oldLabel = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Label_Garage");

        // ---- Yeni sahne + taşıma (dünya konumu korunur, sonra kök kaydırılır)
        Scene gs = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject groot = new GameObject("GarageRoot");
        SceneManager.MoveGameObjectToScene(groot, gs);
        MoveInto(garage, groot.transform);
        if (oldLabel != null) Object.DestroyImmediate(oldLabel.gameObject); // dışarıdaki tabela: kapı artık kapalı, gerek yok
        groot.transform.position = Offset;

        Material wall = LoadMat("Garage"), door = LoadMat("Door"), dark = LoadMat("Dark");
        GameObject g = garage.gameObject;

        // ---- Kuzey ağzını kapatan kapı: KAMYONA (soyguna çıkış)
        GameObject exit = Box(g, "GarageDoor_ToTruck", new Vector3(-29f, 1.5f, -10f), new Vector3(4f, 3f, 0.3f), door != null ? door : wall);
        exit.AddComponent<GarageTravel>().Setup(GarageTravel.Destination.ToTruck);
        Decor(g, "GarageDoor_Stripe", new Vector3(-29f, 1.5f, -10.17f), new Vector3(3.6f, 0.12f, 0.04f), dark);
        Label(exit, "Label", new Vector3(0f, 0f, -0.6f), "label.garagedoor", 0.9f);

        // ---- Doğma noktaları (kapının önü, garajın içine bakar)
        for (int i = 0; i < 4; i++)
        {
            GameObject sp = new GameObject("GarageSpawn_" + (i + 1));
            sp.transform.SetParent(garage, false);
            sp.transform.localPosition = new Vector3(-30.6f + i * 1.1f, 0.1f, -12.4f);
            sp.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sp.AddComponent<GarageSpawnPoint>();
        }

        // ---- Boy aynası: doğu duvarında, kozmetik dolabının yanında, odaya (batıya) bakar
        GameObject mirror = new GameObject("GarageMirror");
        mirror.transform.SetParent(garage, false);
        mirror.transform.localPosition = new Vector3(-22.21f, 1.12f, -14.2f);
        mirror.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        mirror.AddComponent<GarageMirror>().Setup(3.2f, 2.1f); // 4 kişi yan yana bakabilsin
        GameObject ml = new GameObject("MirrorLight");
        ml.transform.SetParent(garage, false);
        ml.transform.localPosition = new Vector3(-23.6f, 2.7f, -14.2f);
        Light l = ml.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.93f, 0.8f);
        l.intensity = 2.2f;
        l.range = 5f;
        l.shadows = LightShadows.None;

        // ---- Sınır (GameFlow: garaj aşaması)
        Bounds b = new Bounds(garage.position + new Vector3(-29f, 2f, -16f), Vector3.zero);
        foreach (Renderer r in garage.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
        b.Expand(new Vector3(0.6f, 1.5f, 0.6f));
        groot.AddComponent<GarageArea>().Setup(b);


        // ---- Kaydet (sahne GUID'i oluşsun) → sahne nesnelerinin ağ kimliğini yenile → tekrar kaydet
        if (!EditorSceneManager.SaveScene(gs, GarageSceneLink.ScenePath)) { Debug.LogError("[Garaj] Garage.unity kaydedilemedi."); return false; }
        RefreshNetworkIds(gs);
        EditorSceneManager.MarkSceneDirty(gs);
        EditorSceneManager.SaveScene(gs);
        EditorSceneManager.MarkSceneDirty(main);
        EditorSceneManager.SaveScene(main);
        EnsureBuildSettings();
        EditorSceneManager.CloseScene(gs, true);
        SceneManager.SetActiveScene(main);

        string done = "Garaj ayrı sahneye taşındı: " + GarageSceneLink.ScenePath + " (Build Settings'e eklendi). Oyunda sunucu garajı ek sahne olarak yükler.";
        Debug.Log("[Garaj] " + done);
        if (!silent) EditorUtility.DisplayDialog("Garaj", done, "Tamam");
        return true;
    }

    /// <summary>Görev ortasında garaja dönüş yok: eski kurulumdan kalan kamyon içi "Garaja dön" kapısını SampleScene'den siler.</summary>
    private static void RemoveTruckDoor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene main = SceneManager.GetSceneByPath(MainPath);
        if (!main.isLoaded) return;
        bool removed = false;
        foreach (GameObject r in main.GetRootGameObjects())
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name == "TruckDoor_ToGarage" || t.name == "ForgeryWorkbench_2")) { Object.DestroyImmediate(t.gameObject); removed = true; }
        if (!removed) return;
        EditorSceneManager.MarkSceneDirty(main); // kaydetmek size kalır; oyunda bu kapı zaten kendini kaldırır
        Debug.Log("[Kurulum] Kamyondaki 'Garaja dön' kapısı / ikinci sahte masası kaldırıldı (tek masa, garaja görev bitince dönülür). Sahneyi kaydedin (Ctrl+S).");
    }

    private static void MoveInto(Transform t, Transform newRoot)
    {
        t.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(t.gameObject, newRoot.gameObject.scene);
        t.SetParent(newRoot, true);
    }

    /// <summary>Sahneye yerleştirilmiş NetworkObject'lerin (garaj mağazası) kimliği yeni sahneye göre yeniden üretilsin.</summary>
    private static void RefreshNetworkIds(Scene s)
    {
        MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (GameObject r in s.GetRootGameObjects())
            foreach (NetworkObject no in r.GetComponentsInChildren<NetworkObject>(true))
            {
                if (validate != null) validate.Invoke(no, null);
                EditorUtility.SetDirty(no);
            }
    }

    public static void EnsureBuildSettings()
    {
        if (!GarageSceneExists) return;
        var list = EditorBuildSettings.scenes.ToList();
        bool changed = false;
        if (!list.Any(x => x.path == MainPath)) { list.Insert(0, new EditorBuildSettingsScene(MainPath, true)); changed = true; }
        int gi = list.FindIndex(x => x.path == GarageSceneLink.ScenePath);
        if (gi < 0) { list.Add(new EditorBuildSettingsScene(GarageSceneLink.ScenePath, true)); changed = true; }
        else if (!list[gi].enabled) { list[gi] = new EditorBuildSettingsScene(GarageSceneLink.ScenePath, true); changed = true; }
        if (changed) EditorBuildSettings.scenes = list.ToArray();
    }

    // ---------------- Yardımcılar ----------------

    private static Material LoadMat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{name}.mat");

    private static GameObject Box(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static void Decor(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = Box(parent, name, localPos, size, mat);
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    private static void Label(GameObject parent, string name, Vector3 localPos, string key, float height)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Vector3 ls = parent.transform.lossyScale;
        go.transform.localPosition = new Vector3(localPos.x / Mathf.Max(ls.x, 0.001f), localPos.y / Mathf.Max(ls.y, 0.001f), localPos.z / Mathf.Max(ls.z, 0.001f));
        go.transform.localScale = new Vector3(1f / Mathf.Max(ls.x, 0.001f), 1f / Mathf.Max(ls.y, 0.001f), 1f / Mathf.Max(ls.z, 0.001f));
        go.AddComponent<WorldLabel>().Setup(key, height);
    }
}
