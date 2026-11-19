using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GARAJ / LOBİ: oyuncuların buluştuğu ve soyguna hazırlandığı aşama. Garajdayken (kamyon ve müze dışında) sol tarafta
/// "SOYGUN HAZIRLIĞI" paneli çıkar: oyuncular (renk, ad, nerede), HAZIR durumu (F3), "Herkes bekleniyor: 2/3 hazır" satırı,
/// rol bilgisi ve yapılacaklar.
///
/// [F3] HAZIR (tekrar basınca hazır değil):
///  - TEK OYUNCULU (GameFlow.Solo): eskisi gibi; kısa yerel geri sayımdan (1,5 sn, yeniden [F3] ile iptal) sonra oyuncu kamyona ışınlanır.
///  - EKİP: kararı SUNUCU verir. Garajdaki herkes hazır olunca sunucu ortak bir geri sayım (3 sn) başlatır ve herkese yayınlar;
///    süre dolunca herkese "Go" gönderir, her istemci AYNI ANDA kendini kamyona ışınlar (GarageTravel.TryTravelLocal; hareket
///    sahibinde olduğu için ışınlanmayı her oyuncu kendisi yapar).
///
/// KİMLER SAYILIR (sunucu): doğmuş oyunculardan sunucudaki konumu garajda olanlar (GameFlow.PhaseAt == Garage).
/// Kamyonda / müzede olanlar sayılmaz (beklenmez). Bağlanmış ama oyuncusu henüz doğmamış (yükleniyor) istemciler BEKLETİR:
/// birazdan garajda doğacaklar, onları geride bırakmamak için. Hazır kümesi yalnızca garajdakilerden oluşur
/// (garajdan çıkan / ayrılan otomatik hazır değil olur).
///
/// İPTAL: biri hazır'ı geri alırsa ya da garaja biri gelirse geri sayım iptal; biri ayrılırsa (geri kalan herkes hazırsa)
/// geri sayım baştan başlar. Ekip tek kişiye düşerse tek oyunculu geri sayıma geçilir.
/// DOĞRULAMA: [F3] basınca önce yerel engel (taşıma, hapis, yol kapalı...) denetlenir; hazırken engel çıkarsa hazır geri alınır.
/// Böylece "Go" anında geçiş nadiren başarısız olur; olursa yalnızca o oyuncuya neden gösterilir, diğerleri yine gider.
/// DURAKLATMA: ortak geri sayım Esc menüsünü beklemez; menüsü açık oyuncu da diğerleriyle birlikte ışınlanır
/// (yalnızca PlayerController.InputLocked açıksa kilit kalkınca ışınlanır).
/// SIFIRLAMA: çıkışta (Go), garaja dönüşte (GarageSceneLink → ServerResetReady) ve yeni görevde (seviye değişince) hazırlar sıfırlanır.
///
/// Ağ: CustomMessagingManager named message ("SS_ReadyReq" istemci -> sunucu, "SS_ReadyState" sunucu -> herkes: hazırlar +
/// sayılar + kalan süre, "SS_ReadyGo" sunucu -> herkes: şimdi çık). Çalışma anında NetworkBehaviour eklenemediği için böyle.
/// Sonradan katılan, durum değişince ve 3 sn'de bir yapılan yayınla güncel durumu alır. Kendini kurar.
/// </summary>
public class HeistLobbyUI : MonoBehaviour
{
    public const Key ReadyKey = Key.F3;
    private const string ReqMsg = "SS_ReadyReq", StateMsg = "SS_ReadyState", GoMsg = "SS_ReadyGo";

    private static HeistLobbyUI instance;

    /// <summary>Hazır olan istemciler (sunucu ve istemcilerde aynı kümeye yakınsar).</summary>
    private static readonly HashSet<ulong> ready = new HashSet<ulong>();

    private NetworkManager regNm;
    private CustomMessagingManager regCmm;
    private bool stateDirty;
    private float nextBroadcast;
    private int serverLevel = -1;

    // Tek oyunculu yerel geri sayım
    private const float GoDelay = 1.5f;
    private float goAt = -1f; // >0: bu zamanda kamyona ışınlan (geri sayım)
    private bool wasSolo = true;

