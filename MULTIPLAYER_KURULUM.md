# Mover-Sim — Online Oda Sistemi Kurulumu

Biriniz **Oda Kur** der, ekranda 6 haneli bir kod çıkar. Diğeri kodu yazıp **Katıl** der.
Port açmak ya da aynı ağda olmak gerekmez (Unity Relay üzerinden bağlanılır).

## Nasıl çalışıyor (raporda kullanabilirsiniz)

- **Netcode for GameObjects (NGO)**, host-client modeli: odayı kuran hem sunucu hem oyuncudur (host).
- **Unity Multiplayer Services (Sessions)**: oda oluşturma ve koda göre katılma. Arka planda **Relay** kullanır,
  yani bağlantı Unity'nin sunucusu üzerinden geçer, NAT/port sorunu olmaz.
- **Oyuncu hareketi owner-authoritative**: herkes kendi karakterini kendi bilgisayarında hareket ettirir,
  pozisyon diğerlerine gönderilir → kendi hareketinde gecikme hissedilmez.
- **Eşya taşıma — sahiplik devri**: E'ye basınca sunucuya istek gider. Obje boştaysa sunucu objenin
  sahipliğini o oyuncuya verir, fizik artık o oyuncuda hesaplanır. Aynı anda iki kişi aynı objeyi alamaz
  ("Başka biri taşıyor" yazar). Objeyi son tutan oyundan çıkarsa obje kaybolmaz, sunucuya döner.

## Eklenen / değişen dosyalar

| Dosya | Görevi |
|---|---|
| `Assets/Script/Network/SessionManager.cs` | Unity servislerine giriş, oda kur / katıl / ayrıl |
| `Assets/Script/Network/SessionMenuUI.cs` | Ana menü, oyun içi oda kodu, Esc menüsü |
| `Assets/Script/Network/PlayerNetworkSetup.cs` | Kendi oyuncun mu başkasının mı → kamera/kontrolleri ayarlar, doğma noktası |
| `Assets/Script/Network/OwnerNetworkTransform.cs` | Pozisyonu sahibin göndermesini sağlar |
| `Assets/Script/Network/NetworkPickupable.cs` | Kutuların ağda kaldırılması / sahiplik devri |
| `Assets/Script/Network/SpawnPoint.cs` | Oyuncu doğma noktaları |
| `ObjectCarrier.cs` (değişti) | Online'da sunucudan izin alarak kaldırma; tek kişilik mod eskisi gibi çalışır |
| `PlayerController.cs` (değişti) | Menü açıkken fare ile kamera dönmez |

---

## Kurulum adımları (Unity Editor'de, bir kere yapılır)

> Paketleri kurmadan önce Unity'yi açarsanız Console'da kırmızı hatalar görürsünüz
> (`Unity.Netcode bulunamadı` gibi). 1. adımdan sonra kendiliğinden kaybolurlar.

### 1. Paketleri kur
`Window > Package Manager` → sol üstteki **+** → **Install package by name...** ve sırayla:

- `com.unity.netcode.gameobjects`
- `com.unity.services.multiplayer`
- `com.unity.multiplayer.playmode` (test için, isteğe bağlı ama çok işe yarar)

