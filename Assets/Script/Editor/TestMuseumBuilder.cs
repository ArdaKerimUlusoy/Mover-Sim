using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tools → Sahte Şaheser → Create Test Museum
/// Açık sahneye GDD'deki oyun döngüsünün geçtiği yerleri gri kutulardan kurar (x doğu, z kuzey):
///  - KAMYON (atölye, GDD Bölüm 3): doğma noktaları, sahte atölyesi, brifing terminali, bozuk para dolabı.
///    Kamyonun içi "TruckZone": kota dolunca herkes buraya dönünce görev tamamlanır.
///  - SOKAK: kamyonun park ettiği yer; karaborsa tezgahı (GDD Bölüm 7) ara sokakta.
///  - GARAJ (GDD Bölüm 7): garaj mağazası, pratik tuvali, ödül panosu (yer tutucu).
///  - MÜZE (GDD Bölüm 5 ve 8): giriş kapısı, uzun hol, tablo galerisi, vazo galerisi (+ personel köşesi),
///    hazine odası, başyapıt salonu. Eserler / güvenlikler / kameralar görev seviyesiyle açılır:
///      Seviye 1: 3 tablo, 2 güvenlik        Seviye 2: + vazo ve 4. tablo, 3. güvenlik, 1. kamera
///      Seviye 3: + yüzük ve 2. vazo, 4. güvenlik, 2. kamera        Seviye 4+: + büst, 5. güvenlik, 3. kamera
///  - Saklanma noktaları (dolaplar, perdeler), ganimet masaları, "Staff Only" köşesi
///  - Gerekli bileşenler ChatManager objesine ve Player prefab'ına otomatik eklenir
/// Eski prototip nesneleri (Zemin, Cube..., Kapı, eski doğma noktaları, yerdeki test eşyaları) silinir.
/// MuseumSession müzeyi nesne adlarına göre boyar: Wall_, Divider, Gallery_Partition, Floor, Display_, LootTable.
/// Kamyon / sokak / garaj bu yüzden başka adlar kullanır (TruckWall_, Ground_, GarageWall_).
/// Kaide adları eserin görünümünü belirler (tohum + ad): adları değiştirirseniz eserler değişir.
/// Oluşturduktan sonra sahneyi KAYDEDİP commit'leyin (sahnedeki ağ nesnelerinin kimlikleri değişir).
/// </summary>
public static class TestMuseumBuilder
{
    private const string RootName = "TestMuseum";
    private const string MatFolder = "Assets/TestMuseum";
    private static readonly Vector3 Origin = new Vector3(40f, 0f, 0f);

    private static Material floorMat, wallMat, pedestalMat, cabinetMat, marketMat, shopMat, doorMat,
        guardMat, cameraMat, lockerMat, staffFloorMat, coneMat,
        groundMat, truckMat, truckFloorMat, garageMat, curtainMat, darkMat, bushMat, lampMat;

    [MenuItem("Tools/Sahte Şaheser/Create Test Museum")]
    public static void BuildFromMenu() => Build(false);

