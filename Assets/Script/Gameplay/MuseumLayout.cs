using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Her yeni haritada (görev tohumu değişince) müzenin İÇİNİ ve DIŞINI baştan kurar.
/// Hepsi tohumdan üretildiği için her bilgisayarda aynı çıkar; ağdan ek veri gönderilmez, sahne yeniden kurulmaz.
///
/// İÇERİ — her haritada yeni bir KAT PLANI:
///   - Girişte bir lobi, geri kalan alan rastgele bölünerek 5–8 oda; odalar arası kapılar (bazı odalara iki yoldan
///     girilir, kaçarken döngü yapılabilir). Odaların yeri, boyu, kapıları her haritada farklı.
///   - Tavan / çatı, her odada tavan lambası.
///   - Tablolar odaların DUVARLARINDA, her birinin üstünde tablo lambası ve onu aydınlatan spot ışık.
///   - Vazo / yüzük / büst kaidelerde (kendi spot ışıklarıyla).
///   - Bir oda "Staff Only" (en değerli eser burada), bir oda GÜVENLİK ODASI (kameraları 4 sn kapatan konsol),
///     bir köşede hapis. Dolaplar / perdeler, ganimet masaları, dekor, her odada bir kamera, güvenlik devriyeleri,
///     ziyaretçilerin gezeceği noktalar da plana göre yerleşir.
/// DIŞARI (MuseumLayoutExterior.cs): temanın gerçek müzesine benzeyen cephe, sokak, ışık.
/// Sunucu düzen değişince NavMesh'i yeniden oluşturur (MuseumSession). TestMuseum köküne kendiliğinden eklenir.
/// </summary>
public partial class MuseumLayout : MonoBehaviour
{
    private const int Salt = 0x5A17;

    // Müze alanı: x -6..38, z -16..14 (2 m'lik hücreler: 22 × 15). Giriş batı duvarında z = 0.
    private const float X0 = -6f, Z0 = -16f, Cell = 2f;
    private const int NX = 22, NZ = 15;
    private const float WallH = 4f;

    public static MuseumLayout Instance { get; private set; }

    /// <summary>Ziyaretçilerin gezeceği noktalar (dünya koordinatı) — VisitorManager kullanır.</summary>
    public readonly List<Vector3> VisitorPoints = new List<Vector3>();
    /// <summary>Lobi ortası (dünya) — ziyaretçiler buradan başlar.</summary>
    public Vector3 LobbyWorld { get; private set; }
    /// <summary>Düzen değiştiğinde artar (ziyaretçiler yeniden doğar).</summary>
    public int Version { get; private set; }

    // ---------------- Plan ----------------
    private sealed class PRoom
    {
        public int id;
        public RectInt r;
        public string role = "gallery"; // lobby, gallery, staff, security
        public readonly List<int> doors = new List<int>();
        public Vector2 Min => new Vector2(X0 + r.xMin * Cell, Z0 + r.yMin * Cell);
        public Vector2 Max => new Vector2(X0 + r.xMax * Cell, Z0 + r.yMax * Cell);
        public Vector2 Center => (Min + Max) * 0.5f;
        public Vector2 Size => Max - Min;
        public int Area => r.width * r.height;
    }

    private struct DoorEdge
    {
        public int a, b;
        public bool vertical; // true: x = X0 + i*Cell çizgisinde (j hücresi), false: z = Z0 + j*Cell çizgisinde (i hücresi)
        public int i, j;
        public Vector2 Center => vertical ? new Vector2(X0 + i * Cell, Z0 + (j + 0.5f) * Cell) : new Vector2(X0 + (i + 0.5f) * Cell, Z0 + j * Cell);
    }

    private struct WallSlot
    {
        public Vector2 pos, normal;
        public int room;
    }

    private readonly List<PRoom> rooms = new List<PRoom>();
    private readonly List<DoorEdge> doors = new List<DoorEdge>();
    private int[,] grid;
    private bool[,] blocked;
    private readonly List<WallSlot> wallSlots = new List<WallSlot>();

    // ---------------- Durum ----------------
    private Transform museum, interior, exterior;
    private int appliedSeed = int.MinValue, appliedTheme = -1;
    private readonly List<Material> mats = new List<Material>();
    private readonly Dictionary<Color, Material> matCache = new Dictionary<Color, Material>();
    private MaterialPropertyBlock block;

    // Işık (ilk hali saklanır)
    private bool lightSaved;
    private Light sun;
    private Color sunColor;
    private float sunIntensity, ambientIntensity, fogDensity;
    private Quaternion sunRot;
    private bool fogOn;
    private Color fogColor;
    private FogMode fogMode;

    private void Awake() => Instance = this;

    /// <summary>Düzeni tohum + temaya göre kurar. Bir şey değiştiyse true (sunucu NavMesh'i yeniler).</summary>
    public static bool Apply(GameObject museumRoot, int seed, int theme)
    {
        if (museumRoot == null) return false;
        MuseumLayout l = museumRoot.GetComponent<MuseumLayout>();
        if (l == null) l = museumRoot.AddComponent<MuseumLayout>();
        Instance = l;
        return l.ApplyInternal(seed, theme);
    }

    private bool ApplyInternal(int seed, int theme)
    {
        if (seed == appliedSeed && theme == appliedTheme) return false;
        appliedSeed = seed;
        appliedTheme = theme;
        Version++;

        MuseumTheme th = ArtworkLook.Theme(theme);
        museum = transform.Find("Museum");

        ClearRuntime();
        interior = NewGroup(museum != null ? museum : transform, "Runtime_Interior");
        exterior = NewGroup(transform, "Runtime_Exterior");

        if (museum != null)
        {
            BuildInterior(new System.Random(seed ^ Salt), th);
            SecurityCamera.Retired = true; // eski sabit kameralar yerine odalardaki RoomCamera'lar
        }
        BuildExterior(new System.Random(seed ^ (Salt * 7)), th, theme);
        ApplyLighting(new System.Random(seed ^ (Salt * 13)), theme);

        Physics.SyncTransforms();
        return true;
    }

    // =====================================================================
    //  KAT PLANI
    // =====================================================================

    private void GeneratePlan(System.Random rng)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            rooms.Clear();
            doors.Clear();
            grid = new int[NX, NZ];

            // Lobi: girişin önünde (batı), 8–12 m genişlik, giriş satırlarını (7, 8) kapsar
            int lw = 4 + rng.Next(3);
            int lh = 4 + rng.Next(3);
            int lj0 = Mathf.Clamp(rng.Next(Mathf.Max(0, 9 - lh), Mathf.Min(7, NZ - lh) + 1), 0, NZ - lh);
            var lobby = new RectInt(0, lj0, lw, lh);

