using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Unity menüsü: Tools → Sahte Şaheser
///  - Create Sample Items: örnek eşyaları (tanım + prefab + malzeme) oluşturur ve listeyi günceller.
///  - Rebuild Item Database: projedeki tüm ItemDefinition'ları Resources/ItemDatabase'e id sırasıyla yazar.
/// Yeni bir eşya tanımı oluşturduğunuzda "Rebuild Item Database" çalıştırın.
/// </summary>
public static class ItemTools
{
    private const string ItemsFolder = "Assets/Items";
    private const string PrefabFolder = "Assets/Items/Prefabs";
    private const string MaterialFolder = "Assets/Items/Materials";
    private const string ResourcesFolder = "Assets/Resources";
    private const string IconFolder = "Assets/Items/Icons";
    private const int IconSize = 256;

    [MenuItem("Tools/Sahte Şaheser/Rebuild Item Database")]
    public static void RebuildDatabase()
    {
        EnsureFolder(ResourcesFolder);
        string path = $"{ResourcesFolder}/{ItemDatabase.ResourcePath}.asset";

        ItemDatabase db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(db, path);
        }

        List<ItemDefinition> list = AssetDatabase.FindAssets("t:ItemDefinition")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null)
            .OrderBy(d => d.id, StringComparer.Ordinal)
            .ToList();

        foreach (IGrouping<string, ItemDefinition> dup in list.GroupBy(d => d.id).Where(g => g.Count() > 1))
        {
            Debug.LogWarning($"[Inventory] Aynı id'ye sahip birden fazla eşya var: '{dup.Key}'. id'ler benzersiz olmalı.");
        }

        db.EditorSetItems(list);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Inventory] ItemDatabase güncellendi: {list.Count} eşya.");
    }

    [MenuItem("Tools/Sahte Şaheser/Create Sample Items")]
    public static void CreateSampleItems()
    {
        EnsureFolder(ItemsFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);

        //       id                  isim              tür                    kg    değer stack max  renk                                  şekil                 boyut                                  benzerlik
        Create("original_painting", "Small Painting",  ItemType.Original,    2f,   500,  false, 1,  new Color(0.85f, 0.65f, 0.35f), PrimitiveType.Cube,     new Vector3(0.6f, 0.45f, 0.05f),   -1);
        Create("original_vase",     "Ancient Vase",    ItemType.Original,    4f,   900,  false, 1,  new Color(0.30f, 0.55f, 0.75f), PrimitiveType.Cylinder, new Vector3(0.3f, 0.2f, 0.3f),      -1);
        Create("original_ring",     "Golden Ring",     ItemType.Original,    0.3f, 1200, false, 1,  new Color(1.00f, 0.82f, 0.20f), PrimitiveType.Sphere,   new Vector3(0.12f, 0.12f, 0.12f),  -1);
        Create("original_bust",     "Marble Bust",     ItemType.Original,    12f,  2500, false, 1,  new Color(0.92f, 0.92f, 0.90f), PrimitiveType.Cube,     new Vector3(0.35f, 0.55f, 0.35f),  -1);
        Create("forgery_painting",  "Forged Painting", ItemType.Forgery,     2f,   0,    false, 1,  new Color(0.75f, 0.60f, 0.40f), PrimitiveType.Cube,     new Vector3(0.6f, 0.45f, 0.05f),   72);
        Create("tool_lockpick",     "Lockpick Set",    ItemType.Tool,        0.5f, 150,  false, 1,  new Color(0.50f, 0.50f, 0.55f), PrimitiveType.Cube,     new Vector3(0.15f, 0.03f, 0.08f),  -1);
        Create("distraction_coin",  "Coin",            ItemType.Distraction, 0.1f, 1,    true,  10, new Color(0.95f, 0.75f, 0.30f), PrimitiveType.Cylinder, new Vector3(0.12f, 0.015f, 0.12f), -1);

        AssetDatabase.SaveAssets();
        RebuildDatabase();
        GenerateIcons();

        Debug.Log("[Inventory] Örnek eşyalar hazır: Assets/Items/Prefabs içindeki prefab'ları sahneye sürükleyin.");
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(PrefabFolder);
    }

    /// <summary>
    /// Her eşyanın prefab'ını görünmez bir sahnede fotoğraflayıp ikon (PNG) üretir ve tanıma bağlar.
    /// Kendi çizdiğiniz ikonu kullanmak isterseniz eşyanın "Icon" alanına sürükleyin; bu araç
    /// sadece Assets/Items/Icons içindeki (kendi ürettiği) ikonların üzerine yazar.
    /// </summary>
    [MenuItem("Tools/Sahte Şaheser/Generate Item Icons")]
    public static void GenerateIcons()
    {
        EnsureFolder(IconFolder);

        List<ItemDefinition> defs = AssetDatabase.FindAssets("t:ItemDefinition")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null && d.worldPrefab != null)
            .ToList();

        Scene scene = EditorSceneManager.NewPreviewScene();
        int made = 0;

        try
        {
            GameObject camGo = new GameObject("IconCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            Camera cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f, 1f);
            cam.fieldOfView = 25f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;

            AddLight(scene, new Vector3(50f, -30f, 0f), 1.2f, Color.white);
            AddLight(scene, new Vector3(-20f, 150f, 0f), 0.5f, new Color(0.85f, 0.9f, 1f));

            RenderTexture rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            foreach (ItemDefinition def in defs)
            {
                string existing = def.icon != null ? AssetDatabase.GetAssetPath(def.icon) : null;
                if (!string.IsNullOrEmpty(existing) && !existing.StartsWith(IconFolder)) continue; // elle verilmiş ikon

                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(def.worldPrefab, scene);
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

                Renderer[] renderers = inst.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) { Object.DestroyImmediate(inst); continue; }

                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

                float radius = Mathf.Max(b.extents.magnitude, 0.01f);
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                Vector3 dir = new Vector3(0.6f, 0.55f, -1f).normalized;
                camGo.transform.position = b.center + dir * dist;
                camGo.transform.LookAt(b.center);

                RenderCamera(cam, rt);

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                string path = $"{IconFolder}/{def.id}.png";
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(inst);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }

                def.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorUtility.SetDirty(def);
                made++;
            }

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Inventory] {made} eşya ikonu üretildi ({IconFolder}).");
    }

    private static void RenderCamera(Camera cam, RenderTexture rt)
    {
        UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request))
        {
            RenderPipeline.SubmitRenderRequest(cam, request);
        }
        else
        {
            cam.Render();
        }
    }

    private static void AddLight(Scene scene, Vector3 euler, float intensity, Color color)
    {
        GameObject go = new GameObject("IconLight");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.rotation = Quaternion.Euler(euler);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
        l.color = color;
    }

    private static void Create(string id, string displayName, ItemType type, float kg, int value,
        bool stackable, int maxStack, Color color, PrimitiveType shape, Vector3 scale, int similarity)
    {
        string defPath = $"{ItemsFolder}/{id}.asset";
        ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(defPath);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(def, defPath);
        }

        def.id = id;
        def.displayName = displayName;
        def.type = type;
        def.weightKg = kg;
        def.baseValue = value;
        def.stackable = stackable;
        def.maxStack = maxStack;
        def.uiColor = color;

        string prefabPath = $"{PrefabFolder}/{id}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
        {
            Material mat = GetOrCreateMaterial(id, color);

            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = id;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            // Silindirin hazır kapsül çarpıştırıcısı yassı şekillerde yanlış olur; mesh çarpıştırıcı kullan.
            if (shape == PrimitiveType.Cylinder)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.convex = true;
            }

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.1f, kg);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();   // sunucu yetkili: fiziği host hesaplar
            go.AddComponent<NetworkRigidbody>();

            WorldItem wi = go.AddComponent<WorldItem>();
            SerializedObject so = new SerializedObject(wi);
            so.FindProperty("item").objectReferenceValue = def;
            so.FindProperty("similarityPercent").intValue = similarity;
            so.ApplyModifiedPropertiesWithoutUndo();

            prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
        }

        def.worldPrefab = prefab;
        EditorUtility.SetDirty(def);
    }

    private static Material GetOrCreateMaterial(string id, Color color)
    {
        string path = $"{MaterialFolder}/{id}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        mat = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