    /// <summary>silent = true: soru sormadan eskisini silip yeniden kurar (otomatik kurulum için).</summary>
    public static void Build(bool silent)
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!silent && !EditorUtility.DisplayDialog("Test Museum", "Sahnede zaten bir TestMuseum var. Silip yeniden kurulsun mu?", "Evet", "Vazgeç"))
                return;
            Undo.DestroyObjectImmediate(old);
        }

        // Örnek eşyalar (büst / yüzük sahteleri dahil) yoksa önce onları oluştur
        if (ItemTools.FindItem("original_painting") == null || ItemTools.FindItem("forgery_vase") == null ||
            ItemTools.FindItem("loot_figurine") == null || ItemTools.FindItem("forgery_bust") == null || ItemTools.FindItem("forgery_ring") == null)
        {
            ItemTools.CreateSampleItems();
        }

        CreateMaterials();
        int removed = RemoveOldPrototypeObjects();

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Test Museum");
        root.transform.position = Origin;

        BuildStreet(root);
        BuildTruck(root);
        BuildGarage(root);
        BuildBlackMarket(root);
        BuildMuseum(root);

        // ---------------- NavMesh (oyun başında oluşturulur) ----------------
        NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        root.AddComponent<RuntimeNavMeshBaker>();

        // ---------------- Gerekli bileşenler ----------------
        EnsureSceneManagers();
        EnsurePlayerPrefabComponents();
        PlaceMenuCamera();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        if (!silent && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log($"[TestMuseum] Kuruldu ({Origin}). {removed} eski prototip nesnesi silindi. Sahneyi kaydetmeyi (Ctrl+S) unutmayın.");
    }

    // =====================================================================
    //  SOKAK
    // =====================================================================
    private static void BuildStreet(GameObject root)
    {
        GameObject street = Group(root, "Street");
        Box(street, "Ground_Street", new Vector3(-23f, -0.1f, -4f), new Vector3(34f, 0.2f, 40f), groundMat);

        // Yol çizgileri (çarpıştırıcısız dekor)
        for (int i = 0; i < 6; i++)
            Decor(street, "Ground_Line", PrimitiveType.Cube, new Vector3(-14f, 0.005f, -20f + i * 6.5f), new Vector3(0.25f, 0.01f, 2.6f), pedestalMat);

        StreetLamp(street, "StreetLamp_A", new Vector3(-9f, 0f, 4f));
        StreetLamp(street, "StreetLamp_B", new Vector3(-20f, 0f, -6f));
        StreetLamp(street, "StreetLamp_C", new Vector3(-9f, 0f, -12f));

        Decor(street, "Bush", PrimitiveType.Sphere, new Vector3(-7.2f, 0.45f, 5f), new Vector3(1.4f, 1.1f, 1.4f), bushMat);
        Decor(street, "Bush", PrimitiveType.Sphere, new Vector3(-7.2f, 0.45f, -5f), new Vector3(1.4f, 1.1f, 1.4f), bushMat);
        Decor(street, "Bush", PrimitiveType.Sphere, new Vector3(-7.3f, 0.4f, 9f), new Vector3(1.1f, 0.9f, 1.1f), bushMat);
        Decor(street, "Bush", PrimitiveType.Sphere, new Vector3(-7.3f, 0.4f, -9f), new Vector3(1.1f, 0.9f, 1.1f), bushMat);

        Label(root, "Label_Museum", new Vector3(-6.6f, 3.4f, 0f), "label.museum", 0f);
    }

    private static void StreetLamp(GameObject parent, string name, Vector3 pos)
    {
        GameObject lamp = Group(parent, name);
        lamp.transform.localPosition = pos;
        Box(lamp, "Pole", new Vector3(0f, 1.6f, 0f), new Vector3(0.14f, 3.2f, 0.14f), darkMat);
        Decor(lamp, "Bulb", PrimitiveType.Sphere, new Vector3(0f, 3.35f, 0f), Vector3.one * 0.4f, lampMat);
        PointLight(lamp, "Light", new Vector3(0f, 3.2f, 0f), new Color(1f, 0.86f, 0.62f), 3f, 12f);
    }

    // =====================================================================
    //  KAMYON (atölye)   iç hacim: x -36..-28, z 2..7
    // =====================================================================
    private static void BuildTruck(GameObject root)
    {
        GameObject truck = Group(root, "Truck");

        Box(truck, "TruckFloor", new Vector3(-32f, 0.03f, 4.5f), new Vector3(8f, 0.06f, 5f), truckFloorMat);
        Box(truck, "TruckWall_North", new Vector3(-32f, 1.5f, 7.05f), new Vector3(8.2f, 3f, 0.1f), truckMat);
        Box(truck, "TruckWall_South", new Vector3(-32f, 1.5f, 1.95f), new Vector3(8.2f, 3f, 0.1f), truckMat);
        Box(truck, "TruckWall_West", new Vector3(-36.05f, 1.5f, 4.5f), new Vector3(0.1f, 3f, 5.2f), truckMat);
        GameObject roof = Box(truck, "TruckRoof", new Vector3(-32f, 3.05f, 4.5f), new Vector3(8.2f, 0.1f, 5.2f), truckMat);
        roof.AddComponent<NavMeshModifier>().ignoreFromBuild = true; // güvenlikler çatıya yol aramasın

        // Kabin ve tekerlekler (dekor)
        GameObject cab = Box(truck, "TruckCab", new Vector3(-37.6f, 1.1f, 4.5f), new Vector3(3f, 2.2f, 4.6f), truckMat);
        cab.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Decor(truck, "TruckWindow", PrimitiveType.Cube, new Vector3(-39.12f, 1.55f, 4.5f), new Vector3(0.05f, 0.8f, 3.8f), darkMat);
        foreach (float x in new[] { -37.6f, -34.5f, -29.5f })
            foreach (float z in new[] { 1.85f, 7.15f })
            {
                GameObject wheel = Decor(truck, "TruckWheel", PrimitiveType.Cylinder, new Vector3(x, 0.45f, z), new Vector3(0.9f, 0.12f, 0.9f), darkMat);
                wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        Decor(truck, "TruckStripe", PrimitiveType.Cube, new Vector3(-32f, 1.7f, 1.89f), new Vector3(8.2f, 0.35f, 0.02f), pedestalMat);
        Decor(truck, "TruckStripe", PrimitiveType.Cube, new Vector3(-32f, 1.7f, 7.11f), new Vector3(8.2f, 0.35f, 0.02f), pedestalMat);

        // Doğma noktaları: kamyonun açık arka kapısına (doğuya, müzeye) bakar
        for (int i = 0; i < 4; i++)
        {
            GameObject sp = new GameObject("SpawnPoint_" + (i + 1));
            sp.transform.SetParent(truck.transform, false);
            sp.transform.localPosition = new Vector3(-29.6f, 0.1f, 3f + i);
            sp.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            sp.AddComponent<SpawnPoint>();
        }

        Workbench(truck, "ForgeryWorkbench", new Vector3(-33.2f, 0.45f, 6.45f));
        Terminal(truck, "BriefingTerminal", new Vector3(-34.8f, 0.6f, 2.3f));
        Cabinet(truck, "Cabinet_Coins", new Vector3(-32.2f, 0.6f, 2.4f), "distraction_coin", 5, -1);

        // Kamyonun içi: kota dolunca herkes buradayken görev tamamlanır
        GameObject zone = new GameObject("TruckZone");
        zone.transform.SetParent(truck.transform, false);
        zone.transform.localPosition = new Vector3(-32f, 1.5f, 4.5f);
        BoxCollider zb = zone.AddComponent<BoxCollider>();
        zb.size = new Vector3(8f, 3f, 5f);
        zb.isTrigger = true;
        zone.AddComponent<TruckZone>();
        zone.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        PointLight(truck, "TruckLight", new Vector3(-32f, 2.6f, 4.5f), new Color(1f, 0.9f, 0.7f), 5f, 9f);
        Label(root, "Label_Truck", new Vector3(-27.8f, 3.3f, 4.5f), "label.truck", 0f);
    }

    // =====================================================================
    //  GARAJ   x -36..-22, z -22..-10 (kapısı kuzeyde, sokağa açılır)
    // =====================================================================
    private static void BuildGarage(GameObject root)
    {
        GameObject garage = Group(root, "Garage");

        Box(garage, "GarageFloor", new Vector3(-29f, 0.02f, -16f), new Vector3(14f, 0.04f, 12f), garageMat);
        Box(garage, "GarageWall_South", new Vector3(-29f, 2f, -22f), new Vector3(14.3f, 4f, 0.3f), garageMat);
        Box(garage, "GarageWall_West", new Vector3(-36f, 2f, -16f), new Vector3(0.3f, 4f, 12f), garageMat);
        Box(garage, "GarageWall_East", new Vector3(-22f, 2f, -16f), new Vector3(0.3f, 4f, 12f), garageMat);
        Box(garage, "GarageWall_NorthA", new Vector3(-33.5f, 2f, -10f), new Vector3(5f, 4f, 0.3f), garageMat);
        Box(garage, "GarageWall_NorthB", new Vector3(-24.5f, 2f, -10f), new Vector3(5f, 4f, 0.3f), garageMat);
        Box(garage, "GarageWall_Header", new Vector3(-29f, 3.5f, -10f), new Vector3(4f, 1f, 0.3f), garageMat);
        GameObject roof = Box(garage, "GarageRoof", new Vector3(-29f, 4.05f, -16f), new Vector3(14.3f, 0.1f, 12.3f), garageMat);
        roof.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject shop = Box(garage, "GarageShop", new Vector3(-33f, 0.75f, -21.4f), new Vector3(1.4f, 1.5f, 0.6f), shopMat);
        shop.AddComponent<NetworkObject>();
        shop.AddComponent<GarageShop>();
        Label(shop, "Label", Vector3.zero, "label.shop", 1.4f);

        // GDD: pratik tuvali, ödül panosu, kozmetik dolabı (şimdilik yer tutucu)
        Decor(garage, "Garage_AwardBoard", PrimitiveType.Cube, new Vector3(-28f, 2f, -21.8f), new Vector3(2.6f, 1.5f, 0.08f), cabinetMat);
        Decor(garage, "Garage_Easel", PrimitiveType.Cube, new Vector3(-24.4f, 1.25f, -21.2f), new Vector3(1.1f, 0.85f, 0.06f), pedestalMat).transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        Box(garage, "Garage_EaselStand", new Vector3(-24.4f, 0.45f, -21.3f), new Vector3(0.9f, 0.9f, 0.5f), cabinetMat);
        Box(garage, "Garage_CosmeticLocker", new Vector3(-22.6f, 1f, -18f), new Vector3(0.7f, 2f, 1.4f), lockerMat);

        PointLight(garage, "GarageLight", new Vector3(-29f, 3.5f, -16f), new Color(1f, 0.92f, 0.75f), 7f, 14f);
        Label(root, "Label_Garage", new Vector3(-29f, 3.4f, -9.4f), "label.garage", 0f);
    }

    // =====================================================================
    //  KARABORSA (ara sokaktaki tezgah)
    // =====================================================================
    private static void BuildBlackMarket(GameObject root)
    {
        GameObject alley = Group(root, "Alley");
        GameObject market = Box(alley, "BlackMarket", new Vector3(-13f, 0.75f, -19f), new Vector3(2.2f, 1.5f, 1f), marketMat);
        market.AddComponent<NetworkObject>();
        market.AddComponent<BlackMarket>();
        Label(market, "Label", Vector3.zero, "label.market", 1.9f);

        Decor(alley, "Market_Awning", PrimitiveType.Cube, new Vector3(-13f, 2.5f, -18.6f), new Vector3(3.2f, 0.08f, 2.4f), marketMat);
        foreach (float x in new[] { -14.5f, -11.5f })
        {
            Box(alley, "Market_Post", new Vector3(x, 1.25f, -17.5f), new Vector3(0.1f, 2.5f, 0.1f), darkMat);
            Box(alley, "Market_Post", new Vector3(x, 1.25f, -19.7f), new Vector3(0.1f, 2.5f, 0.1f), darkMat);
        }
        Box(alley, "Market_Crate", new Vector3(-10.6f, 0.35f, -19.2f), new Vector3(0.7f, 0.7f, 0.7f), cabinetMat);
        PointLight(alley, "MarketLight", new Vector3(-13f, 2.2f, -18.4f), new Color(0.8f, 0.6f, 1f), 4f, 7f);
    }

    // =====================================================================
    //  MÜZE   x -6..38, z -16..14
    //    Hol            : z -3..3 (batıdaki giriş kapısından doğuya uzanır)
    //    Tablo galerisi : x -6..16, z 3..14        Vazo galerisi : x 16..38, z 3..14 (+ personel köşesi)
    //    Başyapıt salonu: x -6..16, z -16..-3      Hazine odası  : x 16..38, z -16..-3
    // =====================================================================
    private static void BuildMuseum(GameObject root)
    {
        GameObject m = Group(root, "Museum");

        Box(m, "Floor", new Vector3(16f, -0.1f, -1f), new Vector3(44f, 0.2f, 30f), floorMat);

        // Dış duvarlar (batıda 2.4 m giriş boşluğu)
        Box(m, "Wall_North", new Vector3(16f, 2f, 14f), new Vector3(44.3f, 4f, 0.3f), wallMat);
        Box(m, "Wall_South", new Vector3(16f, 2f, -16f), new Vector3(44.3f, 4f, 0.3f), wallMat);
        Box(m, "Wall_East", new Vector3(38f, 2f, -1f), new Vector3(0.3f, 4f, 30f), wallMat);
        Box(m, "Wall_West_A", new Vector3(-6f, 2f, -8.6f), new Vector3(0.3f, 4f, 14.8f), wallMat);
        Box(m, "Wall_West_B", new Vector3(-6f, 2f, 7.6f), new Vector3(0.3f, 4f, 12.8f), wallMat);
        Box(m, "Wall_West_Top", new Vector3(-6f, 3.4f, 0f), new Vector3(0.3f, 1.2f, 2.4f), wallMat);

        // Giriş kapısı: menteşe boşluğun kenarında, kapı paneli onun çocuğu
        GameObject hinge = new GameObject("Door_Hinge");
        hinge.transform.SetParent(m.transform, false);
        hinge.transform.localPosition = new Vector3(-6f, 0f, -1.2f);
        Box(hinge, "DoorPanel", new Vector3(0f, 1.4f, 1.2f), new Vector3(0.12f, 2.8f, 2.35f), doorMat);
        hinge.AddComponent<NetworkObject>();
        hinge.AddComponent<DoorInteractable>();
        hinge.AddComponent<NavMeshModifier>().ignoreFromBuild = true; // kapı NavMesh'i kesmesin

        // Hol ile kuzey galerileri arası (kapılar: x 4..6.4 ve 26..28.4)
        Box(m, "Divider_N1", new Vector3(-1f, 2f, 3f), new Vector3(10f, 4f, 0.3f), wallMat);
        Box(m, "Divider_N2", new Vector3(16.2f, 2f, 3f), new Vector3(19.6f, 4f, 0.3f), wallMat);
        Box(m, "Divider_N3", new Vector3(33.2f, 2f, 3f), new Vector3(9.6f, 4f, 0.3f), wallMat);
        // Hol ile güney salonları arası (kapılar: x 8..10.4 ve 24..26.4)
        Box(m, "Divider_S1", new Vector3(1f, 2f, -3f), new Vector3(14f, 4f, 0.3f), wallMat);
        Box(m, "Divider_S2", new Vector3(17.2f, 2f, -3f), new Vector3(13.6f, 4f, 0.3f), wallMat);
        Box(m, "Divider_S3", new Vector3(32.2f, 2f, -3f), new Vector3(11.6f, 4f, 0.3f), wallMat);
        // Tablo galerisi | vazo galerisi (geçit: z 7..9.4)
        Box(m, "Divider_NA", new Vector3(16f, 2f, 5f), new Vector3(0.3f, 4f, 4f), wallMat);
        Box(m, "Divider_NB", new Vector3(16f, 2f, 11.7f), new Vector3(0.3f, 4f, 4.6f), wallMat);
        // Başyapıt salonu | hazine odası (geçit yok)
        Box(m, "Divider_SA", new Vector3(16f, 2f, -9.5f), new Vector3(0.3f, 4f, 13f), wallMat);

        // ---------------- Tablo galerisi (Seviye 1) ----------------
        Label(root, "Label_Paintings", new Vector3(5f, 3.3f, 3.6f), "label.paintings", 0f);
        Pedestal(m, "Display_Painting1", new Vector3(0f, 0.5f, 13f), "original_painting", 1);
        Pedestal(m, "Display_Painting2", new Vector3(6f, 0.5f, 13f), "original_painting", 1);
        Pedestal(m, "Display_Painting3", new Vector3(12f, 0.5f, 13f), "original_painting", 1);
        Box(m, "Gallery_Partition", new Vector3(6f, 1.6f, 8f), new Vector3(6f, 3.2f, 0.3f), wallMat);

        // ---------------- Vazo galerisi (Seviye 2+) + personel köşesi ----------------
        Label(root, "Label_Vases", new Vector3(27f, 3.3f, 3.6f), "label.vases", 0f);
        Pedestal(m, "Display_Vase1", new Vector3(22f, 0.5f, 9f), "original_vase", 2);
        Pedestal(m, "Display_Vase2", new Vector3(28f, 0.5f, 9f), "original_vase", 3);
        Pedestal(m, "Display_Painting4", new Vector3(35f, 0.5f, 13f), "original_painting", 2); // Staff Only köşesinde

        Box(m, "StaffFloor", new Vector3(35f, 0.01f, 11f), new Vector3(6f, 0.02f, 6f), staffFloorMat).GetComponent<Collider>().enabled = false;
        Box(m, "StaffRope_A", new Vector3(33f, 0.5f, 8f), new Vector3(2f, 0.08f, 0.08f), doorMat);
        Box(m, "StaffRope_B", new Vector3(37.2f, 0.5f, 8f), new Vector3(1.6f, 0.08f, 0.08f), doorMat);
        Box(m, "StaffRope_C", new Vector3(32f, 0.5f, 11f), new Vector3(0.08f, 0.08f, 6f), doorMat);
        GameObject staff = new GameObject("StaffOnlyArea");
        staff.transform.SetParent(m.transform, false);
        staff.transform.localPosition = new Vector3(35f, 1.5f, 11f);
        BoxCollider staffBox = staff.AddComponent<BoxCollider>();
        staffBox.size = new Vector3(6f, 3f, 6f);
        staffBox.isTrigger = true;
        staff.AddComponent<RestrictedArea>();
        staff.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Label(staff, "Label", Vector3.zero, "label.staff", 1.4f);

        // ---------------- Hazine odası (Seviye 3+) ----------------
        Label(root, "Label_Treasury", new Vector3(25.2f, 3.3f, -3.6f), "label.treasury", 0f);
        Pedestal(m, "Display_Ring1", new Vector3(27f, 0.5f, -10f), "original_ring", 3);

        // ---------------- Başyapıt salonu (Seviye 4+) ----------------
        Label(root, "Label_Masterpiece", new Vector3(9.2f, 3.3f, -3.6f), "label.masterpiece", 0f);
        Pedestal(m, "Display_Bust1", new Vector3(5f, 0.5f, -10f), "original_bust", 4);

        // ---------------- Küçük ganimet masaları (sahtesi gerekmez; kotayı tamamlamak için) ----------------
        LootTable(m, "LootTable_A", new Vector3(0f, 0.4f, -2.4f), "loot_snuffbox", "loot_coinpouch");
        LootTable(m, "LootTable_B", new Vector3(18f, 0.4f, 2.4f), "loot_figurine", "loot_snuffbox");
        LootTable(m, "LootTable_C", new Vector3(34f, 0.4f, -15.4f), "loot_coinpouch", "loot_figurine");
        LootTable(m, "LootTable_D", new Vector3(-2f, 0.4f, -15.4f), "loot_snuffbox", "loot_figurine");

        // ---------------- Saklanma noktaları: dolaplar ve perdeler (yaw = odaya bakan yön) ----------------
        Hiding(m, "Locker_A", new Vector3(-5.5f, 1.05f, 8f), 90f, false);
        Hiding(m, "Locker_B", new Vector3(37.5f, 1.05f, 6f), 270f, false);
        Hiding(m, "Locker_C", new Vector3(20f, 1.05f, -15.5f), 0f, false);
        Hiding(m, "Curtain_A", new Vector3(-5.6f, 1.1f, -9f), 90f, true);
        Hiding(m, "Curtain_B", new Vector3(37.6f, 1.1f, 0f), 270f, true);
        Hiding(m, "Curtain_C", new Vector3(9f, 1.1f, 13.6f), 180f, true);

        // ---------------- Güvenlik görevlileri (GDD Bölüm 8: seviye 1 → 2, 2 → 3, 3 → 4, 4+ → 5) ----------------
        Guard(m, "Guard_1", 1, new Vector3(-2f, 1f, 0.3f), new[]
        {
            new Vector3(-2f, 0f, 0.3f), new Vector3(34f, 0f, 0.3f), new Vector3(34f, 0f, -0.9f), new Vector3(-2f, 0f, -0.9f)
        });
        Guard(m, "Guard_2", 1, new Vector3(0f, 1f, 5.5f), new[]
        {
            new Vector3(0f, 0f, 5.5f), new Vector3(13f, 0f, 5.5f), new Vector3(13f, 0f, 11f), new Vector3(0f, 0f, 11f)
        });
        Guard(m, "Guard_3", 2, new Vector3(19f, 1f, 6f), new[]
        {
            new Vector3(19f, 0f, 6f), new Vector3(30f, 0f, 6f), new Vector3(30f, 0f, 12f), new Vector3(19f, 0f, 12f)
        });
        Guard(m, "Guard_4", 3, new Vector3(19f, 1f, -6f), new[]
        {
            new Vector3(19f, 0f, -6f), new Vector3(35f, 0f, -6f), new Vector3(35f, 0f, -13f), new Vector3(19f, 0f, -13f)
        });
        Guard(m, "Guard_5", 4, new Vector3(-3f, 1f, -6f), new[]
        {
            new Vector3(-3f, 0f, -6f), new Vector3(13f, 0f, -6f), new Vector3(13f, 0f, -13f), new Vector3(-3f, 0f, -13f)
        });

        // ---------------- Kameralar (GDD: 2. seviyeden itibaren) ----------------
        CameraRig(m, "Camera_Vases", 2, new Vector3(37.5f, 3.2f, 3.6f), 315f);
        CameraRig(m, "Camera_Treasury", 3, new Vector3(16.6f, 3.2f, -15.5f), 45f);
        CameraRig(m, "Camera_Hall", 4, new Vector3(37.5f, 3.2f, 0f), 270f);

        // ---------------- Sıcak müze ışıkları (rengi temadan gelir) ----------------
        Vector3[] lights =
        {
            new Vector3(5f, 3.6f, 8.5f), new Vector3(27f, 3.6f, 8.5f), new Vector3(5f, 3.6f, 0f),
            new Vector3(27f, 3.6f, 0f), new Vector3(27f, 3.6f, -9.5f), new Vector3(5f, 3.6f, -9.5f),
        };
        for (int i = 0; i < lights.Length; i++)
            PointLight(m, "MuseumLight_" + (i + 1), lights[i], new Color(1f, 0.9f, 0.75f), 4f, 16f);
    }

    // =====================================================================
    //  Eski prototip nesneleri
    // =====================================================================

    /// <summary>TestMuseum'dan önceki denemelerden kalan kök nesneleri siler (zemin, küpler, kapı, eski doğma noktaları, yerdeki eşyalar).</summary>
    private static int RemoveOldPrototypeObjects()
    {
        var doomed = new List<GameObject>();
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == RootName) continue;
            // Yöneticilere, ışığa, post-process'e ve kameralara dokunma
            if (go.GetComponentInChildren<NetworkManager>(true) != null || go.GetComponentInChildren<ChatManager>(true) != null ||
                go.GetComponentInChildren<Light>(true) != null || go.GetComponentInChildren<Camera>(true) != null ||
                go.GetComponentInChildren<UnityEngine.Rendering.Volume>(true) != null) continue;

            string n = go.name;
            bool prototype = n == "Zemin" || n == "Kapı" || n == "Cube" || n.StartsWith("Cube (") || n.StartsWith("SpawnPoint") ||
                             go.GetComponent<SpawnPoint>() != null || go.GetComponent<WorldItem>() != null ||
                             go.GetComponent<Pickupable>() != null || go.GetComponent<NetworkPickupable>() != null;
            if (prototype) doomed.Add(go);
        }
        foreach (GameObject go in doomed) Undo.DestroyObjectImmediate(go);
        return doomed.Count;
    }

    /// <summary>Menü kamerası (varsa) yeni sahneye baksın.</summary>
    private static void PlaceMenuCamera()
    {
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name != "Camera" && go.name != "MenuCamera") continue;
            if (go.GetComponentInChildren<Camera>(true) == null) continue;
            Undo.RecordObject(go.transform, "Place menu camera");
            go.transform.position = Origin + new Vector3(-24f, 9f, -9f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-10f, 1f, 1f) - new Vector3(-24f, 9f, -9f));
            foreach (Transform child in go.transform)
            {
                Undo.RecordObject(child, "Place menu camera");
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
            }
        }
    }

    // =====================================================================
    //  Yardımcılar
    // =====================================================================

    private static GameObject Group(GameObject parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

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

    /// <summary>Çarpıştırıcısız süs objesi (NavMesh'i ve oyuncuyu etkilemez).</summary>
    private static GameObject Decor(GameObject parent, string name, PrimitiveType type, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static void PointLight(GameObject parent, string name, Vector3 localPos, Color color, float intensity, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
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

    /// <summary>Sahte atölyesi: tezgah + kamera/çalışma noktaları (kamera tezgahın güneyinde durur).</summary>
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

    /// <summary>Brifing terminali: üstünde hedef eserlerin hologramı döner.</summary>
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

    /// <summary>Saklanma noktası: dolap ya da perde. yaw = çıkış yönü (odaya bakan taraf).</summary>
    private static void Hiding(GameObject root, string name, Vector3 pos, float yaw, bool curtain)
    {
        Vector3 size = curtain ? new Vector3(1.3f, 2.2f, 0.5f) : new Vector3(0.9f, 2.1f, 0.7f);
        GameObject go = Box(root, name, pos, size, curtain ? curtainMat : lockerMat);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.AddComponent<NetworkObject>();
        go.AddComponent<HidingSpot>();
    }

    private static void Guard(GameObject root, string name, int requiredLevel, Vector3 pos, Vector3[] points)
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
        so.FindProperty("requiredLevel").intValue = requiredLevel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CameraRig(GameObject root, string name, int requiredLevel, Vector3 pos, float yaw)
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
        so.FindProperty("requiredLevel").intValue = requiredLevel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetMaterial(VisionConeVisual cone)
    {
        SerializedObject so = new SerializedObject(cone);
        so.FindProperty("material").objectReferenceValue = coneMat;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>ChatManager objesine TeamBank, SecurityManager, PingSystem ve MuseumSession ekler (yoksa).</summary>
    private static void EnsureSceneManagers()
    {
        ChatManager chat = Object.FindFirstObjectByType<ChatManager>();
        if (chat == null)
        {
            Debug.LogWarning("[TestMuseum] Sahnede ChatManager yok. TeamBank / SecurityManager / PingSystem / MuseumSession'ı NetworkObject'li bir objeye elle ekleyin.");
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
        groundMat = Mat("Street", new Color(0.30f, 0.31f, 0.34f));
        truckMat = Mat("Truck", new Color(0.93f, 0.55f, 0.30f));
        truckFloorMat = Mat("TruckFloor", new Color(0.50f, 0.42f, 0.34f));
        garageMat = Mat("Garage", new Color(0.62f, 0.62f, 0.60f));
        curtainMat = Mat("Curtain", new Color(0.55f, 0.12f, 0.18f));
        darkMat = Mat("Dark", new Color(0.12f, 0.12f, 0.14f));
        bushMat = Mat("Bush", new Color(0.30f, 0.58f, 0.32f));
        lampMat = Mat("Lamp", new Color(1f, 0.93f, 0.72f));

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
