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
        float sc = UITheme.Scale;
        UIUtil.Scrim(UITheme.Scrim);

        float w = Mathf.Min(Screen.width - 32f, 1280f * sc), h = Mathf.Min(Screen.height - 48f, 760f * sc);
        Rect panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        UIUtil.Panel(panel, PanelKind.Plaque);
        float pad = Mathf.Round(20f * sc);

        // Başlık: harita ikonu + serif başlık + ince pirinç çizgi
        float titleH = Mathf.Round(36f * sc);
        UIUtil.Icon(new Rect(panel.x + pad, panel.y + pad * 0.6f + 4f * sc, 28f * sc, 28f * sc), IconId.Map, UITheme.BrassLight);
        UIUtil.Text(new Rect(panel.x + pad + 38f * sc, panel.y + pad * 0.6f, panel.width - pad * 2f - 38f * sc, titleH), Loc.T("plan.title"), TextRole.Title, UITheme.Ivory);
        float top = panel.y + pad * 0.6f + titleH + 6f * sc;
        UIUtil.Divider(panel.x + pad, top, panel.width - pad * 2f, UITheme.WithAlpha(UITheme.Brass, 0.6f), true);
        top += 12f * sc;

        // Alt: tuş kapaklı ipucu (dar ekranda iki satıra sarılır)
        float hintH = Mathf.Round(34f * sc);
        DrawHint(new Rect(panel.x + pad, panel.yMax - hintH - pad * 0.4f, panel.width - pad * 2f, hintH), Loc.T("plan.hint"));

        // Solda plan, sağda canlı yayın
        float feedW = Mathf.Min(420f * sc, w * 0.34f);
        Rect planRect = new Rect(panel.x + pad, top, panel.width - feedW - pad * 3f, panel.yMax - top - hintH - pad * 0.8f);
        Draw(planRect, true);
        UIUtil.BrassFrame(planRect);
        Rect feed = new Rect(panel.xMax - feedW - pad, top + 28f, feedW, feedW * 0.75f);
        LiveFeed.Draw(feed);
        DrawArtList(new Rect(feed.x - 6f, feed.yMax + 14f, feed.width + 12f, panel.yMax - feed.yMax - hintH - pad * 0.8f - 14f), sc);
    }

    // ---------------- Yazı yardımcıları ----------------

    private static GUIStyle fit;

    // Plan ölçeklenmeyen px kutularda çizilir: yazı rol boyutundan büyük olmasın ama taşmasın (maxPx)
    private static void Txt(Rect r, string s, TextRole role, Color c, TextAnchor a, int maxPx = 99, bool wrap = false)
    {
        GUIStyle b = UITheme.Style(role, c, a);
        if (fit == null) fit = new GUIStyle(b);
        fit.font = b.font; fit.fontStyle = b.fontStyle; fit.clipping = TextClipping.Clip; fit.padding = b.padding;
        fit.fontSize = Mathf.Min(b.fontSize, maxPx);
        fit.alignment = a; fit.wordWrap = wrap;
        fit.normal.textColor = c;
        GUI.Label(r, s, fit);
    }

    private static float TxtWidth(string s, TextRole role, int maxPx)
    {
        GUIStyle b = UITheme.Style(role);
        int old = b.fontSize;
        b.fontSize = Mathf.Min(old, maxPx);
        float wdt = b.CalcSize(new GUIContent(s)).x;
        b.fontSize = old;
        return wdt;
    }

    // "[M] / [Esc] Kapat • * = ..." → [..] parçaları tuş kapağı, kalanı Caption. Sığmazsa köşeli parantezsiz sarılmış yazı.
    private static void DrawHint(Rect r, string text)
    {
        float total = 0f;
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('[', i);
            int close = open >= 0 ? text.IndexOf(']', open) : -1;
            string plain = open >= 0 && close > open ? text.Substring(i, open - i) : text.Substring(i);
            total += TxtWidth(plain, TextRole.Caption, 13) + 2f;
            if (open < 0 || close < open) break;
            total += UIUtil.KeyCapWidth(text.Substring(open + 1, close - open - 1), 13) + 6f;
            i = close + 1;
        }
        if (total > r.width)
        {
            Txt(r, text, TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft, 13, true);
            return;
        }
        float x = r.x;
        float kh = Mathf.Min(r.height, 22f);
        i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('[', i);
            int close = open >= 0 ? text.IndexOf(']', open) : -1;
            string plain = open >= 0 && close > open ? text.Substring(i, open - i) : text.Substring(i);
            if (plain.Length > 0)
            {
                float pw = TxtWidth(plain, TextRole.Caption, 13) + 2f;
                Txt(new Rect(x, r.y, pw, kh), plain, TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleLeft, 13);
                x += pw;
            }
            if (open < 0 || close < open) break;
            string key = text.Substring(open + 1, close - open - 1);
            float kw = UIUtil.KeyCapWidth(key, 13);
            UIUtil.KeyCap(new Rect(x + 2f, r.y + 1f, kw, kh - 2f), key, 13);
            x += kw + 6f;
            i = close + 1;
        }
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

    // Eser/işaret baklavası: 45 derece döndürülmüş kare (pirinç eser işareti)
    private static void Diamond(Vector2 p, float size, Color fill, Color edge)
    {
        Matrix4x4 old = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, p);
        float s = size * 0.72f;
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f - 1.5f, p.y - s * 0.5f - 1.5f, s + 3f, s + 3f), edge);
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), fill);
        GUI.matrix = old;
    }

    // Yukarı bakan dolu üçgen (tarama çizgileriyle), yaw derece döndürülür
    private static void Triangle(Vector2 p, float size, float yawDeg, Color fill, Color edge)
    {
        Matrix4x4 old = GUI.matrix;
        GUIUtility.RotateAroundPivot(yawDeg, p);
        for (int pass = 0; pass < 2; pass++)
        {
            float sz = pass == 0 ? size + 4f : size;
            Color c = pass == 0 ? edge : fill;
            int rows = Mathf.Max(3, Mathf.RoundToInt(sz));
            for (int k = 0; k < rows; k++)
            {
                float wdt = Mathf.Max(1f, (k + 1f) / rows * sz * 0.9f);
                UIUtil.DrawRect(new Rect(p.x - wdt * 0.5f, p.y - sz * 0.55f + k, wdt, 1.2f), c);
            }
        }
        GUI.matrix = old;
    }

    private static Color RoomFill(string role)
    {
        switch (role)
        {
            case "staff": return UITheme.WithAlpha(UITheme.Bordeaux, 0.55f);
            case "stairs": return UITheme.WithAlpha(UITheme.Ochre, 0.22f);
            case "security": return UITheme.WithAlpha(UITheme.Ochre, 0.14f);
            case "lobby": return UITheme.WithAlpha(UITheme.Ivory, 0.12f);
            default: return UITheme.WithAlpha(UITheme.Ivory, 0.05f);
        }
    }

    /// <summary>Planı bir dikdörtgene çizer. names: eser adları yazılsın mı (küçük ön izlemede yalnızca numara).</summary>
    public static void Draw(Rect area, bool names)
    {
        MuseumLayout layout = MuseumLayout.Instance;
        // Arşiv planı: orman zemini + silik ızgara
        UIUtil.DrawRect(area, UITheme.WithAlpha(UITheme.Forest, 0.9f));
        Color gridC = UITheme.WithAlpha(UITheme.Ivory, 0.035f);
        for (float gx = area.x + 24f; gx < area.xMax; gx += 24f) UIUtil.DrawRect(new Rect(gx, area.y, 1f, area.height), gridC);
        for (float gy = area.y + 24f; gy < area.yMax; gy += 24f) UIUtil.DrawRect(new Rect(area.x, gy, area.width, 1f), gridC);

        if (layout == null || layout.PlanRooms.Count == 0)
        {
            Txt(area, Loc.T("plan.none"), TextRole.Body, UITheme.IvoryDim, TextAnchor.MiddleCenter, 16, true);
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

        // Odalar: yarı saydam zemin + Graphite duvar + içte Ivory ince hat
        float wall = Mathf.Max(2f, scale * 0.3f);
        foreach (MuseumLayout.PlanRoom r in layout.PlanRooms)
        {
            Rect s = m.ToScreen(r.rect);
            UIUtil.DrawRect(s, RoomFill(r.role));
            UIUtil.Outline(s, wall, UITheme.Graphite);
            UIUtil.Outline(new Rect(s.x + wall, s.y + wall, s.width - wall * 2f, s.height - wall * 2f), 1f, UITheme.WithAlpha(UITheme.Ivory, 0.55f));
            string label = UITheme.Caps(RoleLabel(r.role, r.id));
            Txt(new Rect(s.x + 2f, s.y + wall + 1f, s.width - 4f, 16f), label, TextRole.Caption, UITheme.WithAlpha(UITheme.IvoryDim, 0.8f), TextAnchor.UpperCenter, 11);
        }

        // Kapılar: duvardaki açıklık
        float doorLen = 2f * scale, doorTh = wall + 4f;
        foreach (MuseumLayout.PlanDoor d in layout.PlanDoors)
        {
            Vector2 c = m.ToScreen(d.center.x, d.center.y);
            Rect dr = d.vertical ? new Rect(c.x - doorTh * 0.5f, c.y - doorLen * 0.5f, doorTh, doorLen) : new Rect(c.x - doorLen * 0.5f, c.y - doorTh * 0.5f, doorLen, doorTh);
            UIUtil.DrawRect(dr, UITheme.Forest);
        }
        // Giriş
        Vector2 en = m.ToScreen(layout.PlanEntrance.x, layout.PlanEntrance.y);
        UIUtil.DrawRect(new Rect(en.x - doorTh, en.y - doorLen, doorTh * 2f, doorLen * 2f), UITheme.Sage);
        Txt(new Rect(en.x - 60f, en.y + doorLen, 120f, 16f), UITheme.Caps(Loc.T("plan.entrance")), TextRole.Caption, UITheme.Sage, TextAnchor.UpperCenter, 11);

        // Eserler: pirinç baklava + numara; tam ad Caption (küçük ön izlemede yalnızca numara)
        int n = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !d.IsActiveForMission) continue;
            n++;
            Vector2 p = m.ToScreen(d.transform.position);
            float sz = d.WallMounted ? 17f : 15f;
            Diamond(p, sz, d.IsSwapped ? UITheme.Sage : KindColor(d.Kind), UITheme.Charcoal);
            Txt(new Rect(p.x - 10f, p.y - 8f, 20f, 16f), n.ToString(), TextRole.Mono, UITheme.Charcoal, TextAnchor.MiddleCenter, 9);
            if (names)
            {
                string t = ArtNames.FullName(d) + (ArtNames.IsFaded(d) ? " *" : "") + (d.IsSwapped ? "  " + Loc.T("plan.swapped") : "");
                Txt(new Rect(p.x - 80f, p.y + sz * 0.5f + 1f, 160f, 30f), t, TextRole.Caption, d.IsSwapped ? UITheme.Sage : UITheme.Ivory, TextAnchor.UpperCenter, 11, true);
            }
        }

        MuseumLayout lay = layout;
        bool hasB = lay.HasBasement;

        // Merdiven (üst kat): boşluk + simge; hapis alt katta olduğu için burada "B1" etiketiyle gösterilir
        if (hasB)
        {
            Rect hs = m.ToScreen(lay.StairsHoleWorld);
            UIUtil.DrawRect(hs, UITheme.WithAlpha(UITheme.Ochre, 0.35f));
            DrawStairsMarker(m.ToScreen(lay.StairsTopWorld), area, null, Loc.T("plan.stairs") + "  " + Loc.T("plan.basement"));
        }
        else if (JailCell.TryGetPlanPosition(out Vector3 jailPos))
        {
            // Bodrum yok (eski yerleşim): hücre ikonu (plan sınırının dışında kalsa bile kenara sıkıştırılır)
            DrawJailMarker(m.ToScreen(jailPos), area, true);
        }

        // Çıkış / kamyon: kamyon müzenin dışında; planın kenarına simge + etiket
        if (SpawnPoint.All.Count > 0)
        {
            Vector3 tp = Vector3.zero;
            int tn = 0;
            foreach (SpawnPoint sp in SpawnPoint.All) if (sp != null) { tp += sp.transform.position; tn++; }
            if (tn > 0) DrawTruckMarker(m.ToScreen(tp / tn), area, null);
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
        {
            string gh = Loc.T("plan.guardhint");
            float gw = Mathf.Min(area.width * 0.6f, TxtWidth(gh, TextRole.Caption, 12) + 16f);
            Rect chip = new Rect(area.x + 8f, area.yMax - 24f, gw, 18f);
            UIUtil.DrawRect(chip, UITheme.WithAlpha(UITheme.CharDeep, 0.75f));
            Txt(new Rect(chip.x + 6f, chip.y, chip.width - 8f, chip.height), gh, TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleLeft, 12);
        }

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
            bool rec = st != null && st.IsRecording;
            Vector2 p = pm.ToScreen(pos);
            if (rec)
            {
                Vector3 f = ps.transform.forward;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = Quaternion.Euler(0f, side * LiveFeed.Fov * 0.5f, 0f) * f;
                    for (int i = 1; i <= 8; i++)
                    {
                        Vector2 q = pm.ToScreen(pos + dir * (i * 0.6f));
                        UIUtil.DrawRect(new Rect(q.x - 1.5f, q.y - 1.5f, 3f, 3f), UITheme.WithAlpha(UITheme.Danger, 0.9f));
                    }
                }
            }
            // Renk noktası: koyu dış hat + fildişi halka + oyuncu rengi
            UIUtil.DrawRect(new Rect(p.x - 7f, p.y - 7f, 14f, 14f), UITheme.Charcoal);
            UIUtil.DrawRect(new Rect(p.x - 6f, p.y - 6f, 12f, 12f), UITheme.Ivory);
            UIUtil.DrawRect(new Rect(p.x - 4.5f, p.y - 4.5f, 9f, 9f), ps.PlayerColor);
            if (names)
            {
                float nw = Mathf.Max(40f, TxtWidth(ps.PlayerName, TextRole.BodyStrong, 12) + 12f);
                Rect nr = new Rect(p.x - nw * 0.5f, p.y - 26f, nw, 17f);
                UIUtil.DrawRect(nr, UITheme.WithAlpha(UITheme.CharDeep, 0.78f));
                UIUtil.DrawRect(new Rect(nr.x, nr.yMax - 2f, nr.width, 2f), ps.PlayerColor);
                Txt(nr, ps.PlayerName, TextRole.BodyStrong, UITheme.Ivory, TextAnchor.MiddleCenter, 12);
                if (rec) UIUtil.Badge(new Vector2(p.x - 18f, nr.y - 22f), "REC", UITheme.Danger, UITheme.Ivory);
            }
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
            case GuardAI.State.Chase: return UITheme.Danger;
            case GuardAI.State.Investigate: return UITheme.Terracotta;
            case GuardAI.State.Suspicious: return UITheme.Ochre;
            default: return UITheme.DangerLight;
        }
    }

    /// <summary>Güvenlik simgesi (Danger üçgen, bakış yönüne dönük) + görüş konisi çizgileri. Ekran konumunu döndürür (tıklama için).</summary>
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
        // Pinglenmiş güvenlik: nabızlı pirinç halka
        if (pingedUntil.TryGetValue(g, out float until) && Time.unscaledTime < until)
            UIUtil.Outline(new Rect(p.x - 12f, p.y - 12f, 24f, 24f), 2f, UITheme.WithAlpha(UITheme.BrassLight, 0.5f + 0.5f * UIUtil.Pulse(6f)));

        // Üçgenin ucu bakış yönünü gösterir (ekranda +x = dünya +x, yukarı = dünya +z)
        float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        Triangle(p, 13f, yaw, c, UITheme.Charcoal);
        return p;
    }

    // ---------------- Simgeler ----------------

    /// <summary>Merdiven simgesi (basamaklı kare) + etiket (label null değilse ya da text doluysa). BodyCam da kullanır.</summary>
    public static void DrawStairsMarker(Vector2 p, Rect clip, GUIStyle label, string text)
    {
        p.x = Mathf.Clamp(p.x, clip.xMin + 12f, clip.xMax - 12f);
        p.y = Mathf.Clamp(p.y, clip.yMin + 12f, clip.yMax - 22f);
        const float s = 16f;
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f - 1.5f, p.y - s * 0.5f - 1.5f, s + 3f, s + 3f), UITheme.Charcoal);
        UIUtil.DrawRect(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), UITheme.CharSoft);
        for (int i = 0; i < 4; i++)
            UIUtil.DrawRect(new Rect(p.x - s * 0.5f + 1.5f + i * 3f, p.y - s * 0.5f + 1.5f + i * 3.4f, s - 3f - i * 3f, 2.2f), UITheme.Ochre);
        if (string.IsNullOrEmpty(text)) return;
        float tw = Mathf.Min(220f, TxtWidth(text, TextRole.Caption, 11) + 10f);
        Rect lr = new Rect(p.x - tw * 0.5f, p.y + s * 0.5f + 2f, tw, 15f);
        UIUtil.DrawRect(lr, UITheme.WithAlpha(UITheme.CharDeep, 0.75f));
        Txt(lr, text, TextRole.Caption, UITheme.BrassLight, TextAnchor.MiddleCenter, 11);
    }

    /// <summary>Kamyon simgesi (kenara sıkıştırılır) + etiket.</summary>
    private static void DrawTruckMarker(Vector2 p, Rect clip, GUIStyle label)
    {
        p.x = Mathf.Clamp(p.x, clip.xMin + 16f, clip.xMax - 16f);
        p.y = Mathf.Clamp(p.y, clip.yMin + 12f, clip.yMax - 22f);
        Rect chip = new Rect(p.x - 13f, p.y - 11f, 26f, 22f);
        UIUtil.DrawRect(chip, UITheme.WithAlpha(UITheme.CharDeep, 0.85f));
        UIUtil.Outline(chip, 1f, UITheme.Sage);
        UIUtil.Icon(new Rect(p.x - 10f, p.y - 10f, 20f, 20f), IconId.Truck, UITheme.Sage);
        string t = Loc.T("plan.truck");
        float tw = Mathf.Min(180f, TxtWidth(t, TextRole.Caption, 11) + 10f);
        Rect lr = new Rect(p.x - tw * 0.5f, p.y + 13f, tw, 15f);
        UIUtil.DrawRect(lr, UITheme.WithAlpha(UITheme.CharDeep, 0.75f));
        Txt(lr, t, TextRole.Caption, UITheme.Sage, TextAnchor.MiddleCenter, 11);
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
        UIUtil.DrawRect(box, UITheme.WithAlpha(UITheme.CharDeep, 0.95f));
        UIUtil.Outline(box, 1f, UITheme.Brass);

        Rect inner = new Rect(box.x + 6f, box.y + 18f, box.width - 12f, box.height - 24f);
        float scale = Mathf.Min(inner.width / world.width, inner.height / world.height);
        bm = new Map
        {
            area = inner, world = world, scale = scale,
            offset = new Vector2(inner.x + (inner.width - world.width * scale) * 0.5f, inner.y + (inner.height - world.height * scale) * 0.5f)
        };
        Txt(new Rect(box.x + 6f, box.y + 1f, box.width - 12f, 16f), UITheme.Caps(Loc.T("plan.basement")), TextRole.Section, UITheme.Brass, TextAnchor.MiddleLeft, 12);
        Rect hs = bm.ToScreen(hall);
        UIUtil.DrawRect(hs, UITheme.WithAlpha(UITheme.Ivory, 0.07f));
        UIUtil.Outline(hs, 1f, UITheme.Graphite);
        UIUtil.DrawRect(bm.ToScreen(hole), UITheme.WithAlpha(UITheme.Ochre, 0.35f));
        if (JailCell.TryGetJailPosition(out Vector3 jp)) DrawJailMarker(bm.ToScreen(jp), inner, false);
        return true;
    }

    /// <summary>Hapis hücresi ikonu (Jail) + "Hapis" etiketi; BodyCam planı da kullanır (label yalnızca "etiket çiz" işareti).</summary>
    public static void DrawJailMarker(Vector2 p, Rect clip, GUIStyle label) => DrawJailMarker(p, clip, label != null);

    private static void DrawJailMarker(Vector2 p, Rect clip, bool showLabel)
    {
        p.x = Mathf.Clamp(p.x, clip.xMin + 12f, clip.xMax - 12f);
        p.y = Mathf.Clamp(p.y, clip.yMin + 12f, clip.yMax - 22f);
        Rect chip = new Rect(p.x - 11f, p.y - 11f, 22f, 22f);
        UIUtil.DrawRect(chip, UITheme.WithAlpha(UITheme.CharDeep, 0.85f));
        UIUtil.Outline(chip, 1f, UITheme.DangerLight);
        UIUtil.Icon(new Rect(p.x - 9f, p.y - 9f, 18f, 18f), IconId.Jail, UITheme.DangerLight);
        if (!showLabel) return;
        string t = Loc.T("plan.jail");
        float tw = Mathf.Min(120f, TxtWidth(t, TextRole.Caption, 11) + 10f);
        Rect lr = new Rect(p.x - tw * 0.5f, p.y + 12f, tw, 15f);
        UIUtil.DrawRect(lr, UITheme.WithAlpha(UITheme.CharDeep, 0.75f));
        Txt(lr, t, TextRole.Caption, UITheme.DangerLight, TextAnchor.MiddleCenter, 11);
    }

    // Eser türü rengi: yalnızca token paleti (Sage "değiştirildi" için ayrılmış)
    private static Color KindColor(ArtKind k)
    {
        switch (k)
        {
            case ArtKind.Painting: return UITheme.BrassLight;
            case ArtKind.Vase: return UITheme.Terracotta;
            case ArtKind.Jewelry: return UITheme.Ochre;
            case ArtKind.Bust: return UITheme.Ivory;
            default: return UITheme.IvoryDim;
        }
    }

    /// <summary>Planın yanındaki eser listesi: numara, tam ad, hangi oda. Sığmayanlar "+N" ile belirtilir.</summary>
    private static void DrawArtList(Rect r, float sc)
    {
        if (r.height < 40f) return;
        UIUtil.DrawRect(r, UITheme.WithAlpha(UITheme.CharDeep, 0.6f));
        UIUtil.Outline(r, 1f, UITheme.WithAlpha(UITheme.Brass, 0.5f));
        MuseumLayout layout = MuseumLayout.Instance;
        float rowH = Mathf.Round(22f * sc);
        int cap = Mathf.Max(1, Mathf.FloorToInt((r.height - 10f) / rowH));
        int total = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null && d.Artwork != null && d.IsActiveForMission) total++;
        int shown = total > cap ? cap - 1 : total;

        float y = r.y + 5f;
        int n = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !d.IsActiveForMission) continue;
            n++;
            if (n > shown) break;
            int room = layout != null ? layout.RoomAt(d.transform.position) : -1;
            string roomName = room < 0 ? "?" : RoomLabel(layout, room);
            string nm = ArtNames.FullName(d) + (ArtNames.IsFaded(d) ? "  " + Loc.T("plan.faded") : "") + (d.IsSwapped ? "  " + Loc.T("plan.swapped") : "");
            Color kc = d.IsSwapped ? UITheme.Sage : KindColor(d.Kind);

            Diamond(new Vector2(r.x + 14f, y + rowH * 0.5f), 11f, kc, UITheme.Charcoal);
            Txt(new Rect(r.x + 24f, y, 24f, rowH), n.ToString("00"), TextRole.Mono, UITheme.Brass, TextAnchor.MiddleLeft, 12);
            float roomW = Mathf.Min(r.width * 0.34f, TxtWidth(roomName, TextRole.Caption, 12) + 6f);
            Txt(new Rect(r.xMax - roomW - 8f, y, roomW, rowH), roomName, TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleRight, 12);
            Txt(new Rect(r.x + 50f, y, r.width - 58f - roomW, rowH), nm, TextRole.BodyStrong, d.IsSwapped ? UITheme.Sage : UITheme.Ivory, TextAnchor.MiddleLeft, 13);
            y += rowH;
        }
        if (total > shown)
            Txt(new Rect(r.x + 24f, y, r.width - 32f, rowH), "+" + (total - shown), TextRole.Mono, UITheme.IvoryDim, TextAnchor.MiddleLeft, 12);
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
