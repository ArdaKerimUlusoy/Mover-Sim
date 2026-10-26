using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MÜZENİN KAT PLANI (kamyondaki kişi için). Kamyondayken M ile tam ekran açılır; atölyede de yayının altında küçük hali durur.
/// Odalar, kapılar, Staff Only odası, giriş; her eserin TAM ADI ve hangi odada olduğu; ekip arkadaşlarının yeri ve
/// kayıt yapan oyuncunun baktığı yön. Müzede bazı eserlerin tabelası soluk (ArtNames): tam adı yalnızca burada görünür,
/// kamyondaki kişi müzedeki arkadaşını sesli sohbetle doğru esere yönlendirir.
/// Güvenlikler: kamyondaki oyuncuya her zaman (konum + bakış yönü, tıklayınca takıma pinglenir); müzedekine yalnızca görüş içindeyse ya da pinglenmişse.
/// Müzenin altındaki bodrum kat (hapishane) planın köşesinde "B1" kutusunda gösterilir.
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
        PingSystem.GuardPinged -= OnGuardPinged;
        PingSystem.GuardPinged += OnGuardPinged; // biri bir güvenliği pingleyince haritada herkese görünür
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
            Color fill = r.role == "staff" ? new Color(0.45f, 0.14f, 0.14f) : r.role == "stairs" ? new Color(0.3f, 0.25f, 0.1f) : r.role == "security" ? new Color(0.35f, 0.3f, 0.1f) : r.role == "lobby" ? new Color(0.2f, 0.26f, 0.33f) : new Color(0.16f, 0.19f, 0.24f);
            UIUtil.DrawRect(s, fill);
            Color line = new Color(0.85f, 0.85f, 0.9f);
            UIUtil.DrawRect(new Rect(s.x, s.y, s.width, wall), line);
            UIUtil.DrawRect(new Rect(s.x, s.yMax - wall, s.width, wall), line);
            UIUtil.DrawRect(new Rect(s.x, s.y, wall, s.height), line);
            UIUtil.DrawRect(new Rect(s.xMax - wall, s.y, wall, s.height), line);
            string label = RoleLabel(r.role, r.id);
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

        MuseumLayout lay = layout;
        bool hasB = lay.HasBasement;

        // Merdiven (üst kat): boşluk + simge; hapis alt katta olduğu için burada "B1" etiketiyle gösterilir
        if (hasB)
        {
            Rect hs = m.ToScreen(lay.StairsHoleWorld);
            UIUtil.DrawRect(hs, new Color(0.95f, 0.78f, 0.12f, 0.35f));
            DrawStairsMarker(m.ToScreen(lay.StairsTopWorld), area, nameStyle, Loc.T("plan.stairs") + "  " + Loc.T("plan.basement"));
        }
        else if (JailCell.TryGetPlanPosition(out Vector3 jailPos))
        {
            // Bodrum yok (eski yerleşim): hücre ikonu (plan sınırının dışında kalsa bile kenara sıkıştırılır)
            DrawJailMarker(m.ToScreen(jailPos), area, nameStyle);
        }

        // Çıkış / kamyon: kamyon müzenin dışında; planın kenarına simge + etiket
        if (SpawnPoint.All.Count > 0)
        {
            Vector3 tp = Vector3.zero;
            int tn = 0;
            foreach (SpawnPoint sp in SpawnPoint.All) if (sp != null) { tp += sp.transform.position; tn++; }
            if (tn > 0) DrawTruckMarker(m.ToScreen(tp / tn), area, nameStyle);
        }

        // Alt kat kutusu ("B1"): hapishane, merdiven ucu, alt kattaki oyuncular ve güvenlik
        Map bm = default;
        bool insetOk = false;
        if (hasB) insetOk = DrawBasementInset(area, lay, out bm);

        // Güvenlikler: kamyondaki oyuncuya her zaman; müzedeki oyuncuya yalnızca görüş içindeyse ya da pinglenmişse
        bool interactive = IsOpen && names;
        bool clickedPing = false;
        Event ev = Event.current;
        bool click = interactive && ev != null && ev.type == EventType.MouseDown && ev.button == 0;
        RefreshGuards();
        for (int gi = 0; gi < guards.Length; gi++)
        {
            GuardAI g = guards[gi];
            if (g == null || !g.IsSpawned || !g.Online || !GuardShown(g)) continue;
            Vector3 gp = g.transform.position;
            bool below = hasB && lay.IsInBasement(gp);
            if (below && !insetOk) continue;
            Map gm = below ? bm : m;
            if (!below && !world.Contains(new Vector2(gp.x, gp.z))) continue;
            Vector2 sp2 = DrawGuard(gm, g);
            if (click && !clickedPing && Vector2.Distance(ev.mousePosition, sp2) <= 14f)
            {
                clickedPing = true;
                PingSystem ping = FindFirstObjectByType<PingSystem>();
                if (ping != null && ping.RequestGuardPing(gp)) pingedUntil[g] = Time.unscaledTime + 5f;
                ev.Use();
            }
        }
        if (interactive && guards.Length > 0)
            GUI.Label(new Rect(area.x + 8f, area.yMax - 20f, area.width * 0.6f, 18f), Loc.T("plan.guardhint"), hintStyle);

        // Ekip: renkli noktalar; kayıt yapanın baktığı yön (görüş konisi)
        foreach (PlayerNetworkSetup ps in PlayerNetworkSetup.All)
        {
            if (ps == null || !ps.IsSpawned) continue;
            Vector3 pos = ps.transform.position;
            bool below = hasB && lay.IsInBasement(pos);
            if (below && !insetOk) continue;
            Map pm = below ? bm : m;
            if (!below && !world.Contains(new Vector2(pos.x, pos.z))) continue; // müzenin dışında (kamyonda / sokakta)
            PlayerStealth st = ps.GetComponent<PlayerStealth>();
            Vector2 p = pm.ToScreen(pos);
            if (st != null && st.IsRecording)
            {
                Vector3 f = ps.transform.forward;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = Quaternion.Euler(0f, side * LiveFeed.Fov * 0.5f, 0f) * f;
                    for (int i = 1; i <= 8; i++)
                    {
                        Vector2 q = pm.ToScreen(pos + dir * (i * 0.6f));
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

    // ---------------- Güvenlikler ----------------

    private static GuardAI[] guards = new GuardAI[0];
    private static float guardsAt = -10f;
    private static readonly Dictionary<GuardAI, float> pingedUntil = new Dictionary<GuardAI, float>();

    private static void RefreshGuards()
    {
        if (Time.unscaledTime - guardsAt < 1f) return;
        guardsAt = Time.unscaledTime;
        guards = FindObjectsByType<GuardAI>(FindObjectsSortMode.None);
    }

    /// <summary>Biri (herhangi bir oyuncu) bir güvenliği pingledi: en yakın güvenlik birkaç sn herkese haritada görünür.</summary>
    private static void OnGuardPinged(Vector3 pos)
    {
        guardsAt = -10f;
        RefreshGuards();
        GuardAI best = null;
        float bd = float.MaxValue;
        foreach (GuardAI g in guards)
        {
            if (g == null) continue;
            Vector3 d = g.transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = g; }
        }
        if (best != null) pingedUntil[best] = Time.unscaledTime + 5f;
    }

    /// <summary>TASARIM: kamyondaki oyuncu bütün güvenlikleri görür; müzedeki oyuncu yalnızca görüş alanındaki ya da yeni pinglenmiş güvenliği.</summary>
    private static bool GuardShown(GuardAI g)
    {
        if (LocalInTruck) return true;
        if (pingedUntil.TryGetValue(g, out float until) && Time.unscaledTime < until) return true;
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 target = g.transform.position + Vector3.up * 1.2f;
        Vector3 vp = cam.WorldToViewportPoint(target);
        if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;
        if (Physics.Linecast(cam.transform.position, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider != null && hit.collider.GetComponentInParent<GuardAI>() == g;
        return true;
    }

    private static Color GuardColor(GuardAI.State s)
    {
        switch (s)
        {
            case GuardAI.State.Chase: return new Color(1f, 0.2f, 0.2f);
            case GuardAI.State.Investigate: return new Color(1f, 0.6f, 0.2f);
            case GuardAI.State.Suspicious: return new Color(1f, 0.9f, 0.3f);
            default: return new Color(0.4f, 0.65f, 1f);
        }
    }

    /// <summary>Güvenlik simgesi + bakış yönü (görüş konisi çizgileri). Ekran konumunu döndürür (tıklama için).</summary>
    private static Vector2 DrawGuard(Map m, GuardAI g)
    {
        Vector3 pos = g.transform.position;
        Vector3 f = g.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
        f.Normalize();
        Color c = GuardColor(g.CurrentState);
        Vector2 p = m.ToScreen(pos);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 dir = Quaternion.Euler(0f, side * 30f, 0f) * f;
            for (int i = 1; i <= 8; i++)
            {
                Vector2 q = m.ToScreen(pos + dir * (i * 0.7f));
                UIUtil.DrawRect(new Rect(q.x - 1.2f, q.y - 1.2f, 2.4f, 2.4f), new Color(c.r, c.g, c.b, 0.8f));
            }
        }
        // Yön oku: öne doğru kalın noktalar
        for (int i = 1; i <= 4; i++)
        {
            Vector2 q = m.ToScreen(pos + f * (i * 0.45f));
            UIUtil.DrawRect(new Rect(q.x - 1.8f, q.y - 1.8f, 3.6f, 3.6f), c);
        }
        UIUtil.DrawRect(new Rect(p.x - 7.5f, p.y - 7.5f, 15f, 15f), Color.black);
        UIUtil.DrawRect(new Rect(p.x - 6f, p.y - 6f, 12f, 12f), c);
        UIUtil.DrawRect(new Rect(p.x - 2f, p.y - 4f, 4f, 8f), new Color(0.1f, 0.1f, 0.15f));
        return p;
    }

    // ---------------- Simgeler ----------------

    /// <summary>Merdiven simgesi (basamaklı kare) + etiket (label null ise yalnızca simge).</summary>
    public static void DrawStairsMarker(Vector2 p, Rect clip, GUIStyle label, string text)
    {
        p.x = Mathf.Clamp(p.x, clip.xMin + 12f, clip.xMax - 12f);
        p.y = Mathf.Clamp(p.y, clip.yMin + 12f, clip.yMax - 22f);
        const float s = 16f;
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f - 1.5f, p.y - s * 0.5f - 1.5f, s + 3f, s + 3f), Color.white);
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), new Color(0.32f, 0.26f, 0.08f));
        for (int i = 0; i < 4; i++)
            UIUtil.DrawRect(new Rect(p.x - s * 0.5f + 1.5f + i * 3f, p.y - s * 0.5f + 1.5f + i * 3.4f, s - 3f - i * 3f, 2.2f), new Color(0.95f, 0.78f, 0.12f));
        if (label == null) return;
        Color old = label.normal.textColor;
        label.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(p.x - 70f, p.y + s * 0.5f + 1f, 140f, 16f), text, label);
        label.normal.textColor = old;
    }

    /// <summary>Kamyon simgesi (kenara sıkıştırılır) + etiket.</summary>
    private static void DrawTruckMarker(Vector2 p, Rect clip, GUIStyle label)
    {
        p.x = Mathf.Clamp(p.x, clip.xMin + 16f, clip.xMax - 16f);
        p.y = Mathf.Clamp(p.y, clip.yMin + 12f, clip.yMax - 22f);
        UIUtil.DrawRect(new Rect(p.x - 12f, p.y - 7f, 24f, 14f), Color.white);
        UIUtil.DrawRect(new Rect(p.x - 11f, p.y - 6f, 16f, 12f), new Color(0.3f, 0.75f, 0.45f));
        UIUtil.DrawRect(new Rect(p.x + 6f, p.y - 3f, 5f, 9f), new Color(0.2f, 0.45f, 0.3f));
        UIUtil.DrawRect(new Rect(p.x - 8f, p.y + 5f, 4f, 4f), Color.black);
        UIUtil.DrawRect(new Rect(p.x + 4f, p.y + 5f, 4f, 4f), Color.black);
        if (label == null) return;
        Color old = label.normal.textColor;
        label.normal.textColor = new Color(0.55f, 1f, 0.65f);
        GUI.Label(new Rect(p.x - 60f, p.y + 9f, 120f, 16f), Loc.T("plan.truck"), label);
        label.normal.textColor = old;
    }

    /// <summary>Planın köşesine alt kat ("B1") kutusu: salon, rampa, hapishane, alt kattaki oyuncu / güvenlik. Harita dönüşümünü verir.</summary>
    private static bool DrawBasementInset(Rect area, MuseumLayout lay, out Map bm)
    {
        bm = default;
        Rect hall = lay.BasementRect, hole = lay.StairsHoleWorld;
        Rect world = Rect.MinMaxRect(Mathf.Min(hall.xMin, hole.xMin), Mathf.Min(hall.yMin, hole.yMin), Mathf.Max(hall.xMax, hole.xMax), Mathf.Max(hall.yMax, hole.yMax));
        if (world.width < 1f || world.height < 1f) return false;

        float ratio = world.height / world.width;
        float iw = Mathf.Min(area.width * 0.36f, 300f);
        float ih = iw * ratio + 24f;
        if (ih > area.height * 0.5f) { ih = area.height * 0.5f; iw = (ih - 24f) / ratio; }
        Rect box = new Rect(area.xMax - iw - 8f, area.yMax - ih - 8f, iw, ih);
        UIUtil.DrawRect(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), new Color(0.85f, 0.85f, 0.9f));
        UIUtil.DrawRect(box, new Color(0.06f, 0.07f, 0.1f));

        Rect inner = new Rect(box.x + 6f, box.y + 18f, box.width - 12f, box.height - 24f);
        float scale = Mathf.Min(inner.width / world.width, inner.height / world.height);
        bm = new Map
        {
            area = inner, world = world, scale = scale,
            offset = new Vector2(inner.x + (inner.width - world.width * scale) * 0.5f, inner.y + (inner.height - world.height * scale) * 0.5f)
        };
        GUI.Label(new Rect(box.x + 4f, box.y + 1f, box.width - 8f, 16f), Loc.T("plan.basement"), BasementTitleStyle());
        UIUtil.DrawRect(bm.ToScreen(hall), new Color(0.16f, 0.17f, 0.23f));
        UIUtil.DrawRect(bm.ToScreen(hole), new Color(0.95f, 0.78f, 0.12f, 0.35f));
        if (JailCell.TryGetJailPosition(out Vector3 jp)) DrawJailMarker(bm.ToScreen(jp), inner, nameStyle);
        return true;
    }

    private static GUIStyle basementTitle;
    private static GUIStyle BasementTitleStyle()
    {
        if (basementTitle == null)
        {
            basementTitle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            basementTitle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        }
        return basementTitle;
    }

    /// <summary>Hapis hücresi ikonu (parmaklıklı kare) + "Hapis" etiketi; BodyCam planı da kullanır.</summary>
    public static void DrawJailMarker(Vector2 p, Rect clip, GUIStyle label)
    {
        p.x = Mathf.Clamp(p.x, clip.xMin + 12f, clip.xMax - 12f);
        p.y = Mathf.Clamp(p.y, clip.yMin + 12f, clip.yMax - 22f);
        const float s = 16f;
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f - 1.5f, p.y - s * 0.5f - 1.5f, s + 3f, s + 3f), Color.white);
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), new Color(0.22f, 0.22f, 0.26f));
        for (int i = 0; i < 4; i++)
            UIUtil.DrawRect(new Rect(p.x - s * 0.5f + 1.5f + i * 4f, p.y - s * 0.5f, 1.6f, s), new Color(0.85f, 0.85f, 0.9f));
        if (label == null) return;
        Color old = label.normal.textColor;
        label.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(p.x - 40f, p.y + s * 0.5f + 1f, 80f, 16f), Loc.T("plan.jail"), label);
        label.normal.textColor = old;
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

    /// <summary>Odanın plandaki etiketi (Staff Only, güvenlik odası, merdiven, lobi ya da oda numarası).</summary>
    private static string RoleLabel(string role, int id)
    {
        switch (role)
        {
            case "staff": return Loc.T("plan.staff");
            case "security": return Loc.T("plan.security");
            case "stairs": return Loc.T("plan.stairs");
            case "lobby": return Loc.T("plan.lobby");
            default: return Loc.T("plan.room", id);
        }
    }

    private static string RoomLabel(MuseumLayout layout, int id)
    {
        foreach (MuseumLayout.PlanRoom pr in layout.PlanRooms)
            if (pr.id == id) return RoleLabel(pr.role, id);
        return "?";
    }
}
