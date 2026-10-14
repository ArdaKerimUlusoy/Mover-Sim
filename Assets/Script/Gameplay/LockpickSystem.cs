using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MAYMUNCUK: Staff Only odasının kilitli kapısını açar.
///  - Maymuncuk garaj mağazasından adetle alınır (kişisel cüzdan, ucuz: UpgradeCatalog.LockpickPrice, en fazla LockpickMax).
///  - Kapıya bakıp E'yi basılı tut. "Başarı" ve "Kırılma" çubukları yarışır (Aion 2'deki zanaat gibi), önce dolan kazanır:
///    Başarı → kapı açılır. Kırılma → maymuncuk kırılır (bir adet gider), kapı kilitli kalır; başka maymuncukla yeniden dene.
///    Kırılma ihtimali: UpgradeCatalog.LockpickBreakChance. Bu sırada SUÇ işliyorsun: güvenlik görürse yakalar.
///    İtilirsen / bakışını kaçırırsan / E'yi bırakırsan deneme iptal olur (maymuncuk kırılmaz), baştan başlar.
///  - Açılınca kapı bütün ekip için görev sonuna kadar açık kalır (yeni müzede yine kilitli). Açılırken ses çıkar.
///  - Ekipte bir kişinin maymuncuğu olması yeter.
/// Ağ: istemci → sunucu "SS_LockpickReq" (sunucu maymuncuğu ve mesafeyi doğrular). Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class LockpickSystem : MonoBehaviour
{
    public static LockpickSystem Instance { get; private set; }

    /// <summary>Yerel oyuncu şu an kilit açıyor mu? (PlayerInteractor suç bayrağına ekler.)</summary>
    public static bool IsPicking { get; private set; }

    private const string ReqMsg = "SS_LockpickReq";
    private const float Range = 2.6f;        // kapıya ne kadar yakından açılır
    private const float ServerRange = 4f;    // sunucu: gecikme payıyla
    private const float UnlockNoiseRadius = 4.5f;
    private const float BreakNoiseRadius = 2.5f;

    private CustomMessagingManager regCmm;
    private NetworkManager regNm;

    private StaffDoor target;
    private bool waitRelease;
    private float nextTick;
    private float sentAt = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("LockpickSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<LockpickSystem>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        Unregister();
        if (Instance == this) Instance = null;
        IsPicking = false;
    }

    /// <summary>İtildim vb.: kilit açma baştan (E bırakılıp yeniden basılmalı).</summary>
    public static void Interrupt()
    {
        IsPicking = false;
        if (Instance == null) return;
        if (Instance.attempting) Instance.waitRelease = true; // yarıda kalan deneme sayılmaz, maymuncuk kırılmaz
        Instance.CancelAttempt();
    }

    // =====================================================================
    //  Döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;
        if (!active)
        {
            if (regCmm != null) Unregister();
            Reset();
            return;
        }
        if (regCmm != nm.CustomMessagingManager) Register(nm);

        target = FindDoor();
        Keyboard kb = Keyboard.current;
        bool held = kb != null && kb.eKey.isPressed;
        if (!held) waitRelease = false;

        PlayerInventory inv = PlayerInventory.Local;
        bool has = inv != null && inv.HasLockpick;

        if (target == null || !has || !held || waitRelease || Time.unscaledTime - sentAt < 0.8f)
        {
            CancelAttempt();
            return;
        }

        if (!attempting) BeginAttempt();
        IsPicking = true;
        elapsed += Time.deltaTime;
        float x = Mathf.Clamp01(elapsed / attemptTime);
        successBar = (willSucceed ? 1f : loserEnd) * Wobble(x, phaseS);
        failBar = (willSucceed ? loserEnd : 1f) * Wobble(x, phaseF);

        // Kilit kurcalama sesi (yalnızca kendin duyarsın; asıl ses kilit açılınca / kırılınca)
        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + 0.55f;
            GameAudio.PlayAt(SoundId.UiClick, target.Center, 0.35f, 0.25f);
        }

        if (x >= 1f)
        {
            bool ok = willSucceed;
            CancelAttempt();
            waitRelease = true;
            sentAt = Time.unscaledTime;
            SendRequest(nm, ok);
        }
    }

    // ---------------- Deneme: Başarı ve Kırılma çubukları yarışır (önce dolan kazanır) ----------------
    // Sonuç deneme başında atılır (kırılma ihtimali UpgradeCatalog.LockpickBreakChance); çubuklar ona göre
    // dalgalanarak dolar, kaybeden çubuk sona yakın bir yerde kalır.

    private bool attempting, willSucceed;
    private float elapsed, attemptTime, loserEnd, phaseS, phaseF;
    private float successBar, failBar;

    private void BeginAttempt()
    {
        attempting = true;
        willSucceed = UnityEngine.Random.value >= UpgradeCatalog.LockpickBreakChance;
        attemptTime = UpgradeCatalog.LockpickSeconds * UnityEngine.Random.Range(0.8f, 1.15f);
        loserEnd = UnityEngine.Random.Range(0.72f, 0.96f);
        phaseS = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        phaseF = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        elapsed = 0f;
        successBar = failBar = 0f;
    }

    private void CancelAttempt()
    {
        attempting = false;
        IsPicking = false;
        elapsed = 0f;
        successBar = failBar = 0f;
    }

    /// <summary>0→1 giden ama düzgün olmayan (bazen hızlanan, bazen yavaşlayan) eğri. f(0)=0, f(1)=1, hep artar.</summary>
    private static float Wobble(float x, float phase)
    {
        const float A = 0.035f;
        float f = x + A * (Mathf.Sin(2f * Mathf.PI * 2f * x + phase) - Mathf.Sin(phase));
        return Mathf.Clamp01(f);
    }

    private void Reset()
    {
        target = null;
        CancelAttempt();
        waitRelease = false;
    }

    /// <summary>Yerel oyuncu kilitli kapıya bakıyor mu (yakından, arada bir şey yokken)?</summary>
    private StaffDoor FindDoor()
    {
        if (StaffDoor.All.Count == 0 || StaffDoor.Unlocked) return null;
        PlayerStealth me = PlayerStealth.Local;
        Camera cam = Camera.main;
        if (me == null || cam == null || !me.IsSpawned) return null;
        if (ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || WorkbenchUI.IsOpen || BriefingUI.IsOpen) return null;
        if (Cursor.lockState != CursorLockMode.Locked || me.IsJailed || me.CurrentHidingSpot != null || PushSystem.LocalKnockedOut) return null;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, Range + 1.5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Collider first = null;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform.IsChildOf(me.transform) || h.distance >= best) continue;
            best = h.distance;
            first = h.collider;
        }
        if (first == null) return null;
        StaffDoor door = first.GetComponentInParent<StaffDoor>();
        if (door == null) return null;
        Vector3 feet = me.transform.position;
        Vector3 c = door.Center;
        c.y = feet.y;
        return Vector3.Distance(feet, c) <= Range ? door : null;
    }

    // =====================================================================
    //  Ağ
    // =====================================================================

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        regCmm.RegisterNamedMessageHandler(ReqMsg, OnRequest);
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try { regCmm.UnregisterNamedMessageHandler(ReqMsg); }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendRequest(NetworkManager nm, bool success)
    {
        if (nm.IsServer)
        {
            ServerResult(nm.LocalClientId, success);
            return;
        }
        using (var w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe((byte)(success ? 1 : 0));
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (regNm == null || !regNm.IsServer) return;
        try
        {
            reader.ReadValueSafe(out byte ok);
            ServerResult(sender, ok == 1);
        }
        catch (Exception) { }
    }

    // =====================================================================
    //  Sunucu
    // =====================================================================

    private void ServerResult(ulong clientId, bool success)
    {
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        MuseumSession ms = MuseumSession.Instance;
        if (nm == null || !nm.IsServer || ms == null || ms.StaffDoorOpen) return;
        if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null) return;

        PlayerInventory inv = client.PlayerObject.GetComponent<PlayerInventory>();
        PlayerStealth st = client.PlayerObject.GetComponent<PlayerStealth>();
        if (inv == null || !inv.HasLockpick) return;
        if (st != null && (st.IsJailed || st.IsHidden)) return;

        Vector3 pos = client.PlayerObject.transform.position;
        StaffDoor near = null;
        foreach (StaffDoor d in StaffDoor.All)
        {
            if (d == null) continue;
            Vector3 c = d.Center;
            c.y = pos.y;
            if (Vector3.Distance(pos, c) <= ServerRange) { near = d; break; }
        }
        if (near == null) return;

        if (!success)
        {
            // Maymuncuk kırıldı: bir adet gider, kapı kilitli kalır (küçük bir "çıt" sesi)
            inv.ServerBreakLockpick();
            NoiseSystem.Emit(near.Center, BreakNoiseRadius);
            inv.ServerPlaySound(SoundId.ItemThud, near.Center);
            inv.ServerNotify("lockpick.broke", inv.LockpickCount.ToString());
            return;
        }

        ms.ServerOpenStaffDoor();
        NoiseSystem.Emit(near.Center, UnlockNoiseRadius);
        inv.ServerPlaySound(SoundId.HoldComplete, near.Center);
        inv.ServerNotify("lockpick.done");
    }

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private void OnGUI()
    {
        if (target == null || StaffDoor.Unlocked) return;
        PlayerInventory inv = PlayerInventory.Local;
        bool has = inv != null && inv.HasLockpick;

        if (!has)
        {
            UIUtil.DrawPrompt(Loc.T("lockpick.need", UpgradeCatalog.LockpickPrice.ToString("N0")), UIUtil.CenterSlot(), new Color(1f, 0.55f, 0.45f));
            return;
        }
        if (!attempting)
        {
            UIUtil.DrawPrompt(Loc.T("lockpick.prompt", inv.LockpickCount), UIUtil.CenterSlot(), Color.white);
            return;
        }
        DrawRacePanel(inv.LockpickCount);
    }

    private GUIStyle titleStyle, barStyle;

    /// <summary>Başarı (mavi) ve Kırılma (turuncu) çubukları — önce dolan kazanır.</summary>
    private void DrawRacePanel(int count)
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(0.55f, 1f, 0.55f);
            barStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            barStyle.normal.textColor = Color.white;
        }

        const float W = 340f, H = 86f;
        float y = UIUtil.CenterSlot(H);
        Rect panel = new Rect((Screen.width - W) * 0.5f, y + 10f, W, H);
        UIUtil.DrawRect(panel, new Color(0.04f, 0.08f, 0.12f, 0.82f));
        UIUtil.DrawRect(new Rect(panel.x, panel.y, panel.width, 1f), new Color(0.4f, 0.6f, 0.75f, 0.7f));
        GUI.Label(new Rect(panel.x, panel.y + 4f, W, 22f), Loc.T("lockpick.title", count), titleStyle);

        DrawBar(new Rect(panel.x + 14f, panel.y + 32f, W - 28f, 18f), successBar, new Color(0.25f, 0.55f, 0.95f), Loc.T("lockpick.success"));
        DrawBar(new Rect(panel.x + 14f, panel.y + 56f, W - 28f, 18f), failBar, new Color(0.95f, 0.55f, 0.12f), Loc.T("lockpick.fail"));
    }

    private void DrawBar(Rect r, float v, Color fill, string label)
    {
        UIUtil.DrawRect(r, new Color(0f, 0f, 0f, 0.6f));
        UIUtil.DrawRect(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(v), r.height - 2f), fill);
        GUI.Label(new Rect(r.x + 5f, r.y, r.width, r.height), label, barStyle);
    }
}
