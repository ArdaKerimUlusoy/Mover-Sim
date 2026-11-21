using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Emote çarkı (T): tuş basılı tutulunca ekranın ortasında 8 dilimli yarı saydam çark açılır. Fare hareketi görünmez bir
/// imleci kaydırır (imleç kilitli kalır), imlecin baktığı dilim büyür ve parlar. Tuş bırakılınca seçili emote oynar;
/// ortada (seçim yokken) bırakılırsa ya da Esc'ye basılırsa hiçbir şey olmaz. 8'den çok emote olursa fare tekerleği
/// sayfa değiştirir.
///
/// Kendi başına kurulur (sahneye bir şey eklemek gerekmez). Ayarlar: Resources/EmoteSettings (isteğe bağlı varlık).
///
/// Ağ: CustomMessagingManager named message (oyuncu prefab'ına çalışma anında NetworkBehaviour eklenemediği için).
///   "SS_EmoteReq"  istemci → sunucu: [byte id]
///   "SS_Emote"     sunucu → diğerleri: [ulong clientId][byte id]
/// Emote başına yalnızca bu iki küçük mesaj gider; poz her ekranda zamandan hesaplanır. Sunucu bekleme süresini
/// denetler (spam). Basan oyuncu beklemeden kendi ekranında oynatır.
/// </summary>
public class EmoteWheel : MonoBehaviour
{
    private const string ReqMsg = "SS_EmoteReq", PlayMsg = "SS_Emote";
    private const int Slices = 8;

    private static EmoteWheel instance;

    /// <summary>Çark açık mı?</summary>
    public static bool IsOpen => instance != null && instance.open;
    /// <summary>PlayerController: çark açıkken fare kamerayı döndürmesin (ayara göre).</summary>
    public static bool BlocksLook => IsOpen && EmoteSettings.Current.freezeLookWhileOpen;
    /// <summary>PlayerController: yürüme kapalı mı (çark açıkken ya da emote sırasında, ayara göre).</summary>
    public static bool BlocksMove
    {
        get
        {
            if (instance == null) return false;
            EmoteSettings s = EmoteSettings.Current;
            if (instance.open && !s.allowMoveWhileOpen) return true;
            if (!s.allowMoveDuringEmote)
            {
                PlayerAvatar a = LocalAvatar();
                if (a != null && a.IsEmoting) return true;
            }
            return false;
        }
    }
    /// <summary>SessionMenuUI: Esc çarkı kapattıysa menü açılmasın.</summary>
    public static bool EatsEsc => instance != null && (instance.open || instance.escFrame >= Time.frameCount - 1);

    private bool open;
    private float openedAt;
    private Vector2 aim;         // görünmez imleç (-1..1, y yukarı)
    private int page;
    private int escFrame = -10;
    private float nextAllowed;   // yerel bekleme bitişi (Time.unscaledTime)
    private List<EmoteDef> list = new List<EmoteDef>();

