using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tools → Sahte Şaheser → Create Test Museum
/// Açık sahneye, harita gelene kadar test için gri kutulardan basit bir müze kurar:
///  - Kamyon/Garaj odası: doğma noktaları, sahte atölyesi, brifing terminali, bozuk para dolabı, karaborsa, garaj mağazası
///  - Kapı (açılıp kapanır)
///  - Galeri: eser kaideleri (2. ve 3. vazo görev seviyesiyle açılır), küçük ganimet masaları, ara duvar, 2 saklanma dolabı, "Staff Only" odası
///  - Güvenlik: 2 devriye görevlisi, 2 dönen kamera (NavMesh oyun başında otomatik oluşur)
///  - Gerekli bileşenler ChatManager objesine ve Player prefab'ına otomatik eklenir
/// Var olan diğer SpawnPoint'ler kapatılır, oyuncular müzede doğar. Ctrl+Z ile geri alınabilir.
/// Oluşturduktan sonra sahneyi KAYDEDİP commit'leyin.
/// </summary>
public static class TestMuseumBuilder
{
    private const string RootName = "TestMuseum";
    private const string MatFolder = "Assets/TestMuseum";
    private static readonly Vector3 Origin = new Vector3(40f, 0f, 0f);

    private static Material floorMat, wallMat, pedestalMat, cabinetMat, marketMat, shopMat, doorMat,
        guardMat, cameraMat, lockerMat, staffFloorMat, coneMat;

