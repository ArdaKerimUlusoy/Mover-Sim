using UnityEngine;

/// <summary>
/// AYARLAR ekranı (ana menü ve Esc menüsü aynı ekranı kullanır): sekmeler — Ses, Görüntü, Kontroller, Oyun.
/// Her sekme kendi satırlarını çizer (etiket solda, kontrol sağda). Değerler GameSettings / VoiceChat / Loc'a yazılır
/// ve anında uygulanır; ekran kapanınca GameSettings.Save() çağrılır (çağıran yapar).
/// Kendi düzenini kurar (GUILayout yok): verilen Rect içine çizer, içerik uzunsa kaydırılır.
/// </summary>
public static class SettingsUI
{
    public enum Tab { Audio, Video, Controls, Game }

    /// <summary>Açık sekme (oturum boyunca hatırlanır).</summary>
    public static Tab Current = Tab.Audio;

    private static Vector2 scroll;
    private static float contentH = 400f;
    private static Resolution[] resolutions;

    private static readonly string[] TabKeys = { "settings.tab.audio", "settings.tab.video", "settings.tab.controls", "settings.tab.game" };

    /// <summary>
    /// Ayarlar içeriğini area içine çizer. inGame: oyun içindeyken (renk değişikliği oyuncuya uygulanır, öğretici düğmesi oyunu etkiler).
    /// localPlayer: oyundaki kendi oyuncun (renk için); menüde null.
    /// </summary>
    public static void Draw(Rect area, bool inGame, PlayerNetworkSetup localPlayer)
    {
        float sc = UITheme.Scale;
        float tabH = Mathf.Round(38f * sc);
        string[] labels = new string[TabKeys.Length];
        for (int i = 0; i < labels.Length; i++) labels[i] = Loc.T(TabKeys[i]);
        int t = UIUtil.Tabs(new Rect(area.x, area.y, area.width, tabH), labels, (int)Current);
        if (t != (int)Current) { Current = (Tab)t; scroll = Vector2.zero; GameAudio.Play2D(SoundId.UiClick, 0.4f); }

        Rect view = new Rect(area.x, area.y + tabH + 16f * sc, area.width, area.height - tabH - 16f * sc);
        bool scrolls = contentH > view.height + 1f;
        float w = view.width - (scrolls ? 14f : 0f);
        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, w, Mathf.Max(contentH, view.height)));
        float y = 0f;
        switch (Current)
        {
            case Tab.Audio: y = DrawAudio(w); break;
            case Tab.Video: y = DrawVideo(w); break;
            case Tab.Controls: y = DrawControls(w); break;
            default: y = DrawGame(w, inGame, localPlayer); break;
        }
        GUI.EndScrollView();
        if (Event.current.type == EventType.Repaint) contentH = y + 8f;
    }

    // =====================================================================
    //  Sekmeler
    // =====================================================================

    private static float DrawAudio(float w)
    {
        float y = 0f;
        Group(ref y, w, Loc.T("settings.group.general"));
        float mv = Slider(ref y, w, Loc.T("settings.master"), Pct(GameSettings.MasterVolume), GameSettings.MasterVolume, 0f, 1f);
        if (!Mathf.Approximately(mv, GameSettings.MasterVolume)) GameSettings.MasterVolume = mv;

        Group(ref y, w, Loc.T("settings.group.voice"));
        int mode = Choice(ref y, w, Loc.T("settings.voice.mode"), new[] { Loc.T("voice.mode.ptt"), Loc.T("voice.mode.open") }, VoiceChat.OpenMic ? 1 : 0);
        if ((mode == 1) != VoiceChat.OpenMic) VoiceChat.OpenMic = mode == 1;

        string[] devs = VoiceChat.MicDevices;
        if (devs == null || devs.Length == 0) Note(ref y, w, Loc.T("voice.nodevice"));
        else
        {
            int idx = System.Array.IndexOf(devs, VoiceChat.DeviceName);
            if (idx < 0) idx = 0;
            int pick = Cycle(ref y, w, Loc.T("settings.voice.device"), devs[idx], idx, devs.Length);
            if (pick != idx)
            {
                VoiceChat.DeviceName = devs[pick];
                if (VoiceChat.Instance != null) VoiceChat.Instance.RestartMic();
            }
        }
        float vv = Slider(ref y, w, Loc.T("settings.voice.volume"), Pct(VoiceChat.VoiceVolume), VoiceChat.VoiceVolume, 0f, 2f);
        if (!Mathf.Approximately(vv, VoiceChat.VoiceVolume)) VoiceChat.VoiceVolume = vv;
        float mg = Slider(ref y, w, Loc.T("settings.voice.gain"), VoiceChat.MicGain.ToString("0.0") + "x", VoiceChat.MicGain, 0.5f, 4f);
        if (!Mathf.Approximately(mg, VoiceChat.MicGain)) VoiceChat.MicGain = mg;

        // Eşik: canlı mikrofon seviyesinin üstünde (konuşurken geçen, sessizken geçmeyen yere getir)
        float before = y;
        float gt = Slider(ref y, w, Loc.T("settings.voice.gate"), "", VoiceChat.Gate, 0f, 0.1f);
        if (!Mathf.Approximately(gt, VoiceChat.Gate)) VoiceChat.Gate = gt;
        Rect meter = new Rect(w * 0.42f, before + RowH * 0.5f - 13f * UITheme.Scale, w * 0.58f - 50f, 4f);
        float lvl = VoiceChat.Instance != null ? VoiceChat.Instance.InputLevel : 0f;
        UIUtil.Round(meter, new Color(0f, 0f, 0f, 0.35f), 2f);
        UIUtil.Round(new Rect(meter.x, meter.y, meter.width * Mathf.Clamp01(lvl), meter.height), UITheme.Sage, 2f);
        Note(ref y, w, Loc.T("settings.voice.gatehelp"));

        bool detect = Toggle(ref y, w, Loc.T("settings.crimewords"), VoiceKeywordListener.DetectionEnabled);
        if (detect != VoiceKeywordListener.DetectionEnabled) VoiceKeywordListener.DownloadConsent = detect;
        return y;
    }

    private static float DrawVideo(float w)
    {
        float y = 0f;
        Group(ref y, w, Loc.T("settings.group.display"));

        int fs = Choice(ref y, w, Loc.T("settings.window"), new[] { Loc.T("settings.window.full"), Loc.T("settings.window.windowed") }, GameSettings.Fullscreen ? 0 : 1);
        if ((fs == 0) != GameSettings.Fullscreen) GameSettings.Fullscreen = fs == 0;

        if (resolutions == null || resolutions.Length == 0) resolutions = GameSettings.AvailableResolutions();
        if (resolutions.Length > 0)
        {
            int cur = GameSettings.ResolutionIndex(resolutions);
            Resolution r = resolutions[Mathf.Clamp(cur, 0, resolutions.Length - 1)];
            int pick = Cycle(ref y, w, Loc.T("settings.resolution"), r.width + " × " + r.height, cur, resolutions.Length);
            if (pick != cur) GameSettings.SetResolution(resolutions[pick]);
        }

        string[] q = QualitySettings.names;
        if (q != null && q.Length > 0)
        {
            int cur = Mathf.Clamp(GameSettings.QualityLevel, 0, q.Length - 1);
            int pick = Cycle(ref y, w, Loc.T("settings.quality"), q[cur], cur, q.Length);
            if (pick != cur) GameSettings.QualityLevel = pick;
        }

        bool vs = Toggle(ref y, w, Loc.T("settings.vsync"), GameSettings.VSync);
        if (vs != GameSettings.VSync) GameSettings.VSync = vs;
        if (!GameSettings.VSync)
        {
            int[] caps = GameSettings.FrameCaps;
            int ci = System.Array.IndexOf(caps, GameSettings.FrameCap);
            if (ci < 0) ci = caps.Length - 1;
            string shown = caps[ci] <= 0 ? Loc.T("settings.fps.unlimited") : caps[ci].ToString();
            int pick = Cycle(ref y, w, Loc.T("settings.fps"), shown, ci, caps.Length);
            if (pick != ci) GameSettings.FrameCap = caps[pick];
        }

        Group(ref y, w, Loc.T("settings.group.view"));
        float fov = GameSettings.FieldOfView;
        float nf = Slider(ref y, w, Loc.T("settings.fov"), Mathf.RoundToInt(fov) + "°", fov, GameSettings.MinFov, GameSettings.MaxFov);
        if (Mathf.Abs(nf - fov) > 0.01f) GameSettings.FieldOfView = Mathf.Round(nf);
        return y;
    }

    private static float DrawControls(float w)
    {
        float y = 0f;
        Group(ref y, w, Loc.T("settings.group.mouse"));
        float ms = Slider(ref y, w, Loc.T("settings.sensitivity"), GameSettings.MouseSensitivity.ToString("0.0") + "x",
            GameSettings.MouseSensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
        if (!Mathf.Approximately(ms, GameSettings.MouseSensitivity)) GameSettings.MouseSensitivity = ms;
        bool inv = Toggle(ref y, w, Loc.T("settings.inverty"), GameSettings.InvertY);
        if (inv != GameSettings.InvertY) GameSettings.InvertY = inv;

        Group(ref y, w, Loc.T("settings.group.keys"));
        string[,] keys =
        {
            { "W A S D", "keys.move" }, { "Shift", "keys.run" }, { "Ctrl / C", "keys.crouch" }, { "Space", "keys.jump" },
            { "E", "keys.interact" }, { "Tab", "keys.bag" }, { "1 2 3", "keys.quick" }, { "Q", "keys.coin" },
            { "F", "keys.push" }, { "H", "keys.record" }, { "B", "keys.brush" }, { "M", "keys.plan" },
            { "K", "keys.truck" }, { "V", "keys.voice" }, { "Mouse 3", "keys.ping" }, { "Enter", "keys.chat" },
            { "F5", "keys.preview" }, { "Esc", "keys.menu" },
        };
        float sc = UITheme.Scale;
        float rh = Mathf.Round(30f * sc);
        for (int i = 0; i < keys.GetLength(0); i++)
        {
            Rect row = new Rect(0f, y, w, rh);
            if (i % 2 == 0) UIUtil.Round(row, new Color(1f, 1f, 1f, 0.03f), 4f);
            UIUtil.Text(new Rect(row.x + 10f, row.y, w * 0.6f, rh), Loc.T(keys[i, 1]), TextRole.Body, UITheme.Ivory);
            string k = keys[i, 0];
            float kw = UIUtil.KeyCapWidth(k, 13);
            UIUtil.KeyCap(new Rect(row.xMax - kw - 10f, row.y + 5f, kw, rh - 10f), k, 13);
            y += rh;
        }
        return y + 6f;
    }

    private static float DrawGame(float w, bool inGame, PlayerNetworkSetup localPlayer)
    {
        float y = 0f;
        Group(ref y, w, Loc.T("settings.group.game"));
        int lang = Choice(ref y, w, Loc.T("settings.language"), Loc.LanguageNames, (int)Loc.Current);
        if (lang != (int)Loc.Current) Loc.Current = (Language)lang;

        // Renk (ekipte kim kim belli olsun): oyundaysan herkese uygulanır
        float sc = UITheme.Scale;
        Rect row = new Rect(0f, y, w, RowH);
        UIUtil.Text(new Rect(0f, row.y, w * 0.4f, RowH), Loc.T("settings.color"), TextRole.Body, UITheme.Ivory);
        int current = inGame && localPlayer != null ? localPlayer.ColorIndex : PlayerNetworkSetup.SavedColorIndex;
        int picked = ColorRow(new Rect(w * 0.42f, row.y + (RowH - 26f * sc) * 0.5f, w * 0.58f, 26f * sc), current);
        if (picked != current)
        {
            if (inGame && localPlayer != null) localPlayer.SetColor(picked);
            else PlayerNetworkSetup.SavedColorIndex = picked;
        }
        y += RowH + 6f;

        Group(ref y, w, Loc.T("settings.group.tutorial"));
        Note(ref y, w, Loc.T(TutorialUI.Active ? "settings.tutorial.running" : TutorialUI.Completed ? "menu.tutorial.done" : "menu.tutorial.pending"));
        float bh = Mathf.Round(36f * sc);
        float bw = Mathf.Min(w, 260f * sc);
        if (UIUtil.Button(new Rect(0f, y, bw, bh), Loc.T(inGame ? "pause.tutorial.restart" : "menu.tutorial.reset"), ButtonKind.Secondary))
            TutorialUI.Restart();
        if (inGame && TutorialUI.Active && UIUtil.Button(new Rect(bw + 8f, y, Mathf.Min(w - bw - 8f, 160f * sc), bh), Loc.T("pause.tutorial.skip"), ButtonKind.Secondary))
            TutorialUI.Skip();
        y += bh + 8f;
        return y;
    }

    // =====================================================================
    //  Satırlar (etiket solda, kontrol sağda)
    // =====================================================================

    private static float RowH => Mathf.Round(44f * UITheme.Scale);

    private static string Pct(float v) => "%" + Mathf.RoundToInt(v * 100f);

    private static void Group(ref float y, float w, string title)
    {
        if (y > 0f) y += 14f * UITheme.Scale;
        UIUtil.Text(new Rect(0f, y, w, 20f * UITheme.Scale), title, TextRole.Section, UITheme.IvoryDim);
        y += 26f * UITheme.Scale;
    }

    private static void Note(ref float y, float w, string text)
    {
        float h = UIUtil.MeasureHeight(text, TextRole.Caption, w);
        UIUtil.Text(new Rect(0f, y, w, h), text, TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
        y += h + 6f;
    }

    private static float Slider(ref float y, float w, string label, string value, float v, float min, float max)
    {
        float rh = RowH;
        UIUtil.Text(new Rect(0f, y, w * 0.4f, rh), label, TextRole.Body, UITheme.Ivory);
        float vw = 46f;
        if (!string.IsNullOrEmpty(value)) UIUtil.Text(new Rect(w - vw, y, vw, rh), value, TextRole.BodyStrong, UITheme.IvoryDim, TextAnchor.MiddleRight);
        Rect s = new Rect(w * 0.42f, y + rh * 0.5f - 7f, w * 0.58f - vw - 4f, 14f);
        float nv = GUI.HorizontalSlider(s, v, min, max);
        y += rh;
        return nv;
    }

    /// <summary>Az seçenekli satır: parçalı seçici. Seçilen indeksi döner.</summary>
    private static int Choice(ref float y, float w, string label, string[] options, int selected)
    {
        float rh = RowH;
        UIUtil.Text(new Rect(0f, y, w * 0.4f, rh), label, TextRole.Body, UITheme.Ivory);
        float th = Mathf.Round(32f * UITheme.Scale);
        int r = UIUtil.Tabs(new Rect(w * 0.42f, y + (rh - th) * 0.5f, w * 0.58f, th), options, selected);
        y += rh;
        return r;
    }

    /// <summary>Çok seçenekli satır: ‹ değer › (döngüsel). Yeni indeksi döner.</summary>
    private static int Cycle(ref float y, float w, string label, string shown, int index, int count)
    {
        float rh = RowH, sc = UITheme.Scale;
        UIUtil.Text(new Rect(0f, y, w * 0.4f, rh), label, TextRole.Body, UITheme.Ivory);
        float bh = Mathf.Round(32f * sc), bw = bh;
        Rect box = new Rect(w * 0.42f, y + (rh - bh) * 0.5f, w * 0.58f, bh);
        UIUtil.Round(box, new Color(1f, 1f, 1f, 0.06f), UIUtil.RadiusSmall + 2f);
        int r = index;
        if (UIUtil.Button(new Rect(box.x + 3f, box.y + 3f, bw - 6f, bh - 6f), "‹", ButtonKind.Tool, count > 1)) r = (index - 1 + count) % count;
        if (UIUtil.Button(new Rect(box.xMax - bw + 3f, box.y + 3f, bw - 6f, bh - 6f), "›", ButtonKind.Tool, count > 1)) r = (index + 1) % count;
        string fit = EconomyPanelUI.Fit(shown, TextRole.BodyStrong, box.width - bw * 2f - 8f);
        UIUtil.Text(new Rect(box.x + bw, box.y, box.width - bw * 2f, bh), fit, TextRole.BodyStrong, UITheme.Ivory, TextAnchor.MiddleCenter);
        y += rh;
        return r;
    }

    private static bool Toggle(ref float y, float w, string label, bool on)
    {
        int r = Choice(ref y, w, label, new[] { Loc.T("settings.off"), Loc.T("settings.on") }, on ? 1 : 0);
        return r == 1;
    }

    /// <summary>Oyuncu renkleri: başkasının aldığı renk soluk ve seçilemez. Tıklanan rengin numarasını döner.</summary>
    public static int ColorRow(Rect area, int selected, bool onLight = false)
    {
        int result = selected;
        int n = PlayerNetworkSetup.Colors.Length;
        float size = area.height;
        float gap = Mathf.Max(6f, size * 0.3f);
        if (n > 0 && n * size + (n - 1) * gap > area.width) { gap = 6f; size = (area.width - (n - 1) * gap) / n; }
        for (int i = 0; i < n; i++)
        {
            Rect r = new Rect(area.x + i * (size + gap), area.y + (area.height - size) * 0.5f, size, size);
            bool taken = i != selected && PlayerNetworkSetup.IsColorTakenByOther(i);
            if (UIUtil.Swatch(r, PlayerNetworkSetup.Colors[i], i == selected, !taken, onLight)) result = i;
        }
        return result;
    }
}
