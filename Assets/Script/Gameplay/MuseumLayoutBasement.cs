using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MuseumLayout'un parçası: müzenin ALTINDAKİ BODRUM KAT (yalnızca hapishane alanı).
///  - Tohumdan deterministik: sunucu ve istemciler aynı bodrumu üretir (ağdan veri gitmez).
///  - Bir "personel merdiveni" odası (Staff Only benzeri, kilitsiz) seçilir; zeminde dikdörtgen bir boşluk açılır ve içine
///    çarpıştırıcılı bir RAMPA konur (oyuncu + NavMesh yürüyebilir; RuntimeNavMeshBaker çocuk çarpıştırıcıları topladığı için dahil).
///  - Rampanın altında bir hapishane salonu: sahnedeki "Jail" grubu (JailCell) salonun en uzak köşesine taşınır.
///  - Salona bir güvenlik devriye noktası dizisi eklenir (RouteGuards, son devriyeci güvenliğe).
/// Bodrum kurulamazsa (uygun oda yok) hapis eski köşe yerleşimine (PlaceJail) döner.
/// </summary>
public partial class MuseumLayout
{
    private const float BasementDepth = 3f;     // bodrum zemini, müze zemininin kaç metre altında
    private const float StairsWidth = 1.8f;
    private const float HallAlong = 13f, HallAcross = 10f;

    private struct StairsCfg
    {
        public Vector2 A, B, dir;   // A: üst uç (merkez), B: alt uç (merkez), dir: iniş yönü (birim, eksen hizalı)
        public float run;
        public Rect hole;           // yerel XZ (x = x, y = z)
        public float hallAlong;
        public float score;
    }

    private PRoom stairsRoom;
    private StairsCfg stairsCfg;

    /// <summary>Bodrum (hapishane) kurulu mu?</summary>
    public bool HasBasement { get; private set; }
    /// <summary>Merdiven üstü (müze zemininde, odanın içinde) dünya konumu.</summary>
    public Vector3 StairsTopWorld { get; private set; }
    /// <summary>Merdiven alt ucu (bodrum zemini) dünya konumu.</summary>
    public Vector3 StairsBottomWorld { get; private set; }
    /// <summary>Merdiven boşluğunun dünya XZ dikdörtgeni.</summary>
    public Rect StairsHoleWorld { get; private set; }
    /// <summary>Bodrum salonunun dünya XZ dikdörtgeni.</summary>
    public Rect BasementRect { get; private set; }
    /// <summary>Bodrum zemininin dünya yüksekliği.</summary>
    public float BasementFloorY { get; private set; }
    /// <summary>Merdiven odasının plan numarası (-1: yok).</summary>
    public int StairsRoomId => stairsRoom != null ? stairsRoom.id : -1;

    public int AppliedSeed => appliedSeed;
    public int AppliedTheme => appliedTheme;

    /// <summary>Bu dünya noktası bodrum katta mı? (Yükseklik müze zemininin belirgin altında.)</summary>
    public bool IsInBasement(Vector3 world) => HasBasement && world.y < PlanFloorY - 1.5f;

    // ---------------- Oda seçimi (GeneratePlan sonunda) ----------------

    private void ChooseStairsRoom(System.Random rng)
    {
        stairsRoom = null;
        var best = new List<(PRoom room, StairsCfg cfg)>();
        float bestScore = -1f;
        foreach (PRoom r in rooms)
        {
            if (r.role != "gallery" || r.Area < 6 || r.Area > 60) continue;
            for (int ds = -1; ds <= 1; ds += 2)
                for (int ss = -1; ss <= 1; ss += 2)
                {
                    if (!TryStairsConfig(r, ds, ss, out StairsCfg c)) continue;
                    // Tek kapılı (çıkmaz) oda tercih edilir; kapıya uzaklık da puan getirir
                    c.score += r.doors.Count == 1 ? 100f : 0f;
                    if (c.score > bestScore + 0.01f) { bestScore = c.score; best.Clear(); }
                    if (Mathf.Abs(c.score - bestScore) <= 0.01f) best.Add((r, c));
                }
        }
        if (best.Count == 0) return;
        var pick = best[rng.Next(best.Count)];
        stairsRoom = pick.room;
        stairsCfg = pick.cfg;
        stairsRoom.role = "stairs";
    }