    [MenuItem("Tools/Sahte Şaheser/Create Test Museum")]
    public static void Build()
    {
        if (GameObject.Find(RootName) != null)
        {
            if (!EditorUtility.DisplayDialog("Test Museum", "Sahnede zaten bir TestMuseum var. Silip yeniden kurulsun mu?", "Evet", "Vazgeç"))
                return;
            Undo.DestroyObjectImmediate(GameObject.Find(RootName));
        }

        // Örnek eşyalar yoksa önce onları oluştur
        if (ItemTools.FindItem("original_painting") == null || ItemTools.FindItem("forgery_vase") == null || ItemTools.FindItem("loot_figurine") == null)
        {
            ItemTools.CreateSampleItems();
        }

        CreateMaterials();

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Test Museum");
        root.transform.position = Origin;

        // ---------------- Yapı ----------------
        Box(root, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(32f, 0.2f, 22f), floorMat);
        Box(root, "Wall_North", new Vector3(0f, 2f, 11f), new Vector3(32f, 4f, 0.3f), wallMat);
        Box(root, "Wall_South", new Vector3(0f, 2f, -11f), new Vector3(32f, 4f, 0.3f), wallMat);
        Box(root, "Wall_East", new Vector3(16f, 2f, 0f), new Vector3(0.3f, 4f, 22f), wallMat);
        Box(root, "Wall_West", new Vector3(-16f, 2f, 0f), new Vector3(0.3f, 4f, 22f), wallMat);

        // Garaj ile galeri arasındaki duvar (ortada 2.2 m kapı boşluğu)
        Box(root, "Divider_A", new Vector3(-4f, 2f, -6.05f), new Vector3(0.3f, 4f, 9.9f), wallMat);
        Box(root, "Divider_B", new Vector3(-4f, 2f, 6.05f), new Vector3(0.3f, 4f, 9.9f), wallMat);
        Box(root, "Divider_Top", new Vector3(-4f, 3.4f, 0f), new Vector3(0.3f, 1.2f, 2.2f), wallMat);

        // Kapı: menteşe kapı boşluğunun kenarında, kapı paneli onun çocuğu
        GameObject hinge = new GameObject("Door_Hinge");
        hinge.transform.SetParent(root.transform, false);
        hinge.transform.localPosition = new Vector3(-4f, 0f, -1.1f);
        Box(hinge, "DoorPanel", new Vector3(0f, 1.4f, 1.1f), new Vector3(0.12f, 2.8f, 2.15f), doorMat);
        hinge.AddComponent<NetworkObject>();
        hinge.AddComponent<DoorInteractable>();
        hinge.AddComponent<NavMeshModifier>().ignoreFromBuild = true; // kapı NavMesh'i kesmesin

        // ---------------- Garaj / Kamyon odası ----------------
        Label(root, "Label_Garage", new Vector3(-10f, 3.2f, 0f), "label.garage", 0f);

        for (int i = 0; i < 4; i++)
        {
            GameObject sp = new GameObject("SpawnPoint_" + (i + 1));
            sp.transform.SetParent(root.transform, false);
            sp.transform.localPosition = new Vector3(-12f, 0.1f, -3f + i * 2f);
            sp.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // galeriye baksın
            sp.AddComponent<SpawnPoint>();
        }

        // Sahteler artık dolaptan değil ATÖLYEDEN çıkar (çömlek çarkı / tuval, gerçek benzerlik yüzdesiyle)
        Workbench(root, "ForgeryWorkbench", new Vector3(-12.25f, 0.45f, 9.6f));
        Terminal(root, "BriefingTerminal", new Vector3(-6.5f, 0.6f, 9.9f));
        Cabinet(root, "Cabinet_Coins", new Vector3(-8.5f, 0.6f, 9.8f), "distraction_coin", 5, -1);

        GameObject market = Box(root, "BlackMarket", new Vector3(-14f, 0.75f, -9.5f), new Vector3(2.2f, 1.5f, 1f), marketMat);
        market.AddComponent<NetworkObject>();
        market.AddComponent<BlackMarket>();
        Label(market, "Label", Vector3.zero, "label.market", 1.4f);

        GameObject shop = Box(root, "GarageShop", new Vector3(-10.5f, 0.75f, -9.8f), new Vector3(1.4f, 1.5f, 0.6f), shopMat);
        shop.AddComponent<NetworkObject>();
        shop.AddComponent<GarageShop>();
        Label(shop, "Label", Vector3.zero, "label.shop", 1.4f);

        // ---------------- Galeri ----------------
        Label(root, "Label_Gallery", new Vector3(6f, 3.2f, 0f), "label.gallery", 0f);

        Pedestal(root, "Display_Painting", new Vector3(2f, 0.5f, 9f), "original_painting");
        Pedestal(root, "Display_Vase", new Vector3(8f, 0.5f, 9f), "original_vase");
        Pedestal(root, "Display_Vase2", new Vector3(5f, 0.5f, 9f), "original_vase", 2);   // 2. görevden itibaren
        Pedestal(root, "Display_Vase3", new Vector3(11f, 0.5f, -9f), "original_vase", 3); // 3. görevden itibaren
        Pedestal(root, "Display_Ring", new Vector3(2f, 0.5f, -9f), "original_ring");
        Pedestal(root, "Display_Bust", new Vector3(8f, 0.5f, -9f), "original_bust");
        Pedestal(root, "Display_Painting2", new Vector3(14.5f, 0.5f, 9.5f), "original_painting"); // Staff Only odasında

        Box(root, "Gallery_Partition", new Vector3(6f, 1.6f, 0f), new Vector3(5f, 3.2f, 0.3f), wallMat);

        // Küçük ganimet masaları (sahtesi gerekmez; kotayı tamamlamak için)
        LootTable(root, "LootTable_A", new Vector3(-1.5f, 0.4f, -10.3f), "loot_snuffbox", "loot_coinpouch");
        LootTable(root, "LootTable_B", new Vector3(5f, 0.4f, -10.3f), "loot_figurine", "loot_snuffbox");
        LootTable(root, "LootTable_C", new Vector3(-2.5f, 0.4f, 10.3f), "loot_coinpouch", "loot_figurine");

        // ---------------- Staff Only odası (kuzeydoğu köşe) ----------------
        Box(root, "StaffFloor", new Vector3(13.5f, 0.01f, 8f), new Vector3(5f, 0.02f, 6f), staffFloorMat).GetComponent<Collider>().enabled = false;
        Box(root, "StaffRope_A", new Vector3(11.6f, 0.5f, 5f), new Vector3(1.2f, 0.08f, 0.08f), doorMat);
        Box(root, "StaffRope_B", new Vector3(15.1f, 0.5f, 5f), new Vector3(1.8f, 0.08f, 0.08f), doorMat);
        GameObject staff = new GameObject("StaffOnlyArea");
        staff.transform.SetParent(root.transform, false);
        staff.transform.localPosition = new Vector3(13.5f, 1.5f, 8f);
        BoxCollider staffBox = staff.AddComponent<BoxCollider>();
        staffBox.size = new Vector3(5f, 3f, 6f);
        staffBox.isTrigger = true;
        staff.AddComponent<RestrictedArea>();
        staff.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Label(staff, "Label", Vector3.zero, "label.staff", 1.4f);

        // ---------------- Saklanma dolapları ----------------
        Locker(root, "Locker_A", new Vector3(0.5f, 1.05f, 10.45f), 180f);
        Locker(root, "Locker_B", new Vector3(15.45f, 1.05f, -5f), 270f);

        // ---------------- Güvenlik görevlileri ----------------
        Guard(root, "Guard_1", new Vector3(0f, 1f, 6f), new[]
        {
            new Vector3(0f, 0f, 6f), new Vector3(10f, 0f, 6f), new Vector3(10f, 0f, -6f), new Vector3(0f, 0f, -6f)
        });
        Guard(root, "Guard_2", new Vector3(3f, 1f, -2f), new[]
        {
            new Vector3(3f, 0f, -2f), new Vector3(12f, 0f, 2.5f), new Vector3(13.5f, 0f, 8f), new Vector3(12f, 0f, -8f)
        });

        // ---------------- Kameralar ----------------
        CameraRig(root, "Camera_Staff", new Vector3(15.5f, 3.2f, 10.5f), 225f);
        CameraRig(root, "Camera_Gallery", new Vector3(-3.5f, 3.2f, -10.5f), 45f);

        // ---------------- NavMesh (oyun başında oluşturulur) ----------------
        NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        root.AddComponent<RuntimeNavMeshBaker>();

        // ---------------- Gerekli bileşenler ----------------
        EnsureSceneManagers();
        EnsurePlayerPrefabComponents();

        // ---------------- Eski doğma noktalarını kapat ----------------
        int disabled = 0;
        foreach (SpawnPoint sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sp.transform.IsChildOf(root.transform)) continue;
            Undo.RecordObject(sp.gameObject, "Disable old spawn point");
            sp.gameObject.SetActive(false);
            disabled++;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"[TestMuseum] Kuruldu ({Origin}). {disabled} eski doğma noktası kapatıldı. Sahneyi kaydetmeyi (Ctrl+S) unutmayın.");
    }

    // ---------------- Yardımcılar ----------------

    private static GameObject Box(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static void Label(GameObject parent, string name, Vector3 localPos, string key, float height, ItemDefinition item = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        // Ölçekli bir objenin çocuğuysa ölçeği sıfırla (yazı konumu bozulmasın)
        Vector3 ls = parent.transform.lossyScale;
        go.transform.localScale = new Vector3(1f / Mathf.Max(ls.x, 0.001f), 1f / Mathf.Max(ls.y, 0.001f), 1f / Mathf.Max(ls.z, 0.001f));
        go.AddComponent<WorldLabel>().Setup(key, height, item);
    }

    private static void Cabinet(GameObject root, string name, Vector3 pos, string itemId, int count, int similarity)
    {
        GameObject go = Box(root, name, pos, new Vector3(1.6f, 1.2f, 0.7f), cabinetMat);
        go.AddComponent<NetworkObject>();
        ItemContainerInteractable c = go.AddComponent<ItemContainerInteractable>();

        ItemDefinition item = ItemTools.FindItem(itemId);
        SerializedObject so = new SerializedObject(c);
        so.FindProperty("item").objectReferenceValue = item;
        so.FindProperty("count").intValue = count;
        so.FindProperty("similarityPercent").intValue = similarity;
        so.FindProperty("refillSeconds").floatValue = 10f;
        so.FindProperty("holdDuration").floatValue = 1.5f;
        so.ApplyModifiedPropertiesWithoutUndo();

        Label(go, "Label", Vector3.zero, "label.cabinet", 1.1f, item);
    }

    private static void Pedestal(GameObject root, string name, Vector3 pos, string itemId, int requiredLevel = 1)
    {
        GameObject go = Box(root, name, pos, new Vector3(1f, 1f, 1f), pedestalMat);
        go.AddComponent<NetworkObject>();
        ArtworkDisplay d = go.AddComponent<ArtworkDisplay>();

        SerializedObject so = new SerializedObject(d);
        so.FindProperty("artwork").objectReferenceValue = ItemTools.FindItem(itemId);
        so.FindProperty("requiredLevel").intValue = requiredLevel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Sahte atölyesi: tezgah + kamera/çalışma noktaları.</summary>
    private static void Workbench(GameObject root, string name, Vector3 pos)
    {
        GameObject go = Box(root, name, pos, new Vector3(2.6f, 0.9f, 1f), cabinetMat);
        go.AddComponent<NetworkObject>();
        ForgeryWorkbench bench = go.AddComponent<ForgeryWorkbench>();

        GameObject work = new GameObject(name + "_WorkPoint");
        work.transform.SetParent(root.transform, false);
        work.transform.localPosition = pos + new Vector3(0f, 0.45f, 0f);

        GameObject camPoint = new GameObject(name + "_CameraPoint");
        camPoint.transform.SetParent(root.transform, false);
        camPoint.transform.localPosition = pos + new Vector3(0f, 1.2f, -1.15f);

        SerializedObject so = new SerializedObject(bench);
        so.FindProperty("workPoint").objectReferenceValue = work.transform;
        so.FindProperty("cameraPoint").objectReferenceValue = camPoint.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        Label(go, "Label", Vector3.zero, "label.workbench", 1.3f);
    }

    /// <summary>Brifing terminali: üstünde hedef vazoların hologramı döner.</summary>
    private static void Terminal(GameObject root, string name, Vector3 pos)
    {
        GameObject go = Box(root, name, pos, new Vector3(0.8f, 1.2f, 0.5f), marketMat);
        go.AddComponent<NetworkObject>();
        go.AddComponent<BriefingTerminal>();
        Label(go, "Label", Vector3.zero, "label.briefing", 1.9f);
    }

    /// <summary>Küçük ganimet masası: üstüne sahnede duran eşya prefab'ları konur.</summary>
    private static void LootTable(GameObject root, string name, Vector3 pos, params string[] itemIds)
    {
        Box(root, name, pos, new Vector3(2f, 0.8f, 0.9f), pedestalMat);
        for (int i = 0; i < itemIds.Length; i++)
        {
            ItemDefinition def = ItemTools.FindItem(itemIds[i]);
            if (def == null || def.worldPrefab == null) continue;
            GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(def.worldPrefab);
            item.transform.SetParent(root.transform, false);
            float x = (i - (itemIds.Length - 1) * 0.5f) * 0.7f;
            item.transform.localPosition = pos + new Vector3(x, 0.55f, 0f);
            Undo.RegisterCreatedObjectUndo(item, "Loot");
        }
    }

    private static void Locker(GameObject root, string name, Vector3 pos, float yaw)
    {
        GameObject go = Box(root, name, pos, new Vector3(0.9f, 2.1f, 0.7f), lockerMat);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.AddComponent<NetworkObject>();
        go.AddComponent<HidingSpot>();
    }

    private static void Guard(GameObject root, string name, Vector3 pos, Vector3[] points)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        g.name = name;
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos;
        g.GetComponent<Renderer>().sharedMaterial = guardMat;

        GameObject hat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hat.name = "Hat";
        Object.DestroyImmediate(hat.GetComponent<Collider>());
        hat.transform.SetParent(g.transform, false);
        hat.transform.localPosition = new Vector3(0f, 1.0f, 0.1f);
        hat.transform.localScale = new Vector3(0.75f, 0.15f, 0.9f);
        hat.GetComponent<Renderer>().sharedMaterial = cameraMat;

        GameObject coneGo = new GameObject("VisionCone");
        coneGo.transform.SetParent(g.transform, false);
        VisionConeVisual cone = coneGo.AddComponent<VisionConeVisual>();
        SetMaterial(cone);

        g.AddComponent<NetworkObject>();
        g.AddComponent<NetworkTransform>();
        NavMeshAgent agent = g.AddComponent<NavMeshAgent>();
        agent.baseOffset = 1f;
        agent.radius = 0.4f;
        agent.height = 2f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.enabled = false;
        g.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject wpRoot = new GameObject(name + "_Waypoints");
        wpRoot.transform.SetParent(root.transform, false);
        Transform[] wps = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            GameObject wp = new GameObject("WP" + (i + 1));
            wp.transform.SetParent(wpRoot.transform, false);
            wp.transform.localPosition = points[i];
            wps[i] = wp.transform;
        }

        GuardAI ai = g.AddComponent<GuardAI>();
        SerializedObject so = new SerializedObject(ai);
        SerializedProperty arr = so.FindProperty("waypoints");
        arr.arraySize = wps.Length;
        for (int i = 0; i < wps.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = wps[i];
        so.FindProperty("cone").objectReferenceValue = cone;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CameraRig(GameObject root, string name, Vector3 pos, float yaw)
    {
        GameObject cam = new GameObject(name);
        cam.transform.SetParent(root.transform, false);
        cam.transform.localPosition = pos;
        cam.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        Object.DestroyImmediate(head.GetComponent<Collider>());
        head.transform.SetParent(cam.transform, false);
        head.transform.localScale = new Vector3(0.25f, 0.25f, 0.5f);
        head.GetComponent<Renderer>().sharedMaterial = cameraMat;

        GameObject coneGo = new GameObject("VisionCone");
        coneGo.transform.SetParent(cam.transform, false);
        VisionConeVisual cone = coneGo.AddComponent<VisionConeVisual>();
        SetMaterial(cone);

        cam.AddComponent<NetworkObject>();
        SecurityCamera sc = cam.AddComponent<SecurityCamera>();
        SerializedObject so = new SerializedObject(sc);
        so.FindProperty("head").objectReferenceValue = head.transform;
        so.FindProperty("cone").objectReferenceValue = cone;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetMaterial(VisionConeVisual cone)
    {
        SerializedObject so = new SerializedObject(cone);
        so.FindProperty("material").objectReferenceValue = coneMat;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>ChatManager objesine TeamBank, SecurityManager ve PingSystem ekler (yoksa).</summary>
    private static void EnsureSceneManagers()
    {
        ChatManager chat = Object.FindFirstObjectByType<ChatManager>();
        if (chat == null)
        {
            Debug.LogWarning("[TestMuseum] Sahnede ChatManager yok. TeamBank / SecurityManager / PingSystem'i NetworkObject'li bir objeye elle ekleyin.");
            return;
        }

        GameObject go = chat.gameObject;
        if (go.GetComponent<TeamBank>() == null) Undo.AddComponent<TeamBank>(go);
        if (go.GetComponent<SecurityManager>() == null) Undo.AddComponent<SecurityManager>(go);
        if (go.GetComponent<PingSystem>() == null) Undo.AddComponent<PingSystem>(go);
        if (go.GetComponent<MuseumSession>() == null) Undo.AddComponent<MuseumSession>(go);
    }

    /// <summary>Player prefab'ına PlayerStealth (ve yoksa PlayerInventory) ekler.</summary>
    private static void EnsurePlayerPrefabComponents()
    {
        NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
        GameObject prefab = nm != null && nm.NetworkConfig != null ? nm.NetworkConfig.PlayerPrefab : null;
        string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[TestMuseum] Player prefab bulunamadı. PlayerStealth bileşenini Player prefab'ına elle ekleyin.");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        bool changed = false;
        if (contents.GetComponent<PlayerInventory>() == null) { contents.AddComponent<PlayerInventory>(); changed = true; }
        if (contents.GetComponent<PlayerStealth>() == null) { contents.AddComponent<PlayerStealth>(); changed = true; }
        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log("[TestMuseum] Player prefab'ına PlayerStealth eklendi.");
        }
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets", "TestMuseum");

        floorMat = Mat("Floor", new Color(0.55f, 0.52f, 0.48f));
        wallMat = Mat("Wall", new Color(0.85f, 0.83f, 0.78f));
        pedestalMat = Mat("Pedestal", new Color(0.95f, 0.95f, 0.95f));
        cabinetMat = Mat("Cabinet", new Color(0.55f, 0.38f, 0.25f));
        marketMat = Mat("BlackMarket", new Color(0.35f, 0.2f, 0.45f));
        shopMat = Mat("Shop", new Color(0.2f, 0.6f, 0.6f));
        doorMat = Mat("Door", new Color(0.45f, 0.3f, 0.2f));
        guardMat = Mat("Guard", new Color(0.18f, 0.25f, 0.45f));
        cameraMat = Mat("CameraBody", new Color(0.15f, 0.15f, 0.17f));
        lockerMat = Mat("Locker", new Color(0.45f, 0.55f, 0.65f));
        staffFloorMat = Mat("StaffFloor", new Color(0.55f, 0.2f, 0.2f));

        string conePath = $"{MatFolder}/VisionCone.mat";
        coneMat = AssetDatabase.LoadAssetAtPath<Material>(conePath);
        if (coneMat == null)
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            coneMat = new Material(unlit != null ? unlit : Shader.Find("Standard"));
            VisionConeVisual.MakeTransparent(coneMat);
            AssetDatabase.CreateAsset(coneMat, conePath);
        }
    }

    private static Material Mat(string name, Color color)
    {
        string path = $"{MatFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        m = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