    // Ekip: ortak geri sayım (sunucuda asıl değer; istemcide gelen kalan süreden yerel saate çevrilir)
    private const float SharedDelay = 3f;
    private float sharedGoAt = -1f;
    private int teamNeed, teamReady;     // garajda sayılan oyuncu sayısı (yüklenenler dahil) / hazır olanlar
    private bool pendingTravel;          // "Go" geldi: ilk uygun karede kamyona ışınlan
    private float pendingSince;          // en çok 2 sn beklenir (sonra ışınlama iptal: geç kalan ışınlanma olmasın)
    // Sunucu: bağlanıp henüz doğmamış istemcinin ilk görüldüğü an (20 sn'den uzun yüklenen herkesi bekletmesin)
    private readonly Dictionary<ulong, float> pendingSeen = new Dictionary<ulong, float>();
    private readonly List<ulong> pendingDrop = new List<ulong>();
    private float nextBlockCheck;

    // Sunucu
    private readonly List<ulong> participants = new List<ulong>();
    private int countdownSig;
    private int lastConnected = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("HeistLobbyUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HeistLobbyUI>();
    }

    /// <summary>Bu istemci hazır mı?</summary>
    public static bool IsReady(ulong clientId) => ready.Contains(clientId);

    /// <summary>Sunucu: tüm hazır durumlarını sıfırla (garaja dönüşte çağrılır) ve herkese yayınla.</summary>
    public static void ServerResetReady()
    {
        ready.Clear();
        if (instance == null) return;
        instance.sharedGoAt = -1f;
        instance.stateDirty = true;
    }

    private static bool PanelVisible =>
        GameFlow.LocalSpawned && GameFlow.Phase == GamePhase.Garage && !SessionMenuUI.IsPaused &&
        !WorkbenchUI.IsOpen && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen;

    private void OnDisable() => Unregister();

    // ---------------- Ağ ----------------

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        if (regCmm == null) { regNm = null; return; }
        regCmm.RegisterNamedMessageHandler(ReqMsg, OnRequest);
        regCmm.RegisterNamedMessageHandler(StateMsg, OnState);
        regCmm.RegisterNamedMessageHandler(GoMsg, OnGo);
        ResetLocal();
        stateDirty = true;
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try
            {
                regCmm.UnregisterNamedMessageHandler(ReqMsg);
                regCmm.UnregisterNamedMessageHandler(StateMsg);
                regCmm.UnregisterNamedMessageHandler(GoMsg);
            }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    /// <summary>Oturum başında / sonunda tüm yerel durumu temizle.</summary>
    private void ResetLocal()
    {
        ready.Clear();
        goAt = -1f;
        sharedGoAt = -1f;
        pendingTravel = false;
        teamNeed = teamReady = 0;
        serverLevel = -1;
        lastConnected = -1;
        wasSolo = true;
    }