    // Ağ
    private NetworkManager regNm;
    private CustomMessagingManager regCmm;
    private readonly Dictionary<ulong, float> serverLast = new Dictionary<ulong, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("EmoteWheel");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<EmoteWheel>();
    }

    private void OnDisable() => Unregister();

    // =========================================================== Giriş

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { if (regNm != null) Unregister(); }
        else if (regNm != nm || regCmm != nm.CustomMessagingManager) Register(nm);

        Keyboard kb = Keyboard.current;
        if (kb == null) { open = false; return; }
        EmoteSettings set = EmoteSettings.Current;
        KeyControl key = SafeKey(kb, set.wheelKey);
        if (key == null) return;

        if (!open)
        {
            if (key.wasPressedThisFrame && CanOpen())
            {
                // Eller doluyken çark açılmaz (fırça / eşya / taşınan nesne sol tıkı ve tekerleği kullanıyor)
                PlayerAvatar me = LocalAvatar();
                if (me != null && (me.HandsBusyForEmote || ObjectCarrierBusy())) UIUtil.ShowToast(Loc.T("emote.busy"), ToastKind.Warning, 1.8f);
                else Open();
            }
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame) { Close(false); escFrame = Time.frameCount; return; }
        if (!CanStayOpen()) { Close(false); return; }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue();
            float rad = WheelRadius();
            aim += d / Mathf.Max(60f, rad * 0.55f);
            if (aim.sqrMagnitude > 1f) aim.Normalize();
            float sc = mouse.scroll.ReadValue().y;
            int pages = PageCount();
            if (pages > 1 && Mathf.Abs(sc) > 0.01f) { page = (page + (sc < 0f ? 1 : pages - 1)) % pages; GameAudio.Play2D(SoundId.UiClick, 0.3f); }
        }

        if (key.wasReleasedThisFrame || !key.isPressed) Close(true);
    }

    private static KeyControl SafeKey(Keyboard kb, Key k)
    {
        try { return kb[k]; } catch (Exception) { return kb.tKey; }
    }

    private static bool BaseOk =>
        GameFlow.LocalSpawned && !ChatUI.IsTyping && !SessionMenuUI.IsPaused && !PlayerInventory.IsPanelOpen &&
        !WorkbenchUI.IsOpen && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen && !BodyCam.PanelOpen &&
        !PlayerController.InputLocked && !LockpickSystem.IsPicking && Cursor.lockState == CursorLockMode.Locked;

    private static bool CanOpen() => BaseOk && !PlayerAvatar.PreviewActive;
    private static bool CanStayOpen() => BaseOk;

    private void Open()
    {
        list = EmoteCatalog.WheelList();
        open = true;
        openedAt = Time.unscaledTime;
        aim = Vector2.zero;
        page = Mathf.Clamp(page, 0, Mathf.Max(0, PageCount() - 1));
        GameAudio.Play2D(SoundId.UiClick, 0.35f);
    }

    private void Close(bool commit)
    {
        int pick = commit ? Hovered() : -1;
        open = false;
        aim = Vector2.zero;
        if (pick < 0) return;
        EmoteDef d = list[pick];
        TryPlay(d);
    }

    private int PageCount() => Mathf.Max(1, (list.Count + Slices - 1) / Slices);

    /// <summary>İmlecin baktığı dilimdeki emote'un list indeksi; ortadaysa / boş dilimdeyse -1.</summary>
    private int Hovered()
    {
        if (aim.magnitude < 0.35f) return -1;
        int slice = HoveredSlice();
        int idx = page * Slices + slice;
        return idx < list.Count ? idx : -1;
    }

    private int HoveredSlice()
    {
        float ang = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg; // yukarıdan saat yönünde
        if (ang < 0f) ang += 360f;
        return Mathf.RoundToInt(ang / (360f / Slices)) % Slices;
    }

    // =========================================================== Oynatma

    private void TryPlay(EmoteDef d)
    {
        PlayerAvatar me = LocalAvatar();
        if (me == null) return;
        float now = Time.unscaledTime;
        if (me.IsEmoting) return; // oynarken yenisi başlamaz
        if (now < nextAllowed) { UIUtil.ShowToast(Loc.T("emote.wait", (nextAllowed - now).ToString("0.0")), ToastKind.Warning, 1.6f); return; }
        if (me.HandsBusyForEmote || ObjectCarrierBusy()) { UIUtil.ShowToast(Loc.T("emote.busy"), ToastKind.Warning, 1.8f); return; }

        nextAllowed = now + Mathf.Max(EmoteSettings.Current.cooldown, d.Duration);
        PlayOn(me, d);

        NetworkManager nm = regNm;
        if (nm == null || !nm.IsListening || regCmm == null) return;
        if (nm.IsServer) { ServerAccept(nm, nm.LocalClientId, (byte)d.Id); return; }
        using (var w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe((byte)d.Id);
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private static bool ObjectCarrierBusy()
    {
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
            if (p != null && p.IsOwner)
            {
                ObjectCarrier c = p.GetComponent<ObjectCarrier>();
                return c != null && c.IsHolding;
            }
        return false;
    }

    private static void PlayOn(PlayerAvatar a, EmoteDef d)
    {
        if (a == null || d == null) return;
        a.PlayEmote((int)d.Id);
        AudioClip clip = EmoteSettings.Current.SoundFor(d);
        if (clip != null) AudioSource.PlayClipAtPoint(clip, a.transform.position + Vector3.up * 1.5f, EmoteSettings.Current.soundVolume);
    }

    private static PlayerAvatar LocalAvatar()
    {
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
            if (p != null && p.IsOwner) return p.GetComponent<PlayerAvatar>();
        return null;
    }

    private static PlayerAvatar AvatarOf(ulong clientId)
    {
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
            if (p != null && p.IsSpawned && p.OwnerClientId == clientId) return p.GetComponent<PlayerAvatar>();
        return null;
    }

    // =========================================================== Ağ

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        if (regCmm == null) { regNm = null; return; }
        regCmm.RegisterNamedMessageHandler(ReqMsg, OnRequest);
        regCmm.RegisterNamedMessageHandler(PlayMsg, OnPlay);
        serverLast.Clear();
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try
            {
                regCmm.UnregisterNamedMessageHandler(ReqMsg);
                regCmm.UnregisterNamedMessageHandler(PlayMsg);
            }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
        serverLast.Clear();
    }

    private void OnRequest(ulong sender, FastBufferReader r)
    {
        NetworkManager nm = regNm;
        if (nm == null || !nm.IsServer) return;
        if (!r.TryBeginRead(1)) return;
        r.ReadValue(out byte id);
        ServerAccept(nm, sender, id);
    }

    /// <summary>Sunucu: bekleme süresini denetle, emote'u gönderen dışındaki herkese yayınla (sunucu oyuncuysa kendisi de görür).</summary>
    private void ServerAccept(NetworkManager nm, ulong sender, byte id)
    {
        EmoteDef d = EmoteCatalog.Get(id);
        if (d == null) return; // kilitler istemcide (oyuncunun kendi kayıtları); sunucu yalnızca kimliği ve bekleme süresini denetler
        float now = Time.unscaledTime;
        float gap = Mathf.Max(EmoteSettings.Current.cooldown, d.Duration) * 0.8f; // gecikme payı
        if (serverLast.TryGetValue(sender, out float last) && now - last < gap) return;
        serverLast[sender] = now;

        if (sender != nm.LocalClientId) PlayOn(AvatarOf(sender), d); // sunucu oyuncusu da görsün

        List<ulong> ids = null;
        foreach (ulong c in nm.ConnectedClientsIds)
        {
            if (c == sender || c == nm.LocalClientId) continue;
            if (ids == null) ids = new List<ulong>();
            ids.Add(c);
        }
        if (ids == null) return;
        using (var w = new FastBufferWriter(16, Allocator.Temp))
        {
            w.WriteValueSafe(sender);
            w.WriteValueSafe(id);
            regCmm.SendNamedMessage(PlayMsg, ids, w, NetworkDelivery.Reliable);
        }
    }

    private void OnPlay(ulong sender, FastBufferReader r)
    {
        if (!r.TryBeginRead(9)) return;
        r.ReadValue(out ulong who);
        r.ReadValue(out byte id);
        NetworkManager nm = regNm;
        if (nm != null && who == nm.LocalClientId) return; // kendi emote'umuz zaten oynadı
        PlayOn(AvatarOf(who), EmoteCatalog.Get(id));
    }

    // =========================================================== Çizim

    private float WheelRadius() => Mathf.Round(210f * UITheme.Scale * EmoteSettings.Current.wheelScale);

    private void OnGUI()
    {
        if (!open || Event.current.type != EventType.Repaint) return;
        EmoteSettings set = EmoteSettings.Current;
        float sc = UITheme.Scale;
        float R = WheelRadius();
        Vector2 c = new Vector2(Screen.width * set.wheelCenter.x, Screen.height * (1f - set.wheelCenter.y));
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - openedAt) / UITheme.MotionBase));
        float Rn = R * Mathf.Lerp(0.85f, 1f, t);
        Color old = GUI.color;
        Matrix4x4 oldM = GUI.matrix;

        int hovered = Hovered();
        int hoverSlice = hovered >= 0 ? hovered - page * Slices : -1;
        float now = Time.unscaledTime;
        bool cooling = now < nextAllowed;

        // Dilimler
        for (int i = 0; i < Slices; i++)
        {
            int idx = page * Slices + i;
            bool has = idx < list.Count;
            bool hot = i == hoverSlice;
            float r = Rn * (hot ? 1.06f : 1f);
            Color col = hot ? UITheme.WithAlpha(UITheme.Bordeaux, 0.92f) : UITheme.WithAlpha(UITheme.Charcoal, has ? 0.70f : 0.40f);
            GUI.color = new Color(col.r, col.g, col.b, col.a * t);
            GUI.matrix = oldM;
            GUIUtility.RotateAroundPivot(i * (360f / Slices), c);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), EmoteIcons.Slice);
        }
        GUI.matrix = oldM;

        // İkonlar (dönmez)
        for (int i = 0; i < Slices; i++)
        {
            int idx = page * Slices + i;
            float a = i * (360f / Slices) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            bool hot = i == hoverSlice;
            float rr = Rn * (hot ? 0.70f : 0.67f);
            Vector2 p = c + dir * rr;
            if (idx < list.Count)
            {
                float s = Rn * (hot ? 0.30f : 0.25f);
                Color ic = hot ? UITheme.Ivory : UITheme.IvoryDim;
                if (cooling) ic = UITheme.WithAlpha(ic, 0.5f);
                GUI.color = new Color(ic.r, ic.g, ic.b, ic.a * t);
                Texture2D tex = EmoteIcons.Get(list[idx].Id);
                if (tex != null) GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), tex);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, t);
                UIUtil.Text(new Rect(p.x - 50f * sc, p.y - 10f * sc, 100f * sc, 20f * sc), Loc.T("emote.empty"), TextRole.Caption, UITheme.WithAlpha(UITheme.IvoryDim, 0.45f), TextAnchor.MiddleCenter);
            }
        }

        // Merkez: seçili emote'un adı (yoksa ipucu), bekleme süresi
        float cr = Rn * 0.33f;
        GUI.color = new Color(1f, 1f, 1f, t);
        Color cc = UITheme.WithAlpha(UITheme.CharDeep, 0.82f);
        GUI.color = new Color(cc.r, cc.g, cc.b, cc.a * t);
        GUI.DrawTexture(new Rect(c.x - cr, c.y - cr, cr * 2f, cr * 2f), EmoteIcons.Disc);
        GUI.color = new Color(1f, 1f, 1f, t);
        float tw = cr * 1.8f;
        if (hovered >= 0)
        {
            UIUtil.Text(new Rect(c.x - tw * 0.5f, c.y - 16f * sc, tw, 24f * sc), list[hovered].DisplayName, TextRole.BodyStrong, UITheme.Ivory, TextAnchor.MiddleCenter);
            string sub = cooling ? Loc.T("emote.wait", (nextAllowed - now).ToString("0.0")) : Loc.T("emote.release");
            UIUtil.Text(new Rect(c.x - tw * 0.5f, c.y + 8f * sc, tw, 20f * sc), sub, TextRole.Caption, cooling ? UITheme.Ochre : UITheme.IvoryDim, TextAnchor.MiddleCenter);
        }
        else
        {
            UIUtil.Text(new Rect(c.x - tw * 0.5f, c.y - 12f * sc, tw, 24f * sc), Loc.T("emote.title"), TextRole.Section, UITheme.BrassLight, TextAnchor.MiddleCenter);
            UIUtil.Text(new Rect(c.x - tw * 0.5f, c.y + 10f * sc, tw, 20f * sc), Loc.T("emote.hint", set.wheelKey.ToString()), TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleCenter);
        }

        // İmleç yönü: iç halkada küçük pirinç nokta
        if (aim.sqrMagnitude > 0.0025f)
        {
            Vector2 ad = new Vector2(aim.x, -aim.y).normalized;
            Vector2 dp = c + ad * (cr + 10f * sc);
            float ds = 9f * sc;
            Color b = UITheme.BrassLight;
            GUI.color = new Color(b.r, b.g, b.b, t * Mathf.Clamp01(aim.magnitude * 3f));
            GUI.DrawTexture(new Rect(dp.x - ds * 0.5f, dp.y - ds * 0.5f, ds, ds), EmoteIcons.Disc);
        }

        // Sayfa noktaları (8'den çok emote varsa)
        int pages = PageCount();
        if (pages > 1)
        {
            float ds = 8f * sc, gap = 6f * sc;
            float total = pages * ds + (pages - 1) * gap;
            for (int i = 0; i < pages; i++)
            {
                Color pc = i == page ? UITheme.BrassLight : UITheme.WithAlpha(UITheme.IvoryDim, 0.5f);
                GUI.color = new Color(pc.r, pc.g, pc.b, pc.a * t);
                GUI.DrawTexture(new Rect(c.x - total * 0.5f + i * (ds + gap), c.y + Rn + 12f * sc, ds, ds), EmoteIcons.Disc);
            }
            GUI.color = new Color(1f, 1f, 1f, t);
            UIUtil.Text(new Rect(c.x - 120f * sc, c.y + Rn + 24f * sc, 240f * sc, 20f * sc), Loc.T("emote.pages"), TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleCenter);
        }

        GUI.color = old;
        GUI.matrix = oldM;
    }
}
