using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// TEK OYUNCULU MOD: FOTOĞRAF MAKİNESİ + SINIRLI FİLM.
/// Tek başına oynarken canlı kayıt (H, BodyCam) olmaz: aynı anda hem müzede kayıt alıp hem kamyonda çizemezsin. Onun yerine:
///  - [H] fotoğraf makinesini kaldır / indir (vizör). [Sol tık] çek. Makine eser bulunursa ona otomatik odaklanır (kadraj eseri doldurur).
///  - FİLM sınırlı: görev başına 6 kare (+2 / garajdaki "Kamera Pili" seviyesi). Yeni görevde film ve fotoğraflar yenilenir.
///  - Fotoğraf YASAK odada çekerken güvenlik / kamera görürse şüphe artar (görmezse sorun yok; vizörde uyarı çıkar).
///  - Kamyondaki atölyede çizim, o eserin fotoğrafına bakarak yapılır (fotoğraf yoksa görüntü yok, benzerlik gizli).
///    Büst / vazo / yüzükte sağ üstteki panelden hangi fotoğrafa bakılacağı seçilir; tabloda o eserin son fotoğrafı kullanılır.
///  - Kamyon monitöründe son çekilen fotoğraf görünür.
/// Yalnızca SessionManager.IsSinglePlayer iken çalışır; çok oyunculu oyun hiç etkilenmez. Ağ mesajı yok (tek oyuncu = host).
/// Optimizasyon: fotoğraf kamerası yalnızca deklanşöre basıldığı TEK karede çizer; fotoğraflar 768x432 doku.
/// </summary>
public class SoloPhotoCamera : MonoBehaviour
{
    public const int BaseFilm = 6;
    public const int FilmPerBatteryLevel = 2;
    private const int PhotoW = 768, PhotoH = 432;
    private const float TargetRange = 9f;
    private const float TargetAngle = 14f;

    private static SoloPhotoCamera instance;

    private sealed class Photo
    {
        public RenderTexture tex;
        public string target;      // eser objesi adı (Display_...) — eser yoksa null
        public string label;       // ekranda görünen ad
        public float time;
    }

    private readonly List<Photo> photos = new List<Photo>();
    private int filmUsed;
    private int lastSeed = int.MinValue;
    private bool aiming;
    private float aimSince;
    private float flashAt = -10f;
    private Photo lastShot;
    private float lastShotAt = -10f;

    // Atölyede seçim (büst / vazo / yüzük): -1 = otomatik (bu eserin son fotoğrafı)
    private int picked = -1;
    private string pickedFor;

    // Çekim: kamera bir kare açık kalır, sonraki karede kapanır
    private Camera shotCam;
    private Photo pendingShot;
    private int shotFrame = -1;
    private readonly List<Renderer> hidden = new List<Renderer>();