    private void SendReady(bool on)
    {
        NetworkManager nm = regNm;
        if (nm == null || !nm.IsListening) return;
        if (nm.IsServer) { ServerSet(nm.LocalClientId, on); return; }

        if (on) ready.Add(nm.LocalClientId); else ready.Remove(nm.LocalClientId); // kendi ekranında hemen görünsün
        if (!on) sharedGoAt = -1f; // iptal bandı hemen kalksın (sunucu da iptal edip yayınlar)
        using (var w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe((byte)(on ? 1 : 0));
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private void OnRequest(ulong sender, FastBufferReader r)
    {
        if (regNm == null || !regNm.IsServer) return;
        try { r.ReadValueSafe(out byte on); ServerSet(sender, on == 1); } catch (Exception) { }
    }

    private void OnState(ulong sender, FastBufferReader r)
    {
        if (regNm == null || regNm.IsServer || sender != NetworkManager.ServerClientId) return;
        try
        {
            r.ReadValueSafe(out ushort n);
            ready.Clear();
            for (int i = 0; i < n; i++) { r.ReadValueSafe(out ulong id); ready.Add(id); }
            r.ReadValueSafe(out byte need);
            r.ReadValueSafe(out byte rdy);
            r.ReadValueSafe(out float left);
            teamNeed = need;
            teamReady = rdy;
            sharedGoAt = left > 0f ? Time.unscaledTime + left : -1f;
        }
        catch (Exception) { }
    }

    private void OnGo(ulong sender, FastBufferReader r)
    {
        if (regNm == null || regNm.IsServer || sender != NetworkManager.ServerClientId) return;
        sharedGoAt = -1f;
        pendingTravel = true; pendingSince = Time.unscaledTime;
    }

    private void ServerSet(ulong client, bool on)
    {
        bool changed = on ? ready.Add(client) : ready.Remove(client);
        if (changed) stateDirty = true;
    }

    private static bool HasPlayer(ulong clientId)
    {
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
            if (p != null && p.IsSpawned && p.OwnerClientId == clientId) return true;
        return false;
    }

    private void ServerTick(NetworkManager nm)
    {
        float now = Time.unscaledTime;

        // Yeni görevde (seviye değişince) hazır durumları sıfırlanır
        MuseumSession ms = MuseumSession.Instance;
        if (ms != null && ms.Ready && ms.Level != serverLevel)
        {
            if (serverLevel != -1) { ready.Clear(); sharedGoAt = -1f; }
            serverLevel = ms.Level;
            stateDirty = true;
        }

        // Sonradan katılan / ayrılan: durumu hemen yayınla
        int connected = nm.ConnectedClientsIds.Count;
        if (connected != lastConnected) { lastConnected = connected; stateDirty = true; }

        // Sayılanlar: garajdaki doğmuş oyuncular (All kimliğe göre sıralı) + henüz doğmamış (yüklenen) istemciler
        participants.Clear();
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || !p.IsSpawned) continue;
            if (GameFlow.PhaseAt(p.transform.position) == GamePhase.Garage) participants.Add(p.OwnerClientId);
        }
        int pending = 0;
        float tNow = Time.unscaledTime;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (HasPlayer(id)) { pendingSeen.Remove(id); continue; }
            if (!pendingSeen.TryGetValue(id, out float since)) { since = tNow; pendingSeen[id] = since; }
            if (tNow - since < 20f) pending++; // 20 sn'den uzun yükleniyorsa (takıldı) ekibi bekletmez
        }
        pendingDrop.Clear();
        foreach (ulong id in pendingSeen.Keys) if (!nm.ConnectedClients.ContainsKey(id)) pendingDrop.Add(id);
        foreach (ulong id in pendingDrop) pendingSeen.Remove(id);

        // Hazır kümesi yalnızca garajdakiler (ayrılan / garajdan çıkan düşer)
        if (ready.RemoveWhere(id => !participants.Contains(id)) > 0) stateDirty = true;

        int need = participants.Count + pending;
        int rdy = ready.Count;
        if (need != teamNeed || rdy != teamReady) { teamNeed = need; teamReady = rdy; stateDirty = true; }

        // Ortak geri sayım: ekipte (2+ oyuncu), yükleneni yok, garajdaki herkes hazır, kamyon kaçışta değil
        bool team = PlayerNetworkSetup.All.Count >= 2;
        bool allReady = team && participants.Count > 0 && pending == 0 && rdy == participants.Count &&
                        !(ms != null && ms.SequenceActive);
        int sig = 17;
        unchecked { foreach (ulong id in participants) sig = sig * 31 + (int)id + 1; }

        if (allReady)
        {
            if (sharedGoAt < 0f || sig != countdownSig)
            {
                // Başlat ya da (biri ayrıldıysa) baştan başlat
                sharedGoAt = now + SharedDelay;
                countdownSig = sig;
                stateDirty = true;
            }
            else if (now >= sharedGoAt)
            {
                ServerDepart(nm);
            }
        }
        else if (sharedGoAt > 0f)
        {
            sharedGoAt = -1f; // biri hazır'ı geri aldı / biri geldi: iptal
            stateDirty = true;
        }

        if (!stateDirty && now < nextBroadcast) return;
        stateDirty = false;
        nextBroadcast = now + 3f;

        List<ulong> ids = RemoteIds(nm);
        if (ids == null) return;

