using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Görev / müze oturumu (sunucu belirler, herkese senkronlanır):
///  - Her oda kurulduğunda ve her yeni görevde rastgele bir MÜZE TEMASI seçilir (bir öncekiyle asla aynı olmaz).
///    Tema: duvar/zemin renkleri, eser görünümleri ve adları.
///  - Tohum (seed): kaidelerdeki eserler her bilgisayarda bu tohumdan AYNI üretilir (ArtworkLook).
///  - Seviye (GDD Bölüm 8): eserler, güvenlikler ve kameralar seviyeyle açılır (requiredLevel).
///      1: basit tablolar, 2 güvenlik   2: + vazo, 3. güvenlik, kameralar   3: + yüzük   4+: + büst (başyapıt)
///  - Kota: bu görevde ekip kasasına girmesi gereken para.
///  - Kamyonu kimse sürmez: kamyondaki biri K ile arka kapıyı kapatır (hapiste olmayan herkes içerideyse). Kota doluysa kaçış başarılı → sonraki görev;
///    dolu değilse polis yakalar → koşu sıfırlanır (1. seviye). Oyuncuların HEPSİ hapse düşerse de koşu sıfırlanır (çok oyunculuda).
///  - Yeni görevde eski sahteler silinir: eski müzenin eserlerine göre yapılmışlardı.
/// Kurulum: ChatManager objesine (NetworkObject zaten var) bu bileşen. Create Test Museum otomatik ekler.
/// </summary>
public class MuseumSession : NetworkBehaviour
{
    public static MuseumSession Instance { get; private set; }

    /// <summary>Tema/tohum her değiştiğinde artar; kaideler görselini yeniden kurar.</summary>
    public static int LookVersion { get; private set; }

    [Tooltip("Kotayı hesaplarken ana eserlerin karaborsa değerinden ekibe kalan pay (İyi sahte ×0.85, ekip payı ×0.8).")]
    [SerializeField] private float quotaFactor = 0.68f;
    [Tooltip("Ana eserlerin üstüne eklenen miktar: her görevde biraz küçük ganimet / ekstra eser de çalmak gerekir.")]
    [SerializeField] private int quotaExtra = 100;
    [Tooltip("1. seviyede kota, ortalama değerli 3 eserin toplamına uygulanan çarpan. Ekip payı (×0.8) ve orta kalite sahte (×0.65) hesaba katılır; seviye arttıkça quotaFactor'a yaklaşır.")]
    [SerializeField] private float level1QuotaFactor = 0.5f;
    [SerializeField] private string museumRootName = "TestMuseum";

    private readonly NetworkVariable<int> themeIndex = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<int> seed = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> level = new NetworkVariable<int>(1);
    private readonly NetworkVariable<int> quota = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> baseline = new NetworkVariable<int>(int.MinValue);
    private readonly NetworkVariable<bool> staffDoorOpen = new NetworkVariable<bool>(false); // Staff Only kapısı maymuncukla açıldı mı
    private readonly NetworkVariable<bool> doorClosed = new NetworkVariable<bool>(false);      // kamyonun arka kapısı
    private readonly NetworkVariable<byte> sequence = new NetworkVariable<byte>(0);            // EscapeSequence
    private readonly NetworkVariable<double> sequenceStart = new NetworkVariable<double>(0);   // sunucu saatiyle başlangıç (geç katılan doğru yerden görür)

    private static int lastTheme = -1; // aynı uygulama açıkken bir sonraki oda/görev farklı müze olsun

    /// <summary>Koşu sıfırlanırken (polis / herkes yakalandı) KİŞİSEL cüzdan ve kişisel upgrade'ler korunsun mu? false yapılırsa onlar da sıfırlanır (ekip çantası upgrade'i dahil).</summary>
    public static readonly bool ResetKeepsPersonalProgress = true;

    public enum EscapeSequence : byte { None = 0, Departure = 1, Police = 2, AllCaught = 3 }

    /// <summary>Sekans süreleri (sn). Sunucu bitince görevi geçirir / koşuyu sıfırlar.</summary>
    public static float SequenceDuration(EscapeSequence s)
    {
        switch (s)
        {
            case EscapeSequence.Departure: return 7f;
            case EscapeSequence.Police: return 8.5f;
            case EscapeSequence.AllCaught: return 4.5f;
            default: return 0f;
        }
    }

    private GUIStyle hudStyle, hudBig;
    private MaterialPropertyBlock themeBlock;

    public bool Ready => IsSpawned && themeIndex.Value >= 0;
    public int Level => level.Value;
    public int Seed => seed.Value;
    /// <summary>Müze kök objesinin adı (menü arka planı da aynı müzeyi kurar).</summary>
    public string MuseumRootName => museumRootName;
    public int ThemeIndex => Mathf.Max(0, themeIndex.Value);
    public MuseumTheme Theme => ArtworkLook.Theme(ThemeIndex);
    public int Quota => quota.Value;
    /// <summary>Staff Only kapısı bu görevde açıldı mı? (LockpickSystem açar, her yeni müzede yeniden kilitlenir.)</summary>
    public bool StaffDoorOpen => staffDoorOpen.Value;

