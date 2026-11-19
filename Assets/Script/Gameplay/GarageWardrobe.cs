using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GARAJ DOLABI: garajdaki kozmetik dolabı (Garage_CosmeticLocker) etkileşimli olur. Bakıp [E] → DOLAP paneli:
/// şimdilik karakter RENGİ seçilir (bere / boya rengi, herkes görür; başka oyuncunun rengi seçilemez). Kıyafetler sonra eklenecek
/// (panelde yeri hazır, "yakında" yazar). Dolabın görünüşü çalışma anında iki kapak + kulp ile güzelleştirilir.
/// Ağ nesnesi gerektirmez: renk PlayerNetworkSetup.SetColor ile zaten herkese gider. Sahneye bir şey eklemek gerekmez.
/// Ucuz: yalnızca yerel oyuncu garajdayken ve dolaba yakınken tek ışın.
/// </summary>
public class GarageWardrobe : MonoBehaviour
{
    public const string LockerName = "Garage_CosmeticLocker";
    private const float MaxDistance = 3f;
    private const int PromptChannel = 6;

    private static GarageWardrobe instance;
    public static bool IsOpen { get; private set; }

    private Transform locker;
    private Collider lockerCol;
    private float nextFind;
    private bool targeted;
    private int openedFrame;
    private float openedAt;
    private PlayerController frozen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("GarageWardrobe");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GarageWardrobe>();
    }

    private static bool InputFree =>
        !ChatUI.IsTyping && !SessionMenuUI.IsPaused && !PlayerInventory.IsPanelOpen && !PlayerController.InputLocked &&
        !BodyCam.PanelOpen && !WorkbenchUI.IsOpen && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen && Cursor.lockState == CursorLockMode.Locked;

    private void Update()
    {
        if (IsOpen) { UpdateOpen(); return; }
        targeted = false;
        PlayerStealth me = PlayerStealth.Local;
        if (me == null || !me.IsSpawned || GameFlow.Phase != GamePhase.Garage) return;
        if (locker == null && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 1f;
            GameObject go = GameObject.Find(LockerName);
            if (go != null) { locker = go.transform; lockerCol = go.GetComponent<Collider>(); Dress(locker); }
        }
        if (locker == null || lockerCol == null) return;

        Vector3 p = me.transform.position;
        if ((lockerCol.ClosestPoint(p) - p).sqrMagnitude > (MaxDistance + 2f) * (MaxDistance + 2f)) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!lockerCol.Raycast(ray, out RaycastHit hit, MaxDistance + 0.6f)) return;
        if (Physics.Raycast(ray, out RaycastHit first, hit.distance - 0.05f, ~0, QueryTriggerInteraction.Ignore) &&
            first.collider != lockerCol && !first.collider.transform.IsChildOf(locker) && !first.collider.transform.IsChildOf(me.transform)) return;
        targeted = true;

        Keyboard kb = Keyboard.current;
        if (kb != null && InputFree && kb.eKey.wasPressedThisFrame) Open(me);
    }

    private void Open(PlayerStealth me)
    {
        IsOpen = true;
        openedFrame = Time.frameCount;
        openedAt = Time.unscaledTime;
        frozen = me.GetComponent<PlayerController>();
        if (frozen != null) frozen.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GameAudio.Play2D(SoundId.UiClick, 0.6f);
    }

    private void Close()
    {
        IsOpen = false;
        if (frozen != null) frozen.enabled = true;
        frozen = null;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UpdateOpen()
    {
        // Garajdan çıkıldı / menü / oturum kapandı: kapat
        if (PlayerStealth.Local == null || GameFlow.Phase != GamePhase.Garage || SessionMenuUI.IsPaused) { Close(); return; }
        Keyboard kb = Keyboard.current;
        if (kb != null && Time.frameCount != openedFrame && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) Close();
    }

    private void OnDisable() { if (IsOpen) Close(); }

    // ---------------- Görünüş: düz kutuyu dolaba çevir (iki kapak çizgisi, kulplar, üst korniş) ----------------

    private static void Dress(Transform box)
    {
        if (box.Find("WardrobeDress") != null) return;
        Renderer br = box.GetComponent<Renderer>();
        Bounds b = br != null ? br.bounds : new Bounds(box.position, box.lossyScale);
        // Ön yüz: kutunun en ince ekseni yönünde, garajın içine (garaj ortasına) bakan taraf
        Vector3 size = b.size;
        Vector3 inward, toMid;
        GarageArea area = FindFirstObjectByType<GarageArea>();
        Vector3 mid = area != null ? area.transform.position : b.center - box.forward;
        GarageSpawnPoint sp = GarageSpawnPoint.All.Count > 0 ? GarageSpawnPoint.All[0] : null;
        if (sp != null) mid = sp.transform.position;
        toMid = mid - b.center; toMid.y = 0f;
        bool alongX = size.x < size.z; // ince eksen X ise ön yüz ±X
        inward = alongX ? new Vector3(Mathf.Sign(toMid.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toMid.z));
        Vector3 across = alongX ? Vector3.forward : Vector3.right;
        float depth = alongX ? size.x : size.z, width = alongX ? size.z : size.x, height = size.y;

        GameObject root = new GameObject("WardrobeDress");
        root.transform.SetParent(box, true);
        Material wood = ArtworkLook.MakeLit(new Color(0.42f, 0.27f, 0.16f));
        Material dark = ArtworkLook.MakeLit(new Color(0.16f, 0.1f, 0.06f));
        Material brass = ArtworkLook.MakeLit(new Color(0.85f, 0.66f, 0.3f));
        if (br != null) br.sharedMaterial = wood;
        Vector3 front = b.center + inward * (depth * 0.5f + 0.005f);
        // Kapak ayrımı (ortada dikey çizgi) + kenar çerçevesi
        Piece(root.transform, front, across, inward, new Vector3(0.025f, height * 0.92f, 0.01f), Vector3.zero, dark);
        Piece(root.transform, front, across, inward, new Vector3(width * 0.94f, 0.03f, 0.01f), new Vector3(0f, height * 0.46f, 0f), dark);
        Piece(root.transform, front, across, inward, new Vector3(width * 0.94f, 0.03f, 0.01f), new Vector3(0f, -height * 0.46f, 0f), dark);
        // Kulplar
        Piece(root.transform, front, across, inward, new Vector3(0.03f, 0.22f, 0.04f), new Vector3(-0.07f, 0.05f, 0f), brass);
        Piece(root.transform, front, across, inward, new Vector3(0.03f, 0.22f, 0.04f), new Vector3(0.07f, 0.05f, 0f), brass);
        // Üst korniş
        GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(top.GetComponent<Collider>());
        top.name = "WardrobeTop";
        top.transform.SetParent(root.transform, true);
        top.transform.position = b.center + Vector3.up * (height * 0.5f + 0.03f) + inward * 0.02f;
        top.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
        top.transform.localScale = Vector3.one;
        top.transform.localScale = DivideScale(new Vector3(width + 0.08f, 0.06f, depth + 0.08f), root.transform.lossyScale, top.transform);
        top.GetComponent<Renderer>().sharedMaterial = dark;
    }

    private static void Piece(Transform parent, Vector3 front, Vector3 across, Vector3 inward, Vector3 size, Vector3 off, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "WardrobePart";
        g.transform.SetParent(parent, true);
        Quaternion rot = Quaternion.LookRotation(inward, Vector3.up);
        g.transform.position = front + rot * off + inward * (size.z * 0.5f);
        g.transform.rotation = rot;
        g.transform.localScale = DivideScale(size, parent.lossyScale, g.transform);
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    // Ebeveyn ölçekli (kutu) olduğu için dünya boyutunu yerel ölçeğe çevir (dönüş eksenlere hizalı)
    private static Vector3 DivideScale(Vector3 worldSize, Vector3 parentLossy, Transform t)
    {
        if (t.parent == null) return worldSize;
        // Yerel eksenleri dünyada ölçerek düzelt
        t.localScale = Vector3.one;
        Vector3 lx = t.TransformVector(Vector3.right), ly = t.TransformVector(Vector3.up), lz = t.TransformVector(Vector3.forward);
        return new Vector3(worldSize.x / Mathf.Max(1e-4f, lx.magnitude), worldSize.y / Mathf.Max(1e-4f, ly.magnitude), worldSize.z / Mathf.Max(1e-4f, lz.magnitude));
    }

    // ---------------- Çizim ----------------

    private void OnGUI()
    {
        if (!IsOpen)
        {
            if (targeted && InputFree) PromptHud.Show(Loc.T("wardrobe.prompt"), PromptTone.Normal, -1f, PromptChannel);
            else PromptHud.Idle(PromptChannel);
            return;
        }
        PromptHud.Idle(PromptChannel);
        float sc = UITheme.Scale;
        float enter = UIUtil.Ease.OutCubic((Time.unscaledTime - openedAt) / UITheme.MotionBase);
        float pw = Mathf.Min(Screen.width - 32f, 520f * sc);
        float pad = UITheme.S5 * sc;
        int n = PlayerNetworkSetup.Colors.Length;
        int cols = Mathf.Min(n, 6);
        int rows = Mathf.CeilToInt(n / (float)cols);
        float sw = Mathf.Min(54f * sc, (pw - pad * 2f - (cols - 1) * 10f * sc) / cols);
        float ph = pad + 34f * sc + 26f * sc + rows * (sw + 10f * sc) + 18f * sc + 22f * sc + 70f * sc + pad;
        Rect page = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f + (1f - enter) * 12f, pw, ph);

        Color oldC = GUI.color;
        UIUtil.Scrim(UITheme.WithAlpha(UITheme.Scrim, UITheme.Scrim.a * enter));
        GUI.color = new Color(1f, 1f, 1f, enter);
        UIUtil.Panel(page, PanelKind.Plaque);
        float x = page.x + pad, w = page.width - pad * 2f, y = page.y + pad;
        float cb = 32f * sc;
        if (UIUtil.Button(new Rect(page.xMax - pad - cb + 6f, y - 2f, cb, cb), "", ButtonKind.Tool, true, false, IconId.Close)) { GUI.color = oldC; Close(); return; }
        UIUtil.Text(new Rect(x, y, w - cb - 10f, 32f * sc), Loc.T("wardrobe.title"), TextRole.Title, UITheme.Ivory);
        y += 34f * sc;

        // Renk
        y += UIUtil.SectionHeader(new Rect(x, y, w, 20f * sc), Loc.T("wardrobe.color"));
        PlayerNetworkSetup me = null;
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All) if (p != null && p.IsSpawned && p.IsOwner) { me = p; break; }
        int current = me != null ? me.ColorIndex : PlayerNetworkSetup.SavedColorIndex;
        float rowW = cols * sw + (cols - 1) * 10f * sc;
        float sx = x + (w - rowW) * 0.5f;
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            Rect sr = new Rect(sx + c * (sw + 10f * sc), y + r * (sw + 10f * sc), sw, sw);
            bool taken = i != current && PlayerNetworkSetup.IsColorTakenByOther(i);
            if (UIUtil.Swatch(sr, PlayerNetworkSetup.Colors[i], i == current, !taken) && i != current && me != null)
            {
                me.SetColor(i);
                GameAudio.Play2D(SoundId.UiClick, 0.6f);
            }
        }
        y += rows * (sw + 10f * sc) + 4f * sc;
        UIUtil.Text(new Rect(x, y, w, 20f * sc), Loc.T("wardrobe.color.note"), TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleCenter);
        y += 30f * sc;

        // Kıyafetler: yakında
        y += UIUtil.SectionHeader(new Rect(x, y, w, 20f * sc), Loc.T("wardrobe.outfits"));
        Rect soon = new Rect(x, y, w, 44f * sc);
        UIUtil.Round(soon, new Color(1f, 1f, 1f, 0.05f), UIUtil.RadiusSmall);
        UIUtil.Text(soon, Loc.T("wardrobe.soon"), TextRole.Body, UITheme.IvoryDim, TextAnchor.MiddleCenter);
        GUI.color = oldC;
    }
}
