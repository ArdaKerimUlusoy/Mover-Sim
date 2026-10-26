using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MuseumLayout'un parçası: KAT PLANI ve KAYIT (fotoğraf) KURALLARI.
///  - Kamyondaki monitör (BodyCam) bu planı çizer: odalar, kapılar, eserler, takım arkadaşları.
///  - Her harita için tohumdan: bazı galerilerde kayıt / fotoğraf SERBEST (ziyaretçiler de fotoğraf çeker, aralarına
///    karışabilirsin), bazılarında YASAK. Staff Only ve güvenlik odasında her zaman yasak. Lobi serbest.
///  - Yasak odalarda tavanın altında "KAYIT YASAK" tabelası (yakından, duvar arkasından görünmez).
/// Kat planı ve kurallar her bilgisayarda aynı çıkar (ağdan veri gönderilmez).
/// </summary>
public partial class MuseumLayout
{
    // PlanRoom yapısı MuseumLayout.cs'te (arkadaşın kat planıyla ortak); noPhoto alanı oraya eklendi.

    private readonly List<PlanRoom> plan = new List<PlanRoom>();
    private readonly List<Vector2> planDoors = new List<Vector2>(); // kapı ortaları (dünya X-Z)
    private int planVersion = -1;
    private Rect planBounds;

    /// <summary>Kat planı (odalar). Düzen değişince kendiliğinden yenilenir.</summary>
    public List<PlanRoom> Plan { get { EnsurePlan(); return plan; } }
    public List<Vector2> PlanDoorPoints { get { EnsurePlan(); return planDoors; } } // kapı ortaları (arkadaşın PlanDoors'u ayrı)
    public Rect PlanArea { get { EnsurePlan(); return planBounds; } } // odaların kapladığı alan (arkadaşın PlanBounds'u tüm ızgara)
    /// <summary>Müze zemininin dünya yüksekliği (haritadan işaret koymak için).</summary>
    public float PlanFloorY => museum != null ? museum.TransformPoint(Vector3.zero).y : 0f;

    /// <summary>Bu dünya noktası kaydın YASAK olduğu bir odada mı? (Müze dışı / kamyon: serbest.)</summary>
    public bool IsNoPhotoAt(Vector3 world)
    {
        EnsurePlan();
        PlanRoom? r = PlanRoomAt(world);
        return r.HasValue && r.Value.noPhoto;
    }

    /// <summary>Dünya noktasının içinde olduğu oda (müzenin dışındaysa null).</summary>
    public PlanRoom? PlanRoomAt(Vector3 world)
    {
        EnsurePlan();
        if (museum == null || grid == null || plan.Count == 0) return null;
        Vector3 local = museum.InverseTransformPoint(world);
        if (local.x < X0 || local.x > X0 + NX * Cell || local.z < Z0 || local.z > Z0 + NZ * Cell) return null;
        if (local.y < -1f || local.y > WallH + 1f) return null;
        Vector2Int c = CellOf(new Vector2(local.x, local.z));
        int id = grid[c.x, c.y];
        return id >= 0 && id < plan.Count ? plan[id] : (PlanRoom?)null;
    }

    private void EnsurePlan()
    {
        if (planVersion == Version) return;
        planVersion = Version;
        plan.Clear();
        planDoors.Clear();
        if (museum == null || rooms.Count == 0) return;

        var rng = new System.Random(appliedSeed ^ 0x2F07);
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (PRoom r in rooms)
        {
            Vector3 a = museum.TransformPoint(new Vector3(r.Min.x, 0f, r.Min.y));
            Vector3 b = museum.TransformPoint(new Vector3(r.Max.x, 0f, r.Max.y));
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
            bool noPhoto;
            switch (r.role)
            {
                case "staff":
                case "security":
                case "stairs": noPhoto = true; break;
                case "lobby": noPhoto = false; break;
                default: noPhoto = rng.NextDouble() < 0.4; break; // galerilerin ~%40'ında kayıt yasak
            }
            plan.Add(new PlanRoom { id = r.id, rect = rect, role = r.role, noPhoto = noPhoto });
            minX = Mathf.Min(minX, rect.xMin); minZ = Mathf.Min(minZ, rect.yMin);
            maxX = Mathf.Max(maxX, rect.xMax); maxZ = Mathf.Max(maxZ, rect.yMax);
        }
        foreach (DoorEdge d in doors)
        {
            Vector3 w = museum.TransformPoint(new Vector3(d.Center.x, 0f, d.Center.y));
            planDoors.Add(new Vector2(w.x, w.z));
        }
        planBounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    /// <summary>BuildInterior sonunda: kaydın yasak olduğu odalara tabela (sadece görsel + yakın yazı).</summary>
    private void BuildPhotoSigns()
    {
        planVersion = -1; // plan yeni odalardan hesaplansın
        EnsurePlan();
        foreach (PlanRoom pr in plan)
        {
            if (pr.role == "lobby") continue;
            PRoom r = rooms[pr.id];
            GameObject go = new GameObject(pr.noPhoto ? "Sign_NoPhoto" : "Sign_PhotoOk");
            go.transform.SetParent(interior, false);
            go.transform.localPosition = L(r.Center, WallH - 0.55f);
            PhotoSign s = go.AddComponent<PhotoSign>();
            s.allowed = !pr.noPhoto;
            if (pr.noPhoto)
            {
                // Tavandan sarkan kırmızı daire tabela
                Decor(go.transform, "NoPhotoDisc", PrimitiveType.Cylinder, Vector3.zero, 0f, new Vector3(0.5f, 0.02f, 0.5f), new Color(0.85f, 0.1f, 0.1f));
                Decor(go.transform, "NoPhotoBar", PrimitiveType.Cube, Vector3.down * 0.02f, 45f, new Vector3(0.5f, 0.015f, 0.07f), Color.white);
                Decor(go.transform, "NoPhotoWire", PrimitiveType.Cube, Vector3.up * 0.28f, 0f, new Vector3(0.015f, 0.55f, 0.015f), new Color(0.2f, 0.2f, 0.2f));
            }
        }
    }
}