        using (var w = new FastBufferWriter(256, Allocator.Temp))
        {
            int n = Mathf.Min(ready.Count, 24);
            w.WriteValueSafe((ushort)n);
            int written = 0;
            foreach (ulong id in ready)
            {
                if (written++ >= n) break;
                w.WriteValueSafe(id);
            }
            w.WriteValueSafe((byte)Mathf.Min(teamNeed, 255));
            w.WriteValueSafe((byte)Mathf.Min(teamReady, 255));
            w.WriteValueSafe(sharedGoAt > 0f ? Mathf.Max(0.05f, sharedGoAt - now) : -1f);
            regCmm.SendNamedMessage(StateMsg, ids, w, NetworkDelivery.Reliable);
        }
    }

    private static List<ulong> RemoteIds(NetworkManager nm)
    {
        List<ulong> ids = null;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (id == nm.LocalClientId) continue;
            if (ids == null) ids = new List<ulong>();
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>Sunucu: geri sayım bitti, herkes aynı anda kamyona. Hazırlar sıfırlanır.</summary>
    private void ServerDepart(NetworkManager nm)
    {
        sharedGoAt = -1f;
        ready.Clear();
        stateDirty = true;

        List<ulong> ids = RemoteIds(nm);
        if (ids != null)
        {
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)1);
                regCmm.SendNamedMessage(GoMsg, ids, w, NetworkDelivery.Reliable);
            }
        }
        if (!nm.IsHost) return;
        pendingTravel = true; pendingSince = Time.unscaledTime; // sunucu aynı zamanda oyuncu: kendisi de çıkar (garajda değilse Update yok sayar)
    }

    // ---------------- Güncelleme ----------------

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            if (regNm != null) { Unregister(); ResetLocal(); }
            return;
        }
        if (regNm != nm || regCmm != nm.CustomMessagingManager) Register(nm);
        if (regNm == null) return;

        if (nm.IsServer) ServerTick(nm);

        float now = Time.unscaledTime;
        bool solo = GameFlow.Solo;
        bool inGarage = GameFlow.LocalSpawned && GameFlow.Phase == GamePhase.Garage;
        bool meReady = ready.Contains(nm.LocalClientId);

        // Ekip "Go": Esc menüsü açık olsa da çıkılır (herkes aynı anda); yalnızca giriş kilidi varsa kalkınca
        if (pendingTravel)
        {
            if (!inGarage || now - pendingSince > 2f) pendingTravel = false;
            else if (!PlayerController.InputLocked) { pendingTravel = false; TravelNow(); }
        }

        // Tek oyunculu geri sayım: garajdan çıkıldıysa / ekip olduysa iptal; süre dolunca kamyona ışınlan
        if (goAt > 0f)
        {
            if (!inGarage || !solo) goAt = -1f;
            else if (now >= goAt && !SessionMenuUI.IsPaused && !PlayerController.InputLocked)
            {
                goAt = -1f;
                TravelNow();
            }
        }
        // Ekip dağıldı, tek kaldım ve hazırdım: tek oyunculu geri sayıma geç
        if (solo && !wasSolo && inGarage && meReady && goAt < 0f) goAt = now + GoDelay;
        wasSolo = solo;

        // Ekipte hazırken artık geçilemez duruma düştüysem (ör. bir şey taşıyorum) hazır'ı geri al: "Go" anında takılmasın
        if (!solo && inGarage && meReady && now >= nextBlockCheck)
        {
            nextBlockCheck = now + 0.3f;
            string why = GarageTravel.LocalBlockReason();
            if (why != null) { SendReady(false); Toast(why); }
        }

        Keyboard kb = Keyboard.current;
        if (kb == null || !PanelVisible || ChatUI.IsTyping || PlayerInventory.IsPanelOpen) return;
        if (Cursor.lockState == CursorLockMode.Locked && kb[ReadyKey].wasPressedThisFrame)
        {
            if (solo)
            {
                bool nowReady = goAt < 0f; // geri sayım yoksa: hazır + geri sayım; geri sayım sırasında tekrar basmak iptal eder
                SendReady(nowReady);
                goAt = nowReady ? now + GoDelay : -1f;
                GameAudio.Play2D(SoundId.UiClick, nowReady ? 0.6f : 0.4f);
            }
            else
            {
                // Ekip: hazır / hazır değil arasında geçiş; çıkışı sunucu herkes hazır olunca başlatır
                bool nowReady = !meReady;
                if (nowReady)
                {
                    string why = GarageTravel.LocalBlockReason();
                    if (why != null) { Toast(why); return; }
                }
                SendReady(nowReady);
                GameAudio.Play2D(SoundId.UiClick, nowReady ? 0.6f : 0.4f);
            }
        }
    }

    /// <summary>Yerel oyuncuyu şimdi kamyona ışınla; olmazsa nedenini göster, hazır'ı geri al.</summary>
    private void TravelNow()
    {
        string why = GarageTravel.TryTravelLocal();
        if (why == null) return;
        SendReady(false);
        Toast(why);
    }

    private static void Toast(string text)
    {
        if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(text);
        GameAudio.Play2D(SoundId.UiClick, 0.4f);
    }

    // ---------------- Çizim ----------------

    private static string PlaceKey(GamePhase p)
    {
        switch (p)
        {
            case GamePhase.Truck: return "lobby.place.truck";
            case GamePhase.Museum: return "lobby.place.museum";
            default: return "lobby.place.garage";
        }
    }

    private void OnGUI()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !PanelVisible) return;

        bool solo = GameFlow.Solo;
        bool meReady = ready.Contains(nm.LocalClientId);
        var players = PlayerNetworkSetup.All;

        // Sayılar: sunucudan gelen; henüz gelmediyse listeden
        int need = teamNeed, rdyCount = teamReady;
        if (need <= 0)
        {
            need = players.Count;
            rdyCount = 0;
            foreach (PlayerNetworkSetup q in players) if (q != null && ready.Contains(q.OwnerClientId)) rdyCount++;
        }
        bool counting = !solo && sharedGoAt > 0f;
        int sharedLeft = counting ? Mathf.Max(1, Mathf.CeilToInt(sharedGoAt - Time.unscaledTime)) : 0;

        // Üst bant: tek oyunculu geri sayım / ekip ortak geri sayımı / hazırım, herkes bekleniyor
        if (solo && goAt > 0f)
        {
            int left = Mathf.Max(1, Mathf.CeilToInt(goAt - Time.unscaledTime));
            HudKit.Banner(Loc.T("lobby.going", left, ReadyKey.ToString()), IconId.Truck, UITheme.Sage, UITheme.Ivory);
        }
        else if (counting)
            HudKit.Banner(Loc.T("lobby.going.team", sharedLeft, ReadyKey.ToString()), IconId.Truck, UITheme.Sage, UITheme.Ivory);
        else if (!solo && meReady)
            HudKit.Banner(Loc.T("lobby.waiting", rdyCount, need, ReadyKey.ToString()), IconId.Truck, UITheme.BrassLight, UITheme.Ivory);

        // Tek oyunculuda hazırlık paneli gereksiz (sağ üstteki hedef aynı şeyi söyler): yalnızca ekipte görünür
        if (solo) return;

        float sc = UITheme.Scale;
        float w = Mathf.Min(Screen.width - 24f, 340f * sc);
        float padX = UITheme.S4 * sc, padY = UITheme.S3 * sc;
        float innerW = w - padX * 2f - 3f;
        float rowH = Mathf.Round(28f * sc);

        // Yerleşim yüksekliği: başlık + oyuncu satırları + bilgi (sarılmış) + bekleme satırı + yapılacak satırı
        string info = Loc.T("lobby.info.team");
        string wait = counting ? null : Loc.T("lobby.waitall", rdyCount, need);
        Color waitCol = meReady ? UITheme.BrassLight : UITheme.IvoryDim;
        string last;
        Color lastCol = UITheme.BrassLight;
        if (counting) { last = Loc.T("lobby.allready", sharedLeft); lastCol = UITheme.Sage; }
        else if (meReady) last = Loc.T("lobby.todo.unready", ReadyKey.ToString());
        else last = Loc.T("lobby.todo.ready", ReadyKey.ToString());
        float lastH = last.IndexOf('[') >= 0 ? 26f * sc : Mathf.Max(26f * sc, Mathf.Ceil(UIUtil.MeasureHeight(last, TextRole.Body, innerW)));
        float infoH = Mathf.Ceil(UIUtil.MeasureHeight(info, TextRole.Caption, innerW));
        float waitH = wait != null ? Mathf.Max(22f * sc, Mathf.Ceil(UIUtil.MeasureHeight(wait, TextRole.BodyStrong, innerW))) : 0f;
        int shown = players.Count;
        float h = padY + 26f * sc + shown * rowH + (shown > 0 ? UITheme.S2 * sc : 0f) + infoH + UITheme.S2 * sc + waitH + lastH + padY;

        // Sol üst köşe: oda kodu kutusunun altına dizilir (görev paneliyle çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.Lobby, w, h);
        UIUtil.HudCard(r);

        float x = r.x + padX + 3f;
        float y = r.y + padY;
        y += UIUtil.SectionHeader(new Rect(x, y, innerW, 20f * sc), Loc.T("lobby.title"));

        foreach (PlayerNetworkSetup p in players)
        {
            if (p == null) continue;
            bool isReady = ready.Contains(p.OwnerClientId);

            Rect row = new Rect(x, y, innerW, rowH);
            if (p.IsOwner) UIUtil.Round(row, new Color(1f, 1f, 1f, 0.06f), 4f);

            // Oyuncu rengi: dolu kare + ince fildişi hat
            float sw = 14f * sc;
            Rect swatch = new Rect(row.x + 4f, row.y + (rowH - sw) * 0.5f, sw, sw);
            UIUtil.Round(swatch, p.PlayerColor, 3f);

            // Sağdan sola: durum rozeti, konum; kalan genişlik ad
            string badgeText = Loc.T(isReady ? "lobby.ready" : "lobby.notready");
            float badgeW = UIUtil.Measure(badgeText, TextRole.Caption).x + 24f;
            float badgeX = row.xMax - badgeW - 2f;
            UIUtil.Badge(new Vector2(badgeX, row.y + (rowH - 20f * sc) * 0.5f), badgeText,
                isReady ? UITheme.Sage : UITheme.Graphite, isReady ? UITheme.Charcoal : UITheme.IvoryDim);

            string place = Loc.T(PlaceKey(GameFlow.PhaseAt(p.transform.position)));
            float placeW = Mathf.Min(UIUtil.Measure(place, TextRole.Caption).x + 4f, innerW * 0.28f);
            Rect placeR = new Rect(badgeX - placeW - 8f, row.y, placeW, rowH);
            UIUtil.Text(placeR, place, TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleRight);

            string name = p.PlayerName + (p.IsOwner ? " " + Loc.T("pause.tag.you") : "");
            float nameX = swatch.xMax + 8f;
            UIUtil.Text(new Rect(nameX, row.y, placeR.x - nameX - 4f, rowH), name, TextRole.BodyStrong, UITheme.Ivory);

            y += rowH;
        }
        if (shown > 0) y += UITheme.S2 * sc;

        // Rol bilgisi
        UIUtil.Text(new Rect(x, y, innerW, infoH), info, TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
        y += infoH + UITheme.S2 * sc;

        // Herkes bekleniyor: X/Y hazır
        if (wait != null)
        {
            UIUtil.Text(new Rect(x, y, innerW, waitH), wait, TextRole.BodyStrong, waitCol, TextAnchor.UpperLeft);
            y += waitH;
        }

        // Yapılacak: tuş kapaklı ipucu; herkes hazırsa (geri sayım) Sage
        if (last.IndexOf('[') >= 0) DrawHintLine(new Rect(x, y, innerW, 24f * sc), last, lastCol);
        else UIUtil.Text(new Rect(x, y, innerW, lastH), last, TextRole.Body, lastCol, TextAnchor.UpperLeft);
    }

    // "Hazır durumu için [F3] bas" → [..] parçaları tuş kapağı, kalanı BodyStrong/Caption yazı
    private static void DrawHintLine(Rect r, string text, Color textColor)
    {
        float x = r.x;
        int i = 0;
        while (i < text.Length && x < r.xMax)
        {
            int open = text.IndexOf('[', i);
            int close = open >= 0 ? text.IndexOf(']', open) : -1;
            string plain = open >= 0 && close > open ? text.Substring(i, open - i) : text.Substring(i);
            if (plain.Length > 0)
            {
                float pw = Mathf.Min(UIUtil.Measure(plain, TextRole.BodyStrong).x + 2f, r.xMax - x);
                UIUtil.Text(new Rect(x, r.y, pw, r.height), plain, TextRole.BodyStrong, textColor);
                x += pw;
            }
            if (open < 0 || close < open) break;
            string key = text.Substring(open + 1, close - open - 1);
            float kw = UIUtil.KeyCapWidth(key, 14);
            UIUtil.KeyCap(new Rect(x + 2f, r.y + 2f, kw, r.height - 4f), key, 14);
            x += kw + 6f;
            i = close + 1;
        }
    }
}