### 2. Projeyi Unity Cloud'a bağla
1. `Edit > Project Settings > Services` → Unity hesabınla giriş yap, bir organizasyon seç, **Link / Create project**.
2. Arkadaşını [cloud.unity.com](https://cloud.unity.com) üzerinden aynı organizasyona üye olarak ekle.
3. Unity Cloud panelinde projeyi aç → **Relay** ve **Lobby** ürünlerinin açık olduğundan emin ol
   (kapalıysa "Get started / Enable" de). Küçük projeler için ücretsiz katman yeterli.
4. `ProjectSettings` klasörünü git'e commit'leyin ki ikinizde de aynı proje ID'si olsun.

### 3. Player'ı prefab yap
1. `Assets` içinde `Prefabs` klasörü aç, sahnedeki **Player**'ı bu klasöre sürükle → prefab oldu.
2. Prefab'ı aç (çift tık) ve **kök objeye** şunları ekle:
   - `NetworkObject`
   - `OwnerNetworkTransform` → *Sync Scale* tiklerini kaldır (gerek yok)
   - `PlayerNetworkSetup`
3. Prefab'ı kaydet, sonra **sahnedeki Player'ı sil** (artık NetworkManager oluşturacak).

### 4. Menü kamerası
Sahneye `GameObject > Camera` ile yeni bir kamera ekle, adını `MenuCamera` yap ve sahneyi
güzel gören bir açıya koy. (Oyuncunun kamerası prefab'a gittiği için menüde bu görünecek.)

### 5. NetworkManager objesi
1. Boş bir obje oluştur: `NetworkManager`.
2. Ekle: `NetworkManager` bileşeni → *Network Transport* kısmından **UnityTransport** seç.
3. *Player Prefab* alanına Player prefab'ını sürükle.
4. Aynı objeye `SessionManager` ve `SessionMenuUI` ekle.
5. `SessionMenuUI` → *Menu Camera* alanına `MenuCamera`'yı sürükle.

### 6. Taşınabilir kutular
Rigidbody'si olan **her** kutuyu seç (Ctrl ile çoklu seçebilirsin) ve ekle:
- `NetworkObject`
- `OwnerNetworkTransform`
- `NetworkRigidbody`
- `NetworkPickupable`

Zemin, merdiven, duvar gibi hareket etmeyen objelere bir şey eklemeye gerek yok.

### 7. Doğma noktaları
Sahneye 4 boş obje koy, her birine `SpawnPoint` ekle (sahnede yeşil küre olarak görünür).
Koymazsan oyuncular ilk pozisyonda yan yana doğar.

### 8. Sahne build listesinde olmalı
`File > Build Profiles` → *Scene List*'te `SampleScene` işaretli olsun.

---

## Test etme

**Tek bilgisayarda (en kolayı):** `Window > Multiplayer > Multiplayer Play Mode` → *Player 2*'yi aktif et.
Play'e bas: ana pencerede **Oda Kur**, kodu kopyala, Player 2 penceresinde kodu yapıştırıp **Katıl**.

**İki bilgisayarda:** biriniz build alıp arkadaşına gönderir (ya da ikiniz de editörden çalıştırır).
Biri oda kurar, kodu WhatsApp'tan atar, diğeri katılır.

## Kontroller
- `Esc` → menü (Devam / Kodu Kopyala / Odadan Ayrıl)
- Host odayı kapatırsa diğer oyuncular otomatik ana menüye döner.

## Sık karşılaşılan sorunlar
- **"Servislere bağlanılamadı"** → 2. adım (Unity Cloud bağlantısı) yapılmamış ya da internet yok.
- **Diğer oyuncunun karakteri hareket etmiyor** → Player prefab'ında `OwnerNetworkTransform` yerine
  normal `NetworkTransform` eklenmiş olabilir.
- **Kutular client'ta hareket etmiyor / titriyor** → kutuda `NetworkRigidbody` ve `OwnerNetworkTransform` ikisi de olmalı.
- **"Bağlanılıyor..." ekranında kalıyor** → NetworkManager'da *Player Prefab* boş olabilir.

## Sonraki adım fikirleri
- 40 kg üstü objeleri **iki kişinin birlikte taşıması** (oyunun temasına çok uyar).
- Oyuncu isimleri / kafa üstünde isim etiketi.
- Kamera bakış açısını (yukarı/aşağı) senkronlamak, basit karakter modeli.

---

## Ek: Sohbet, isimler, katıldı/ayrıldı mesajları

**Yeni dosyalar:** `Assets/Script/Network/ChatManager.cs`, `Assets/Script/Network/ChatUI.cs`
**Değişenler:** `PlayerNetworkSetup.cs` (isim + kafa üstü etiket), `SessionMenuUI.cs` (isim kutusu),
`PlayerController.cs` / `ObjectCarrier.cs` (sohbete yazarken karakter hareket etmez)

### Kurulum (tek adım)
1. Sahnede boş bir obje oluştur: `ChatManager`.
2. Üzerine ekle: `NetworkObject` + `ChatManager` + `ChatUI`.
   (NetworkManager objesine EKLEME, ayrı obje olmalı.)

Player prefab'ında değişiklik yapmana gerek yok.

### Kullanım
- Ana menüde **Adın** kutusuna ismini yaz (bir sonraki açılışta hatırlanır).
- Oyunda **Enter** → yaz → **Enter** gönderir (boşsa sadece kapatır), **Esc** vazgeçer (menü açılmaz).
- Biri katılınca / ayrılınca sohbete sarı renkte bilgi mesajı düşer.
- Diğer oyuncuların kafasının üstünde isimleri görünür.
- Mesajlar 10 saniye sonra soluklaşıp kaybolur; Enter'a basınca son mesajlar tekrar görünür.

### Nasıl çalışıyor
Mesaj önce sunucuya (host) gider, sunucu gönderenin adını ekleyip herkese dağıtır;
bu yüzden kimse başkasının adıyla mesaj gönderemez. İsimler oyuncu objesinde bir
`NetworkVariable` olarak tutulur, sonradan katılan da herkesin adını görür.

---

## Ek 2: Oyuncu listesi, renk, ayarlar, ping, oyuncu atma

**Yeni dosya:** `Assets/Script/Network/GameSettings.cs`
**Değişenler:** `SessionMenuUI.cs`, `PlayerNetworkSetup.cs`, `ChatManager.cs`, `SessionManager.cs`, `PlayerController.cs`

Unity'de ek kurulum **gerekmez**. Sadece Player prefab'ındaki `PlayerNetworkSetup` bileşeninde
eski "Remote Player Color" alanı kalktı, Unity bunu kendiliğinden temizler.

- **Ana menü:** isim + 8 renkten biri seçilir (hatırlanır).
- **Sağ üst:** ping (yeşil < 80 ms, sarı < 150 ms, kırmızı üstü). Host'ta "Host • 0 ms" yazar.
- **Esc menüsü:**
  - Oyuncu listesi: renk, isim, (Host) / (Sen) etiketleri.
  - Host'ta her oyuncunun yanında **At** butonu → oyuncu ana menüye düşer, ekranında
    "Host seni odadan attı." yazar, sohbete "X odadan atıldı." düşer. (Kodla tekrar girebilir; kalıcı ban yok.)
  - Oyun içinden renk değiştirme.
  - Ses ve fare hassasiyeti kaydırıcıları (bilgisayara kaydedilir).

> Not: GDD'de **Tab = çanta** olduğu için oyuncu listesi Tab'a değil Esc menüsüne konuldu.

---

## Ek 3: Dil desteği (varsayılan İngilizce)

**Yeni dosya:** `Assets/Script/Localization/Loc.cs`. Oyundaki tüm metinler buradaki tablodan gelir.
Unity'de ek kurulum gerekmez.

- Ana menünün altında ve Esc menüsünde **English / Türkçe** seçimi var; seçim kaydedilir.
- Sohbetteki "katıldı / ayrıldı / atıldı" mesajları her oyuncuya **kendi dilinde** görünür
  (ağdan metin değil çeviri anahtarı gönderilir).

### Yeni metin eklerken
Koda doğrudan yazı yazmayın:
```csharp
GUILayout.Label("Ready");            // YANLIŞ
GUILayout.Label(Loc.T("lobby.ready")); // DOĞRU
```
ve `Loc.cs` tablosuna satır ekleyin:
```csharp
{ "lobby.ready", new[] { "Ready", "Hazır" } },
```
Parametreli metin: `{ "chat.joined", new[] { "{0} joined the room.", "{0} odaya katıldı." } }` →
`Loc.T("chat.joined", isim)`. Tabloda olmayan anahtar ekranda anahtarın kendisi olarak görünür,
böylece eksik çeviri hemen fark edilir. Bir dilde metin boş bırakılırsa İngilizcesi gösterilir.

### Yeni dil eklemek
1. `Language` enum'una ekleyin (ör. `German = 2`).
2. `LanguageNames` dizisine dilin kendi adını ekleyin (`"Deutsch"`).
3. Tablodaki satırlara üçüncü metni ekleyin.

> İleride metin sayısı çok artarsa (yüzlerce satır) tablo bir CSV/Google Sheets dosyasına taşınabilir;
> koddaki `Loc.T("...")` çağrıları değişmez.

---

## Ek 4: Çanta (envanter) sistemi

Nakliye mantığından GDD'ye geçiş: **eserler, aletler ve dikkat dağıtma eşyaları çantaya girer**;
elde fiziksel taşıma (E ile kaldır/fırlat) sadece sahne objeleri (sandalye, kova...) için kalır.

**Yeni dosyalar** (`Assets/Script/Inventory/`): `ItemDefinition`, `ItemDatabase`, `InventorySlot`,
`WorldItem`, `PlayerInventory` + editör aracı `Assets/Script/Editor/ItemTools.cs`
**Değişenler:** `PlayerController` (çanta ağırlığı hızı düşürür), `ObjectCarrier` (çanta eşyalarını
elde taşımaz), `SessionMenuUI` (Esc önce çantayı kapatır), `ChatUI`, `Loc`

### Kurallar
- Çanta kapasitesi **kilo** ile: başlangıç **10 kg** (`PlayerInventory` → *Base Capacity Kg*).
  Upgrade'ler ileride `ServerSetCapacity(kg)` ile artıracak.
- Sığmayan eşya alınamaz ("won't fit in your bag").
- Çanta doldukça yürüme yavaşlar: tam doluyken %60 hız (*Min Speed At Full Load*).
- Bozuk para gibi eşyalar tek satırda birikir (x10'a kadar).
- Sahte eserlerin üzerinde benzerlik yüzdesi taşınır (şimdilik elle verilir, ileride yapay zeka hesaplayacak).
- Çantanın içeriği host'ta tutulur → ileride "arkadaşın çantasından çalma" kolayca eklenir.

### Kurulum (bir kişi yapsın, sonra commit'lesin!)
1. Unity menüsünden **Tools → Sahte Şaheser → Create Sample Items**.
   `Assets/Items` (eşyalar, prefab'lar) ve `Assets/Resources/ItemDatabase.asset` oluşur.
2. **Player prefab**'ını aç → `PlayerInventory` bileşenini ekle → kaydet.
3. `Assets/Items/Prefabs` içindeki prefab'lardan birkaçını sahneye sürükle (doğma noktasının yakınına).
   *Mermer Büst 12 kg'dır: 10 kg'lık çantaya sığmadığını görmek için koyun.*
4. NetworkManager → *Network Prefabs Lists* → `DefaultNetworkPrefabs` içinde yeni eşya prefab'larının
   olduğunu kontrol et (genelde kendiliğinden eklenir). Yoksa **Add** ile ekle; yoksa "Bırak" çalışmaz.
5. Sahneyi kaydet (Ctrl+S) ve `Assets/Items`, `Assets/Resources`, sahne ve Player prefab'ını commit'le.

### Kontroller
- **E**: baktığın eşyayı çantaya al
- **Tab**: çantayı aç/kapat (panelde her eşyanın yanında **Drop** butonu)
- **Esc**: çanta açıksa önce çantayı kapatır
- Sağ altta her zaman kilo göstergesi durur.

### Yeni eşya eklemek
1. Project penceresinde sağ tık → **Create → Sahte Şaheser → Item**; id, isim, tür, kilo, değer gir.
2. Sahnede görünmesi için bir prefab yap: obje + `NetworkObject` + `NetworkTransform` +
   `Rigidbody` + `NetworkRigidbody` + `WorldItem` (Item alanına tanımı sürükle). Prefab'ı tanımın
   *World Prefab* alanına koy. (En kolayı: hazır bir örnek prefab'ı kopyalayıp düzenlemek.)
3. **Tools → Sahte Şaheser → Rebuild Item Database** çalıştır.
4. İsmin çevirisi için `Loc.cs`'ye `{ "item.<id>", new[] { "English", "Türkçe" } }` ekle.

> ⚠️ Eşya listesi ağda **sıra numarasıyla** gönderilir. İki oyuncunun `ItemDatabase`'i aynı olmalı:
> yeni eşya ekleyen commit'lesin, diğeri çeksin. Farklı olursa çantada yanlış eşyalar görünür.

---

## Ek 5: Etkileşim, çalma, para atma, ele alma, para ve sesler

### Unity'de yapılacaklar (bir kişi yapsın, commit'lesin)
1. **Tools → Sahte Şaheser → Generate Item Icons** → `Assets/Items/Icons` içine eşya fotoğrafları üretilir
   ve eşyalara bağlanır. Çanta ve bilgi kartında artık düz renk yerine bu resimler görünür.
   (Kendi çizdiğiniz ikonu kullanmak için eşyanın *Icon* alanına sürükleyin; araç onu ezmez.)
2. Sahnedeki **ChatManager** objesine `TeamBank` bileşenini ekle (ekip kasası).
3. (İsteğe bağlı test) Etkileşim noktaları:
   - **Kapı:** boş obje `DoorHinge` (kapının menteşe kenarına koy) → içine çocuk olarak bir küp (kapı).
     `DoorHinge`'e `NetworkObject` + `DoorInteractable` ekle.
   - **Aranabilir dolap:** bir küp → `NetworkObject` + `ItemContainerInteractable`, *Item* alanına bir eşya
     sürükle, *Hold Duration* = 2. E'ye 2 sn basılı tutunca eşya çantana girer.
4. Ctrl+S, commit.

`PlayerInteractor` (E tuşu) ve `PlayerFootsteps` (ayak sesi) oyuncuya **otomatik** eklenir.

### Kontroller
| Tuş | Ne yapar |
|---|---|
| **E** | Yerdeki eşyayı al / kapıyı aç-kapa |
| **E basılı tut** | Dolabı ara (halka dolar) / arkadaşının **arkasından** çantasını aç |
| **Tab** | Çanta. Satırda **Hold** = ele al, **Drop** = bırak. Satırın üstüne gelince bilgi kartı |
| **Q** | Çantadaki bozuk parayı fırlat (yere düşünce ses + gürültü) |
| **F7 / F8** | (Sadece editör/test) Ekip kasasına / kendine +$100 |

### Çalma (GDD Bölüm 6)
- Arkadaşının **arkasına** geç (önünden olmaz), E'ye 1.5 sn basılı tut → onun çantası açılır.
- Bir eşya seç → **Steal**. Tek seferde bir eşya; sığmıyorsa alamazsın.
- Kurbanın ekranına "X çantanı karıştırıyor!" ve "X çantandan Y çaldı!" yazar, çalma sesi çıkar.
- Uzaklaşırsan panel kapanır.

### Gürültü sistemi (güvenlikler için hazırlık)
Para düşmesi, eşya çarpması ve çalma, `NoiseSystem.Emit(pozisyon, yarıçap)` ile **gürültü** yayar.
Play modunda Scene penceresinde turuncu çember olarak görünür. Güvenlik yapay zekası yazıldığında
`NoiseSystem.NoiseMade` olayını dinleyecek.

### Sesler
Şu an kodla üretilmiş **geçici** sesler çalar (ayak sesi, alma, bırakma, çanta fermuarı, para, kapı,
çalma, halka tamamlanma). Gerçek ses koymak için:
1. Project → sağ tık → **Create → Sahte Şaheser → Sound Bank**, adı `SoundBank`, `Assets/Resources` içine koy.
2. Listeye satır ekle: ses türünü seç (ör. *Footstep*), ses dosyalarını sürükle (birden fazla = rastgele).
Listede olmayan sesler geçici sesle çalmaya devam eder.

### Para
- Sağ üstte **Team** (ekip kasası) ve **You** (kişisel cüzdan) görünür.
- Kod tarafı hazır: `TeamBank.Instance.ServerAdd/ServerTrySpend`, `PlayerInventory.ServerAddMoney/ServerTrySpendMoney`.
  Karaborsa ve garaj mağazası bunları kullanacak.

---

## Ek 6: Eser değiştirme, karaborsa, garaj mağazası ve test müzesi

### Kurulum (bir kişi yapsın, commit'lesin)
1. **Tools → Sahte Şaheser → Create Sample Items** (tekrar çalıştır): yeni **Forged Vase** eşyası eklenir
   ve sahteler orijinallerine bağlanır (Forged Painting → Small Painting, Forged Vase → Ancient Vase).
2. **Tools → Sahte Şaheser → Create Test Museum**: sahnenin x=40 civarına gri kutulardan bir müze kurulur.
   Eski doğma noktaları kapatılır, oyuncular müzenin garaj odasında doğar. (Ctrl+Z ile geri alınır.)
3. Sahnede **ChatManager** objesinde `TeamBank` olduğundan emin ol (Ek 5).
4. **Ctrl+S** ve commit: `Assets/Items`, `Assets/Resources`, `Assets/TestMuseum`, sahne.
   ⚠️ Eşya listesi değiştiği için arkadaşın mutlaka çekmeli.

### Test müzesinde oynanış döngüsü
1. **Garaj odası:** kahverengi dolaplara E basılı tut → *Forged Painting* (benzerlik %72) ve
   *Forged Vase* (%85) çantana girer. Bozuk para dolabından 5 para al. Dolaplar 10 sn'de yeniden dolar.
2. **Kapıdan** (E) galeriye geç.
3. **Eser kaidesi:** sahtesi çantandaysa E basılı tut (eser ağırsa daha uzun sürer) →
   orijinal çantana girer, kaidede artık sahte durur. Sahtesi olmayan eserler (yüzük, büst) değiştirilemez.
4. Garaja dön, **mor kutu = Karaborsa**: orijinalleri sat.
   Fiyat = taban değer × bırakılan sahtenin kalitesi (Kusursuz %100, İyi %85, Şüpheli %65, Kaba %45;
   sahte bırakmadan alınan "sıcak mal" %50). Paranın %80'i ekip kasasına, %20'si sana.
5. **Turkuaz kutu = Garaj Mağazası:** upgrade al.

| Upgrade | Etki | Seviye | Fiyat | Kim öder |
|---|---|---|---|---|
| Bigger Bag | +5 kg çanta | 3 | 150 / 300 / 500 | Sen |
| Quiet Shoes | Ayak sesi %30 kısılır (her seviye) | 2 | 120 / 260 | Sen |
| Swift Gloves | Değiştirme %20 hızlanır (her seviye) | 2 | 200 / 420 | Sen |
| Team Backpacks | Herkese +3 kg | 2 | 400 / 800 | Ekip kasası |

Fiyat ve etkiler `Assets/Script/Economy/UpgradeCatalog.cs` ve `PriceRules.cs` içinde; denge ayarı oradan.

### Kendi haritanızda kullanmak
- **Eser kaidesi:** obje + `NetworkObject` + `ArtworkDisplay` → *Artwork* alanına orijinal eseri sürükle.
- **Karaborsa / mağaza:** obje + `NetworkObject` + `BlackMarket` / `GarageShop`.
- **Yüzen yazı:** `WorldLabel` (çeviri anahtarı ile).
- Yeni bir sahte eser eklerken tanımındaki **Forgery Of** alanına orijinalini koyun.

---

## Ek 7: Güvenlik görevlileri, kameralar, saklanma, işaretleme

### Kurulum (bir kişi yapsın, commit'lesin)
1. **Tools → Sahte Şaheser → Create Test Museum** → "Evet" ile müzeyi yeniden kur. Bu sefer otomatik olarak:
   - 2 güvenlik görevlisi (devriye noktalarıyla), 2 dönen kamera, 2 saklanma dolabı, kırmızı zeminli **Staff Only** odası
   - ChatManager objesine `SecurityManager` ve `PingSystem`
   - **Player prefab'ına `PlayerStealth`** eklenir.
2. **Ctrl+S** ve commit (sahne + Player prefab + `Assets/TestMuseum`). ⚠️ Arkadaşın mutlaka çekmeli.

NavMesh (güvenliklerin yürüme alanı) oyun başında otomatik oluşur; elle bake etmenize gerek yok.

### Şüphe sistemi (GDD Bölüm 5)
Oyuncular sıradan ziyaretçidir: **görülmek tek başına sorun değil.** Şüpheli bir şey yaparken görülürsen
ekranın üstünde **Suspicion** çubuğu dolar; dolarsa **ALARM** çalar.

| Şüpheli davranış | Etki |
|---|---|
| Eser değiştirirken / arkadaşından çalarken görülmek | Çok hızlı dolar (×2) |
| Elinde eser (orijinal/sahte) taşımak | Dolar (×1) |
| Staff Only odasında bulunmak | Dolar (×1) |
| Alarm sırasında "aranan" kişi olmak | Dolar (×1.5) |
| Eğilmek (Ctrl/C) | Güvenlikler %25 daha kısa mesafeden görür, şüphe %40 yavaş dolar |

- Görülmezsen şüphe yavaşça azalır.
- **Değiştirme sırasında biri izliyorsa işlem yarıda kalır** ("Not now, someone is watching!").

### Güvenlik görevlisi
- Yerde **görüş konisi**: sarı = devriye, turuncu = şüpheleniyor/araştırıyor ("?"), kırmızı = kovalıyor ("!").
- **Bozuk para (Q)** düştüğü yeri duyan güvenlik oraya gidip etrafa bakar → dikkat dağıtma.
- **Sahte eser kontrolü:** değiştirilmiş bir eserin önünden geçerken benzerliğe göre fark edebilir:
  Kusursuz %0, İyi %15, Şüpheli %50, Kaba %90 → fark ederse esere gider ve alarm çalar.
- Kapıları kendisi açar.
- **Alarm** (25 sn): tüm güvenlikler kovalar. Yakalanırsan **çantandaki orijinallere el konur** ve garaja
  gönderilirsin (GDD'deki polis minibüsü + kurtarma ileride).

### Kamera
- Sağa sola tarar, görüş konisi mavi. Şüpheli davranan oyuncuyu görünce şüphe ekler ve
  **en yakın güvenliği oraya yollar.**

### Saklanma dolabı
- **E** ile gir, **E** ile çık. İçerideyken güvenlikler ve kameralar seni göremez. Tek kişilik.

### İşaretleme (ping)
- **Orta fare tuşu** veya **F** → baktığın yere senin renginde bir işaret koyar, herkes görür (8 sn).
  Bir esere / eşyaya bakıyorsan adı da yazar.

### Ayar yapmak
- Görevli: `GuardAI` (görüş mesafesi/açısı, hızlar, şüphe hızı, devriye noktaları)
- Kamera: `SecurityCamera` (tarama açısı/hızı, görüş)
- Alarm süresi: `SecurityManager`