            // Batı şeridinin lobi dışında kalan parçaları (yeterince büyükse ayrı oda, değilse lobiye kat)
            if (lj0 > 0 && lj0 < 3) { lobby.yMin = 0; }
            if (NZ - lobby.yMax > 0 && NZ - lobby.yMax < 3) { lobby.yMax = NZ; }
            AddRoom(lobby, "lobby");
            if (lobby.yMin >= 3) AddRoom(new RectInt(0, 0, lw, lobby.yMin), "gallery");
            if (NZ - lobby.yMax >= 3) AddRoom(new RectInt(0, lobby.yMax, lw, NZ - lobby.yMax), "gallery");

            // Doğu: rastgele ikiye bölerek odalar
            Split(new RectInt(lw, 0, NX - lw, NZ), 0, rng);

            if (rooms.Count >= 6 && rooms.Count <= 10) break;
        }

        foreach (PRoom r in rooms)
            for (int x = r.r.xMin; x < r.r.xMax; x++)
                for (int z = r.r.yMin; z < r.r.yMax; z++)
                    grid[x, z] = r.id;

        // Komşuluklar ve kapılar: lobiden başlayan rastgele ağaç + birkaç ekstra kapı (kaçış döngüleri)
        var shared = new Dictionary<long, List<DoorEdge>>();
        for (int x = 1; x < NX; x++)
            for (int z = 0; z < NZ; z++)
                if (grid[x - 1, z] != grid[x, z]) AddShared(shared, new DoorEdge { a = grid[x - 1, z], b = grid[x, z], vertical = true, i = x, j = z });
        for (int x = 0; x < NX; x++)
            for (int z = 1; z < NZ; z++)
                if (grid[x, z - 1] != grid[x, z]) AddShared(shared, new DoorEdge { a = grid[x, z - 1], b = grid[x, z], vertical = false, i = x, j = z });

        var connected = new HashSet<int> { 0 };
        var pairs = new List<long>(shared.Keys);
        Shuffle(pairs, rng);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (long k in pairs)
            {
                List<DoorEdge> edges = shared[k];
                int a = edges[0].a, b = edges[0].b;
                if (connected.Contains(a) == connected.Contains(b)) continue;
                AddDoor(edges, rng);
                connected.Add(a);
                connected.Add(b);
                grew = true;
            }
        }

        // Özel odalar: tek kapılı (çıkmaz) odalardan biri Staff Only, biri güvenlik odası
        var deadEnds = new List<PRoom>();
        foreach (PRoom r in rooms) if (r.role == "gallery" && r.doors.Count == 1 && r.Area >= 6 && r.Area <= 48) deadEnds.Add(r);
        Shuffle(deadEnds, rng);
        if (deadEnds.Count == 0) foreach (PRoom r in rooms) if (r.role == "gallery" && r.Area <= 60) deadEnds.Add(r);
        if (deadEnds.Count > 0) deadEnds[0].role = "staff";
        if (deadEnds.Count > 1) deadEnds[1].role = "security";
        if (deadEnds.Count == 1)
        {
            // İkinci çıkmaz oda yoksa en küçük galeri güvenlik odası olur
            PRoom smallest = null;
            foreach (PRoom r in rooms) if (r.role == "gallery" && (smallest == null || r.Area < smallest.Area)) smallest = r;
            if (smallest != null) smallest.role = "security";
        }

        // Ekstra kapılar (özel odalar hariç): kovalamacada döngü yapılabilsin
        foreach (long k in pairs)
        {
            List<DoorEdge> edges = shared[k];
            PRoom ra = rooms[edges[0].a], rb = rooms[edges[0].b];
            if (HasDoorBetween(ra.id, rb.id)) continue;
            if (ra.role == "staff" || ra.role == "security" || rb.role == "staff" || rb.role == "security") continue;
            if (rng.NextDouble() < 0.4) AddDoor(edges, rng);
        }
    }

    private void AddRoom(RectInt r, string role)
    {
        rooms.Add(new PRoom { id = rooms.Count, r = r, role = role });
    }

    private void Split(RectInt r, int depth, System.Random rng)
    {
        int area = r.width * r.height;
        bool canV = r.width >= 8, canH = r.height >= 6;
        bool stop = depth >= 4 || (!canV && !canH) || (depth >= 2 && area <= 48) || (depth >= 2 && area < 80 && rng.NextDouble() < 0.3);
        if (stop) { AddRoom(r, "gallery"); return; }

        bool vertical;
        if (canV && !canH) vertical = true;
        else if (canH && !canV) vertical = false;
        else if (r.width > r.height * 1.3f) vertical = true;
        else if (r.height > r.width * 1.3f) vertical = false;
        else vertical = rng.Next(2) == 0;

        if (vertical)
        {
            int k = r.xMin + 4 + rng.Next(r.width - 7);
            Split(new RectInt(r.xMin, r.yMin, k - r.xMin, r.height), depth + 1, rng);
            Split(new RectInt(k, r.yMin, r.xMax - k, r.height), depth + 1, rng);
        }
        else
        {
            int k = r.yMin + 3 + rng.Next(r.height - 5);
            Split(new RectInt(r.xMin, r.yMin, r.width, k - r.yMin), depth + 1, rng);
            Split(new RectInt(r.xMin, k, r.width, r.yMax - k), depth + 1, rng);
        }
    }

    private static void AddShared(Dictionary<long, List<DoorEdge>> shared, DoorEdge e)
    {
        int lo = Mathf.Min(e.a, e.b), hi = Mathf.Max(e.a, e.b);
        long key = ((long)lo << 32) | (uint)hi;
        if (!shared.TryGetValue(key, out List<DoorEdge> list)) shared[key] = list = new List<DoorEdge>();
        list.Add(e);
    }

    private void AddDoor(List<DoorEdge> edges, System.Random rng)
    {
        // Ortak duvarın uçlarına değil ortasına yakın bir yere kapı
        int n = edges.Count;
        int pick = n >= 3 ? 1 + rng.Next(n - 2) : rng.Next(n);
        DoorEdge d = edges[pick];
        doors.Add(d);
        rooms[d.a].doors.Add(doors.Count - 1);
        rooms[d.b].doors.Add(doors.Count - 1);
    }

    private bool HasDoorBetween(int a, int b)
    {
        foreach (DoorEdge d in doors) if ((d.a == a && d.b == b) || (d.a == b && d.b == a)) return true;
        return false;
    }

    private bool IsDoorEdge(bool vertical, int i, int j)
    {
        foreach (DoorEdge d in doors) if (d.vertical == vertical && d.i == i && d.j == j) return true;
        // Ana giriş (batı duvarı, z -2..2)
        if (vertical && i == 0 && (j == 7 || j == 8)) return true;
        return false;
    }

    // =====================================================================
    //  İÇERİ
    // =====================================================================

    private Vector3 L(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);
    private Vector3 W(Vector2 p, float y = 0f) => museum.TransformPoint(new Vector3(p.x, y, p.y));

    private void BuildInterior(System.Random rng, MuseumTheme th)
    {
        GeneratePlan(rng);
        blocked = new bool[NX, NZ];

        // Sahnedeki eski sabit iç duvarları / bölmeyi / ipleri kapat (yerlerine plan kurulur)
        foreach (Transform child in museum)
        {
            string n = child.name;
            if (n.StartsWith("Divider_") || n == "Gallery_Partition" || n.StartsWith("StaffRope")) child.gameObject.SetActive(false);
            if (n == "Floor") { Renderer fr = child.GetComponent<Renderer>(); if (fr != null) fr.enabled = false; }
        }

        BuildWallsAndDoors(th);
        BuildFloorsCeilingsLights(th);
        CollectWallSlots();
        PlaceJail(rng);

        PRoom staff = rooms.Find(r => r.role == "staff");
        PRoom security = rooms.Find(r => r.role == "security");
        PRoom lobby = rooms[0];
        var galleries = rooms.FindAll(r => r.role == "gallery");
        galleries.Sort((a, b) => b.Area.CompareTo(a.Area));
        if (galleries.Count == 0) galleries.Add(lobby);

        // Oda görevleri: tablolar en büyük galeride, vazolar ikincide, yüzük hazinede, büst başyapıt salonunda
        PRoom paintRoom = galleries[0];
        PRoom vaseRoom = galleries[Mathf.Min(1, galleries.Count - 1)];
        PRoom ringRoom = galleries[Mathf.Min(2, galleries.Count - 1)];
        PRoom bustRoom = galleries[Mathf.Min(3, galleries.Count - 1)];

        // Eserler
        PlacePainting("Display_Painting1", paintRoom, galleries, rng, th);
        PlacePainting("Display_Painting2", paintRoom, galleries, rng, th);
        PlacePainting("Display_Painting3", galleries.Count > 4 ? galleries[4] : paintRoom, galleries, rng, th);
        PlacePainting("Display_Painting4", staff ?? paintRoom, galleries, rng, th);
        PlacePedestal("Display_Vase1", vaseRoom, rng, th);
        PlacePedestal("Display_Vase2", vaseRoom, rng, th);
        PlacePedestal("Display_Ring1", ringRoom, rng, th);
        PlacePedestal("Display_Bust1", bustRoom, rng, th);

        MoveLabel("Label_Paintings", paintRoom);
        MoveLabel("Label_Vases", vaseRoom);
        MoveLabel("Label_Treasury", ringRoom);
        MoveLabel("Label_Masterpiece", bustRoom);

        if (staff != null) SetupStaffRoom(staff, th);
        if (security != null) SetupSecurityRoom(security, th);

        PlaceHidingSpots(rng);
        PlaceLootTables(rng);

        // Dekor ve kameralar
        foreach (PRoom r in rooms)
        {
            if (r.role == "gallery" || r.role == "lobby") RoomDecor(r, rng, th, r.role == "lobby" ? 2 : 1 + rng.Next(3));
            if (r.role != "security") PlaceCamera(r, rng);
        }

        RouteGuards(rng, paintRoom, vaseRoom, ringRoom, bustRoom, staff);

        // Ziyaretçi noktaları (personel / güvenlik odaları hariç)
        VisitorPoints.Clear();
        foreach (PRoom r in rooms)
        {
            if (r.role == "staff" || r.role == "security") continue;
            VisitorPoints.Add(W(r.Center));
        }
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.name == "Display_Painting4") continue;
            Vector3 front = d.transform.position + d.transform.forward * (d.WallMounted ? 1.6f : 1.4f);
            VisitorPoints.Add(new Vector3(front.x, museum.position.y, front.z));
        }
        LobbyWorld = W(lobby.Center);
    }

    // ---------------- Duvarlar ve kapılar ----------------

    private void BuildWallsAndDoors(MuseumTheme th)
    {
        // Dikey duvarlar (x sabit)
        for (int x = 1; x < NX; x++)
        {
            int start = -1;
            for (int z = 0; z <= NZ; z++)
            {
                bool wall = z < NZ && grid[x - 1, z] != grid[x, z] && !IsDoorEdge(true, x, z);
                if (wall && start < 0) start = z;
                if (!wall && start >= 0)
                {
                    float a = Z0 + start * Cell, b = Z0 + z * Cell;
                    a += start > 0 && IsDoorEdge(true, x, start - 1) ? 0.2f : -0.15f;
                    b += z < NZ && IsDoorEdge(true, x, z) ? -0.2f : 0.15f;
                    WallBox(new Vector2(X0 + x * Cell, (a + b) * 0.5f), new Vector2(0.3f, b - a), th);
                    start = -1;
                }
            }
        }
        // Yatay duvarlar (z sabit)
        for (int z = 1; z < NZ; z++)
        {
            int start = -1;
            for (int x = 0; x <= NX; x++)
            {
                bool wall = x < NX && grid[x, z - 1] != grid[x, z] && !IsDoorEdge(false, x, z);
                if (wall && start < 0) start = x;
                if (!wall && start >= 0)
                {
                    float a = X0 + start * Cell, b = X0 + x * Cell;
                    a += start > 0 && IsDoorEdge(false, start - 1, z) ? 0.2f : -0.15f;
                    b += x < NX && IsDoorEdge(false, x, z) ? -0.2f : 0.15f;
                    WallBox(new Vector2((a + b) * 0.5f, Z0 + z * Cell), new Vector2(b - a, 0.3f), th);
                    start = -1;
                }
            }
        }

        // Kapı üstü lento ve pervazlar
        foreach (DoorEdge d in doors)
        {
            Vector2 c = d.Center;
            Vector2 along = d.vertical ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            Vector3 size = d.vertical ? new Vector3(0.3f, 1.2f, 2.4f) : new Vector3(2.4f, 1.2f, 0.3f);
            GameObject lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Divider_Lintel";
            lintel.transform.SetParent(interior, false);
            lintel.transform.localPosition = L(c, 3.4f);
            lintel.transform.localScale = size;
            lintel.GetComponent<Renderer>().sharedMaterial = Mat(th.wall);
            foreach (float s in new[] { -1.25f, 1.25f })
            {
                Vector2 p = c + along * s;
                Decor(interior, "DoorFrame", PrimitiveType.Cube, L(p, 1.4f), 0f, d.vertical ? new Vector3(0.36f, 2.8f, 0.12f) : new Vector3(0.12f, 2.8f, 0.36f), th.trim);
            }
        }
    }

    private void WallBox(Vector2 center, Vector2 size, MuseumTheme th)
    {
        if (size.x <= 0.05f || size.y <= 0.05f) return;
        GameObject w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        w.name = "Divider_R"; // MuseumSession duvar rengiyle boyar
        w.transform.SetParent(interior, false);
        w.transform.localPosition = L(center, WallH * 0.5f);
        w.transform.localScale = new Vector3(size.x, WallH, size.y);
        w.GetComponent<Renderer>().sharedMaterial = Mat(th.wall);
        // Duvar dibinde süpürgelik
        Decor(interior, "Baseboard", PrimitiveType.Cube, L(center, 0.08f), 0f, new Vector3(size.x + 0.04f, 0.16f, size.y + 0.04f), th.trim * 0.8f);
    }

    // ---------------- Zemin, tavan, çatı, ışık ----------------

    private void BuildFloorsCeilingsLights(MuseumTheme th)
    {
        Color ceiling = Color.Lerp(th.wall, Color.white, 0.55f);
        int lightIndex = 0;
        var sceneLights = new List<Light>();
        foreach (Light l in museum.GetComponentsInChildren<Light>(true))
            if (l.gameObject.name.StartsWith("MuseumLight")) sceneLights.Add(l);

        foreach (PRoom r in rooms)
        {
            Vector2 c = r.Center, s = r.Size;
            // Zemin (oda başına ayrı parça: her odanın ışığı kendi zeminini aydınlatsın)
            // (Staff / güvenlik odasının zemini tema rengiyle boyanmasın diye farklı adla)
            if (r.role == "staff") Decor(interior, "Zone_StaffFloor", PrimitiveType.Cube, L(c, 0.005f), 0f, new Vector3(s.x, 0.01f, s.y), new Color(0.55f, 0.2f, 0.2f));
            else if (r.role == "security") Decor(interior, "Zone_SecurityFloor", PrimitiveType.Cube, L(c, 0.005f), 0f, new Vector3(s.x, 0.01f, s.y), new Color(0.22f, 0.24f, 0.28f));
            else Decor(interior, "Floor_R" + r.id, PrimitiveType.Cube, L(c, 0.005f), 0f, new Vector3(s.x, 0.01f, s.y), th.floor);
            // Tavan (iç yüz) ve ortada lamba
            Decor(interior, "Ceiling_R" + r.id, PrimitiveType.Cube, L(c, WallH - 0.02f), 0f, new Vector3(s.x, 0.04f, s.y), ceiling);
            Decor(interior, "CeilingLamp", PrimitiveType.Cylinder, L(c, WallH - 0.08f), 0f, new Vector3(0.9f, 0.04f, 0.9f), new Color(1f, 0.95f, 0.8f));

            Light light;
            if (lightIndex < sceneLights.Count) light = sceneLights[lightIndex];
            else
            {
                GameObject lg = new GameObject("MuseumLight_R" + r.id);
                lg.transform.SetParent(interior, false);
                light = lg.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
            }
            lightIndex++;
            light.transform.localPosition = L(c, WallH - 0.4f);
            light.range = Mathf.Max(s.x, s.y) * 1.05f + 2f;
            light.intensity = r.role == "security" ? 1.6f : 2.6f;
            light.enabled = true;
        }
        for (int i = lightIndex; i < sceneLights.Count; i++) sceneLights[i].enabled = false;

        // Çatı: bütün müzenin üstünü kapatır (dışarıdan görünen), dışa taşan saçakla
        Transform roof = Decor(interior, "Roof_Museum", PrimitiveType.Cube, new Vector3(16f, WallH + 0.12f, -1f), 0f, new Vector3(45.2f, 0.24f, 31.2f), Color.Lerp(th.trim, Color.black, 0.45f));
        roof.gameObject.AddComponent<BoxCollider>();
        roof.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true; // güvenlikler çatıda yol aramasın
        Decor(interior, "Roof_Cornice", PrimitiveType.Cube, new Vector3(16f, WallH + 0.02f, -1f), 0f, new Vector3(45.6f, 0.12f, 31.6f), th.trim);
    }

    // ---------------- Duvar yuvaları (tablo, dolap) ----------------

    private void CollectWallSlots()
    {
        wallSlots.Clear();
        for (int x = 0; x < NX; x++)
            for (int z = 0; z < NZ; z++)
            {
                int id = grid[x, z];
                TrySlot(x, z, id, -1, 0, true, x, z);      // batı kenarı
                TrySlot(x, z, id, 1, 0, true, x + 1, z);   // doğu kenarı
                TrySlot(x, z, id, 0, -1, false, x, z);     // güney kenarı
                TrySlot(x, z, id, 0, 1, false, x, z + 1);  // kuzey kenarı
            }
    }

    private void TrySlot(int x, int z, int id, int dx, int dz, bool vertical, int ei, int ej)
    {
        int nx = x + dx, nz = z + dz;
        bool outside = nx < 0 || nz < 0 || nx >= NX || nz >= NZ;
        if (!outside && grid[nx, nz] == id) return;      // aynı oda: duvar yok
        if (IsDoorEdge(vertical, ei, ej)) return;
        // Kapının hemen yanındaki duvar parçalarını da boş bırak
        if (vertical && (IsDoorEdge(true, ei, ej - 1) || IsDoorEdge(true, ei, ej + 1))) return;
        if (!vertical && (IsDoorEdge(false, ei - 1, ej) || IsDoorEdge(false, ei + 1, ej))) return;
        // Girişin önü
        if (outside && dx == -1 && z >= 6 && z <= 9) return;

        Vector2 center = vertical ? new Vector2(X0 + ei * Cell, Z0 + (z + 0.5f) * Cell) : new Vector2(X0 + (x + 0.5f) * Cell, Z0 + ej * Cell);
        Vector2 normal = new Vector2(-dx, -dz); // odanın içine doğru
        float inset = outside ? 0.15f : 0.15f;
        wallSlots.Add(new WallSlot { pos = center + normal * inset, normal = normal, room = id });
    }

    private bool TakeWallSlot(PRoom room, System.Random rng, out WallSlot slot, float minSpacing = 2.5f)
    {
        var candidates = new List<int>();
        for (int i = 0; i < wallSlots.Count; i++)
        {
            if (wallSlots[i].room != room.id) continue;
            Vector2Int c = CellOf(wallSlots[i].pos + wallSlots[i].normal * 0.5f);
            if (blocked[c.x, c.y]) continue;
            candidates.Add(i);
        }
        Shuffle(candidates, rng);
        foreach (int i in candidates)
        {
            slot = wallSlots[i];
            wallSlots.RemoveAt(i);
            // Aynı duvardaki komşu yuvaları da kapat (eserler üst üste binmesin)
            for (int k = wallSlots.Count - 1; k >= 0; k--)
                if (wallSlots[k].room == slot.room && Vector2.Distance(wallSlots[k].pos, slot.pos) < minSpacing) wallSlots.RemoveAt(k);
            return true;
        }
        slot = default;
        return false;
    }

    private static Vector2Int CellOf(Vector2 p)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt((p.x - X0) / Cell), 0, NX - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((p.y - Z0) / Cell), 0, NZ - 1);
        return new Vector2Int(x, z);
    }

    // ---------------- Eserler ----------------

    /// <summary>Tablo: odanın duvarına asılır; üstünde pirinç tablo lambası, karşıdan onu aydınlatan spot.</summary>
    private void PlacePainting(string name, PRoom room, List<PRoom> fallback, System.Random rng, MuseumTheme th)
    {
        Transform t = museum.Find(name);
        if (t == null) return;
        ArtworkDisplay d = t.GetComponent<ArtworkDisplay>();

        bool ok = TakeWallSlot(room, rng, out WallSlot slot);
        for (int i = 0; !ok && i < fallback.Count; i++) ok = TakeWallSlot(fallback[i], rng, out slot);
        if (!ok) return;

        Vector3 normal = new Vector3(slot.normal.x, 0f, slot.normal.y);
        t.localPosition = L(slot.pos + slot.normal * 0.05f, 1.75f);
        t.localRotation = Quaternion.LookRotation(normal);
        t.localScale = new Vector3(1.25f, 1.0f, 0.06f); // arka pano (çerçevenin arkasında)
        if (d != null) d.WallMounted = true;

        Vector2Int c = CellOf(slot.pos + slot.normal * 0.5f);
        blocked[c.x, c.y] = true;

        // Tablo lambası (dekor) ve spot ışık
        Vector2 lampPos = slot.pos + slot.normal * 0.22f;
        Decor(interior, "PictureLampArm", PrimitiveType.Cube, L(slot.pos + slot.normal * 0.12f, 2.42f), 0f, new Vector3(slot.normal.y != 0 ? 0.04f : 0.24f, 0.04f, slot.normal.x != 0 ? 0.04f : 0.24f), new Color(0.75f, 0.6f, 0.3f));
        Transform lamp = Decor(interior, "PictureLamp", PrimitiveType.Cylinder, L(lampPos, 2.4f), 0f, new Vector3(0.12f, 0.3f, 0.12f), new Color(0.8f, 0.65f, 0.3f));
        lamp.localRotation = Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, 90f);

        GameObject sg = new GameObject("PaintingSpot");
        sg.transform.SetParent(interior, false);
        Vector3 spotPos = L(slot.pos + slot.normal * 1.6f, WallH - 0.35f);
        sg.transform.localPosition = spotPos;
        sg.transform.localRotation = Quaternion.LookRotation(L(slot.pos, 1.75f) - spotPos);
        Light spot = sg.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.spotAngle = 55f;
        spot.innerSpotAngle = 30f;
        spot.range = 5f;
        spot.intensity = 6f;
        spot.color = new Color(1f, 0.92f, 0.78f);
        spot.shadows = LightShadows.None;
    }

    /// <summary>Vazo / yüzük / büst: odanın içinde (kapı önlerinden ve duvar dibinden uzak) bir kaide + üstüne spot.</summary>
    private void PlacePedestal(string name, PRoom room, System.Random rng, MuseumTheme th)
    {
        Transform t = museum.Find(name);
        if (t == null) return;
        ArtworkDisplay d = t.GetComponent<ArtworkDisplay>();
        if (d != null) d.WallMounted = false;

        Vector2Int cell = PickFreeCell(room, rng, true);
        if (cell.x < 0) cell = PickFreeCell(room, rng, false);
        if (cell.x < 0) return;
        blocked[cell.x, cell.y] = true;
        Vector2 p = new Vector2(X0 + (cell.x + 0.5f) * Cell, Z0 + (cell.y + 0.5f) * Cell);

        Vector2 away = p - room.Center;
        float yaw = away.sqrMagnitude > 0.01f ? Mathf.Round(Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg / 45f) * 45f : 0f;
        t.localPosition = L(p, 0.5f);
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        t.localScale = Vector3.one;

        // Kaideyi çevreleyen ipler ve üstten spot
        Transform rope = NewGroup(interior, "Decor_Rope");
        rope.localPosition = L(p);
        rope.localRotation = Quaternion.Euler(0f, yaw, 0f);
        foreach (float x in new[] { -0.8f, 0.8f })
            Decor(rope, "Post", PrimitiveType.Cylinder, new Vector3(x, 0.45f, -1.05f), 0f, new Vector3(0.07f, 0.45f, 0.07f), th.trim);
        Decor(rope, "Rope", PrimitiveType.Cube, new Vector3(0f, 0.8f, -1.05f), 0f, new Vector3(1.6f, 0.04f, 0.04f), th.accent);

        GameObject sg = new GameObject("PedestalSpot");
        sg.transform.SetParent(interior, false);
        sg.transform.localPosition = L(p, WallH - 0.3f);
        sg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Light spot = sg.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.spotAngle = 40f;
        spot.range = 5f;
        spot.intensity = 5f;
        spot.color = new Color(1f, 0.93f, 0.8f);
        spot.shadows = LightShadows.None;
    }

    /// <summary>Odada boş hücre (kapı önü, hapis, başka eşya değil). interiorOnly: duvara bitişik olmayan.</summary>
    private Vector2Int PickFreeCell(PRoom room, System.Random rng, bool interiorOnly)
    {
        var cells = new List<Vector2Int>();
        for (int x = room.r.xMin; x < room.r.xMax; x++)
            for (int z = room.r.yMin; z < room.r.yMax; z++)
            {
                if (blocked[x, z] || IsDoorCell(x, z)) continue;
                bool edge = x == room.r.xMin || x == room.r.xMax - 1 || z == room.r.yMin || z == room.r.yMax - 1;
                if (interiorOnly && edge && room.r.width > 2 && room.r.height > 2) continue;
                cells.Add(new Vector2Int(x, z));
            }
        if (cells.Count == 0) return new Vector2Int(-1, -1);
        return cells[rng.Next(cells.Count)];
    }

    private bool IsDoorCell(int x, int z)
    {
        foreach (DoorEdge d in doors)
        {
            if (d.vertical && d.j == z && (d.i == x || d.i - 1 == x)) return true;
            if (!d.vertical && d.i == x && (d.j == z || d.j - 1 == z)) return true;
        }
        if (x <= 1 && (z == 7 || z == 8)) return true; // giriş
        return false;
    }

    private void MoveLabel(string name, PRoom room)
    {
        Transform l = transform.Find(name);
        if (l == null) return;
        l.position = W(room.Center, 3.3f);
    }

    // ---------------- Özel odalar ----------------

    private void SetupStaffRoom(PRoom room, MuseumTheme th)
    {
        Transform area = museum.Find("StaffOnlyArea");
        if (area != null)
        {
            area.localPosition = L(room.Center, 1.5f);
            area.localRotation = Quaternion.identity;
            BoxCollider box = area.GetComponent<BoxCollider>();
            if (box != null) { box.center = Vector3.zero; box.size = new Vector3(room.Size.x - 0.3f, 3f, room.Size.y - 0.3f); }
        }
        Transform staffFloor = museum.Find("StaffFloor");
        if (staffFloor != null) staffFloor.gameObject.SetActive(false); // oda zemini zaten kırmızı

        // Kapısına "girilmez" ipi ve uyarı tabelası (dekor)
        foreach (int di in room.doors) DoorRope(doors[di], room, th, new Color(0.75f, 0.1f, 0.12f));

        // Kilitli kapı: maymuncukla açılır (MuseumLayoutStaffDoor.cs, LockpickSystem)
        foreach (int di in room.doors) BuildStaffDoor(doors[di], room, th);
    }

    private void DoorRope(DoorEdge d, PRoom room, MuseumTheme th, Color c)
    {
        Vector2 along = d.vertical ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        Vector2 inward = room.Center - d.Center;
        inward = d.vertical ? new Vector2(Mathf.Sign(inward.x), 0f) : new Vector2(0f, Mathf.Sign(inward.y));
        Vector2 basePos = d.Center + inward * 0.6f;
        foreach (float s in new[] { -1f, 1f })
            Decor(interior, "Rope_Post", PrimitiveType.Cylinder, L(basePos + along * s, 0.45f), 0f, new Vector3(0.08f, 0.45f, 0.08f), th.trim);
        Decor(interior, "Rope_Line", PrimitiveType.Cube, L(basePos, 0.8f), 0f, d.vertical ? new Vector3(0.05f, 0.05f, 2f) : new Vector3(2f, 0.05f, 0.05f), c);
    }

    /// <summary>Güvenlik odası: monitörlü masa + kameraları kapatan konsol (sahnedeki eski bir kamera objesinin ağ kimliği kullanılır).
    /// Odaya girmek suç: içerde bir güvenliğe görünürsen alarm çalar.</summary>
    private void SetupSecurityRoom(PRoom room, MuseumTheme th)
    {
        // Alan: içerde görülmek = anında alarm
        GameObject areaGo = new GameObject("SecurityRoomArea");
        areaGo.transform.SetParent(interior, false);
        areaGo.transform.localPosition = L(room.Center, 1.5f);
        BoxCollider box = areaGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(room.Size.x - 0.3f, 3f, room.Size.y - 0.3f);
        RestrictedArea ra = areaGo.AddComponent<RestrictedArea>();
        ra.instantAlarm = true;
        areaGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        foreach (int di in room.doors) DoorRope(doors[di], room, th, new Color(0.9f, 0.75f, 0.15f));

        // Masa: kapının karşısındaki duvara
        Vector2 doorC = room.doors.Count > 0 ? doors[room.doors[0]].Center : room.Min;
        Vector2 far = room.Center + (room.Center - doorC).normalized * Mathf.Max(0.5f, Mathf.Min(room.Size.x, room.Size.y) * 0.5f - 1.1f);
        Vector2 toDoor = (doorC - far).normalized;
        float yaw = Mathf.Atan2(toDoor.x, toDoor.y) * Mathf.Rad2Deg; // masanın önü kapıya baksın
        Vector2Int deskCell = CellOf(far);
        blocked[deskCell.x, deskCell.y] = true;

        Transform desk = NewGroup(interior, "SecurityDesk");
        desk.localPosition = L(far);
        desk.localRotation = Quaternion.Euler(0f, yaw, 0f);
        BoxCollider dc = desk.gameObject.AddComponent<BoxCollider>();
        dc.center = new Vector3(0f, 0.45f, 0f);
        dc.size = new Vector3(2.4f, 0.9f, 0.9f);
        Decor(desk, "Top", PrimitiveType.Cube, new Vector3(0f, 0.85f, 0f), 0f, new Vector3(2.4f, 0.08f, 0.9f), new Color(0.3f, 0.3f, 0.33f));
        Decor(desk, "Body", PrimitiveType.Cube, new Vector3(0f, 0.42f, 0.1f), 0f, new Vector3(2.3f, 0.8f, 0.6f), new Color(0.2f, 0.2f, 0.23f));
        for (int i = 0; i < 3; i++)
        {
            float x = -0.8f + i * 0.8f;
            Decor(desk, "Monitor", PrimitiveType.Cube, new Vector3(x, 1.25f, -0.25f), 0f, new Vector3(0.7f, 0.45f, 0.06f), new Color(0.1f, 0.1f, 0.12f));
            Decor(desk, "Screen", PrimitiveType.Cube, new Vector3(x, 1.25f, -0.21f), 0f, new Vector3(0.62f, 0.37f, 0.02f), new Color(0.25f, 0.6f, 0.75f));
        }
        Decor(desk, "Chair", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.9f), 0f, new Vector3(0.55f, 0.1f, 0.55f), new Color(0.15f, 0.15f, 0.17f));

        // Konsol: sahnedeki eski kameralardan biri (ağ nesnesi) buraya taşınır ve "kameraları kapat" düğmesi olur
        SecurityCamera console = SecurityCamera.PickConsole();
        if (console != null)
        {
            console.transform.localPosition = L(far, 0f) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.95f, 0.15f);
            console.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            console.SetupAsConsole();
            // Kırmızı büyük düğme (dekor; tıklanan konsolun çarpıştırıcısı)
            Decor(desk, "ButtonBase", PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0.15f), 0f, new Vector3(0.3f, 0.03f, 0.3f), new Color(0.2f, 0.2f, 0.2f));
            Decor(desk, "Button", PrimitiveType.Cylinder, new Vector3(0f, 0.95f, 0.15f), 0f, new Vector3(0.2f, 0.05f, 0.2f), new Color(0.9f, 0.12f, 0.1f));
        }

        // Duvarda uyarı levhası
        if (TakeWallSlot(room, new System.Random(room.id), out WallSlot ws))
            Decor(interior, "SecuritySign", PrimitiveType.Cube, L(ws.pos + ws.normal * 0.03f, 2f), Mathf.Atan2(ws.normal.x, ws.normal.y) * Mathf.Rad2Deg, new Vector3(1f, 0.5f, 0.03f), new Color(0.95f, 0.8f, 0.15f));
    }

    // ---------------- Hapis ----------------

    /// <summary>Hapis hücresini (sahnedeki "Jail" grubu) müzenin bir köşesine taşır. Hücrenin iki duvarı dış duvarlardır.</summary>
    private void PlaceJail(System.Random rng)
    {
        Transform jail = museum.Find("Jail");
        if (jail == null) return;

        // Köşe → (köşe noktası, dönüş, kaplanan hücreler)
        var options = new List<(Vector2 corner, float yaw, RectInt cells)>
        {
            (new Vector2(38f, -16f), 0f, new RectInt(NX - 2, 0, 2, 3)),
            (new Vector2(38f, 14f), -90f, new RectInt(NX - 3, NZ - 2, 3, 2)),
            (new Vector2(-6f, 14f), 180f, new RectInt(0, NZ - 3, 2, 3)),
            (new Vector2(-6f, -16f), 90f, new RectInt(0, 0, 3, 2)),
        };
        Shuffle(options, rng);
        foreach (var o in options)
        {
            int id = grid[o.cells.xMin, o.cells.yMin];
            PRoom room = rooms[id];
            if (room.role != "gallery" || room.r.width < 3 || room.r.height < 3) continue;
            bool ok = true;
            for (int x = o.cells.xMin; x < o.cells.xMax && ok; x++)
                for (int z = o.cells.yMin; z < o.cells.yMax && ok; z++)
                    if (grid[x, z] != id || IsDoorCell(x, z)) ok = false;
            if (!ok) continue;

            Quaternion rot = Quaternion.Euler(0f, o.yaw, 0f);
            Vector3 se = new Vector3(38f, 0f, -16f);
            jail.localRotation = rot;
            jail.localPosition = new Vector3(o.corner.x, 0f, o.corner.y) - rot * se;
            for (int x = o.cells.xMin; x < o.cells.xMax; x++)
                for (int z = o.cells.yMin; z < o.cells.yMax; z++)
                    blocked[x, z] = true;
            // Hücre önündeki kurtarma alanı da boş kalsın
            Vector3 front = jail.localPosition + rot * new Vector3(35.6f, 0f, -10.9f);
            Vector2Int fc = CellOf(new Vector2(front.x, front.z));
            blocked[fc.x, fc.y] = true;
            // Duvar yuvaları: hücrenin içine düşenleri sil
            wallSlots.RemoveAll(s => { Vector2Int c = CellOf(s.pos + s.normal * 0.5f); return blocked[c.x, c.y]; });
            return;
        }
        // Uygun köşe yoksa olduğu yerde kalır (güneydoğu)
    }

    // ---------------- Dolaplar, ganimet masaları ----------------

    private void PlaceHidingSpots(System.Random rng)
    {
        var spots = new List<Transform>();
        foreach (Transform child in museum)
            if (child.name.StartsWith("Locker_") || child.name.StartsWith("Curtain_")) spots.Add(child);

        var candidateRooms = rooms.FindAll(r => r.role != "security");
        Shuffle(candidateRooms, rng);
        int k = 0;
        foreach (Transform s in spots)
        {
            WallSlot slot = default;
            bool ok = false;
            for (int tries = 0; tries < candidateRooms.Count && !ok; tries++)
            {
                PRoom r = candidateRooms[(k + tries) % candidateRooms.Count];
                ok = TakeWallSlot(r, rng, out slot, 2f);
            }
            k++;
            if (!ok) continue;
            float depth = s.localScale.z;
            s.localPosition = L(slot.pos + slot.normal * (depth * 0.5f + 0.02f), s.localPosition.y);
            s.localRotation = Quaternion.LookRotation(new Vector3(slot.normal.x, 0f, slot.normal.y));
            Vector2Int c = CellOf(slot.pos + slot.normal * 0.5f);
            blocked[c.x, c.y] = true;
        }
    }

    /// <summary>Ganimet masaları ve üstlerindeki eşyalar (eşyalar ağ nesnesi: onları sadece sunucu taşır, NetworkTransform herkese iletir).</summary>
    private void PlaceLootTables(System.Random rng)
    {
        bool server = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        var tables = new List<Transform>();
        foreach (Transform child in museum) if (child.name.StartsWith("LootTable_")) tables.Add(child);

        var publicRooms = rooms.FindAll(r => r.role == "gallery" || r.role == "lobby");
        WorldItem[] items = server ? FindObjectsByType<WorldItem>(FindObjectsSortMode.None) : new WorldItem[0];

        int k = 0;
        foreach (Transform t in tables)
        {
            PRoom room = publicRooms[(k++ + rng.Next(publicRooms.Count)) % publicRooms.Count];
            Vector2Int cell = PickFreeCell(room, rng, false);
            if (cell.x < 0) continue;
            blocked[cell.x, cell.y] = true;
            Vector3 oldPos = t.position;
            Vector2 p = new Vector2(X0 + (cell.x + 0.5f) * Cell, Z0 + (cell.y + 0.5f) * Cell);
            t.localPosition = L(p, t.localPosition.y);
            t.localRotation = Quaternion.Euler(0f, rng.Next(2) * 90f, 0f);
            Vector3 delta = t.position - oldPos;

            if (!server) continue;
            foreach (WorldItem wi in items)
            {
                if (wi == null) continue;
                Vector3 rel = wi.transform.position - oldPos;
                if (new Vector2(rel.x, rel.z).magnitude > 1.4f || rel.y < -0.2f || rel.y > 1.5f) continue;
                Vector3 local = Quaternion.Inverse(Quaternion.identity) * rel;
                Vector3 np = t.position + t.rotation * new Vector3(local.x, 0f, local.z) + Vector3.up * (rel.y + 0.05f);
                Rigidbody rb = wi.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = np; }
                wi.transform.position = np;
            }
        }
    }

    // ---------------- Dekor ----------------

    private void RoomDecor(PRoom room, System.Random rng, MuseumTheme th, int count)
    {
        if (rng.NextDouble() < 0.75 && room.r.width >= 3 && room.r.height >= 3)
        {
            Vector2 s = room.Size;
            Decor(interior, "Decor_Rug", PrimitiveType.Cube, L(room.Center, 0.012f), 0f,
                  new Vector3(Mathf.Max(2f, s.x - 3.2f), 0.012f, Mathf.Max(2f, s.y - 3.2f)), Color.Lerp(th.accent, th.floor, 0.3f));
        }
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = PickFreeCell(room, rng, i == 0);
            if (cell.x < 0) return;
            blocked[cell.x, cell.y] = true;
            Vector2 p = new Vector2(X0 + (cell.x + 0.5f) * Cell, Z0 + (cell.y + 0.5f) * Cell);
            int kind = rng.Next(3);
            if (kind == 0) Bench(p, room.r.width > room.r.height ? 0f : 90f, th);
            else if (kind == 1) Plant(p, rng);
            else Statue(p, th, rng);
        }
    }

    private void Bench(Vector2 p, float yaw, MuseumTheme th)
    {
        Transform b = NewGroup(interior, "Decor_Bench");
        b.localPosition = L(p);
        b.localRotation = Quaternion.Euler(0f, yaw, 0f);
        BoxCollider col = b.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.25f, 0f);
        col.size = new Vector3(1.7f, 0.5f, 0.55f);
        Color wood = new Color(0.42f, 0.27f, 0.16f);
        Decor(b, "Seat", PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f), 0f, new Vector3(1.7f, 0.08f, 0.55f), wood);
        Decor(b, "LegA", PrimitiveType.Cube, new Vector3(-0.7f, 0.2f, 0f), 0f, new Vector3(0.08f, 0.4f, 0.45f), th.trim * 0.7f);
        Decor(b, "LegB", PrimitiveType.Cube, new Vector3(0.7f, 0.2f, 0f), 0f, new Vector3(0.08f, 0.4f, 0.45f), th.trim * 0.7f);
    }

    private void Plant(Vector2 p, System.Random rng)
    {
        Transform t = NewGroup(interior, "Decor_Plant");
        t.localPosition = L(p);
        CapsuleCollider col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.6f, 0f);
        col.radius = 0.3f;
        col.height = 1.2f;
        Decor(t, "Pot", PrimitiveType.Cylinder, new Vector3(0f, 0.25f, 0f), 0f, new Vector3(0.5f, 0.25f, 0.5f), new Color(0.7f, 0.38f, 0.25f));
        float h = Range(rng, 0.7f, 1.1f);
        Decor(t, "Leaves", PrimitiveType.Sphere, new Vector3(0f, 0.5f + h * 0.5f, 0f), 0f, new Vector3(0.75f, h, 0.75f), new Color(0.25f, 0.52f, 0.28f));
    }

    private void Statue(Vector2 p, MuseumTheme th, System.Random rng)
    {
        Transform t = NewGroup(interior, "Decor_Statue");
        t.localPosition = L(p);
        BoxCollider col = t.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.8f, 0f);
        col.size = new Vector3(0.7f, 1.6f, 0.7f);
        Color stone = new Color(0.86f, 0.85f, 0.82f);
        Decor(t, "Plinth", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), 0f, new Vector3(0.7f, 0.9f, 0.7f), th.trim);
        if (rng.Next(2) == 0) Decor(t, "Sphere", PrimitiveType.Sphere, new Vector3(0f, 1.2f, 0f), 0f, Vector3.one * 0.6f, stone);
        else Decor(t, "Obelisk", PrimitiveType.Cube, new Vector3(0f, 1.3f, 0f), 45f, new Vector3(0.3f, 0.8f, 0.3f), stone);
    }

    // ---------------- Kameralar ----------------

    private void PlaceCamera(PRoom room, System.Random rng)
    {
        var corners = new List<Vector2>
        {
            new Vector2(room.Min.x + 0.45f, room.Min.y + 0.45f), new Vector2(room.Max.x - 0.45f, room.Min.y + 0.45f),
            new Vector2(room.Min.x + 0.45f, room.Max.y - 0.45f), new Vector2(room.Max.x - 0.45f, room.Max.y - 0.45f),
        };
        corners.RemoveAll(c => blocked[CellOf(c).x, CellOf(c).y]);
        if (corners.Count == 0) return;
        Vector2 cp = corners[rng.Next(corners.Count)];
        Vector2 to = room.Center - cp;
        float yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
        float range = Mathf.Clamp(room.Size.magnitude * 0.75f, 7f, 14f);
        RoomCamera.Create(interior, "RoomCamera_" + room.id, L(cp, WallH - 0.75f), yaw, 1, range, new Color(0.18f, 0.18f, 0.2f));
    }

    // ---------------- Devriye ----------------

    private void RouteGuards(System.Random rng, PRoom paint, PRoom vase, PRoom ring, PRoom bust, PRoom staff)
    {
        PRoom[] primary = { rooms[0], paint, vase, ring, bust };
        for (int g = 0; g < primary.Length; g++)
        {
            Transform guard = museum.Find("Guard_" + (g + 1));
            Transform wpRoot = museum.Find("Guard_" + (g + 1) + "_Waypoints");
            if (guard == null || wpRoot == null) continue;

            // İki oda: ana oda + kapıyla bağlı bir komşu (Staff Only'e bakan güvenlik varsa onu da dolaşır)
            PRoom a = primary[g];
            PRoom b = null;
            var neigh = new List<PRoom>();
            foreach (int di in a.doors)
            {
                DoorEdge d = doors[di];
                PRoom o = rooms[d.a == a.id ? d.b : d.a];
                if (o.role != "security") neigh.Add(o);
            }
            if (staff != null && neigh.Contains(staff) && g == 1) b = staff;
            else if (neigh.Count > 0) b = neigh[rng.Next(neigh.Count)];

            var pts = new List<Vector2>();
            AddRoomLoop(a, pts, rng);
            if (b != null)
            {
                Vector2 door = DoorBetween(a, b);
                pts.Add(door);
                AddRoomLoop(b, pts, rng);
                pts.Add(door);
            }

            // Gereken kadar devriye noktası (eksikse yeni boş obje)
            while (wpRoot.childCount < pts.Count)
            {
                GameObject wp = new GameObject("WP" + (wpRoot.childCount + 1));
                wp.transform.SetParent(wpRoot, false);
            }
            var wps = new Transform[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Transform w = wpRoot.GetChild(i);
                w.localPosition = L(pts[i]);
                wps[i] = w;
            }
            GuardAI ai = guard.GetComponent<GuardAI>();
            if (ai != null) ai.SetWaypoints(wps);
        }
    }

    private void AddRoomLoop(PRoom r, List<Vector2> pts, System.Random rng)
    {
        float ix = Mathf.Min(1.6f, r.Size.x * 0.3f), iz = Mathf.Min(1.6f, r.Size.y * 0.3f);
        var c = new List<Vector2>
        {
            new Vector2(r.Min.x + ix, r.Min.y + iz), new Vector2(r.Max.x - ix, r.Min.y + iz),
            new Vector2(r.Max.x - ix, r.Max.y - iz), new Vector2(r.Min.x + ix, r.Max.y - iz),
        };
        int start = rng.Next(4);
        bool rev = rng.Next(2) == 0;
        for (int i = 0; i < 4; i++) pts.Add(c[rev ? (start - i + 8) % 4 : (start + i) % 4]);
    }

    private Vector2 DoorBetween(PRoom a, PRoom b)
    {
        foreach (int di in a.doors)
        {
            DoorEdge d = doors[di];
            if ((d.a == a.id && d.b == b.id) || (d.b == a.id && d.a == b.id)) return d.Center;
        }
        return (a.Center + b.Center) * 0.5f;
    }

    // =====================================================================
    //  Yardımcılar
    // =====================================================================

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    private static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private static bool TooClose(Vector2 p, List<Vector2> others, float min)
    {
        foreach (Vector2 o in others) if ((o - p).sqrMagnitude < min * min) return true;
        return false;
    }

    private void ClearRuntime()
    {
        if (interior != null) DestroyImmediate(interior.gameObject);
        if (exterior != null) DestroyImmediate(exterior.gameObject);
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t != null && (t.name == "Runtime_Interior" || t.name == "Runtime_Exterior")) DestroyImmediate(t.gameObject);
        }
        interior = exterior = null;
        foreach (Material m in mats) if (m != null) Destroy(m);
        mats.Clear();
        matCache.Clear();
    }

    private static Transform NewGroup(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private Material Mat(Color c)
    {
        c.a = 1f;
        if (matCache.TryGetValue(c, out Material m) && m != null) return m;
        m = ArtworkLook.MakeLit(c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
        mats.Add(m);
        matCache[c] = m;
        return m;
    }

    /// <summary>Çarpıştırıcısız süs parçası.</summary>
    private Transform Decor(Transform parent, string name, PrimitiveType type, Vector3 localPos, float yaw, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        return go.transform;
    }

    private void OnDestroy()
    {
        foreach (Material m in mats) if (m != null) Destroy(m);
        if (Instance == this) Instance = null;
    }
}