    private bool TryStairsConfig(PRoom r, int dirSign, int sideSign, out StairsCfg cfg)
    {
        cfg = default;
        Vector2 min = r.Min, max = r.Max, size = r.Size;
        bool alongX = size.x >= size.y;
        float longSize = alongX ? size.x : size.y;
        float shortSize = alongX ? size.y : size.x;
        if (longSize < 5.9f || shortSize < 3.9f) return false;

        float run = Mathf.Min(5.2f, longSize - 0.55f - 1.2f); // duvara 0.55, üst uçta 1.2 m boşluk
        if (run < 4.2f) return false;

        float wallC = alongX ? (dirSign > 0 ? max.x : min.x) : (dirSign > 0 ? max.y : min.y);
        float sB = wallC - dirSign * 0.55f;
        float sA = sB - dirSign * run;
        float perpC = alongX
            ? (sideSign > 0 ? max.y - 0.5f - StairsWidth * 0.5f : min.y + 0.5f + StairsWidth * 0.5f)
            : (sideSign > 0 ? max.x - 0.5f - StairsWidth * 0.5f : min.x + 0.5f + StairsWidth * 0.5f);

        Vector2 A = alongX ? new Vector2(sA, perpC) : new Vector2(perpC, sA);
        Vector2 B = alongX ? new Vector2(sB, perpC) : new Vector2(perpC, sB);
        Vector2 dir = alongX ? new Vector2(dirSign, 0f) : new Vector2(0f, dirSign);

        // Salon, B düzleminden iniş yönünde uzar; müze sınırına sığmalı
        float limit = alongX
            ? (dirSign > 0 ? X0 + NX * Cell - 0.4f - B.x : B.x - (X0 + 0.4f))
            : (dirSign > 0 ? Z0 + NZ * Cell - 0.4f - B.y : B.y - (Z0 + 0.4f));
        float hallAlong = Mathf.Min(HallAlong, limit);
        if (hallAlong < 9f) return false;
        float acrossMin = alongX ? Z0 + 0.4f + StairsWidth : X0 + 0.4f + StairsWidth;
        float acrossMax = alongX ? Z0 + NZ * Cell - 0.4f - StairsWidth : X0 + NX * Cell - 0.4f - StairsWidth;
        float pc = alongX ? B.y : B.x;
        if (pc < acrossMin || pc > acrossMax) return false;

        Rect hole = alongX
            ? new Rect(Mathf.Min(sA, sB), perpC - StairsWidth * 0.5f, run, StairsWidth)
            : new Rect(perpC - StairsWidth * 0.5f, Mathf.Min(sA, sB), StairsWidth, run);

        // Kapıların boşluğa yakınlığı: kapı boşluğun içine / hemen önüne düşmemeli
        float minDoor = 99f;
        Rect inflated = new Rect(hole.x - 1.2f, hole.y - 1.2f, hole.width + 2.4f, hole.height + 2.4f);
        foreach (int di in r.doors)
        {
            Vector2 dc = doors[di].Center;
            if (inflated.Contains(dc)) return false;
            minDoor = Mathf.Min(minDoor, Vector2.Distance(dc, A));
        }
        cfg = new StairsCfg { A = A, B = B, dir = dir, run = run, hole = hole, hallAlong = hallAlong, score = Mathf.Min(minDoor, 30f) };
        return true;
    }

    // ---------------- Kurulum (BuildInterior içinde, zemin/tavan kurulduktan sonra) ----------------