    public void ServerOpenStaffDoor()
    {
        if (IsServer) staffDoorOpen.Value = true;
    }
    /// <summary>Bu görevde ekibin kazandığı para: kasaya GİREN gelir (TeamBank.TotalEarned); kasadan yapılan harcamalar kotayı düşürmez.</summary>
    public int Earned => TeamBank.Instance != null && baseline.Value != int.MinValue ? TeamBank.Instance.TotalEarned - baseline.Value : 0;

    /// <summary>Para kotası doldu mu? (Görevin bitmesi için ekibin kamyona da dönmesi gerekir.)</summary>
    public bool QuotaMet => quota.Value > 0 && Earned >= quota.Value;

    /// <summary>Kaçış sekansı sürüyor mu? (kontroller kilitli, yeni yakalanma yok)</summary>
    public bool SequenceActive => sequence.Value != 0;
    public EscapeSequence ActiveSequence => (EscapeSequence)sequence.Value;
    /// <summary>Kamyonun arka kapısı kapalı mı?</summary>
    public bool DoorClosed => doorClosed.Value;
    /// <summary>Sekansın başlangıcından beri geçen süre (sn, sunucu saatine göre).</summary>
    public float SequenceTime => SequenceActive && NetworkManager != null ? Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - sequenceStart.Value)) : 0f;

    /// <summary>Kapı kapatılıp kamyon gidebilir mi: kota dolu ve hapiste olmayan herkes kamyonda.</summary>
    public bool Complete => QuotaMet && TruckZone.GetStatus().EveryoneIn;

    private void Awake()
    {
        Instance = this;
        if (GetComponent<VisitorManager>() == null) gameObject.AddComponent<VisitorManager>(); // müzeyi gezen ziyaretçiler
        if (GetComponent<TruckEscapeUI>() == null) gameObject.AddComponent<TruckEscapeUI>(); // kapı tuşu + kalkış / polis / yakalandı sekansları (ağ nesnesi değil)
    }

    // ---------------- Ziyaretçiler (VisitorManager konumları bu objenin RPC'siyle gönderir) ----------------

    public void ServerSendVisitors(byte[] data)
    {
        if (IsServer && IsSpawned && data != null) VisitorSnapshotRpc(data);
    }

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    private void VisitorSnapshotRpc(byte[] data)
    {
        if (VisitorManager.Instance != null) VisitorManager.Instance.ClientApply(data);
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        ForgeryRegistry.Clear(); // yeni oda: önceki oturumun sahteleri kalmasın

        themeIndex.OnValueChanged += OnLookChanged;
        seed.OnValueChanged += OnSeedChanged;
        level.OnValueChanged += OnLookChanged;

        if (IsServer)
        {
            level.Value = 1;
            PickNewMuseum();
            lootPending = true; // yeni oda: ilk görevde de ganimetler masalara konur
            lootRecheckAt = 0f;
        }
        ApplyTheme();
    }

    public override void OnNetworkDespawn()
    {
        PlayerController.InputLocked = false;
        PlayerController.ShakeAmount = 0f;
        themeIndex.OnValueChanged -= OnLookChanged;
        seed.OnValueChanged -= OnSeedChanged;
        level.OnValueChanged -= OnLookChanged;
    }

    private void OnLookChanged(int a, int b) => ApplyTheme();

    private void OnSeedChanged(int a, int b)
    {
        WorkbenchUI.ForceClose(); // atölye açıksa silinecek dokuyu göstermesin
        ForgeryRegistry.Clear(); // yeni görev: eski sahtelerin verisi artık gereksiz
        ArtworkLook.ClearCaches(); // eski müzenin eser dokuları silinsin (önce temizle, sonra yeniden kur)
        ApplyTheme();

        // Eldeki ve yerdeki eserlerin görselleri silinen dokulara bakmasın: yeni dokularla yeniden kur
        foreach (PlayerInventory inv in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None)) inv.RebuildHeldVisual();
        foreach (WorldItem wi in FindObjectsByType<WorldItem>(FindObjectsSortMode.None)) wi.RebuildArtVisual();
    }

    // ---------------- Sunucu ----------------

    private void PickNewMuseum()
    {
        int n = ArtworkLook.Themes.Length;
        int pick = Random.Range(0, n);
        if (n > 1 && pick == lastTheme) pick = (pick + 1 + Random.Range(0, n - 1)) % n;
        lastTheme = pick;
        themeIndex.Value = pick;
        seed.Value = Random.Range(1, int.MaxValue);
        quota.Value = 0; // Update'te kaideler hazır olunca hesaplanır
        staffDoorOpen.Value = false; // yeni müzede Staff Only kapısı yine kilitli
    }

    private float nextCaughtCheck;
    private const float ConfirmWindow = 3.5f; // istemci penceresi 3 sn + ağ payı
    private readonly System.Collections.Generic.Dictionary<ulong, float> pendingConfirm = new System.Collections.Generic.Dictionary<ulong, float>();

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            if (baseline.Value == int.MinValue && TeamBank.Instance != null && TeamBank.Instance.IsSpawned)
                baseline.Value = TeamBank.Instance.TotalEarned;

            if (quota.Value == 0 && ArtworkDisplay.All.Count > 0)
                quota.Value = ComputeQuota();

            // Ganimetler: yerleşim bu görevin tohumuyla kurulduktan SONRA ve kaideler hazırken (quota>0) masalara konur
            if (lootPending && quota.Value > 0 && (LayoutReady() || MuseumLayout.Instance == null))
            {
                lootPending = false;
                ServerRefreshLoot();
                lootRecheckAt = Time.time + 1.5f;
            }
            else if (lootRecheckAt > 0f && Time.time >= lootRecheckAt)
            {
                lootRecheckAt = 0f;
                ServerRecheckLoot();
            }

            if (SequenceActive)
            {
                // Sekans bitti: kaçış başarılıysa sonraki görev, polis / herkes yakalandı ise koşu sıfırlanır
                EscapeSequence s = ActiveSequence;
                if (NetworkManager.ServerTime.Time - sequenceStart.Value >= SequenceDuration(s))
                {
                    sequence.Value = 0;
                    if (s == EscapeSequence.Departure) ServerNextMission();
                    else ServerResetRun();
                }
            }
            else if (Time.unscaledTime >= nextCaughtCheck)
            {
                nextCaughtCheck = Time.unscaledTime + 0.25f;
                ServerCheckAllCaught(); // periyodik: biri oyundan çıkınca kalanların hepsi hapisteyse de yakalanır
            }
        }
    }

    // ---------------- Kamyon kapısı / sekanslar (sunucu) ----------------

    private float noEscapeSince = -1f;

    /// <summary>Sunucu: HERKES hapisteyse ve kimse kaçamıyorsa koşu biter.
    /// Çok oyunculu: hepsi içerideyse (kurtaracak kimse yok). Tek oyunculu: içeride ve anahtar çizecek fırça / 2 boya yoksa (birkaç sn sonra).</summary>
    public void ServerCheckAllCaught()
    {
        if (!IsServer || !Ready || SequenceActive) return;
        int total = 0, jailed = 0, canEscape = 0;
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;
            total++;
            if (p.IsJailed) { jailed++; if (JailCell.CanKeyEscape(p)) canEscape++; }
        }
        if (total == 0 || jailed < total) { noEscapeSince = -1f; return; }
        // Tek oyunculu: anahtar çizerek ya da 30 sn sonra çıkılır (JailCell) → koşu bitmez
        if (JailCell.KeyEscapeAllowed) return;
        ServerBeginSequence(EscapeSequence.AllCaught);
    }

    private void ServerBeginSequence(EscapeSequence s)
    {
        sequenceStart.Value = NetworkManager.ServerTime.Time;
        sequence.Value = (byte)s;
        if (s == EscapeSequence.Departure || s == EscapeSequence.Police) doorClosed.Value = true;
    }

    /// <summary>İstemci (kamyondaki oyuncu): arka kapıyı kapatmayı iste. confirmed = kota dolmadan gitmeyi onayladı.</summary>
    public void RequestCloseDoor(bool confirmed)
    {
        if (IsSpawned) CloseDoorRpc(confirmed);
    }

    [Rpc(SendTo.Server)]
    private void CloseDoorRpc(bool confirmed, RpcParams rpcParams = default)
    {
        if (!Ready || SequenceActive || doorClosed.Value) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) || client.PlayerObject == null) return;

        PlayerNetworkSetup ps = client.PlayerObject.GetComponent<PlayerNetworkSetup>();
        PlayerStealth st = client.PlayerObject.GetComponent<PlayerStealth>();
        PlayerInventory inv = client.PlayerObject.GetComponent<PlayerInventory>();
        if (ps == null || st == null || st.IsJailed || st.IsKnockedOutNet) return;
        if (!TruckZone.IsInside(ps)) return; // yalnızca kamyonun içindeyken (sunucudaki konuma göre)

        TruckZone.Status status = TruckZone.GetStatus();
        if (status.outsideCount > 0)
        {
            if (inv != null) inv.ServerNotify("truck.outside", status.firstOutside ?? "");
            return;
        }
        bool met = QuotaMet;
        if (!met)
        {
            // Onay istemci beyanı olamaz: sunucu önce aynı oyuncudan gelen onaysız (uyarı) isteği kaydeder,
            // onaylı istek yalnızca bundan en fazla ConfirmWindow sn sonra geçerlidir.
            float now = Time.unscaledTime;
            if (!confirmed)
            {
                pendingConfirm[sender] = now;
                return; // uyarıyı istemci kendi ekranında gösterir
            }
            if (!pendingConfirm.TryGetValue(sender, out float asked) || now - asked > ConfirmWindow) return;
            pendingConfirm.Remove(sender);
            if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("truck.leftearly", ps.PlayerName, "");
        }
        ServerBeginSequence(met ? EscapeSequence.Departure : EscapeSequence.Police);
    }

    // ---------------- Küçük ganimetler (galeri masalarındaki eşyalar) ----------------
    // ESKİ SORUN (üç kez görüldü): sahneye elle konmuş ganimetler masaya YAKINLIKLA "yakalanıyordu". Müze yerleşimi masaları taşırken
    // sahne eşyaları henüz ağda doğmamışsa (ilk görev) eşyalar yerinde kalıp YERE düşüyor, masaların yeni yerinin yanında olmadıkları için
    // hiç kaydedilmiyor ve sonraki seviyelerde de hiç doğmuyordu.
    // YENİ DÜZEN: ganimet listesi ItemDatabase'den (kimliği loot_ ile başlayan eşyalar) alınır. HER görevde (ilki dahil) yerleşim bittikten
    // sonra masada duran eski ganimetler silinir ve her masanın üst yüzeyine (masanın görsel sınırlarından) yeni eşyalar doğar.
    // Masadaki eşyalar sabitlenir (kinematik): fizik onları masadan kaydırıp yere düşüremez. Alınınca normal eşya gibi çantaya girer.
    private const int LootPerTable = 2;
    private readonly System.Collections.Generic.List<ItemDefinition> lootDefs = new System.Collections.Generic.List<ItemDefinition>();
    private readonly System.Collections.Generic.List<WorldItem> lootLive = new System.Collections.Generic.List<WorldItem>();
    private readonly System.Collections.Generic.List<Vector3> lootTargets = new System.Collections.Generic.List<Vector3>();
    private bool lootPending = true; // yerleşim kurulunca ganimetler masalara konacak (ilk görev dahil)
    private float lootRecheckAt;     // ikinci kontrol zamanı (konumu bozulan masa eşyası varsa düzeltir)
    private int lootRound;

    private void BuildLootDefs()
    {
        if (lootDefs.Count > 0) return;
        ItemDatabase db = ItemDatabase.Instance;
        if (db != null)
            for (int i = 0; i < db.Count; i++)
            {
                ItemDefinition d = db.Get(i);
                if (d != null && d.IsSmallLoot && d.worldPrefab != null && !lootDefs.Contains(d)) lootDefs.Add(d);
            }
        if (lootDefs.Count > 0) return;
        // Veritabanı yoksa sahnede duran ganimetlerden öğren
        foreach (WorldItem wi in WorldItem.All)
            if (wi != null && wi.Item != null && wi.Item.IsSmallLoot && wi.Item.worldPrefab != null && !lootDefs.Contains(wi.Item)) lootDefs.Add(wi.Item);
    }

    /// <summary>Masanın dünya sınırları (görsel + çarpışma). Üst yüzey = max.y.</summary>
    private static bool TableBounds(Transform table, out Bounds b)
    {
        b = default;
        bool any = false;
        foreach (Renderer r in table.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        foreach (Collider c in table.GetComponentsInChildren<Collider>())
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        return any;
    }

    /// <summary>Masanın bu noktadaki üst yüzeyi: yukarıdan aşağı ışın, yalnızca masanın kendi çarpıştırıcıları (üstteki yazı / süs sayılmaz).</summary>
    private static float TableTopAt(Transform table, Vector3 xz, float fallback)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(xz.x, fallback + 1.5f, xz.z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || !h.collider.transform.IsChildOf(table)) continue;
            if (h.collider.GetComponentInParent<WorldItem>() != null) continue;
            best = Mathf.Max(best, h.point.y);
        }
        return best > float.NegativeInfinity ? best : fallback;
    }

    /// <summary>Sunucu: masa eşyası mı (masaya bizim koyduğumuz ya da sahneye elle konmuş, oyuncunun atmadığı ganimet)?</summary>
    private bool IsTableLoot(WorldItem wi)
    {
        if (wi == null || wi.Item == null || !wi.Item.IsSmallLoot) return false;
        return lootLive.Contains(wi) || !wi.WasThrown;
    }

    /// <summary>Sunucu: yerleşim bitti → eski masa ganimetlerini sil, her masanın üstüne yenilerini koy.</summary>
    private void ServerRefreshLoot()
    {
        if (!IsServer) return;
        BuildLootDefs();
        Physics.SyncTransforms();

        // 1) Önceki görevden kalan (alınmamış) masa ganimetleri ve sahneye elle konmuş olanlar silinir
        foreach (WorldItem wi in WorldItem.All.ToArray())
            if (wi != null && wi.IsSpawned && !wi.IsConsumed && IsTableLoot(wi)) wi.ServerRemoveSceneItem();
        lootLive.Clear();
        lootTargets.Clear();

        GameObject root = GameObject.Find(museumRootName);
        // Masalar müze kökünün doğrudan altında DEĞİL (TestMuseum/Museum/LootTable_*): tüm alt hiyerarşide aranır
        var tables = new System.Collections.Generic.List<Transform>();
        if (root != null)
            foreach (Transform tr in root.GetComponentsInChildren<Transform>(false))
                if (tr.name.StartsWith("LootTable_") && (tr.parent == null || !tr.parent.name.StartsWith("LootTable_"))) tables.Add(tr);
        if (tables.Count == 0) Debug.LogWarning("[Loot] Müzede LootTable_ masası bulunamadı (" + museumRootName + ")");
        if (lootDefs.Count == 0) Debug.LogWarning("[Loot] ItemDatabase'de loot_ eşyası yok: ganimet doğmaz");
        if (tables.Count > 0 && lootDefs.Count > 0)
        {
            int t = 0;
            lootRound++;
            foreach (Transform table in tables)
            {
                if (!TableBounds(table, out Bounds b)) continue;
                bool alongX = b.size.x >= b.size.z;
                Vector3 axis = alongX ? Vector3.right : Vector3.forward;
                float len = alongX ? b.size.x : b.size.z;
                float spacing = Mathf.Min(0.7f, len / (LootPerTable + 0.5f));
                for (int i = 0; i < LootPerTable; i++)
                {
                    ItemDefinition def = lootDefs[(t * LootPerTable + i + lootRound + seed.Value) % lootDefs.Count];
                    Vector3 xz = new Vector3(b.center.x, 0f, b.center.z) + axis * ((i - (LootPerTable - 1) * 0.5f) * spacing);
                    Quaternion rot = Quaternion.Euler(0f, ((t * 37 + i * 71 + lootRound * 13) % 360), 0f);
                    GameObject go = Instantiate(def.worldPrefab, new Vector3(xz.x, b.max.y + 0.3f, xz.z), rot);
                    NetworkObject no = go.GetComponent<NetworkObject>();
                    WorldItem nw = go.GetComponent<WorldItem>();
                    if (no == null || nw == null) { Destroy(go); continue; }
                    Physics.SyncTransforms();
                    Vector3 target = new Vector3(xz.x, TableTopAt(table, xz, b.max.y) + nw.PivotToBottom() + 0.005f, xz.z);
                    go.transform.position = target;
                    nw.SetPinned(true);
                    no.Spawn(true);
                    nw.SetPinned(true); // ağ fizik bileşeni spawn'da kinematiği geri açmış olabilir
                    lootLive.Add(nw);
                    lootTargets.Add(target);
                }
                t++;
            }
        }

        // Diğer sahne eşyaları (masa dışındakiler): havada ya da zeminin altında kalmasınlar
        foreach (WorldItem wi in WorldItem.All.ToArray())
        {
            if (wi == null || !wi.IsSpawned || wi.IsConsumed || !wi.IsSceneItem || lootLive.Contains(wi)) continue;
            wi.ServerSettleOnGround();
        }
    }

    /// <summary>Sunucu: masadaki ganimetlerin konumu bozulduysa (ağ / fizik) yerine geri koy.</summary>
    private void ServerRecheckLoot()
    {
        for (int i = 0; i < lootLive.Count && i < lootTargets.Count; i++)
        {
            WorldItem wi = lootLive[i];
            if (wi == null || !wi.IsSpawned || wi.IsConsumed || !wi.Pinned) continue;
            if ((wi.transform.position - lootTargets[i]).sqrMagnitude > 0.04f) wi.ServerPlaceAt(lootTargets[i], wi.transform.rotation);
        }
    }

    /// <summary>Yerleşim (MuseumLayout) bu tohum + temayla kuruldu mu?</summary>
    private bool LayoutReady()
    {
        MuseumLayout l = MuseumLayout.Instance;
        return l != null && l.AppliedSeed == seed.Value && l.AppliedTheme == themeIndex.Value;
    }

    private int ComputeQuota()
    {
        int sum = 0, count = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !IsDisplayActive(d)) continue;
            if (d.Kind == ArtKind.None) continue; // ana hedefler: sahtesi yapılabilen eserler
            // Staff Only eseri BONUS: kota için zorunlu değil (kapı maymuncukla açılır, maymuncuk bitebilir → görev kilitlenmesin)
            if (d.IsStaffOnlyPiece) continue;
            sum += d.Value;
            count++;
        }
        if (count == 0) return 50;

        // 1. seviye: ortalama değerli 3 eser çalınınca kota dolsun (kasaya yalnızca ekip payı girer, sahte kalitesi fiyatı düşürür).
        // Seviye arttıkça (6. seviyede) hedef tüm eserlerin toplamına ve eski formüle (quotaFactor + quotaExtra) yaklaşır.
        float t = Mathf.Clamp01((level.Value - 1) / 5f);
        float baseSum = Mathf.Min(sum, sum / (float)count * 3f);
        float target = Mathf.Lerp(baseSum, sum, t);
        float factor = Mathf.Lerp(level1QuotaFactor, quotaFactor, t);
        float extra = Mathf.Lerp(0f, quotaExtra, t);
        int q = Mathf.RoundToInt(target * factor + extra);
        // Tek oyunculu denge: bağlı tek oyuncu varken kota ~%60'a düşer (tek kişi aynı yükü taşıyamaz)
        if (NetworkManager != null && NetworkManager.ConnectedClientsIds.Count <= 1) q = Mathf.RoundToInt(q * 0.6f);
        return Mathf.Max(50, Mathf.RoundToInt(q / 50f) * 50);
    }

    /// <summary>Sunucu: kaçış başarılı, sonraki müze (seviye + 1).</summary>
    public void ServerNextMission()
    {
        if (!IsServer) return;
        ServerStartMission(level.Value + 1, false);
    }

    /// <summary>Sunucu: koşuyu baştan başlatır (polis yakaladı / herkes yakalandı). Seviye 1, yeni müze, ekip kasası ve çantadaki eserler sıfırlanır.
    /// Kişisel cüzdan + kişisel upgrade'ler ResetKeepsPersonalProgress true iken KALIR.</summary>
    public void ServerResetRun()
    {
        if (!IsServer) return;
        if (TeamBank.Instance != null)
        {
            TeamBank.Instance.ServerReset();
            if (!ResetKeepsPersonalProgress) TeamBank.Instance.ServerResetUpgrades();
        }
        foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            PlayerInventory inv = c.PlayerObject.GetComponent<PlayerInventory>();
            if (inv == null) continue;
            inv.ServerConfiscateOriginals(null); // çalınmış orijinaller yok olur (müze zaten yenilenecek)
            if (!ResetKeepsPersonalProgress) inv.ServerResetPersonalProgress();
        }
        foreach (WorldItem wi in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
        {
            if (wi != null && wi.IsSpawned && wi.Item != null && wi.Item.type == ItemType.Original) wi.ServerConsume();
        }
        ServerStartMission(1, true);
    }

    /// <summary>Ortak görev başlangıcı (sonraki seviye ya da koşu sıfırlaması).</summary>
    private void ServerStartMission(int newLevel, bool runReset)
    {
        if (!IsServer) return;

        // Eski müze için yapılan sahteler yeni müzede işe yaramaz: çantalardan ve yerden kaldır
        foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            PlayerInventory inv = c.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null) inv.ServerRemoveForgeries();
        }
        foreach (WorldItem wi in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
        {
            if (wi != null && wi.IsSpawned && wi.Item != null && wi.Item.type == ItemType.Forgery) wi.ServerConsume();
        }
        ForgeryRegistry.Clear();


        if (SecurityManager.Instance != null) SecurityManager.Instance.ServerResetForMission(); // alarm / aranma / kovalama sıfırlanır

        // ÖNCE yeni tohum/harita (bölmeler, hapis, dolaplar yeniden konumlanır), SONRA oyuncular serbest bırakılıp kamyona ışınlanır:
        // böylece kimse eski hücrenin/dolabın yerindeki yeni duvarın içinde kalmaz.
        level.Value = Mathf.Max(1, newLevel);
        PickNewMuseum();
        lootPending = true; // ganimetler, yerleşim kurulduktan SONRA (Update) masaların üstüne oturtulur
        baseline.Value = TeamBank.Instance != null ? TeamBank.Instance.TotalEarned : 0;
        doorClosed.Value = false; // kapı yeni görevde açık başlar
        sequence.Value = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null) d.ServerResetForMission();

        JailCell.ServerReleaseAll(false);   // hapistekiler çıkar (ışınlamayı aşağıda kamyona biz yaparız)
        foreach (PlayerStealth p in PlayerStealth.All) if (p != null && p.IsSpawned && p.IsJailed) p.ServerSetJailed(0); // hapis yoksa da temizle
        HidingSpot.ServerReleaseAll();      // dolaptakiler çıkar: doluluk + hidden temizlenir, sahibine çıkış bildirilir
        ServerSendEveryoneToTruck();
        // Garaj ayrı sahnedeyse yeni görev garajda başlar (hazırlık, boya, alet); kamyona garaj kapısından binilir
        GarageSceneLink.ServerSendEveryoneToGarage();
        // Güvence: yeni harita kurulurken (hapis bodrumu silinip yeniden kurulurken) ışınlanma kaçarsa oyuncu boşlukta kalıyordu.
        // Kısa süre sonra garajda / kamyonda OLMAYAN oyuncu tekrar ışınlanır.
        StartCoroutine(ServerVerifySpawns());
        if (ChatManager.Instance != null)
        {
            if (runReset) ChatManager.Instance.ServerSystemMessage("mission.restart", Theme.name, "");
            else ChatManager.Instance.ServerSystemMessage("mission.next", Theme.name, level.Value.ToString());
        }
    }

    /// <summary>Sunucu: bütün oyuncuları kamyondaki doğma noktalarına ışınlar (yeni görev başı).</summary>
    private System.Collections.IEnumerator ServerVerifySpawns()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            yield return new WaitForSeconds(pass == 0 ? 0.4f : 0.8f);
            foreach (PlayerStealth p in PlayerStealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsJailed) continue;
                GamePhase ph = GameFlow.PhaseAt(p.transform.position);
                if (ph == GamePhase.Garage || ph == GamePhase.Truck) continue;
                ServerSendToStart(p);
            }
        }
    }

    /// <summary>Sunucu: oyuncuyu görev başı noktasına (garaj varsa garaj, yoksa kamyon) ışınlar.</summary>
    public static void ServerSendToStart(PlayerStealth p)
    {
        if (p == null) return;
        GarageSpawnPoint gp = GarageSpawnPoint.For(p.OwnerClientId);
        if (gp != null) { p.ServerTeleport(gp.transform.position + Vector3.up, gp.transform.eulerAngles.y); return; }
        if (SpawnPoint.All.Count > 0)
        {
            SpawnPoint sp = SpawnPoint.All[(int)(p.OwnerClientId % (ulong)SpawnPoint.All.Count)];
            p.ServerTeleport(sp.transform.position + Vector3.up, sp.transform.eulerAngles.y);
        }
    }

    private void ServerSendEveryoneToTruck()
    {
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;
            if (p.IsHidden) p.ServerSetHidden(false); // dolap kaydı bulunamayan artık-gizli durum
            p.ServerClearHeat();
            if (SpawnPoint.All.Count > 0)
            {
                SpawnPoint sp = SpawnPoint.All[(int)(p.OwnerClientId % (ulong)SpawnPoint.All.Count)];
                p.ServerTeleport(sp.transform.position + Vector3.up, sp.transform.eulerAngles.y);
            }
        }
    }

    // ---------------- Herkes ----------------

    public static bool IsDisplayActive(ArtworkDisplay d) => IsLevelActive(d.RequiredLevel);

    /// <summary>Bu seviyede açılan şey (eser, güvenlik, kamera) şu anki görevde aktif mi?</summary>
    public static bool IsLevelActive(int requiredLevel)
    {
        return Instance == null || !Instance.Ready || Instance.Level >= requiredLevel;
    }

    public VaseLook VaseFor(ArtworkDisplay d) => ArtworkLook.Vase(Seed, ThemeIndex, d.name);
    public PaintingLook PaintingFor(ArtworkDisplay d) => ArtworkLook.Painting(Seed, ThemeIndex, d.name);
    public BustLook BustFor(ArtworkDisplay d) => ArtworkLook.Bust(Seed, ThemeIndex, d.name);
    public JewelLook JewelFor(ArtworkDisplay d) => ArtworkLook.Jewel(Seed, ThemeIndex, d.name);

    /// <summary>Kaide için görünür eser adı (temaya göre).</summary>
    /// <summary>Eserin adı. Etiketi SOLUK olup henüz kimsenin yakından okumadığı eserde harflerin çoğu "?" (BodyCam).</summary>
    public string ArtworkName(ArtworkDisplay d) => BodyCam.MaskName(d, RealArtworkName(d));

    /// <summary>Eserin gerçek adı (soluk etiket maskesi olmadan).</summary>
    public string RealArtworkName(ArtworkDisplay d)
    {
        if (!Ready || d == null) return d != null && d.Artwork != null ? d.Artwork.DisplayName : "";
        switch (d.Kind)
        {
            case ArtKind.Vase: return VaseFor(d).Name;
            case ArtKind.Painting: return PaintingFor(d).Name;
            case ArtKind.Bust: return BustFor(d).Name;
            case ArtKind.Jewelry: return JewelFor(d).Name;
            default: return d.Artwork != null ? d.Artwork.DisplayName : "";
        }
    }

    private void ApplyTheme()
    {
        if (!Ready) return;
        LookVersion++;
        MuseumTheme th = Theme;

        // Müze yapısını boya. MaterialPropertyBlock: malzeme kopyası üretmez, paylaşılan asset'ler değişmez.
        // Kamyon, sokak ve garaj farklı adlar kullandığı için (TruckWall_, Ground_, GarageWall_) temadan etkilenmez.
        GameObject root = GameObject.Find(museumRootName);

        // Her haritada müzenin içi (kaideler, bölmeler, dekor, kameralar, devriye) ve dışı (cephe, sokak, ışık) değişir
        bool layoutChanged = MuseumLayout.Apply(root, Seed, ThemeIndex);
        if (layoutChanged && IsServer)
        {
            RuntimeNavMeshBaker.Rebake(); // güvenlikler yeni bölmelerin etrafından dolaşsın
            foreach (GuardAI g in SecurityManager.Guards) if (g != null) g.ServerResetForLayout();
        }
        if (root != null)
        {
            if (themeBlock == null) themeBlock = new MaterialPropertyBlock();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                Color? c = null;
                if (n.StartsWith("Wall_") || n.StartsWith("Divider") || n.StartsWith("Gallery_Partition")) c = th.wall;
                else if (n == "Floor" || n.StartsWith("Floor_")) c = th.floor;
                else if (n.StartsWith("Display_")) c = th.trim;
                else if (n.StartsWith("LootTable")) c = th.trim * 0.6f;
                else if (n.StartsWith("Trim_")) c = th.accent;
                if (c == null) continue;
                Color col = c.Value; col.a = 1f;
                r.GetPropertyBlock(themeBlock);
                themeBlock.SetColor("_BaseColor", col);
                themeBlock.SetColor("_Color", col);
                r.SetPropertyBlock(themeBlock);
            }
            foreach (Light l in root.GetComponentsInChildren<Light>(true))
            {
                if (l.gameObject.name.StartsWith("MuseumLight")) l.color = th.light;
            }
        }

        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null) d.ForceRefreshVisual();
    }

    // ---------------- HUD ----------------

    private void OnGUI()
    {
        if (!Ready || SessionMenuUI.IsPaused) return;
        if (WorkbenchUI.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        MuseumTheme th = Theme;
        bool quotaMet = QuotaMet;
        PlayerStealth me = PlayerStealth.Local;
        bool localInside = me != null && me.IsSpawned && !me.IsJailed && TruckZone.IsInside(me.GetComponent<PlayerNetworkSetup>());
        bool showTruck = quotaMet || localInside;

        float sc = UITheme.Scale;
        float pad = 12f * sc;
        float rowH = 22f * sc;
        float w = 300f * sc;
        float innerW = w - pad * 2f;
        float h = pad * 0.8f + 18f * sc + rowH + 8f * sc + (showTruck ? rowH : 0f) + pad * 0.8f;

        // Sol üst köşe: görev adı (küçük), kota ve ince çubuk — simgesiz, hafif saydam kart
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.Mission, w, h);
        UIUtil.HudCard(r);

        float x = r.x + pad, y = r.y + pad * 0.8f;
        string title = EconomyPanelUI.Fit(Loc.T("mission.title", Level, th.name, th.City), TextRole.Caption, innerW);
        UIUtil.Text(new Rect(x, y, innerW, 18f * sc), title, TextRole.Caption, UITheme.IvoryDim);
        y += 18f * sc;

        int earned = Mathf.Max(0, Earned);
        UIUtil.Text(new Rect(x, y, innerW, rowH), Loc.T("mission.quota", earned, Quota), TextRole.BodyStrong, quotaMet ? UITheme.Sage : UITheme.Ivory);
        y += rowH + 2f * sc;
        float ratio = Quota > 0 ? Mathf.Clamp01(earned / (float)Quota) : 0f;
        UIUtil.ProgressBar(new Rect(x, y, innerW, 4f * sc), ratio, quotaMet ? UITheme.Sage : UITheme.Ivory);
        y += 6f * sc;

        if (!showTruck) return;

        // Kamyon durumu: kimler kamyonda, kimler hapiste
        TruckZone.Status st = TruckZone.GetStatus();
        UIUtil.Text(new Rect(x, y, innerW, rowH), Loc.T("mission.truckinfo", st.inside, st.total, st.jailed),
            TextRole.Caption, st.jailed > 0 ? UITheme.Ochre : UITheme.IvoryDim);

        // Kota doldu ama yerel oyuncu kamyonun dışında: geri dönmesi gerek (kamyondakilere kapı ipucunu TruckEscapeUI çizer)
        if (quotaMet && !localInside && !SequenceActive)
            HudKit.Banner(Loc.T("mission.return"), IconId.Truck, UITheme.Sage, UITheme.Ivory);
    }
}
