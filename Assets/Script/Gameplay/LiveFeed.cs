using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// KAMYON EKRANI / CANLI YAYIN.
/// Müzedeki oyuncu H ile kayıt açınca (PlayerStealth.Camcorder) onun BAKIŞ AÇISI kamyondaki ekrana gelir.
/// Görüntü ağdan gönderilmez: herkesin bilgisayarında müze aynı kurulduğu ve oyuncunun konumu / bakış eğimi zaten
/// senkron olduğu için, izleyen bilgisayar sahneyi o oyuncunun gözünden KENDİSİ çizer (ucuz, gecikmesiz).
///  - Yalnızca biri izlerken (atölye açık / kamyondaki monitöre yakın) çizilir.
///  - Dar açılı, düşük çözünürlüklü bir kamera: ayrıntı görmek için müzedeki kişinin esere yaklaşması gerekir
///    (yaklaşmak da güvenliklerin dikkatini çeker).
///  - Kayıt yoksa ekranda parazit ve "SİNYAL YOK".
/// Kamyonda tezgahın üstüne bir monitör de kurulur (kamyondaki herkes izleyebilir).
/// Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class LiveFeed : MonoBehaviour
{
    public const int Width = 512, Height = 384; // 4:3 (tuval oranı)
    public const float Fov = 46f;
    private const int MonitorLayer = 31;

    private static LiveFeed instance;
    private static int requestFrame = -10;
    private static int sourceIndex;

    private Camera cam;
    private RenderTexture rt;
    private Texture2D noise;
    private float noiseTimer;
    private PlayerStealth source;
    private Renderer[] hiddenRenderers;
    private PlayerStealth hiddenFor;

    // Kamyondaki monitör
    private ForgeryWorkbench monitorBench;
    private GameObject monitor;
    private Material monitorMat;
    private float nextBenchSearch;


    /// <summary>Şu an yayın yapan (izlenen) oyuncu, yoksa null.</summary>
    public static PlayerStealth Source => instance != null ? instance.source : null;
    public static bool HasSignal => Source != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!FeatureMerge.FriendLiveFeed) return; // birleştirme: kamyon ekranı BodyCam'de (Draw onun görüntüsünü gösterir)
        if (instance != null) return;
        GameObject go = new GameObject("LiveFeed");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LiveFeed>();
    }

    /// <summary>Bu karede (ve bir sonrakinde) yayını çiz: izleyen arayüzler her kare çağırır.</summary>
    public static void Request() => requestFrame = Time.frameCount;

    /// <summary>Birden çok kişi kayıt yapıyorsa sıradakine geç.</summary>
    public static void NextSource() => sourceIndex++;

    private void Awake()
    {
        rt = new RenderTexture(Width, Height, 24) { name = "LiveFeedRT", filterMode = FilterMode.Bilinear };
        rt.Create();

        GameObject cg = new GameObject("LiveFeedCamera");
        cg.transform.SetParent(transform, false);
        cam = cg.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.fieldOfView = Fov;
        cam.nearClipPlane = 0.08f;
        cam.farClipPlane = 80f;
        cam.depth = -50f;
        cam.cullingMask = ~(1 << MonitorLayer); // kamyondaki monitör kendi yayınını çizmesin (aynı dokuya hem yazıp hem okumasın)
        cam.enabled = false;

        noise = new Texture2D(96, 72, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "FeedNoise" };
        RefreshNoise();

        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        if (rt != null) rt.Release();
        if (noise != null) Destroy(noise);
        if (monitorMat != null) Destroy(monitorMat);
        if (instance == this) instance = null;
    }

    private void RefreshNoise()
    {
        var px = new Color32[noise.width * noise.height];
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)Random.Range(20, 200);
            px[i] = new Color32(v, v, v, 255);
        }
        noise.SetPixels32(px);
        noise.Apply();
    }

    private void Update()
    {
        if (Keyboard.current != null && WorkbenchUI.IsOpen && Keyboard.current.nKey.wasPressedThisFrame) NextSource();
    }

    private void LateUpdate()
    {
        source = PlayerStealth.FindRecorder(sourceIndex, true, out int count);
        if (count > 0) sourceIndex %= count; else sourceIndex = 0;

        UpdateMonitor();

        noiseTimer -= Time.unscaledDeltaTime;
        if (noiseTimer <= 0f) { noiseTimer = 0.08f; if (source == null) RefreshNoise(); }

        bool want = Time.frameCount - requestFrame <= 1;
        if (!want || source == null)
        {
            cam.enabled = false;
            return;
        }

        // Kaynak oyuncunun gözü: konum + yön (yaw) zaten senkron, bakış eğimi PlayerNetworkSetup.LookPitch
        Transform p = source.transform;
        Camera pc = source.GetComponentInChildren<Camera>(true);
        Vector3 eye;
        if (pc != null && pc.transform.parent != null)
        {
            Vector3 lp = pc.transform.localPosition;
            if (source.IsCrouching) lp.y = Mathf.Min(lp.y, 0.1f);
            eye = pc.transform.parent.TransformPoint(lp);
        }
        else eye = p.position + Vector3.up * (source.IsCrouching ? 0.6f : 1.1f);

        PlayerNetworkSetup ns = source.GetComponent<PlayerNetworkSetup>();
        float pitch = ns != null && ns.IsSpawned ? ns.LookPitch : 0f;
        Quaternion rot = Quaternion.Euler(pitch, p.eulerAngles.y, 0f);
        cam.transform.SetPositionAndRotation(
            Vector3.Lerp(cam.transform.position, eye, cam.enabled ? 0.5f : 1f),
            cam.enabled ? Quaternion.Slerp(cam.transform.rotation, rot, 0.5f) : rot);
        cam.enabled = true;
    }

    // Kaynak oyuncunun kendi gövdesi (kafası, kayıt ışığı) yayını kapatmasın
    private void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
    {
        if (c != cam || source == null) return;
        if (hiddenFor != source || hiddenRenderers == null)
        {
            hiddenFor = source;
            hiddenRenderers = source.GetComponentsInChildren<Renderer>(true);
        }
        foreach (Renderer r in hiddenRenderers) if (r != null) r.forceRenderingOff = true;
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera c)
    {
        if (c != cam || hiddenRenderers == null) return;
        foreach (Renderer r in hiddenRenderers) if (r != null) r.forceRenderingOff = false;
    }

    // ---------------- Kamyondaki monitör ----------------

    private void UpdateMonitor()
    {
        if (monitorBench == null)
        {
            if (monitor != null) Destroy(monitor);
            if (Time.unscaledTime < nextBenchSearch) return;
            nextBenchSearch = Time.unscaledTime + 1f;
            monitorBench = ForgeryWorkbench.NearestToLocal();
            if (monitorBench == null) return;
            BuildMonitor();
        }
        else if (monitor != null && Time.unscaledTime >= nextBenchSearch)
        {
            // Oyuncu başka bir masaya yaklaştıysa monitör o masaya taşınsın
            nextBenchSearch = Time.unscaledTime + 1f;
            Camera cm = Camera.main;
            if (cm != null)
            {
                ForgeryWorkbench nb = ForgeryWorkbench.Nearest(cm.transform.position);
                if (nb != null && nb != monitorBench &&
                    (nb.transform.position - cm.transform.position).sqrMagnitude + 1f <
                    (monitorBench.transform.position - cm.transform.position).sqrMagnitude)
                {
                    Destroy(monitor);
                    monitor = null;
                    monitorBench = null;
                    return;
                }
            }
        }
        if (monitor == null) return;

        // Kamyondaki (ya da kamyona yakın) yerel oyuncu monitörü görüyorsa yayını çiz
        Camera main = Camera.main;
        bool near = main != null && Vector3.Distance(main.transform.position, monitor.transform.position) < 9f;
        if (near && source != null) Request();
        if (monitorMat != null)
        {
            Texture t = source != null ? (Texture)rt : noise;
            if (monitorMat.HasProperty("_BaseMap")) monitorMat.SetTexture("_BaseMap", t);
            monitorMat.mainTexture = t;
        }
    }

    private void BuildMonitor()
    {
        Vector3 work = monitorBench.WorkPosition;
        Vector3 dir = work - monitorBench.CameraPoint.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : monitorBench.transform.forward;

        monitor = new GameObject("TruckMonitor");
        monitor.transform.SetPositionAndRotation(work + dir * 0.42f + Vector3.up * 0.95f, Quaternion.LookRotation(dir));

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(monitor.transform, false);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        frame.transform.localScale = new Vector3(1.28f, 0.98f, 0.05f);
        frame.GetComponent<Renderer>().sharedMaterial = ArtworkLook.MakeLit(new Color(0.06f, 0.06f, 0.07f));

        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "Screen";
        Destroy(screen.GetComponent<Collider>());
        screen.transform.SetParent(monitor.transform, false);
        screen.transform.localPosition = Vector3.zero;
        screen.transform.localScale = new Vector3(1.2f, 0.9f, 1f);
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Texture");
        monitorMat = new Material(sh) { name = "TruckMonitorMat" };
        screen.GetComponent<Renderer>().sharedMaterial = monitorMat;
        screen.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // Monitörün altındaki ayak
        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "Stand";
        Destroy(arm.GetComponent<Collider>());
        arm.transform.SetParent(monitor.transform, false);
        arm.transform.localPosition = new Vector3(0f, -0.62f, 0.05f);
        arm.transform.localScale = new Vector3(0.08f, 0.3f, 0.08f);
        arm.GetComponent<Renderer>().sharedMaterial = frame.GetComponent<Renderer>().sharedMaterial;
        foreach (Transform t in monitor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = MonitorLayer;
    }

    // ---------------- Arayüz ----------------

    private static GUIStyle fitStyle;

    // Çağıranlar sabit px dikdörtgen verir (ölçeklenmez); yazı en çok maxPx olsun ki satıra sığsın
    private static GUIStyle Fit(TextRole role, Color color, TextAnchor anchor, int maxPx)
    {
        GUIStyle b = UITheme.Style(role, color, anchor);
        if (fitStyle == null) fitStyle = new GUIStyle(b);
        fitStyle.font = b.font; fitStyle.fontStyle = b.fontStyle; fitStyle.wordWrap = b.wordWrap;
        fitStyle.clipping = TextClipping.Clip; fitStyle.padding = b.padding;
        fitStyle.fontSize = Mathf.Min(b.fontSize, maxPx);
        fitStyle.alignment = anchor;
        fitStyle.normal.textColor = color;
        return fitStyle;
    }

    // "[N] Sonraki kamera (2)" → [..] tuş kapağı, kalan Caption
    private static void DrawKeyed(Rect r, string text, Color color)
    {
        UIUtil.SplitKey(text, out string key, out string label);
        float x = r.x;
        if (!string.IsNullOrEmpty(key))
        {
            float kw = UIUtil.KeyCapWidth(key, 12);
            UIUtil.KeyCap(new Rect(x, r.y + 1f, kw, r.height - 2f), key, 12);
            x += kw + 6f;
        }
        GUI.Label(new Rect(x, r.y, Mathf.Max(10f, r.xMax - x), r.height), label, Fit(TextRole.Caption, color, TextAnchor.MiddleLeft, 12));
    }

    // Başlık şeridi: Camera ikonu + başlık (kayıt varken nabızlı Danger nokta)
    private static void DrawHeader(Rect frame, Rect r, string title, bool live)
    {
        Rect hdr = new Rect(frame.x, frame.y, frame.width, r.y - frame.y);
        float ix = hdr.x + 8f;
        UIUtil.Icon(new Rect(ix, hdr.y + 4f, 18f, 18f), IconId.Camera, live ? UITheme.BrassLight : UITheme.IvoryDim);
        float tx = ix + 24f;
        if (live)
        {
            float a = 0.35f + 0.65f * UIUtil.Pulse(5f);
            UIUtil.DrawRect(new Rect(tx, hdr.y + 9f, 8f, 8f), UITheme.WithAlpha(UITheme.Danger, a));
            tx += 14f;
        }
        GUI.Label(new Rect(tx, hdr.y, hdr.xMax - tx - 8f, hdr.height - 2f), UITheme.Caps(title), Fit(TextRole.Section, live ? UITheme.BrassLight : UITheme.Brass, TextAnchor.MiddleLeft, 14));
    }

    private static Rect FrameOf(Rect r) => new Rect(r.x - 6f, r.y - 26f, r.width + 12f, r.height + 32f);

    // Sinyal yok: statik parazit + tarama (Hatch) + ortada Camera ikonu ve iki satır yazı
    private static void DrawNoSignal(Rect r, Texture noiseTex)
    {
        if (noiseTex != null) GUI.DrawTexture(r, noiseTex, ScaleMode.StretchToFill, false);
        else UIUtil.DrawRect(r, UITheme.CharDeep);
        UIUtil.DrawRect(r, UITheme.WithAlpha(UITheme.CharDeep, 0.55f));
        UIUtil.Hatch(r, UITheme.WithAlpha(UITheme.Graphite, 0.5f));

        string[] parts = Loc.T("feed.nosignal").Split('\n');
        float bandH = Mathf.Min(r.height, Mathf.Max(70f, r.height * 0.4f));
        Rect band = new Rect(r.x, r.y + (r.height - bandH) * 0.5f, r.width, bandH);
        UIUtil.DrawRect(band, UITheme.WithAlpha(UITheme.CharDeep, 0.82f));
        UIUtil.DrawRect(new Rect(band.x, band.y, band.width, 1f), UITheme.WithAlpha(UITheme.Brass, 0.6f));
        UIUtil.DrawRect(new Rect(band.x, band.yMax - 1f, band.width, 1f), UITheme.WithAlpha(UITheme.Brass, 0.6f));

        float icon = Mathf.Min(24f, bandH * 0.3f);
        UIUtil.Icon(new Rect(band.center.x - icon * 0.5f, band.y + 6f, icon, icon), IconId.Camera, UITheme.BrassLight);
        float ty = band.y + icon + 8f;
        float th = band.yMax - ty - 4f;
        float head = parts.Length > 1 ? Mathf.Min(th * 0.4f, 24f) : th;
        GUI.Label(new Rect(band.x + 10f, ty, band.width - 20f, head), UITheme.Caps(parts[0]), Fit(TextRole.BodyStrong, UITheme.BrassLight, TextAnchor.MiddleCenter, 18));
        if (parts.Length > 1)
            GUI.Label(new Rect(band.x + 12f, ty + head, band.width - 24f, th - head), parts[1], Fit(TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperCenter, 13));
    }

    /// <summary>Birleştirme: LiveFeed kapalıyken (FeatureMerge) aynı yere BodyCam'in monitör görüntüsü çizilir (M kat planı vb.).</summary>
    private static void DrawBodyCam(Rect r)
    {
        if (CanvasPainter.ReferenceProvider == null) return;
        Texture t = CanvasPainter.ReferenceProvider(out string label);
        Rect frame = FrameOf(r);
        UIUtil.Panel(frame, PanelKind.Plaque, 0.95f);
        DrawHeader(frame, r, label, false);
        UIUtil.DrawRect(r, UITheme.CharDeep);
        if (t != null) GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, false);
        else DrawNoSignal(r, null);
        UIUtil.BrassFrame(r);
    }

    /// <summary>Yayını bir dikdörtgene çizer (çerçeve, başlık, kayıt / pil bilgisi). Atölye ve plan kullanır.</summary>
    public static void Draw(Rect r)
    {
        Request();
        if (instance == null) { DrawBodyCam(r); return; }

        Rect frame = FrameOf(r);
        UIUtil.Panel(frame, PanelKind.Plaque, 0.95f);

        PlayerStealth src = instance.source;
        if (src != null)
        {
            GUI.DrawTexture(r, instance.rt, ScaleMode.StretchToFill, false);
            // tarama çizgileri
            for (float y = r.y; y < r.yMax; y += 4f) UIUtil.DrawRect(new Rect(r.x, y, r.width, 1f), new Color(0f, 0f, 0f, 0.12f));

            PlayerNetworkSetup ns = src.GetComponent<PlayerNetworkSetup>();
            string who = ns != null ? ns.PlayerName : "?";
            DrawHeader(frame, r, Loc.T("feed.live", who), true);

            // Alt bilgi şeridi: pil (+ sonraki kamera ipucu)
            float bat = src.BatterySeconds;
            string batText = Loc.T("feed.battery", Mathf.FloorToInt(bat / 60f), Mathf.FloorToInt(bat % 60f).ToString("00"));
            Rect strip = new Rect(r.x, r.yMax - 26f, r.width, 26f);
            UIUtil.DrawRect(strip, UITheme.WithAlpha(UITheme.CharDeep, 0.72f));
            GUI.Label(new Rect(strip.x + 8f, strip.y, strip.width * 0.5f, strip.height), batText, Fit(TextRole.Mono, UITheme.Ivory, TextAnchor.MiddleLeft, 13));
            PlayerStealth.FindRecorder(0, true, out int count);
            if (count > 1 && WorkbenchUI.IsOpen)
                DrawKeyed(new Rect(strip.x + strip.width * 0.5f, strip.y + 3f, strip.width * 0.5f - 8f, strip.height - 6f), Loc.T("feed.next", count), UITheme.IvoryDim);

            if (src.IsRecordingConspicuous)
            {
                Rect warn = new Rect(r.x, r.y, r.width, 24f);
                UIUtil.DrawRect(warn, UITheme.WithAlpha(UITheme.Danger, 0.82f));
                UIUtil.Icon(new Rect(warn.x + 6f, warn.y + 3f, 18f, 18f), IconId.Warning, UITheme.Ivory);
                GUI.Label(new Rect(warn.x + 30f, warn.y, warn.width - 38f, warn.height), Loc.T("feed.noticed"), Fit(TextRole.BodyStrong, UITheme.Ivory, TextAnchor.MiddleLeft, 14));
            }
        }
        else
        {
            DrawHeader(frame, r, Loc.T("feed.title"), false);
            DrawNoSignal(r, instance.noise);
        }
        UIUtil.BrassFrame(r);
    }
}