    private Transform SolidBox(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Color color, bool collider = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!collider) DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        return go.transform;
    }

    /// <summary>XZ aralığı (yerel) → kutu (yüksekliği y0..y1).</summary>
    private void RectBox(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1, Color color, bool collider = true)
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f || y1 - y0 < 0.001f) return;
        SolidBox(parent, name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), Quaternion.identity,
                 new Vector3(x1 - x0, y1 - y0, z1 - z0), color, collider);
    }

    /// <summary>true: bodrum kuruldu (hapis oraya taşındı).</summary>
    private bool BuildBasement(System.Random rng, MuseumTheme th)
    {
        HasBasement = false;
        Transform jail = museum.Find("Jail");
        if (stairsRoom == null || jail == null) return false;

        StairsCfg c = stairsCfg;
        float D = BasementDepth;
        Color concrete = new Color(0.42f, 0.42f, 0.45f);
        Color dark = new Color(0.22f, 0.22f, 0.25f);
        Color hazard = new Color(0.95f, 0.78f, 0.12f);

        // --- Müze zemini: sahnedeki Floor çarpıştırıcısı kapanır, yerine boşluklu döşeme parçaları ---
        Transform sceneFloor = museum.Find("Floor");
        if (sceneFloor != null)
        {
            Collider fc = sceneFloor.GetComponent<Collider>();
            if (fc != null) fc.enabled = false;
        }
        Rect hole = c.hole;
        float fx0 = X0, fx1 = X0 + NX * Cell, fz0 = Z0, fz1 = Z0 + NZ * Cell;
        Transform slabs = NewGroup(interior, "Base_Slabs");
        Color slabColor = Color.Lerp(th.floor, Color.black, 0.35f);
        RectBox(slabs, "Base_Slab", fx0, hole.xMin, fz0, fz1, -0.2f, 0f, slabColor);
        RectBox(slabs, "Base_Slab", hole.xMax, fx1, fz0, fz1, -0.2f, 0f, slabColor);
        RectBox(slabs, "Base_Slab", hole.xMin, hole.xMax, fz0, hole.yMin, -0.2f, 0f, slabColor);
        RectBox(slabs, "Base_Slab", hole.xMin, hole.xMax, hole.yMax, fz1, -0.2f, 0f, slabColor);

        // Merdiven odasının görsel zemini de boşluğu kapatmasın
        Transform roomFloor = interior.Find("Floor_R" + stairsRoom.id);
        if (roomFloor != null) DestroyImmediate(roomFloor.gameObject);
        Vector2 rmin = stairsRoom.Min, rmax = stairsRoom.Max;
        FloorPiece(rmin.x, hole.xMin, rmin.y, rmax.y, th.floor);
        FloorPiece(hole.xMax, rmax.x, rmin.y, rmax.y, th.floor);
        FloorPiece(hole.xMin, hole.xMax, rmin.y, hole.yMin, th.floor);
        FloorPiece(hole.xMin, hole.xMax, hole.yMax, rmax.y, th.floor);

        // --- Rampa (çarpıştırıcı + görsel) ---
        Transform stairs = NewGroup(interior, "Base_Stairs");
        Vector3 a3 = new Vector3(c.A.x, 0f, c.A.y), b3 = new Vector3(c.B.x, -D, c.B.y);
        Vector3 fwd = (b3 - a3).normalized;
        Quaternion rampRot = Quaternion.LookRotation(fwd, Vector3.up);
        float rampLen = (b3 - a3).magnitude;
        const float rampThick = 0.3f;
        // Üst ucu zeminin üstüne taşmasın: yalnızca alt uca 0.2 m uzatılır
        Vector3 rampCenter = (a3 + b3) * 0.5f + fwd * 0.1f - (rampRot * Vector3.up) * (rampThick * 0.5f);
        SolidBox(stairs, "Base_Ramp", rampCenter, rampRot, new Vector3(StairsWidth, rampThick, rampLen + 0.2f), concrete);
        // Basamak çizgileri (yalnızca görsel)
        int steps = Mathf.RoundToInt(rampLen / 0.34f);
        for (int i = 1; i < steps; i++)
        {
            Vector3 p = Vector3.Lerp(a3, b3, i / (float)steps) + (rampRot * Vector3.up) * 0.012f;
            SolidBox(stairs, "Base_Step", p, rampRot, new Vector3(StairsWidth - 0.05f, 0.012f, 0.07f), dark, false);
        }
        // Yan duvarlar (boşluğun kenarları, üstte 1 m korkuluk) — yalnızca A..B boyunca
        Vector2 perp = new Vector2(-c.dir.y, c.dir.x);
        for (int s = -1; s <= 1; s += 2)
        {
            Vector2 mid = (c.A + c.B) * 0.5f + perp * (s * (StairsWidth * 0.5f + 0.075f));
            bool alongX = Mathf.Abs(c.dir.x) > 0.5f;
            Vector3 sz = alongX ? new Vector3(c.run + 0.3f, D + 1f, 0.15f) : new Vector3(0.15f, D + 1f, c.run + 0.3f);
            SolidBox(stairs, "Base_StairWall", new Vector3(mid.x, (1f - D) * 0.5f, mid.y), Quaternion.identity, sz, concrete);
            Vector3 rs = alongX ? new Vector3(c.run, 0.06f, 0.2f) : new Vector3(0.2f, 0.06f, c.run);
            SolidBox(stairs, "Base_Rail", new Vector3(mid.x, 1f, mid.y), Quaternion.identity, rs, hazard, false);
        }
        // Üst uçta sarı-siyah uyarı şeridi
        {
            bool alongX = Mathf.Abs(c.dir.x) > 0.5f;
            Vector2 top = c.A - c.dir * 0.12f;
            Vector3 sz = alongX ? new Vector3(0.2f, 0.015f, StairsWidth + 0.3f) : new Vector3(StairsWidth + 0.3f, 0.015f, 0.2f);
            SolidBox(stairs, "Base_Hazard", new Vector3(top.x, 0.012f, top.y), Quaternion.identity, sz, hazard, false);
        }

        // --- Salon ---
        bool dirX = Mathf.Abs(c.dir.x) > 0.5f;
        float along = c.hallAlong;
        Vector2 hMin, hMax;
        if (dirX)
        {
            float x0 = c.dir.x > 0f ? c.B.x : c.B.x - along;
            float z0 = Mathf.Clamp(c.B.y - HallAcross * 0.5f, Z0 + 0.4f, Z0 + NZ * Cell - 0.4f - HallAcross);
            hMin = new Vector2(x0, z0); hMax = new Vector2(x0 + along, z0 + HallAcross);
        }
        else
        {
            float z0 = c.dir.y > 0f ? c.B.y : c.B.y - along;
            float x0 = Mathf.Clamp(c.B.x - HallAcross * 0.5f, X0 + 0.4f, X0 + NX * Cell - 0.4f - HallAcross);
            hMin = new Vector2(x0, z0); hMax = new Vector2(x0 + HallAcross, z0 + along);
        }
        Transform hall = NewGroup(interior, "Base_Hall");
        RectBox(hall, "Base_HallFloor", hMin.x, hMax.x, hMin.y, hMax.y, -D - 0.2f, -D, dark);
        float wallTop = -0.2f, wallBot = -D - 0.2f;
        const float wt = 0.3f;
        // Dış duvarlar (salonun kenarları); B tarafındaki duvarda rampa girişi boşluğu
        if (dirX || c.dir.y > 0f) RectBox(hall, "Base_HallWall", hMin.x - wt, hMax.x + wt, hMax.y, hMax.y + wt, wallBot, wallTop, concrete);
        if (dirX || c.dir.y < 0f) RectBox(hall, "Base_HallWall", hMin.x - wt, hMax.x + wt, hMin.y - wt, hMin.y, wallBot, wallTop, concrete);
        if (!dirX || c.dir.x < 0f) RectBox(hall, "Base_HallWall", hMin.x - wt, hMin.x, hMin.y, hMax.y, wallBot, wallTop, concrete);
        if (!dirX || c.dir.x > 0f) RectBox(hall, "Base_HallWall", hMax.x, hMax.x + wt, hMin.y, hMax.y, wallBot, wallTop, concrete);
        // Rampa tarafındaki duvarın (B düzlemi) iki yanı kapatılır, ortası (rampa genişliği) açık kalır
        float hw = StairsWidth * 0.5f + 0.15f;
        if (dirX)
        {
            float wx0 = c.dir.x > 0f ? hMin.x - wt : hMax.x;
            RectBox(hall, "Base_HallBack", wx0, wx0 + wt, hMin.y - wt, c.B.y - hw, wallBot, wallTop, concrete);
            RectBox(hall, "Base_HallBack", wx0, wx0 + wt, c.B.y + hw, hMax.y + wt, wallBot, wallTop, concrete);
        }
        else
        {
            float wz0 = c.dir.y > 0f ? hMin.y - wt : hMax.y;
            RectBox(hall, "Base_HallBack", hMin.x - wt, c.B.x - hw, wz0, wz0 + wt, wallBot, wallTop, concrete);
            RectBox(hall, "Base_HallBack", c.B.x + hw, hMax.x + wt, wz0, wz0 + wt, wallBot, wallTop, concrete);
        }

        // Aydınlatma
        Vector2 hc = (hMin + hMax) * 0.5f;
        Vector2[] lightPts = { hc, hc + c.dir * (along * 0.3f), hc - c.dir * (along * 0.3f) };
        foreach (Vector2 lp in lightPts)
        {
            GameObject lg = new GameObject("MuseumLight_Basement");
            lg.transform.SetParent(interior, false);
            lg.transform.localPosition = new Vector3(lp.x, -D + 2.4f, lp.y);
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            l.range = 9f;
            l.intensity = 2.2f;
            l.color = new Color(0.8f, 0.88f, 1f);
        }
        GameObject rl = new GameObject("MuseumLight_Stairs");
        rl.transform.SetParent(interior, false);
        rl.transform.localPosition = (a3 + b3) * 0.5f + Vector3.up * 1.6f;
        Light rlight = rl.AddComponent<Light>();
        rlight.type = LightType.Point; rlight.shadows = LightShadows.None; rlight.range = 7f; rlight.intensity = 1.8f;
        rlight.color = new Color(1f, 0.9f, 0.7f);

        // --- Hapis hücresi: salonun B'ye en uzak köşesine ---
        var corners = new List<(Vector2 corner, float yaw)>
        {
            (new Vector2(hMax.x, hMin.y), 0f),     // GD
            (new Vector2(hMax.x, hMax.y), -90f),   // KD
            (new Vector2(hMin.x, hMax.y), 180f),   // KB
            (new Vector2(hMin.x, hMin.y), 90f),    // GB
        };
        int bi = 0; float bd = -1f;
        for (int i = 0; i < corners.Count; i++)
        {
            float d = (corners[i].corner - c.B).sqrMagnitude;
            if (d > bd) { bd = d; bi = i; }
        }
        Quaternion rot = Quaternion.Euler(0f, corners[bi].yaw, 0f);
        Vector3 se = new Vector3(38f, 0f, -16f);
        jail.localRotation = rot;
        jail.localPosition = new Vector3(corners[bi].corner.x, -D, corners[bi].corner.y) - rot * se;

        // --- Bodrum bilgileri (dünya) ---
        HasBasement = true;
        Vector3 topW = W(c.A - c.dir * 0.6f);
        StairsTopWorld = topW;
        StairsBottomWorld = W(c.B + c.dir * 1.0f, -D);
        StairsHoleWorld = WorldRect(new Vector2(hole.xMin, hole.yMin), new Vector2(hole.xMax, hole.yMax));
        BasementRect = WorldRect(hMin, hMax);
        BasementFloorY = W(Vector2.zero, -D).y;

        // Basement devriye noktaları (yerel): merdiven üstü → iniş → salon → hücre önü → salon → iniş
        basementPatrol.Clear();
        basementPatrol.Add(L(c.A - c.dir * 0.9f, 0f));
        basementPatrol.Add(L(c.B + c.dir * 1.6f, -D));
        basementPatrol.Add(L(hc, -D));
        Vector3 front = jail.localPosition + rot * new Vector3(35.6f, 0f, -10.9f);
        basementPatrol.Add(new Vector3(front.x, -D, front.z));
        basementPatrol.Add(L(hc, -D));
        basementPatrol.Add(L(c.B + c.dir * 1.6f, -D));

        // --- Plan kısıtları: boşluk hücreleri dolu, boşluğa bakan duvar yuvaları iptal ---
        MarkBlocked(hole);
        var infl = new Rect(hole.x - 0.9f, hole.y - 0.9f, hole.width + 1.8f, hole.height + 1.8f);
        wallSlots.RemoveAll(s => infl.Contains(s.pos));

        // --- Merdiven başı güvenlik aynası: direğe monte, bodrum salonunu gösterir (sabit kamera noktası) ---
        {
            Vector2 pole = c.A + perp * (hw + 0.35f);
            Decor(interior, "Mirror_Pole", PrimitiveType.Cube, L(pole, 1.15f), 0f, new Vector3(0.06f, 2.3f, 0.06f), dark);
            Vector2 mpos = pole - c.dir * 0.14f;
            Vector3 mn = new Vector3(-c.dir.x * 0.94f - perp.x * 0.34f, -0.26f, -c.dir.y * 0.94f - perp.y * 0.34f).normalized; // odaya doğru, merdiven eksenine ve aşağı eğik
            Vector3 pp = museum.TransformPoint(L(c.B + c.dir * 0.8f, -D + 2.2f));
            Vector3 pd = museum.TransformDirection(new Vector3(c.dir.x * 0.98f, -0.17f, c.dir.y * 0.98f));
            SecurityMirror.Create(interior, W(mpos, 2.3f), museum.TransformDirection(mn), pp, pd);
        }
        return true;
    }

    private readonly List<Vector3> basementPatrol = new List<Vector3>();

    private void FloorPiece(float x0, float x1, float z0, float z1, Color color)
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return;
        Decor(interior, "Floor_RS" + stairsRoom.id, PrimitiveType.Cube, L(new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f), 0.005f), 0f,
              new Vector3(x1 - x0, 0.01f, z1 - z0), color);
    }

    private void MarkBlocked(Rect r)
    {
        for (int x = 0; x < NX; x++)
            for (int z = 0; z < NZ; z++)
            {
                Rect cell = new Rect(X0 + x * Cell, Z0 + z * Cell, Cell, Cell);
                if (cell.Overlaps(r)) blocked[x, z] = true;
            }
    }
}
