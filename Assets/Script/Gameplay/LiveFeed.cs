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

    private static GUIStyle titleStyle, smallStyle, bigStyle;

    /// <summary>Şu an yayın yapan (izlenen) oyuncu, yoksa null.</summary>
    public static PlayerStealth Source => instance != null ? instance.source : null;
    public static bool HasSignal => Source != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
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
            monitorBench = FindAnyObjectByType<ForgeryWorkbench>();
            if (monitorBench == null) return;
            BuildMonitor();
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

    private static void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        titleStyle.normal.textColor = Color.white;
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        bigStyle.normal.textColor = Color.white;
    }

    /// <summary>Yayını bir dikdörtgene çizer (çerçeve, başlık, kayıt / pil bilgisi). Atölye ve plan kullanır.</summary>
    public static void Draw(Rect r)
    {
        Request();
        EnsureStyles();
        if (instance == null) return;

        Rect frame = new Rect(r.x - 6f, r.y - 26f, r.width + 12f, r.height + 32f);
        UIUtil.DrawRect(frame, new Color(0.05f, 0.05f, 0.06f, 0.95f));

        PlayerStealth src = instance.source;
        if (src != null)
        {
            GUI.DrawTexture(r, instance.rt, ScaleMode.StretchToFill, false);
            // tarama çizgileri
            for (float y = r.y; y < r.yMax; y += 4f) UIUtil.DrawRect(new Rect(r.x, y, r.width, 1f), new Color(0f, 0f, 0f, 0.12f));

            PlayerNetworkSetup ns = src.GetComponent<PlayerNetworkSetup>();
            string who = ns != null ? ns.PlayerName : "?";
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
            GUI.Label(new Rect(r.x, r.y - 24f, r.width, 22f), (blink ? "●  " : "    ") + Loc.T("feed.live", who), titleStyle);
            float bat = src.BatterySeconds;
            string batText = Loc.T("feed.battery", Mathf.FloorToInt(bat / 60f), Mathf.FloorToInt(bat % 60f).ToString("00"));
            GUI.Label(new Rect(r.x + 8f, r.yMax - 24f, r.width - 16f, 22f), batText, smallStyle);
            if (src.IsRecordingConspicuous)
            {
                UIUtil.DrawRect(new Rect(r.x, r.y, r.width, 22f), new Color(0.75f, 0.1f, 0.1f, 0.7f));
                GUI.Label(new Rect(r.x + 8f, r.y, r.width - 16f, 22f), Loc.T("feed.noticed"), smallStyle);
            }
            PlayerStealth.FindRecorder(0, true, out int count);
            if (count > 1 && WorkbenchUI.IsOpen)
                GUI.Label(new Rect(r.x + 8f, r.yMax - 44f, r.width - 16f, 20f), Loc.T("feed.next", count), smallStyle);
        }
        else
        {
            GUI.DrawTexture(r, instance.noise, ScaleMode.StretchToFill, false);
            UIUtil.DrawRect(new Rect(r.x, r.y + r.height * 0.35f, r.width, r.height * 0.3f), new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(r.x + 10f, r.y + r.height * 0.35f, r.width - 20f, r.height * 0.3f), Loc.T("feed.nosignal"), bigStyle);
            GUI.Label(new Rect(r.x, r.y - 24f, r.width, 22f), Loc.T("feed.title"), titleStyle);
        }
    }
}
