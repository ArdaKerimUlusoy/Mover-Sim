using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MÜZENİN KAT PLANI (kamyondaki kişi için). Kamyondayken M ile tam ekran açılır; atölyede de yayının altında küçük hali durur.
/// Odalar, kapılar, Staff Only odası, giriş; her eserin TAM ADI ve hangi odada olduğu; ekip arkadaşlarının yeri ve
/// kayıt yapan oyuncunun baktığı yön. Müzede bazı eserlerin tabelası soluk (ArtNames): tam adı yalnızca burada görünür,
/// kamyondaki kişi müzedeki arkadaşını sesli sohbetle doğru esere yönlendirir.
/// Güvenlikler planda görünmez (müzedeki kişi gözüyle bakmalı).
/// Plan MuseumLayout'tan okunur (her haritada yeniden kurulan oda planı). Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class FloorPlanUI : MonoBehaviour
{
    public const Key ToggleKey = Key.M;

    public static bool IsOpen { get; private set; }
    private static FloorPlanUI instance;
    private static GUIStyle nameStyle, roomStyle, titleStyle, hintStyle, playerStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("FloorPlanUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<FloorPlanUI>();
    }

    /// <summary>Yerel oyuncu kamyonda mı (planı açabilir mi)?</summary>
    public static bool LocalInTruck
    {
        get
        {
            PlayerStealth me = PlayerStealth.Local;
            if (me == null || !me.IsSpawned || me.IsJailed) return false;
            return TruckZone.IsInside(me.GetComponent<PlayerNetworkSetup>());
        }
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        bool canOpen = LocalInTruck && !WorkbenchUI.IsOpen && !SessionMenuUI.IsPaused && !BriefingUI.IsOpen;
        if (!canOpen) { IsOpen = false; return; }
        if (kb == null || ChatUI.IsTyping || PlayerInventory.IsPanelOpen) return;
        if (kb[ToggleKey].wasPressedThisFrame) IsOpen = !IsOpen;
        if (IsOpen && kb.escapeKey.wasPressedThisFrame) IsOpen = false;
    }

    private void OnGUI()
    {
        if (!IsOpen) return;
        GUI.depth = -15;
        float w = Mathf.Min(Screen.width - 40f, 1280f), h = Mathf.Min(Screen.height - 80f, 760f);
        Rect panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        UIUtil.DrawRect(panel, new Color(0.04f, 0.05f, 0.07f, 0.93f));
        EnsureStyles();
        GUI.Label(new Rect(panel.x + 16f, panel.y + 8f, panel.width - 32f, 30f), Loc.T("plan.title"), titleStyle);
        GUI.Label(new Rect(panel.x + 16f, panel.yMax - 30f, panel.width - 32f, 24f), Loc.T("plan.hint"), hintStyle);

        // Solda plan, sağda canlı yayın
        float feedW = Mathf.Min(420f, w * 0.34f);
        Rect planRect = new Rect(panel.x + 16f, panel.y + 46f, panel.width - feedW - 52f, panel.height - 86f);
        Draw(planRect, true);
        Rect feed = new Rect(panel.xMax - feedW - 16f, panel.y + 74f, feedW, feedW * 0.75f);
        LiveFeed.Draw(feed);
        DrawArtList(new Rect(feed.x, feed.yMax + 16f, feed.width, panel.yMax - feed.yMax - 56f));
    }

    private static void EnsureStyles()
    {
        if (nameStyle != null) return;
        nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true };
        nameStyle.normal.textColor = new Color(1f, 0.9f, 0.6f);
        roomStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        roomStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = Color.white;
        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        hintStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        playerStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
        playerStyle.normal.textColor = Color.white;
    }

    // ---------------- Çizim ----------------

    private struct Map
    {
        public Rect area;
        public Rect world;
        public float scale;
        public Vector2 offset;
        public Vector2 ToScreen(float x, float z) => new Vector2(offset.x + (x - world.xMin) * scale, offset.y + (world.yMax - z) * scale);
        public Vector2 ToScreen(Vector3 p) => ToScreen(p.x, p.z);
        public Rect ToScreen(Rect r)
        {
            Vector2 a = ToScreen(r.xMin, r.yMax), b = ToScreen(r.xMax, r.yMin);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
    }

    /// <summary>Planı bir dikdörtgene çizer. names: eser adları yazılsın mı (küçük ön izlemede yalnızca numara).</summary>
    public static void Draw(Rect area, bool names)
    {
        EnsureStyles();
        MuseumLayout layout = MuseumLayout.Instance;
        UIUtil.DrawRect(area, new Color(0.08f, 0.1f, 0.13f, 0.95f));
        if (layout == null || layout.PlanRooms.Count == 0)
        {
            GUI.Label(area, Loc.T("plan.none"), roomStyle);
            return;
        }

        Rect world = layout.PlanBounds;
        float pad = 8f;
        float scale = Mathf.Min((area.width - pad * 2f) / world.width, (area.height - pad * 2f) / world.height);
        Map m = new Map
        {
            area = area, world = world, scale = scale,
            offset = new Vector2(area.x + (area.width - world.width * scale) * 0.5f, area.y + (area.height - world.height * scale) * 0.5f)
        };

        // Odalar (zemin + duvar çizgisi)
        float wall = Mathf.Max(2f, scale * 0.3f);
        foreach (MuseumLayout.PlanRoom r in layout.PlanRooms)
        {
            Rect s = m.ToScreen(r.rect);
            Color fill = r.role == "staff" ? new Color(0.45f, 0.14f, 0.14f) : r.role == "lobby" ? new Color(0.2f, 0.26f, 0.33f) : new Color(0.16f, 0.19f, 0.24f);
            UIUtil.DrawRect(s, fill);
            Color line = new Color(0.85f, 0.85f, 0.9f);
            UIUtil.DrawRect(new Rect(s.x, s.y, s.width, wall), line);
            UIUtil.DrawRect(new Rect(s.x, s.yMax - wall, s.width, wall), line);
            UIUtil.DrawRect(new Rect(s.x, s.y, wall, s.height), line);
            UIUtil.DrawRect(new Rect(s.xMax - wall, s.y, wall, s.height), line);
            string label = r.role == "staff" ? Loc.T("plan.staff") : r.role == "lobby" ? Loc.T("plan.lobby") : Loc.T("plan.room", r.id);
            GUI.Label(new Rect(s.x, s.y + 2f, s.width, 18f), label, roomStyle);
        }

        // Kapılar: duvardaki açıklık
        float doorLen = 2f * scale, doorTh = wall + 4f;
        foreach (MuseumLayout.PlanDoor d in layout.PlanDoors)
        {
            Vector2 c = m.ToScreen(d.center.x, d.center.y);
            Rect dr = d.vertical ? new Rect(c.x - doorTh * 0.5f, c.y - doorLen * 0.5f, doorTh, doorLen) : new Rect(c.x - doorLen * 0.5f, c.y - doorTh * 0.5f, doorLen, doorTh);
            UIUtil.DrawRect(dr, new Color(0.16f, 0.19f, 0.24f));
        }
        // Giriş
        Vector2 en = m.ToScreen(layout.PlanEntrance.x, layout.PlanEntrance.y);
        UIUtil.DrawRect(new Rect(en.x - doorTh, en.y - doorLen, doorTh * 2f, doorLen * 2f), new Color(0.35f, 0.8f, 0.45f));
        GUI.Label(new Rect(en.x - 60f, en.y + doorLen, 120f, 18f), Loc.T("plan.entrance"), nameStyle);

        // Eserler (tam adlarıyla)
        int n = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !d.IsActiveForMission) continue;
            n++;
            Vector2 p = m.ToScreen(d.transform.position);
            Color c = KindColor(d.Kind);
            float sz = d.WallMounted ? 12f : 10f;
            UIUtil.DrawRect(new Rect(p.x - sz * 0.5f - 1.5f, p.y - sz * 0.5f - 1.5f, sz + 3f, sz + 3f), Color.black);
            UIUtil.DrawRect(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), d.IsSwapped ? new Color(0.4f, 0.9f, 0.45f) : c);
            GUI.Label(new Rect(p.x - 8f, p.y - 8f, 16f, 16f), n.ToString(), roomStyle);
            if (names)
            {
                string t = ArtNames.FullName(d) + (ArtNames.IsFaded(d) ? " *" : "") + (d.IsSwapped ? "  " + Loc.T("plan.swapped") : "");
                GUI.Label(new Rect(p.x - 80f, p.y + sz * 0.5f + 1f, 160f, 34f), t, nameStyle);
            }
        }

        // Ekip: renkli noktalar; kayıt yapanın baktığı yön (görüş konisi)
        foreach (PlayerNetworkSetup ps in PlayerNetworkSetup.All)
        {
            if (ps == null || !ps.IsSpawned) continue;
            Vector3 pos = ps.transform.position;
            if (!world.Contains(new Vector2(pos.x, pos.z))) continue; // müzenin dışında (kamyonda / sokakta)
            PlayerStealth st = ps.GetComponent<PlayerStealth>();
            Vector2 p = m.ToScreen(pos);
            if (st != null && st.IsRecording)
            {
                Vector3 f = ps.transform.forward;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = Quaternion.Euler(0f, side * LiveFeed.Fov * 0.5f, 0f) * f;
                    for (int i = 1; i <= 8; i++)
                    {
                        Vector2 q = m.ToScreen(pos + dir * (i * 0.6f));
                        UIUtil.DrawRect(new Rect(q.x - 1.5f, q.y - 1.5f, 3f, 3f), new Color(1f, 0.3f, 0.25f, 0.9f));
                    }
                }
            }
            Color pc = ps.PlayerColor;
            UIUtil.DrawRect(new Rect(p.x - 6f, p.y - 6f, 12f, 12f), Color.white);
            UIUtil.DrawRect(new Rect(p.x - 4.5f, p.y - 4.5f, 9f, 9f), pc);
            if (names) GUI.Label(new Rect(p.x - 60f, p.y - 26f, 120f, 18f), ps.PlayerName + (st != null && st.IsRecording ? "  ●REC" : ""), playerStyle);
        }
    }

    private static Color KindColor(ArtKind k)
    {
        switch (k)
        {
            case ArtKind.Painting: return new Color(0.95f, 0.75f, 0.3f);
            case ArtKind.Vase: return new Color(0.45f, 0.7f, 1f);
            case ArtKind.Jewelry: return new Color(0.9f, 0.5f, 0.95f);
            case ArtKind.Bust: return new Color(0.85f, 0.85f, 0.85f);
            default: return new Color(0.6f, 0.6f, 0.6f);
        }
    }

    /// <summary>Planın yanındaki eser listesi: numara, tam ad, hangi oda.</summary>
    private static void DrawArtList(Rect r)
    {
        if (r.height < 40f) return;
        UIUtil.DrawRect(r, new Color(0.08f, 0.1f, 0.13f, 0.95f));
        MuseumLayout layout = MuseumLayout.Instance;
        float y = r.y + 6f;
        int n = 0;
        var style = new GUIStyle(nameStyle) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !d.IsActiveForMission) continue;
            n++;
            if (y > r.yMax - 18f) break;
            int room = layout != null ? layout.RoomAt(d.transform.position) : -1;
            string roomName = room < 0 ? "?" : RoomLabel(layout, room);
            string line = $"{n}.  {ArtNames.FullName(d)}  —  {roomName}" + (ArtNames.IsFaded(d) ? "  " + Loc.T("plan.faded") : "") + (d.IsSwapped ? "  " + Loc.T("plan.swapped") : "");
            style.normal.textColor = d.IsSwapped ? new Color(0.5f, 0.9f, 0.55f) : KindColor(d.Kind);
            GUI.Label(new Rect(r.x + 8f, y, r.width - 16f, 20f), line, style);
            y += 20f;
        }
    }

    private static string RoomLabel(MuseumLayout layout, int id)
    {
        foreach (MuseumLayout.PlanRoom pr in layout.PlanRooms)
            if (pr.id == id) return pr.role == "staff" ? Loc.T("plan.staff") : pr.role == "lobby" ? Loc.T("plan.lobby") : Loc.T("plan.room", id);
        return "?";
    }
}