    // Vizörde odak (0.1 sn'de bir)
    private ArtworkDisplay focus;
    private float nextFocus;
    private bool noPhotoHere;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("SoloPhotoCamera");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoloPhotoCamera>();
    }

    // ---------------- Dışarıya açık ----------------

    /// <summary>Tek oyunculu oyunda mıyız (fotoğraf makinesi modu)?</summary>
    public static bool Active
    {
        get
        {
            SessionManager sm = SessionManager.Instance;
            NetworkManager nm = NetworkManager.Singleton;
            return sm != null && sm.IsSinglePlayer && nm != null && nm.IsListening;
        }
    }

    /// <summary>Tek oyunculu (fotoğraf) modda bu metnin ".photo" sürümü varsa onu, yoksa kendisini döner.</summary>
    public static string K(string key)
    {
        if (string.IsNullOrEmpty(key) || !Active) return key;
        string alt = key + ".photo";
        return Loc.Has(alt) ? alt : key;
    }

    /// <summary>Vizör açık mı (sol tık fotoğraf çeker; fırça vb. bu sırada çalışmaz)?</summary>
    public static bool Aiming => instance != null && instance.aiming && Active;

    public static int PhotoCount => instance != null ? instance.photos.Count : 0;
    public static int FilmLeft => instance != null ? Mathf.Max(0, instance.FilmTotal - instance.filmUsed) : 0;

    private int FilmTotal
    {
        get
        {
            PlayerInventory inv = PlayerInventory.Local;
            int lv = inv != null ? inv.GetUpgradeLevel(UpgradeType.CameraBattery) : 0;
            return BaseFilm + FilmPerBatteryLevel * lv;
        }
    }

    /// <summary>Son çekilen fotoğraf (kamyon monitörü için). Yoksa null.</summary>
    public static Texture Latest => instance != null && instance.photos.Count > 0 ? instance.photos[instance.photos.Count - 1].tex : null;

    /// <summary>Atölye: bu eser için gösterilecek fotoğraf (seçilen ya da o eserin son fotoğrafı). Yoksa null.</summary>
    public static Texture ReferenceFor(ArtworkDisplay target, out string label)
    {
        label = Loc.T("photo.none");
        if (instance == null) return null;
        Photo p = instance.CurrentFor(target);
        if (p == null) return null;
        label = Loc.T("photo.ref", p.label);
        return p.tex;
    }

    private Photo CurrentFor(ArtworkDisplay target)
    {
        string tn = target != null ? target.name : null;
        if (pickedFor != tn) { pickedFor = tn; picked = -1; } // başka esere geçildi: otomatik seçim
        if (picked >= 0 && picked < photos.Count) return photos[picked];
        for (int i = photos.Count - 1; i >= 0; i--)
            if (photos[i].target != null && photos[i].target == tn) return photos[i];
        return null;
    }

    // ---------------- Döngü ----------------

    private void Update()
    {
        // Çekilen karenin kamerasını kapat, gizlenenleri geri aç
        if (shotCam != null && shotCam.enabled && Time.frameCount > shotFrame)
        {
            shotCam.enabled = false;
            shotCam.targetTexture = null;
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            pendingShot = null;
        }

        if (!Active) { if (aiming) aiming = false; if (photos.Count > 0 && !InGame()) ClearAll(); return; }

        // Yeni görev (yeni müze): film ve fotoğraflar yenilenir
        MuseumSession ms = MuseumSession.Instance;
        if (ms != null && ms.Ready && ms.Seed != lastSeed)
        {
            lastSeed = ms.Seed;
            ClearAll();
        }

        PlayerStealth me = PlayerStealth.Local;
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool canAct = me != null && me.IsSpawned && !me.IsJailed && me.CurrentHidingSpot == null && kb != null &&
                      !ChatUI.IsTyping && !SessionMenuUI.IsPaused && !PlayerInventory.IsPanelOpen && !WorkbenchUI.IsOpen &&
                      !BodyCam.PanelOpen && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen && !PlayerController.InputLocked &&
                      !PushSystem.LocalKnockedOut && Cursor.lockState == CursorLockMode.Locked;
        if (canAct)
        {
            ObjectCarrier carrier = me.GetComponent<ObjectCarrier>();
            if (carrier != null && carrier.IsHolding) canAct = false; // taşırken sol tık fırlatma: makine kalkmaz
        }
        if (!canAct) { aiming = false; return; }

        if (kb.hKey.wasPressedThisFrame)
        {
            aiming = !aiming;
            aimSince = Time.unscaledTime;
            GameAudio.Play2D(SoundId.BagOpen, 0.35f);
        }
        if (!aiming) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        if (Time.unscaledTime >= nextFocus)
        {
            nextFocus = Time.unscaledTime + 0.1f;
            focus = FindFocus(cam.transform.position, cam.transform.forward);
            MuseumLayout l = MuseumLayout.Instance;
            noPhotoHere = l != null && l.IsNoPhotoAt(me.transform.position);
        }

        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Time.unscaledTime - aimSince > 0.15f) Shoot(cam, me);
    }

    private static bool InGame()
    {
        SessionManager sm = SessionManager.Instance;
        return sm != null && sm.InGame;
    }

    private void ClearAll()
    {
        foreach (Photo p in photos) if (p.tex != null) { p.tex.Release(); Destroy(p.tex); }
        photos.Clear();
        filmUsed = 0;
        picked = -1;
        lastShot = null;
    }

    /// <summary>Vizörün ortasına en yakın, görünen, bu görevdeki eser.</summary>
    private static ArtworkDisplay FindFocus(Vector3 eye, Vector3 fwd)
    {
        ArtworkDisplay best = null;
        float bestA = TargetAngle;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || !d.IsActiveForMission) continue;
            Vector3 c = FocusPoint(d, out _);
            Vector3 to = c - eye;
            float dist = to.magnitude;
            if (dist > TargetRange || dist < 0.2f) continue;
            float a = Vector3.Angle(fwd, to);
            if (a > bestA) continue;
            if (Physics.Raycast(eye, to / dist, out RaycastHit hit, dist - 0.3f, ~0, QueryTriggerInteraction.Ignore) &&
                !hit.collider.transform.IsChildOf(d.transform) && hit.collider.GetComponentInParent<PlayerStealth>() == null) continue;
            bestA = a;
            best = d;
        }
        return best;
    }

    /// <summary>Eserin görselinin merkezi ve yarıçapı (görsel yoksa kaidenin üstü).</summary>
    private static Vector3 FocusPoint(ArtworkDisplay d, out float radius)
    {
        GameObject v = d.Visual;
        if (v != null && ItemVisuals.TryGetBounds(v, out Bounds b))
        {
            radius = Mathf.Max(0.08f, b.extents.magnitude);
            return b.center;
        }
        radius = 0.4f;
        return d.transform.position + Vector3.up * (d.WallMounted ? 0f : 0.4f);
    }

    private void Shoot(Camera cam, PlayerStealth me)
    {
        if (pendingShot != null) return;
        if (FilmLeft <= 0)
        {
            if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(Loc.T("photo.nofilm"));
            GameAudio.Play2D(SoundId.UiClick, 0.3f);
            return;
        }
        EnsureCam();

        // Kadraj: eser bulunduysa otomatik odak (eseri dolduran dar açı), yoksa gördüğün kadar
        Vector3 eye = cam.transform.position;
        Quaternion rot = cam.transform.rotation;
        float fov = cam.fieldOfView;
        ArtworkDisplay target = focus != null ? focus : FindFocus(eye, cam.transform.forward);
        if (target != null)
        {
            Vector3 c = FocusPoint(target, out float rad);
            Vector3 to = c - eye;
            float dist = Mathf.Max(0.3f, to.magnitude);
            rot = Quaternion.LookRotation(to / dist, Vector3.up);
            fov = Mathf.Clamp(2f * Mathf.Atan(rad * 1.35f / dist) * Mathf.Rad2Deg, 10f, cam.fieldOfView);
        }

        var tex = new RenderTexture(PhotoW, PhotoH, 24) { name = "SoloPhoto" };
        tex.Create();
        shotCam.CopyFrom(cam);
        shotCam.enabled = false;
        shotCam.targetTexture = tex;
        shotCam.transform.SetPositionAndRotation(eye, rot);
        shotCam.fieldOfView = fov;
        shotCam.nearClipPlane = 0.1f;
        shotCam.depth = cam.depth - 1;
        UniversalAdditionalCameraData data = shotCam.GetUniversalAdditionalCameraData();
        if (data != null) { data.renderPostProcessing = false; data.requiresColorTexture = false; data.requiresDepthTexture = false; data.antialiasing = AntialiasingMode.None; }

        // Kendi elin / eldeki eşya (kameranın çocukları) fotoğrafa girmesin: bu kare gizlenir
        hidden.Clear();
        foreach (Renderer r in cam.GetComponentsInChildren<Renderer>())
            if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); }

        MuseumSession ms = MuseumSession.Instance;
        var p = new Photo
        {
            tex = tex,
            target = target != null ? target.name : null,
            label = target != null ? (ms != null ? ms.ArtworkName(target) : target.name) : Loc.T("photo.untitled"),
            time = Time.unscaledTime
        };
        photos.Add(p);
        pendingShot = p;
        shotFrame = Time.frameCount;
        shotCam.enabled = true; // bu karenin sonunda bir kez çizer, sonraki Update'te kapanır
        filmUsed++;
        lastShot = p;
        lastShotAt = Time.unscaledTime;
        flashAt = Time.unscaledTime;
        GameAudio.Play2D(SoundId.UiClick, 0.9f);
        GameAudio.Play2D(SoundId.Pickpocket, 0.5f);

        // Fotoğraf yasak odada: güvenlik / kamera görürse şüphe artar
        if (noPhotoHere && me != null && me.IsServer)
        {
            if (JailCell.SeenBySecurity(me))
            {
                me.ServerAddSuspicionNoAlarm(0.45f);
                if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(Loc.T("photo.seen"));
            }
        }
        if (target == null && PlayerInventory.Local != null) UIUtil.ShowToast(Loc.T("photo.notarget"), ToastKind.Warning, 4f, IconId.Camera);
    }

    private void EnsureCam()
    {
        if (shotCam != null) return;
        GameObject go = new GameObject("SoloPhotoCamera_Lens");
        go.transform.SetParent(transform, false);
        shotCam = go.AddComponent<Camera>();
        shotCam.enabled = false;
    }

    // ---------------- Atölye: fotoğraf seçici ----------------

    /// <summary>Atölyede (büst / vazo / yüzük) fotoğraf küçük resimleri; tıklayınca o fotoğraf referans olur. Kullandığı yüksekliği döner.
    /// Yalnızca Repaint ve MouseDown'da çağrılır (BodyCam'in çizim düzeniyle aynı).</summary>
    public static float DrawPicker(Rect area, ArtworkDisplay target)
    {
        if (instance == null) return 0f;
        return instance.Picker(area, target);
    }

    private float Picker(Rect area, ArtworkDisplay target)
    {
        Event ev = Event.current;
        float sc = UITheme.Scale;
        float titleH = 18f * sc;
        string title = UITheme.Caps(Loc.T("photo.album", photos.Count, FilmTotal));
        if (ev.type == EventType.Repaint) UIUtil.Text(new Rect(area.x, area.y, area.width, titleH), title, TextRole.Section, UITheme.Brass);
        float y = area.y + titleH + 4f;
        if (photos.Count == 0)
        {
            string none = Loc.T("photo.album.empty");
            float h = Mathf.Ceil(UIUtil.MeasureHeight(none, TextRole.Caption, area.width));
            if (ev.type == EventType.Repaint) UIUtil.Text(new Rect(area.x, y, area.width, h), none, TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
            return titleH + 4f + h;
        }
        Photo cur = CurrentFor(target);
        float tw = Mathf.Floor((area.width - 3f * 6f) / 4f), th = Mathf.Floor(tw * 9f / 16f);
        float capH = 14f * sc;
        int col = 0;
        float x = area.x;
        for (int i = photos.Count - 1; i >= 0; i--) // en yeni önce
        {
            Photo p = photos[i];
            Rect r = new Rect(x, y, tw, th);
            bool sel = p == cur;
            bool match = target != null && p.target == target.name;
            if (ev.type == EventType.Repaint)
            {
                UIUtil.DrawRect(r, UITheme.CharDeep);
                if (p.tex != null) GUI.DrawTexture(r, p.tex, ScaleMode.ScaleAndCrop, false);
                if (sel) UIUtil.BrassFrame(r);
                else if (r.Contains(ev.mousePosition)) UIUtil.DrawRect(r, new Color(1f, 1f, 1f, 0.08f));
                UIUtil.Text(new Rect(r.x, r.yMax + 1f, r.width, capH), p.label, TextRole.Caption, match ? UITheme.Sage : UITheme.IvoryDim);
            }
            else if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition))
            {
                picked = i;
                pickedFor = target != null ? target.name : null;
                ev.Use();
                GameAudio.Play2D(SoundId.UiClick, 0.5f);
            }
            col++;
            x += tw + 6f;
            if (col == 4 && i > 0) { col = 0; x = area.x; y += th + capH + 6f; }
        }
        return (y + th + capH + 2f) - area.y;
    }

    // ---------------- Ekran ----------------

    private void OnGUI()
    {
        if (!Active || Event.current.type != EventType.Repaint) return;
        if (SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BodyCam.PanelOpen || BriefingUI.IsOpen || FloorPlanUI.IsOpen) return;
        PlayerStealth me = PlayerStealth.Local;
        if (me == null || !me.IsSpawned || me.IsJailed) return;
        float sc = UITheme.Scale;

        // Deklanşör flaşı
        float fl = 1f - (Time.unscaledTime - flashAt) / 0.22f;
        if (fl > 0f) UIUtil.DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(1f, 1f, 1f, 0.75f * fl));

        if (aiming) DrawViewfinder(sc);
        else DrawHudCard(sc);

        // Son çekilen fotoğraf: sağ altta kısa süre "polaroid"
        float age = Time.unscaledTime - lastShotAt;
        if (lastShot != null && lastShot.tex != null && age < 2.6f)
        {
            float a = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((2.6f - age) / 0.4f);
            float w = 200f * sc, h = w * 9f / 16f;
            Rect card = new Rect(Screen.width - w - 34f * sc, Screen.height * 0.42f + (1f - a) * 14f, w + 16f, h + 36f * sc);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            UIUtil.Round(card, UITheme.Ivory, 4f);
            Rect img = new Rect(card.x + 8f, card.y + 8f, w, h);
            GUI.DrawTexture(img, lastShot.tex, ScaleMode.ScaleAndCrop, false);
            UIUtil.Text(new Rect(card.x + 8f, img.yMax + 4f, w, 20f * sc), lastShot.label, TextRole.Caption, UITheme.Charcoal, TextAnchor.MiddleCenter);
            GUI.color = old;
        }
    }

    /// <summary>Vizör kapalıyken sol altta: makine ve kalan film (kayıt pili kartının yerinde).</summary>
    private void DrawHudCard(float sc)
    {
        GamePhase ph = GameFlow.Phase;
        if (ph != GamePhase.Museum && ph != GamePhase.Truck) return;
        string t1 = Loc.T("photo.hud");
        string t2 = Loc.T("photo.film", FilmLeft, FilmTotal);
        float w = Mathf.Max(UIUtil.Measure(t1, TextRole.BodyStrong).x, UIUtil.Measure(t2, TextRole.Caption).x) + 52f * sc;
        float h = 46f * sc;
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.CamBattery, w, h);
        UIUtil.HudCard(r);
        float s = 20f * sc;
        UIUtil.Icon(new Rect(r.x + 10f * sc, r.y + (h - s) * 0.5f, s, s), IconId.Camera, FilmLeft > 0 ? UITheme.Ivory : UITheme.DangerLight);
        float x = r.x + 18f * sc + s;
        UIUtil.Text(new Rect(x, r.y + 4f * sc, r.width - (x - r.x) - 6f, 20f * sc), t1, TextRole.BodyStrong, UITheme.Ivory);
        UIUtil.Text(new Rect(x, r.y + 24f * sc, r.width - (x - r.x) - 6f, 18f * sc), t2, TextRole.Caption, FilmLeft > 0 ? UITheme.IvoryDim : UITheme.DangerLight);
    }

    /// <summary>Vizör: kenarlar karartılır, köşe çerçeveleri, ortada odak işareti; üstte odaklanan eser, altta film ve tuşlar.</summary>
    private void DrawViewfinder(float sc)
    {
        float W = Screen.width, H = Screen.height;
        float fw = Mathf.Min(W * 0.78f, H * 0.78f * 16f / 9f), fh = fw * 9f / 16f;
        Rect f = new Rect((W - fw) * 0.5f, (H - fh) * 0.5f, fw, fh);
        Color dark = new Color(0f, 0f, 0f, 0.55f);
        UIUtil.DrawRect(new Rect(0f, 0f, W, f.y), dark);
        UIUtil.DrawRect(new Rect(0f, f.yMax, W, H - f.yMax), dark);
        UIUtil.DrawRect(new Rect(0f, f.y, f.x, fh), dark);
        UIUtil.DrawRect(new Rect(f.xMax, f.y, W - f.xMax, fh), dark);

        bool ok = focus != null;
        Color line = noPhotoHere ? UITheme.DangerLight : ok ? UITheme.Sage : UITheme.Ivory;
        float L = 34f * sc, t = 3f;
        // köşe çerçeveleri
        UIUtil.DrawRect(new Rect(f.x, f.y, L, t), line); UIUtil.DrawRect(new Rect(f.x, f.y, t, L), line);
        UIUtil.DrawRect(new Rect(f.xMax - L, f.y, L, t), line); UIUtil.DrawRect(new Rect(f.xMax - t, f.y, t, L), line);
        UIUtil.DrawRect(new Rect(f.x, f.yMax - t, L, t), line); UIUtil.DrawRect(new Rect(f.x, f.yMax - L, t, L), line);
        UIUtil.DrawRect(new Rect(f.xMax - L, f.yMax - t, L, t), line); UIUtil.DrawRect(new Rect(f.xMax - t, f.yMax - L, t, L), line);
        // odak işareti
        Vector2 c = new Vector2(W * 0.5f, H * 0.5f);
        float g = 14f * sc;
        UIUtil.DrawRect(new Rect(c.x - g, c.y - 1f, g * 0.6f, 2f), line); UIUtil.DrawRect(new Rect(c.x + g * 0.4f, c.y - 1f, g * 0.6f, 2f), line);
        UIUtil.DrawRect(new Rect(c.x - 1f, c.y - g, 2f, g * 0.6f), line); UIUtil.DrawRect(new Rect(c.x - 1f, c.y + g * 0.4f, 2f, g * 0.6f), line);

        // Üst: odak
        MuseumSession ms = MuseumSession.Instance;
        string top = ok ? Loc.T("photo.focus", ms != null ? ms.ArtworkName(focus) : focus.name) : Loc.T("photo.nofocus");
        DrawBand(new Rect(f.x, f.y + 12f * sc, f.width, 26f * sc), top, ok ? UITheme.Sage : UITheme.IvoryDim, sc);
        if (noPhotoHere) DrawBand(new Rect(f.x, f.y + 42f * sc, f.width, 26f * sc), Loc.T("photo.forbidden"), UITheme.DangerLight, sc);

        // Alt: film + tuşlar
        string bottom = Loc.T("photo.keys", FilmLeft, FilmTotal);
        DrawBand(new Rect(f.x, f.yMax - 38f * sc, f.width, 26f * sc), bottom, FilmLeft > 0 ? UITheme.Ivory : UITheme.DangerLight, sc);
        // Kayıt ışığı yerine küçük kırmızı nokta: makine kalkık
        if (Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f) UIUtil.Round(new Rect(f.xMax - 26f * sc, f.y + 14f * sc, 10f * sc, 10f * sc), UITheme.Danger, 5f * sc);
    }

    private static void DrawBand(Rect r, string text, Color color, float sc)
    {
        float tw = UIUtil.Measure(text, TextRole.BodyStrong).x + 28f * sc;
        Rect b = new Rect(r.center.x - tw * 0.5f, r.y, tw, r.height);
        UIUtil.Round(b, new Color(0f, 0f, 0f, 0.55f), 6f);
        UIUtil.Text(b, text, TextRole.BodyStrong, color, TextAnchor.MiddleCenter);
    }

    private void OnDestroy()
    {
        foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        ClearAll();
    }
}
