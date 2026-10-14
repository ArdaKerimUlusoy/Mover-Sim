using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Oyuncunun gizlilik durumu (Player prefab'ında olmalı; Test Museum oluşturucu otomatik ekler).
/// GDD: oyuncular sıradan ziyaretçidir; GÖRÜLMEK sorun değil, ŞÜPHELİ BİR ŞEY YAPARKEN görülmek sorundur:
///   - eser değiştirirken / arkadaşından çalarken (en şüpheli)
///   - elinde eser (orijinal veya sahte) taşırken
///   - "Staff Only" alanındayken
///   - alarm sırasında aranan kişiyken
/// Şüphe 0→1 dolar; dolunca alarm çalar. Görülmeyince yavaşça azalır.
/// </summary>
public class PlayerStealth : NetworkBehaviour
{
    public static readonly List<PlayerStealth> All = new List<PlayerStealth>();
    public static PlayerStealth Local { get; private set; }

    [SerializeField] private float decayPerSecond = 0.12f;
    [SerializeField] private float decayDelay = 1.5f;

    private readonly NetworkVariable<float> suspicion = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<bool> hidden = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<bool> watched = new NetworkVariable<bool>(false);
    // Hapisteyse: sunucu saatine göre kendiliğinden çıkacağı an (0 = serbest)
    private readonly NetworkVariable<double> jailedUntil = new NetworkVariable<double>(0);
    private readonly NetworkVariable<bool> crouching = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> doingCrime = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Jopla vurulup baygın mı? Sahibi yazar (PlayerController.IsKnockedOut), herkes okur → PlayerAvatar yere yığılır.
    private readonly NetworkVariable<bool> knockedOut = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerInventory inventory;
    private PlayerController controller;
    private CharacterController characterController;

    // Sunucu
    private float lastSeenTime = -10f;
    private float wantedUntil = -1f;
    private float offenderUntil = -1f;
    private float overheardUntil = -1f; // sesli sohbette hırsızlık kelimesi söyledi, güvenlik duydu (CrimeTalk)
    [Tooltip("Hapisten çıkan / kaçan oyuncuyu güvenlikler bu kadar saniye tanır (görünce hemen şüphelenir).")]
    [SerializeField] private float offenderMemorySeconds = 60f;

    // Sahibi: içinde saklandığı dolap
    public HidingSpot CurrentHidingSpot { get; private set; }

    public float Suspicion => suspicion.Value;
    public bool IsHidden => hidden.Value;
    public bool IsWatched => watched.Value;
    public bool IsCrouching => crouching.Value;
    public bool IsDoingCrime => doingCrime.Value;
    /// <summary>Baygın mı (ağ üzerinden, tüm istemcilerde geçerli)?</summary>
    public bool IsKnockedOutNet => knockedOut.Value;
    public bool IsWanted => IsServer && Time.time < wantedUntil;
    /// <summary>Sunucu: güvenliklerin tanıdığı (yakalanıp kaçmış / kaçırılmış) oyuncu mu?</summary>
    public bool IsKnownOffender => IsServer && Time.time < offenderUntil;
    /// <summary>Sunucu: sesli sohbette söyledikleri bir güvenliğe duyuldu mu? (Bir süre görülünce şüphe çeker.)</summary>
    public bool IsOverheard => IsServer && Time.time < overheardUntil;
    public bool IsJailed => jailedUntil.Value > 0;
    public double JailedUntil => jailedUntil.Value;

    /// <summary>Bağlı (doğmuş) oyuncu sayısı; her bilgisayarda aynıdır.</summary>
    public static int ConnectedCount
    {
        get
        {
            int n = 0;
            foreach (PlayerStealth p in All) if (p != null && p.IsSpawned) n++;
            return n;
        }
    }

    /// <summary>Tek kişilik oyun mu? Hapisten kendiliğinden çıkış YALNIZCA bu durumda çalışır; çok oyunculuda yalnızca arkadaş kurtarır.</summary>
    public static bool SoloGame => ConnectedCount <= 1;

    private string PlayerName
    {
        get
        {
            PlayerNetworkSetup ps = GetComponent<PlayerNetworkSetup>();
            return ps != null ? ps.PlayerName : $"Player{OwnerClientId}";
        }
    }

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        controller = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this)) All.Add(this);
        if (IsOwner) Local = this;
        hidden.OnValueChanged += OnHiddenChanged;
        ApplyHiddenVisuals(hidden.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (Local == this) Local = null;
        hidden.OnValueChanged -= OnHiddenChanged;
    }

    public override void OnDestroy()
    {
        All.Remove(this);
        base.OnDestroy();
    }

    // ---------------- Sahibi ----------------

    /// <summary>PlayerInteractor çağırır: eser değiştirme / çalma tuşuna basılı tutuluyor mu?</summary>
    public void SetDoingCrime(bool value)
    {
        if (IsOwner && IsSpawned && doingCrime.Value != value) doingCrime.Value = value;
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner && controller != null)
        {
            bool c = controller.IsCrouching;
            if (crouching.Value != c) crouching.Value = c;
            bool k = controller.IsKnockedOut;
            if (knockedOut.Value != k) knockedOut.Value = k;
        }

        if (IsServer) ServerTick();
    }

    // ---------------- Sunucu ----------------

    /// <summary>Şu an ne kadar şüpheli davranıyor? 0 = sıradan ziyaretçi.</summary>
    public float SuspiciousLevel
    {
        get
        {
            float level = 0f;
            if (IsJailed) return 0f; // hapisteki oyuncu kimsenin dikkatini çekmez
            if (doingCrime.Value) level += 2f;
            if (IsHoldingArtwork()) level += 1f;
            if (RestrictedArea.IsInside(transform.position)) level += 1f;
            if (IsWanted) level += 1.5f;
            if (IsKnownOffender) level += 1f; // yakalanıp kaçan oyuncuyu güvenlikler tanır
            if (IsOverheard) level += 0.75f;  // "çaldım" dediği duyuldu: görülünce şüphe çeker
            return level;
        }
    }

    private bool IsHoldingArtwork()
    {
        if (inventory == null) return false;
        int h = inventory.HeldSlot;
        if (h < 0 || h >= inventory.SlotCount) return false;
        ItemDefinition d = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(inventory.GetSlot(h).ItemIndex) : null;
        return d != null && (d.type == ItemType.Original || d.type == ItemType.Forgery);
    }

    /// <summary>Sunucu: bir güvenlik/kamera bu oyuncuyu bu karede görüyor. gainRate = saniyede şüphe artışı (seviye 1 için).</summary>
    public void ServerReportSeen(float gainRate, Vector3 seenFrom)
    {
        if (!IsServer || hidden.Value || IsJailed) return;
        lastSeenTime = Time.time;

        float level = SuspiciousLevel;
        if (level <= 0f) return;

        float rate = gainRate * level * (crouching.Value ? 0.6f : 1f);
        suspicion.Value = Mathf.Min(1f, suspicion.Value + rate * Time.deltaTime);

        if (suspicion.Value >= 1f && SecurityManager.Instance != null)
        {
            SecurityManager.Instance.ServerTriggerAlarm(transform.position, "alarm.spotted", this);
        }
    }

    public void ServerMarkWanted(float until)
    {
        if (IsServer) wantedUntil = Mathf.Max(wantedUntil, until);
    }

    /// <summary>Sunucu: sesli sohbette hırsızlıkla ilgili bir şey söylediği duyuldu (CrimeTalk). Şüphe biraz artar,
    /// bir süre güvenlik / kamera onu görünce şüphelenir. Tek başına alarm çaldırmaz.</summary>
    public void ServerMarkOverheard(float seconds)
    {
        if (!IsServer || IsJailed) return;
        overheardUntil = Mathf.Max(overheardUntil, Time.time + seconds);
        if (suspicion.Value < 0.9f) suspicion.Value = Mathf.Min(0.9f, suspicion.Value + 0.25f);
    }

    /// <summary>Sunucu: güvenlikler bu oyuncuyu bir süre tanısın (hapisten çıktı / kaçırıldı / kovalamadan kaçtı).</summary>
    public void ServerMarkOffender()
    {
        if (IsServer) offenderUntil = Mathf.Max(offenderUntil, Time.time + offenderMemorySeconds);
    }

    private void ServerTick()
    {
        bool seen = Time.time - lastSeenTime < 0.3f;
        if (watched.Value != seen) watched.Value = seen;

        if (!seen && Time.time - lastSeenTime > decayDelay && suspicion.Value > 0f)
        {
            suspicion.Value = Mathf.Max(0f, suspicion.Value - decayPerSecond * Time.deltaTime);
        }
    }

    /// <summary>Sunucu: güvenlik yakaladı. Çantadaki orijinallere el konur ve KAİDELERİNE GERİ DÖNER;
    /// oyuncu müzenin hapsine kapatılır (JailCell). Sahnede hapis yoksa kamyona gönderilir.</summary>
    public void ServerCatch()
    {
        if (!IsServer || IsJailed) return;
        if (MuseumSession.Instance != null && MuseumSession.Instance.SequenceActive) return; // kamyon / sıfırlama sekansı sürerken yakalanma yok

        var returned = new List<int>();
        int lost = inventory != null ? inventory.ServerConfiscateOriginals(returned) : 0;
        foreach (int key in returned)
        {
            ArtworkDisplay d = ArtworkDisplay.FindByKey(key);
            if (d != null) d.ServerRestoreOriginal();
        }
        suspicion.Value = 0f;
        wantedUntil = -1f;

        if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("chat.caught", PlayerName, lost.ToString());
        if (inventory != null) inventory.ServerPlaySound(SoundId.Caught, transform.position);

        if (JailCell.Instance != null && JailCell.Instance.IsSpawned)
        {
            JailCell.Instance.ServerImprison(this);
            if (MuseumSession.Instance != null) MuseumSession.Instance.ServerCheckAllCaught(); // herkes yakalandıysa koşu biter
            return;
        }

        SpawnPoint sp = SpawnPoint.All.Count > 0 ? SpawnPoint.All[(int)(OwnerClientId % (ulong)SpawnPoint.All.Count)] : null;
        Vector3 pos = sp != null ? sp.transform.position + Vector3.up : transform.position;
        float yaw = sp != null ? sp.transform.eulerAngles.y : transform.eulerAngles.y;
        TeleportRpc(pos, yaw);
    }

    /// <summary>Sunucu: bu oyuncu hakkındaki bütün şüphe / aranma / tanınma bilgisini siler: normal ziyaretçi olur.
    /// Hapisten çıkınca ve yeni göreve geçince çağrılır. Onu kovalayan / arayan güvenlikler de bırakır.</summary>
    public void ServerClearHeat()
    {
        if (!IsServer) return;
        suspicion.Value = 0f;
        wantedUntil = -1f;
        offenderUntil = -1f;
        overheardUntil = -1f;
        foreach (GuardAI g in SecurityManager.Guards) if (g != null) g.ServerForget(this);
    }

    /// <summary>Sunucu: hapse girdi (until = çıkış anı) ya da çıktı (0).</summary>
    public void ServerSetJailed(double until)
    {
        if (!IsServer) return;
        jailedUntil.Value = until;
        if (until > 0) { suspicion.Value = 0f; wantedUntil = -1f; }
    }

    /// <summary>Sunucu: oyuncuyu bir noktaya ışınlar (konumu sahibi yönettiği için ona komut gider).</summary>
    public void ServerTeleport(Vector3 pos, float yaw)
    {
        if (IsServer) TeleportRpc(pos, yaw);
    }

    public void ServerSetHidden(bool value)
    {
        if (IsServer) hidden.Value = value;
    }

    // ---------------- Sahibe komutlar ----------------

    [Rpc(SendTo.Owner)]
    private void TeleportRpc(Vector3 pos, float yaw)
    {
        WorkbenchUI.ForceClose(); // yakalandıysa atölye / kamera açık kalmasın
        LocalTeleport(pos, yaw);
    }

    public void LocalTeleport(Vector3 pos, float yaw)
    {
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (characterController != null) characterController.enabled = CurrentHidingSpot == null;
    }

    /// <summary>Sahibi: dolaba girildi/çıkıldı (HidingSpot çağırır).</summary>
    public void LocalSetHidingSpot(HidingSpot spot, Vector3 pos, float yaw)
    {
        CurrentHidingSpot = spot;
        bool hiding = spot != null;

        // Dolapta yürüyemez ama fareyle etrafa bakabilir (sabit açı yok)
        if (controller != null) { controller.enabled = true; controller.SetLookOnly(hiding, yaw); }
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (characterController != null) characterController.enabled = !hiding;
    }

    // ---------------- Görünüm ----------------

    private void OnHiddenChanged(bool oldValue, bool newValue) => ApplyHiddenVisuals(newValue);

    private void ApplyHiddenVisuals(bool isHidden)
    {
        if (IsOwner) return; // kendi kapsülünü zaten görmüyorsun
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = !isHidden;
        }
    }

    private void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;

        if (IsJailed)
        {
            double left = jailedUntil.Value - NetworkManager.ServerTime.Time;
            float jy = UIUtil.TopSlot(60f);
            UIUtil.DrawPrompt(Loc.T("jail.banner"), jy, new Color(1f, 0.6f, 0.4f), 24);
            string jailLine = SoloGame
                ? Loc.T("jail.wait", Mathf.Max(0, Mathf.CeilToInt((float)left)))
                : Loc.T("jail.waitmulti"); // çok oyunculu: geri sayım yok, arkadaş kurtarmalı
            UIUtil.DrawPrompt(jailLine, jy + 30f, Color.white, 16);
            return;
        }

        float s = suspicion.Value;
        bool showEye = watched.Value && SuspiciousLevel > 0f;
        if (s <= 0.01f && !showEye && !hidden.Value) return;

        float w = 260f;
        Rect r = new Rect((Screen.width - w) / 2f, UIUtil.TopSlot(34f), w, 34f);
        GUI.Box(r, GUIContent.none, UIUtil.BoxStyle);

        string label = hidden.Value ? Loc.T("stealth.hidden") : showEye ? Loc.T("stealth.watched") : Loc.T("stealth.suspicion");
        GUI.Label(new Rect(r.x + 10f, r.y + 2f, r.width - 20f, 16f), label, UIUtil.SmallStyle);

        Rect bar = new Rect(r.x + 10f, r.y + 20f, r.width - 20f, 7f);
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        Color c = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.3f, 0.25f), s);
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * s, bar.height), c);
    }
}
