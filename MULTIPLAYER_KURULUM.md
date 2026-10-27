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

---

## Ek 8: Sahte atölyesi, brifing, görevler ve rastgele müzeler

**Yeni dosyalar** (`Assets/Script/Forgery/`): `ForgeryWorkbench`, `WorkbenchUI`, `ClaySculptor`, `CanvasPainter`,
`ForgeryGhost.shader`, `BriefingTerminal` (+ `BriefingUI`), `MuseumSession`, `ArtworkLook`
**Değişenler:** `ArtworkDisplay` (görev seviyesi, temalı görünüm, görev sıfırlama), `SessionMenuUI` / `PlayerInventory`
(atölye/brifing açıkken Esc ve Tab), `Loc` (yeni metinler), `ItemTools` (küçük ganimetler), `TestMuseumBuilder`

### Kurulum (bir kişi yapsın, commit'lesin)
1. **Tools → Sahte Şaheser → Create Sample Items** → 3 küçük ganimet eklenir (Enfiye Kutusu, Bronz Heykelcik, Sikke Kesesi).
2. **Tools → Sahte Şaheser → Create Test Museum** → "Evet". Bu sefer:
   - Garajdaki sahte dolapları yerine **Sahte Atölyesi** ve **Brifing Terminali** gelir
   - Galeriye 2. ve 3. vazo kaideleri (2. ve 3. görevde açılır) ve 3 küçük ganimet masası eklenir
   - ChatManager objesine `MuseumSession` eklenir
3. NetworkManager → *Network Prefabs Lists*'te yeni ganimet prefab'larını kontrol et (Ek 4, 4. adım).
4. **Ctrl+S** ve commit: `Assets/Items`, `Assets/Resources`, `Assets/Script`, sahne. ⚠️ Eşya listesi değişti, arkadaşın çekmeli.

### Sahte atölyesi (GDD Bölüm 3)
- Atölyeye **E** → "Neyin sahtesini yapacaksın?" → müzedeki aktif vazo / tablolardan birini seç.
  **Orijinali almaya gerek yok:** önce sahteyi yap, sonra müzede kaideye E basılı tutup değiştir.
- **Vazo (çömlek çarkı):** kil hedefle aynı boyda oluşur, üstünde **saydam yeşil rehber** görünür
  (kilin içinde kalan kısım ince çizgi, eksik kısım yeşil dolgu).
  Sol tık = tüm halkayı içeri it, sağ tık = dışarı çek, Shift + tık = tek nokta, orta tuş = çevir, tekerlek = fırça.
  Şekil %75'i geçince **SPACE** → boyama: yandaki orijinale bakarak boya (Shift = bant, B = doldur, 1-6 = renk).
- **Tablo (tuval):** ekrana tuval gelir, yanında orijinal; üstünde **yeşil kontur rehberi** (G aç/kapa).
  Sol tık fırça, Shift + tık kova, 1-7 renk, B doldur.
- **Esc** = bitir → sahte, gerçek benzerlik yüzdesiyle **çantana** girer (sunucu ekler; çanta doluysa uyarır).
  Vazo benzerliği = %70 şekil + %30 boya. Güvenliklerin sahteyi fark etme şansı ve karaborsa fiyatı bu yüzdeye bağlı.
- Kaideye konan sahte, benzerliğine göre **bozuk görünür** (kaba sahte kil rengine kayar, şekli dalgalanır).

### Brifing terminali
- Üstünde bu görevin vazoları sırayla dönen hologram. **E** → müze adı, görev seviyesi, kota ve
  hedef eserlerin resmi / değeri / durumu.

### Görevler ve rastgele müze (MuseumSession)
- Her oda kurulduğunda ve her yeni görevde **8 müze temasından** biri rastgele seçilir (bir öncekiyle asla aynı olmaz):
  Le Grand Musée (Paris), Royal Antiquities Museum (Londra), Rijks Galerij (Amsterdam), Galleria Fiorentina (Floransa),
  Museo Real (Madrid), Saray Hazinesi (İstanbul), Nile Antiquities Museum (Kahire), Museum of Eastern Arts (Tokyo).
- Tema: duvar / zemin / kaide renkleri, vazo ve tablo görünümleri ve adları. Vazolar (5 şekil, rastgele boy ve bant deseni)
  ve tablolar (6 kompozisyon) host'un gönderdiği **tohumdan** her bilgisayarda aynı üretilir → ağdan mesh/doku gitmez.
- **Seviye:** 1. görev 1 vazo + tablolar, 2. görev 2 vazo, 3+ görev 3 vazo (`ArtworkDisplay` → *Required Level*).
- **Kota:** sol üstte "Ekip kazancı: $X / $Y". Aktif vazo/tabloların değerinin ekibe kalan payı + $100; yani her görevde
  biraz küçük ganimet ya da yüzük/büst de satmak gerekir. Kota dolunca **host N** → sonraki görev
  (yeni müze, +1 vazo, kaideler orijinal haline döner).
- Ayar: `MuseumSession` → *Quota Factor*, *Quota Extra*. Temalar ve üretim: `ArtworkLook.cs`.

### Küçük ganimetler
Galeri masalarında durur, E ile çantaya alınır; sahtesi gerekmez, karaborsada "sıcak mal" (×0.5) olarak satılır.

### Kendi haritanızda kullanmak
- **Atölye:** obje + `NetworkObject` + `ForgeryWorkbench` (isteğe bağlı *Camera Point* / *Work Point*).
- **Brifing:** obje + `NetworkObject` + `BriefingTerminal`.
- **Görev:** NetworkObject'li bir sahne objesine (ör. ChatManager) `MuseumSession`.
- Vazo/tablo kaidesi olarak görünmesi için eserin id'sinde `vase` ya da `painting` geçmeli.

---

## Ek 9: Değişen müze, oda kameraları, hapis alarmı, daha akıllı güvenlik, yeni görünümler

Sahneyi yeniden kurmaya GEREK YOK: hepsi oyun başında koddan kurulur (ağ nesnelerinin kimlikleri değişmez).
Sadece kodu commit'leyin, arkadaşınız pull etsin; ikinizde de aynı kod olmalı.

### Her haritada farklı müze (MuseumLayout)
Görev tohumu değişince (yeni harita / N ile sonraki görev) herkeste aynı şekilde:
- **İçeride:** eser kaidelerinin yerleri, ara bölmeler, banklar / saksılar / heykeller / halılar / hol sütunları,
  güvenliklerin devriye yolları değişir. NavMesh sunucuda yeniden oluşturulur.
- **Her odada bir güvenlik kamerası** (hol, tablo galerisi, vazo galerisi, hazine, başyapıt salonu) — yeri
  haritadan haritaya değişir; ileri seviyelerde hol / vazo / hazine odasına ikinci kamera gelir.
  Sahnedeki eski sabit Camera_* objeleri gizlenir ve çalışmaz.
- **Dışarıda:** temaya göre cephe (Paris sütunları, Londra tuğlası, Amsterdam basamaklı çatısı, Floransa kemerleri,
  Madrid bayrakları, İstanbul kubbe + minareler, Kahire pilonları + dikilitaşlar, Tokyo torii kapısı),
  sokakta ağaçlar / banklar / park etmiş arabalar, yer rengi, günün saati (gece / akşam / gündüz + sis).
  Giriş tabelasında müzenin adı yazar.
- Sabit kalanlar: kapılar, dolaplar / perdeler, ganimet masaları, Staff Only köşesi, hapis.

### Alarm ve hapis
- Alarm artık **15 sn**.
- Biri hapse atılınca **alarm susar**.
- Arkadaşın kapıda **E'ye basılı tutmaya başlayınca** güvenlikler hapse koşar (kilidi kurcalamak da suç sayılır).
- Kurtarınca **alarm yeniden çalar**; kurtaran ve kaçan aranır.

### Daha akıllı güvenlik
- Hapisten çıkan / kaçırılan / kovalamadan kaçan oyuncuyu **60 sn tanırlar**: görünce hemen şüphelenirler.
- Kovalarken birbirlerine telsizle yer bildirirler, kaçtığın yöne önden keserler.
- Gözlerinin önünde dolaba / perdeye girersen gelip **açarlar ve yakalarlar**.
- Alarm bitse bile seni hâlâ görüyorlarsa bırakmazlar (alarmı yeniden çaldırırlar); kaybedince
  son görüldüğün yerin çevresini ~14 sn ararlar.

### Diğer
- Tablo benzerliği artık **bölge bölge**: gökyüzü + deniz tek başına yetmez; tekne, yelken, bulutlar da ayrı sayılır.
  Atölyede "Tamamlanan şekiller: 4/6" yazar.
- Yapılan sahte (vazo / tablo) verisi sunucudan herkese hemen gönderilir; gelmezse 3 sn'de bir yeniden istenir.
  Hâlâ silindir görünürse Console'daki `[Forgery]` satırlarını gönderin.
- Atılan bozuk para 6 sn'de kaybolur.
- Oyuncuların kolları gövdenin dışında, eldivenli; güvenlikler kapsül yerine üniformalı, şapkalı, fenerli bekçi.

## Ek 10: Değişen oda planı, çatı, gerçekçi dış cepheler, ziyaretçiler, kamera odası, yeni yüzük

**Sahne yeniden kurmaya gerek yok.** Her şey kodla ve harita tohumuna göre oluşur; aynı tohum herkeste aynı müzeyi üretir.
Kodu (yeni `.cs` dosyaları ve `.meta` dosyaları dahil) commitleyin, arkadaşınız pull etsin.
İki tarafta da kod **birebir aynı** olmalı.

### Müze içi
- Her haritada oda planı baştan çizilir: lobi (girişte), 6–10 galeri, Staff Only odası ve güvenlik (kamera) odası.
- Kapılar ve hapsin hangi köşede olduğu da değişir.
- Tüm müzelerin **çatısı** ve tavan lambaları var.
- Tablolar artık **ışıklı duvar panolarına** asılır; her tablonun üstünde bir tablo lambası ve spot ışık bulunur.

### Dış cephe (harita temasına göre)
- Louvre: cam piramit ve havuzlar, mansard çatılı saray.
- British Museum: sütunlar ve alınlık.
- Rijksmuseum: tuğla, iki kule.
- Uffizi: revaklar ve Duomo kubbesi.
- Prado: tuğla cephe ve Dor sütunlu giriş.
- Topkapı: kuleli kapı, Ayasofya kubbesi ve minareler.
- Mısır Müzesi: pembe cephe, obelisk, sfenks.
- Tokyo: büyük kırma çatı ve torii.

### Ziyaretçiler
- Her biri farklı görünen 5–10 ziyaretçi müzeyi gezer.
- Bir şey çaldığını, kilit kurcaladığını veya konsolu kullandığını görürlerse en yakın güvenliğe koşup haber verirler. Güvenlik onlar gidene kadar alarm çalmaz; haber gelince alarm başlar.
- Kafalarındaki "!" işareti ihbara gittiklerini gösterir.

### Kamera odası
- Masadaki konsolda **E basılı tut**: tüm kameralar 4 sn kapanır.
- Konsol sonra 20 sn bekleme süresine girer.
- Bir güvenlik seni bu odada görürse **anında alarm** çalar.

### Staff Only eseri
- Staff Only odasındaki tablo her zaman **×2,5 değerlidir**.
- Brifing ekranında ve karaborsada bu fiyat gösterilir.

### Yüzük (kuyumcu tezgahı)
1. **Döküm:** Kalıp ve metali seç, ardından basılı tutup döküm yap. Göstergeyi çizgide bırak; taşarsa kalıbı boşaltıp yeniden başlarsın.
2. **Taşlar:** Tepsiden bir taş seç, sonra referanstaki açıya göre halkanın üstüne tıkla. Sağ tık taşı kaldırır.
3. **Cila:** Fareyi halkanın üstünde gezdirerek cilala.

Benzerlik oranı döküm %35, taşlar %45, cila %20 üzerinden hesaplanır.

### Diğer
- Büst ekranı kaydırılabilir; benzerlik oranı en üstte görünür.
- Oyuncular ve güvenlikler daha uzun gövdeli: kollar, dirsekler, eller ve bacaklar ayrı ayrı görünür.

## Ek 11: Polis görünüşü, yeni Louvre, atölye arayüzü, kamyonda çöp kutusu

Yine sadece kod değişti, sahneye dokunmaya gerek yok. Kodu (yeni `TrashBin.cs` ve `.meta` dosyası dahil) commitleyin, arkadaşınız pull etsin.

- **Polisler:** Ziyaretçilerle aynı oranlarda yapıldı. Açık mavi kısa kollu gömlek, lacivert pantolon ve kravat, polis şapkası, rozet ve fener var.
  - Sahnedeki güvenlik objesi ölçeklenmişse görünüş otomatik olarak 1:1'e düzeltilir. Console'da `[GuardLook]` satırı çıkarsa bu düzeltme yapılmış demektir.
- **Louvre (Paris):** Cour Napoléon avlusu olarak yeniden yapıldı.
  - Arkada 3 katlı Sully kanadı ve saatli orta köşk var.
  - İki yanda öne uzanan Richelieu ve Denon kanatları, uçlarında da kare kubbeli köşkler var.
  - Cephede rustik kemerler, pilastrlar ve alınlıklı pencereler; çatıda mansart çatı pencereleri bulunur.
  - Ortada çelik baklava örgülü büyük cam piramit, iki küçük piramit, fıskiyeli üçgen havuzlar ve taş döşeme var.
  - Gece piramit ve cephe aydınlanır.
- **Atölye arayüzü:**
  - Atölye açıkken oda kodu, para ve çanta kutuları gizlenir; atölye paneli her şeyin üstünde çizilir.
  - Benzerlik oranı sol panelin en altında, renkli çubuklu bir kutuda gösterilir (vazo, büst, yüzük).
- **Çöp kutusu:** Kamyonun içinde, atölye tezgahının yanında (batı duvarının dibinde) duruyor.
  - Bakıp **E**'ye basınca çantadaki sahteler listelenir. Her birinin yanında "Çöpe at" düğmesi, altta "Bütün sahteleri at" düğmesi var.
  - Atılan sahte geri gelmez.

## Ek 12: Sesli sohbette hırsızlık kelimeleri (güvenlik duyar)

Sesli sohbette hırsızlıkla ilgili bir şey söylersen yakındaki güvenlikler duyar ve gelip bakar.

- **Türkçe:** çaldım / çaldık / çalalım / çalıyorum / çalmak, çalıntı, hırsız, soygun / soyalım / soyduk, değiştirdim / değiştirelim, sahte, aşırdım, arakladım.
- **İngilizce:** steal / stole / stolen, swap / swapped, fake, forged / forgery, heist, rob / robbed / robbery, thief, loot.
- "Çalışıyorum", "stealth" gibi benzer ama ilgisiz kelimeler sayılmaz. Kelime listesi: `VoiceKeywordListener.cs` → `CrimeWords`.

### Ne olur?
- Güvenlik açık alanda **14 m**, duvar arkasından **6 m** içinden duyar.
- Duyan güvenlik konuştuğun yerin yanındaki esere yürüyüp inceler. Yakında **değiştirdiğin bir eser** varsa önce ona gider ve sahteyi fark etme şansı **+%30** artar. Fark ederse oraya varınca alarm çalar.
- Sen **25 sn** boyunca "duyulmuş" sayılırsın: bu sürede bir güvenlik / kamera seni görünce şüphelenir. Şüphe çubuğun da biraz dolar, ama tek başına alarm çalmaz.
- Ekranında "Bir güvenlik ... dediğini duydu!" yazar. Söylediğin kelime algılanınca mikrofon kutusunun üstünde de gösterilir.
- Sadece ses **gerçekten gönderilirken** dinlenir: V basılıyken ya da açık mikrofonda eşik geçilince.

### Kurulum
- Konuşma tanıma **Vosk** ile, çevrimdışı ve ücretsiz. Motor `Assets/Plugins/x86_64/` içinde (libvosk.dll + 3 yardımcı dll), commit'leyin.
- Türkçe ve İngilizce küçük modeller (~80 MB) oyuncu ilk kez odaya girince **kendiliğinden indirilir**. Bir kez indirilir; yeri `%USERPROFILE%/AppData/LocalLow/<şirket>/<oyun>/VoskModels`.
- İnternet yoksa ya da indirme olmazsa modelleri elle koyun:
  - https://alphacephei.com/vosk/models adresinden `vosk-model-small-tr-0.3` ve `vosk-model-small-en-us-0.15` zip'lerini indirin.
  - Zip'leri `Assets/StreamingAssets/Vosk/` içine açın (klasör adları aynen kalsın).
- Ayar: **Esc → Sesli sohbet** altında *Kapalı / TR + EN / Türkçe / İngilizce* ve modellerin durumu.
- Şimdilik sadece **Windows**'ta çalışır; diğer sistemlerde sessizce kapalı kalır.

### Değişen dosyalar
- Yeni: `Network/VoiceKeywordListener.cs` (dinleme + model indirme + kelime listesi), `Stealth/CrimeTalk.cs` (sunucu: kim duyar, ne yapar), `Assets/Plugins/x86_64/*.dll`.
- Küçük eklemeler:
  - `VoiceChat`: gönderilen ses paketini dinleyiciye veren bir olay ve ayarlar satırı.
  - `GuardAI`: `ServerHearCrimeTalk`.
  - `PlayerStealth`: "duyuldu" durumu.
  - `Loc`: yeni metinler.

## Ek 13: Q ile yere bırakma, yarım sahteye devam etme, F ile itme

### Q: elindekini yere bırak
- Elinde tablo, büst, vazo, yüzük (ya da başka bir eşya) varken **Q** basınca önüne bırakırsın.
- Elin boşsa ya da bozuk para tutuyorsan Q eskisi gibi bozuk para fırlatır.
- Yere düşen eşya ses çıkarır (ağır büst daha çok): yakındaki güvenlik bakmaya gelebilir.

### Atölyede "Devam et"
- Boş ya da yarım bıraktığın **tablo, büst veya yüzük** sahtesi çantandaysa, tezgahta E basınca listenin en üstünde
  "Devam et: … (şu an %X)" olarak çıkar. Elinde tutarken E basarsan doğrudan o açılır.
- Kaldığın yerden devam edersin. Esc ile bitirince yeni hali çantana girer, eski yarım sahte çıkar.
- Vazo için yok: kil şekli kaydedilmiyor, her seferinde baştan yapılır.

### F: itme
- Önündeki (yaklaşık 2 m) **takım arkadaşını, ziyaretçiyi ya da güvenliği** F ile itersin. İtebileceğin biri varsa
  üstünde "[F] İt" yazar; o anda F işaret (ping) koymaz. İşaret için orta fare tuşu her zaman çalışır.
- **Takım arkadaşı** biraz savrulur, bakışı kayar ve "Biri seni itti!" yazar.
  - E basılı tutarak yaptığı iş (eser değiştirme, çalma, kilit, konsol...) **sıfırlanır**: E'yi bırakıp baştan basması gerekir.
  - **Atölyedeyse atölyeden çıkar:** tablo, büst ya da yüzük yarım haliyle çantasına girer, sonra "Devam et" ile sürdürür. Şekli bitmemiş vazo kaybolur.
  - Saklanan ya da hapisteki oyuncu itilmez.
- **Ziyaretçi** savrulur.
- **Güvenlik** savrulur, sonra dönüp itenin olduğu yere bakar. Güvenliği itmek dikkat çeker.
- İtmek ses çıkarır.

### Değişen dosyalar
- Yeni: `Gameplay/PushSystem.cs`.
- Küçük eklemeler:
  - `PlayerInventory`: Q ile bırakma.
  - `PlayerInteractor`: `InterruptHold`.
  - `PlayerController`: `AddPush`.
  - `WorkbenchUI`: Devam et ve `Interrupt`.
  - `ForgeryWorkbench`: eski yarım sahteyi değiştirme.
  - `CanvasPainter`: `LoadFrom`.
  - `VisitorManager`: ziyaretçi bulma ve itme yardımcıları.
  - `PingSystem`: F tuşu çakışması.
  - `Loc`: yeni metinler.

## Ek 14: Telsiz, yasaklı kelime rengi, fırça boyutu, yeni eserler, düzeltmeler

### Sesli sohbet
- **Yasaklı kelime** (çaldım, sahte...) algılanınca mikrofon kutusunun **tamamı kırmızı** olur ve "Yasaklı kelime!" yazar.
- **Fısıltı:** güvenliğin duyma mesafesi ses yüksekliğine bağlıdır. Fısıltıyla yaklaşık 2,5 m (arkadaşının kulağına söylersen güvenlik duymaz), normal sesle yaklaşık 6 m, bağırınca yaklaşık 10 m. Duvar arkasından bunun %40'ı. Söylediğin kelimenin yanında hangi mesafeden duyulabileceği yazar.
- **Telsiz:** 12 m'ye kadar ses tam gelir. Uzaklaştıkça kısılır (70 m'de %18) ve telsiz gibi incelip hafif cızırdar.

### Atölye
- **Fırça boyutu:** tuvalde imlecin etrafında fırçanın gerçek boyutunda bir halka görünür. Paletin altında kaydırıcı ve − / + düğmeleri var; [ ] ya da - + tuşları da çalışır. Vazo boyamada da kaydırıcı var.
- **Yeni eserler:**
  - 6 yeni tablo: deniz feneri, lale tarlaları, çöl ve piramitler, kiraz çiçeği, portre, daireler. Tablolar artık her görevde değişir.
  - 5 yeni vazo şekli: amfora, kase, testi, şişe, lale ağızlı. Ayrıca 4 desen türü: bant, zikzak, benek, dikey çizgi.
  - Büst: 2 yeni taş (yeşim, altın varak), 2 yeni kaide rengi, 4 yeni ad.
  - Yüzük: 2 yeni metal (siyah, beyaz altın), 3 yeni taş (ametist, topaz, akuamarin), 3 yeni ad.

### Düzeltmeler
- **Hapisten çıkınca** (süre dolunca ya da kurtarılınca) oyuncu normal ziyaretçiye döner: aranmaz, tanınmaz, kovalanmaz. Kurtaran kişi hâlâ aranır ve alarm çalar.
- **Yeni göreve geçince** her şey sıfırlanır: alarm susar, kameralar açılır, kimse aranmaz, güvenlikler devriyeye döner.
- **Host'tan kopup ana menüye düşünce** alarm sesi artık çalmaz.
- **Üst üste binen yazılar** düzeltildi:
  - Üstteki uyarılar (şüphe, alarm, kamera, hapis, görev) alt alta dizilir.
  - Nişangah altındaki yazılar (etkileşim, taşıma, bildirim) alt alta dizilir.
- **Sessiz ayakkabı** upgrade'i mağazadan kaldırıldı.

### Değişen dosyalar
- `Gameplay/DayCycle.cs`: sabah–akşam döngüsü kaldırıldı, dosya artık boş. Silebilirsiniz (`.meta` dosyasıyla birlikte).
- Değişenler:
  - Aranma ve alarm sıfırlama: `PlayerStealth`, `GuardAI`, `JailCell`, `SecurityManager`.
  - Sesli sohbet: `VoiceChat`, `VoicePlayback`, `VoiceKeywordListener`, `CrimeTalk` (fısıltı mesafesi).
  - Atölye ve yeni eserler: `CanvasPainter`, `WorkbenchUI`, `ArtworkLook`, `ArtworkLookExtra`.
  - Yazı düzeni: `UIUtil`, `PlayerInteractor`, `ObjectCarrier`, `PlayerInventory`.
  - Diğer: `UpgradeCatalog` (sessiz ayakkabı), `Loc` (yeni metinler).

## Ek 15: Maymuncuk ve kilitli Staff Only kapısı

### Nasıl oynanır
- Staff Only odasının kapısında artık **kilitli bir kapı** var.
  - Kilitliyken kimse içeri giremez. Odadaki değerli eser (×2.5) ancak kapı açılınca çalınır.
- Kapı dışarıdan kolayca fark edilir:
  - Kapının üstünde kırmızı tabela ve "STAFF ONLY" yazısı var.
  - Kapı kilitliyken "KİLİTLİ — maymuncuk gerekir" yazısı da görünür.
  - Kırmızı uyarı lambası yanıp söner; kapı açılınca lamba yeşile döner.
  - Kapının önünde yerde sarı-siyah tehlike şeridi var.
- **Maymuncuk** garaj mağazasında adetle satılır.
  - Fiyatı **$50**, kişisel cüzdandan ödenir.
  - Aynı anda en fazla 5 tane taşınabilir.
- Kapıya bak ve **E'yi basılı tut**. Ekranda iki çubuk yarışır (Aion 2'deki zanaat gibi):
  - **Başarı** (mavi) önce dolarsa kapı açılır.
  - **Kırılma** (turuncu) önce dolarsa maymuncuk kırılır ve bir adet gider. Kapı kilitli kalır, başka maymuncukla yeniden denenebilir.
  - Kırılma ihtimali %35, başarı %65. Bir deneme yaklaşık 3–4,5 saniye sürer.
- Deneme sırasında:
  - Suç işliyorsun: bir güvenlik görürse yakalar.
  - İtilirsen, bakışın kayarsa ya da E'yi bırakırsan deneme iptal olur. Maymuncuk kırılmaz.
- Kapı açılınca bütün ekip için **görev sonuna kadar açık** kalır.
  - Ekipte bir kişinin kapıyı açması yeter.
  - Yeni müzede kapı yine kilitli olur.
- Sesler: kilit açılırken çıkan ses yakındaki güvenliklere duyulur. Kırılma sesi daha kısıktır.
- Güvenlikler devriyede kapıyı kendileri açıp geçer. Oyuncular arkalarından geçemez.

### Oyun döngüsü
- Staff Only eseri 2. görevde açılır.
- 1. görevde tek bir tablo satan oyuncunun payı (%20, yaklaşık $100) iki maymuncuğa yeter. İki denemeden en az birinin tutma ihtimali yaklaşık %88.
- Ayarlar `UpgradeCatalog` içindedir:
  - `LockpickPrice`: fiyat.
  - `LockpickMax`: en fazla taşınabilen adet.
  - `LockpickBreakChance`: kırılma ihtimali.
  - `LockpickSeconds`: bir denemenin süresi.

### Dosyalar
- Yeni dosyalar (`Gameplay` klasöründe):
  - `LockpickSystem.cs`: deneme, çubuklar, ağ mesajı.
  - `StaffDoor.cs`: kapının açılıp kapanması, lamba, tabela yazısı.
  - `MuseumLayoutStaffDoor.cs`: kapıyı ve süslerini her haritada kurar.
- Sahneye bir şey eklemeye gerek yok.
- Değişenler:
  - `UpgradeCatalog`: maymuncuk adet, fiyat ve kırılma ayarları.
  - `PlayerInventory`: maymuncuk adedi ve kırılma.
  - `GarageShop`: maymuncuk satırında seviye yerine adet yazar.
  - `MuseumSession`: kapının açık olup olmadığı.
  - `MuseumLayout`: `SetupStaffRoom` içine tek satır.
  - `PlayerInteractor`: kilit açmak suç sayılır.
  - `PushSystem`: itilince deneme iptal olur.
  - `Loc`: yeni metinler.

## Ek 16: Sesli sohbette mesafe

Eski "uzaklaştıkça hafif telsiz" ayarının yerini aldı. Ayrı bir telsiz tuşu yok. Normal oyun içi konuşma (V ya da sürekli açık mikrofon) mesafeye göre değişir:
- 5 m içinde arkadaşın sesi tam gelir.
- Uzaklaştıkça sesi azalır ve biraz boğuklaşır.
- 35 m'den sonra hiç duyulmaz.
- Arada duvar varsa ses biraz daha kısık gelir.
- Ses arkadaşının olduğu yönden (sağ / sol) gelir.
- Duyamadığın kadar uzaktaki konuşmalar soldaki konuşanlar listesinde görünmez.

### Ayarlar
`VoiceChat.cs` başındaki değerler:
- `ProxClear`: sesin tam geldiği mesafe.
- `ProxSilent`: sesin tamamen kesildiği mesafe.
- `WallVolume` / `WallMuffle`: duvarın etkisi.

### Değişen dosyalar
- `VoiceChat`
- `VoicePlayback`
- `Loc`

Ses paketinin biçimi değişti. Host ve oyuncuların hepsi aynı sürümü kullanmalı.

## Ek 17: Staff Only soygunu (kamera süresi ve görev hedefi)

### Önerilen akış
1. Bir oyuncu Staff Only kapısını maymuncukla açar.
2. Bir arkadaşı güvenlik odasındaki konsoldan kameraları kapatır.
3. Kameralar kapalıyken değerli eser değiştirilir.

### Kamera kapalı kalma süresi
- Konsol kameraları artık **20 sn** kapatır (önceden 4 sn). Sonrasında 30 sn beklemek gerekir.
- Bu süreler `SecurityCamera.cs` içindeki `MinDisableSeconds` ve `MinConsoleCooldown` değerleridir.
- Sahnede eski 4 sn değeri kayıtlı olsa bile en az bu süreler kullanılır.

### Görevde özel hedef
- Brifing terminalinde (görev paneli) bu eserin altında "ÖZEL HEDEF — Staff Only odasında" yazar.
- Sol üstteki görev kutusunda gösterilmez.

### Değişen dosyalar
- `SecurityCamera`
- `MuseumSession`
- `BriefingTerminal`
- `Loc`

## Ek 18: Kelime algılama, dolap arama, dolapta bakış, jop

### Hırsızlık kelimeleri
- Ayarlar menüsündeki "Hırsızlık kelimeleri" bölümü kaldırıldı.
- Oyun her zaman Türkçe ve İngilizce kelimeleri birlikte dinler. Arayüz dili fark etmez. Eski "Kapalı" ya da tek dil ayarı kayıtlı olsa bile yok sayılır.
- Algılama daha iyi:
  - Her dil için ikinci bir tanıyıcı çalışır. Bu tanıyıcı sadece hırsızlık kelimelerini arar.
  - "steal", "çalalım" gibi kısa ya da hızlı söylenen kelimeler artık yakalanır.
- Ses yüksekliği artık her oyuncunun kendi normal sesine göre ölçülür. Mikrofonu kısık olan oyuncu "fısıltı" sayılmaz.
- Model indirilemediyse oyun yarım dakikada bir yeniden dener.
- Her oyuncunun bilgisayarında konuşma modeli bir kez indirilir (yaklaşık 80 MB).

### Güvenlikler dolapları açar
- Bir güvenlik kaybettiği kişiyi ararken yakındaki dolapları büyük ihtimalle açıp bakar.
- Bir sesi ya da şüpheli bir şeyi incelerken de küçük ihtimalle açıp bakar.
- Dolapta birini bulursa çıkarır ve yakalar.
- Kovalarken gözünün önünde saklanırsan zaten gelip açıyordu; bu aynen devam ediyor.
- Staff Only kapısı: güvenlikler devriyede kapıyı kendileri açıp geçiyor (Ek 15).

### Dolapta fareyle bakış
- Dolapta saklanırken artık fareyle etrafa bakabilirsin.
- Dolabın baktığı yönden sağa ve sola 110°'ye kadar dönülebilir.
- Yürüyemezsin. E ile çıkarsın.

### Güvenliği itmek: jop
- Aynı güvenliği 20 sn içinde 2–3 kez itersen sana jopla vurur.
- Yaklaşık 2,5 sn yerde baygın kalırsın:
  - Ekran kararır ve "Polis sana saldırdı!" yazar.
  - Hareket edemez, etkileşim yapamazsın.
  - Elindeki iş (değiştirme, kilit, atölye) bölünür.

### Değişen dosyalar
- `VoiceKeywordListener`
- `VoiceChat` (ayar satırı kaldırıldı)
- `PlayerController`
- `PlayerStealth`
- `HidingSpot`
- `GuardAI`
- `PushSystem`
- `PlayerInteractor`
- `LockpickSystem`
- `Loc`

## Ek 19: Güvenlik duyma mesafesi, saklanma, hapis kaçışı, Staff kapısı, takılma

### Hırsızlık kelimesini duyma mesafesi
- Mesafeler büyütüldü:
  - Fısıltı: 3–6 m. Arkadaşının kulağına söylersen duyulmaz.
  - Normal konuşma: yaklaşık 15 m.
  - Bağırma: 22 m.
- Arada duvar varsa bunun %40'ı geçerli.
- Konuşanın 6 m yakınında bir eser varsa güvenlik o esere gider. Yoksa doğrudan **konuşanın yanına** gelir, böylece duyduğu görülür.

### Kaçarken saklanma
- Kovalanırken dolaba ya da perdeye saklanırsan güvenlik seni ancak **kendi gözüyle** saklanırken gördüyse bulur.
- Kameranın görmesi ya da başka bir güvenliğin telsiz haberi artık saklandığın yeri ele vermez.

### Hapisten kurtarma
- Kurtarırken (E basılıyken ya da kapı açıldığı anda) bir **güvenlik ya da kamera seni görürse** alarm çalar ve kurtaran aranır.
- Kimse görmeden açarsan **sessiz kaçış** olur: alarm çalmaz, polis peşinize düşmez.
- Kapalı kameralar görmez.

### Staff Only kapısı
- Güvenlikler kapıyı hiçbir şekilde açamaz ve geçemez.
- Kilitliyken kapı NavMesh'te yolu keser (`NavMeshObstacle`). Güvenliklerin devriyesinde Staff odası artık yok.
- Maymuncukla açılınca yol açılır. Bundan sonra güvenlik seni kovalarken içeri girebilir.

### Güvenliklerin yüz yüze takılması
- Her güvenliğin yol verme önceliği farklı. Karşılaşınca biri yol veriyor.
- Yürümesi gerekirken yaklaşık 1 sn yerinde sayan güvenlik yana çekilir.
- Yine geçemezse devriyede sıradaki noktaya, incelemede bulunduğu yere geçer.

### Değişen dosyalar
- `CrimeTalk`
- `GuardAI`
- `JailCell`
- `RoomCamera`
- `StaffDoor`
- `MuseumLayoutStaffDoor`
- `MuseumLayout` (devriyeden Staff odası çıkarıldı)

## Ek 20: Tanık ziyaretçiyi F ile bayıltma

### Nasıl çalışır
- Bir ziyaretçi seni suç işlerken görünce başında "!" çıkar. Sonra güvenliğe koşar ya da bağırır.
- O güvenliğe ulaşmadan önce yanına gidip **F'ye bir kez** basarsan bayılır:
  - Sırtüstü yere yığılır ve başında "zZ" görünür. Herkes onu yerde yatarken görür.
  - Gördüğünü unutur ve şikayet edemez.
  - 5 sn sonra kalkar ve sakin gezmeye devam eder. Seni yeniden suç işlerken görmezse şikayet etmez; görürse tekrar "!" olur.
- Yazılar:
  - Tanık bir ziyaretçiye bakınca ipucu "[F] İt" yerine "[F] Bayılt" olur.
  - Bayıltınca ekranda "Tanığı bayılttın — seni şikayet edemez!" yazar.
- Sakin, hiçbir şey görmemiş ziyaretçi eskisi gibi sadece savrulur.

### Dikkat
- Bayıltma sesi çıkar. Yakındaki güvenlik bunu duyabilir.
- Alarm zaten çaldıysa (tanık güvenliğe ulaştıysa) artık geç.
- Bayılma süresi `VisitorManager.KnockSeconds` değeridir.

### Değişen dosyalar
- `VisitorManager`
- `VisitorLook` (yere yatma görünümü)
- `PushSystem`
- `Loc`

## Ek 21: Şüphe barı bitince aranma da biter

- Güvenliklerin gözünden kaçıp şüphe barı tamamen sıfırlanınca aranma ve "tanınan suçlu" durumu da silinir.
  - Güvenliklerin önüne geri çıkınca seni yeniden kovalamazlar.
  - Seni kovalayan güvenlikler de bırakır.
- Ancak yeniden şüphe çekecek bir şey yaparsan tekrar şüphelenirler. Örnekler: eser değiştirmek, eser taşımak, yasak alana girmek.
- Bar, görülmediğin her saniye biraz azalır. Görüldüğün an azalma durur.
- Değişen dosya: `PlayerStealth` (`ServerTick`).

## Ek 22: Yaka kamerası, kamyon monitörü, kat planı, sprey boya, kötü sahte

### Yaka kamerası (H)
- Müzedeki oyuncu **H** ile kayıt açar. Kamyondaki monitöre onun **bakış açısı** canlı gelir.
- Görüntü ağdan video olarak gönderilmez. Herkes müzeyi aynı kurduğu için her bilgisayar görüntüyü kayıt yapanın konumundan kendisi çizer. Bu yüzden çok ucuzdur: 640x360, saniyede 15 kare ve sadece kamyondayken ya da atölyedeyken çizilir.
- Aynı anda tek kişi yayın yapabilir.
- **Pil:**
  - Temel süre 60 sn.
  - Garajdaki "Kamera Pili" upgrade'i her seviyede +45 sn ekler (3 seviye: $60 / $120 / $200).
  - Pil kamyonda şarj olur. Her yeni müzede dolu başlar.
  - Sol altta pil çubuğu ve kayıt durumu görünür.
- **Son kayıt:** Kayıt yapan bir esere yaklaşık 1 sn bakınca o eserin "son kaydı" saklanır. Yayın kapansa da ressam ona bakarak çizebilir.

### Ressam monitöre bakarak çizer
- Atölyede orijinal artık doğrudan görünmez.
  - Tabloda orijinalin yerinde monitör görüntüsü durur: önce canlı yayın, yoksa o eserin son kaydı, o da yoksa "SİNYAL YOK".
  - Vazo, büst ve yüzükte orijinal masada durmaz. Monitör görüntüsü sağ üstte çıkar.
- Yeşil kontur rehberi varsayılan olarak kapalıdır. G ile yine açılabilir.

### Kamyon monitörü ve kat planı
- Kamyondaki monitör, atölye tezgahının üstündedir. Monitöre bakıp **E**'ye basınca büyük ekran açılır.
  - Solda canlı görüntü.
  - Sağda müzenin **kat planı**: odalar, kayıt yasak odalar (koyu kırmızı), Staff Only, güvenlik odası, eserler ve adları, takım arkadaşları (yönleriyle) ve kayıt yapan (● işaretiyle).
- Kamyondaki kişi müzedekini sesle yönetir.

### Kayıt ışığı ve kayıt yasak odalar
- Kayıt yapanın göğsünde kırmızı ışık yanıp söner. Herkes görür.
- Kaydın yasak olduğu yerler:
  - Her zaman: Staff Only ve güvenlik odası.
  - Galerilerin yaklaşık %40'ı (her haritada farklı). Bu odalarda tavanda kırmızı tabela ve yakından "FOTOĞRAF / KAYIT YASAK" yazısı var.
- Yasak odada kayıt yaparsan:
  - Ekranda uyarı çıkar.
  - Işığını gören ziyaretçi güvenliğe gidip şikayet eder. Alarm çalmaz ama şüphe barın artar ve güvenlik bakmaya gelir.
  - Güvenlik ya da kamera seni görürse şüphelenir.
- Serbest odalarda ziyaretçilerin bir kısmı telefonla esere fotoğraf çeker (kol önde, flaş patlar). Aralarına karışabilirsin.

### Tablo başında çok durmak
- Bir esere 3 m içinden yüzün dönük olarak yaklaşık 8 sn'den fazla bakarsan güvenlik ve kameralar biraz şüphelenir. 15 sn'yi geçerse çok şüphelenir.
- Köşelerden, uygun açıdan ve kısa kayıt yapmak gerekir.

### Soluk etiketler
- Eserlerin yaklaşık %35'inin isim etiketi soluktur.
- Adı listelerde, brifingde, planda ve etkileşim yazısında "G?n B?t??" gibi görünür.
- Müzedeki biri etikete çok yaklaşınca (≤1,4 m, yüzü dönük) herkes için açılır.

### Sprey boya (X basılı tut, 1 sn)
- Garaj mağazasında kutu kutu satılır: $40, en fazla 6 kutu. Her kullanım bir kutu harcar.
- **Kamera lensi:** Oda kamerasına nişan al (9 m içi). Lens boyanır ve kamera **45 sn kör** olur. Lensinde renkli boya lekesi görünür.
- **Duvar:** Duvara nişan al (2,6 m içi). Duvara koyu bir **sahte kapı / gölge** boyanır.
  - Önünde (yaklaşık 0,75 m) durana 3 m'den uzaktaki güvenlik, ziyaretçi ve kamera **göremez**. Yakına gelen görür.
  - Ekranda "Boyalı gölgede gizlisin" yazar.
  - Görev boyunca kalır.
- Boya sıkarken suç işliyorsun: biri görürse şüphelenir.

### Kötü sahte fark edilir
- Benzerliği **%60'ın altındaki** bir sahte konunca 4–8 sn sonra onu gören ilk ziyaretçi ya da güvenlik fark eder ve **ALARM** çalar.
- Kimse görmüyorsa biri görene kadar bekler. Kaba sahte koyup kaçmak zorlaşır.

### Dosyalar
- Yeni dosyalar (`Gameplay` klasöründe):
  - `BodyCam.cs`: kamera, monitör, plan, soluk etiketler, tablo başında durma.
  - `SprayPaint.cs`
  - `FakeSpotter.cs`
  - `MuseumLayoutPlan.cs`: kat planı ve kayıt kuralları.
  - `PhotoSign.cs`
- Sahneye bir şey eklemeye gerek yok.
- Değişenler (küçük eklemeler):
  - `MuseumLayout` (tabela satırı)
  - `VisitorManager` (kayıt ışığı tanığı, fotoğraf çekenler, görüş kontrolü)
  - `VisitorLook` (telefon ve flaş)
  - `CanvasPainter` (orijinal yerine monitör)
  - `WorkbenchUI` (mevcut hedef, 3B orijinali gizle)
  - `MuseumSession` (soluk isim maskesi)
  - `UpgradeCatalog`, `PlayerInventory`, `GarageShop` (pil, sprey)
  - `PlayerInteractor` (boya sıkmak suç; monitör açıkken etkileşim yok)
  - `PlayerStealth` (ek şüphe, şüphe ekleme)
  - `RoomCamera` (boyalı lens)
  - `Vision` (boyalı gölge)
  - `Loc`

## Ek 23: Boyalı kapıya saklanma, dolaplar kaldırıldı, rehber kaldırıldı, kamyon mini haritası, optimizasyon

- **Ayakta boya:** Duvara boya sıkmak için artık eğilmek gerekmiyor. Menzil 3,2 m. Duvarın dibine ve boy hizasına kısa ışınlarla gerçek duvar mı diye bakılıyor; kaide, masa ve tablo sayılmıyor.
- **Boyalı kapıya saklanma:** Duvara sahte kapı boyayınca ona bakıp **E**'ye basarsan eski dolaplar gibi içine saklanırsın.
  - Kimse göremez; fareyle etrafa bakabilirsin. E ile çıkarsın.
  - Güvenlik ararken boyalı kapıyı da açıp bakabilir. Saklandığını kendi gözüyle görmüşse gelip çıkarır.
  - Aynı anda saklanılabilen kapı sayısı, sahnedeki eski saklanma noktası sayısı kadardır (test müzesinde 6). Fazla boyanan kapılar sadece önünde durana gizlilik verir.
- **Dolaplar / perdeler kaldırıldı:**
  - Müzede görünmüyorlar ve güvenlikler onları aramıyor.
  - Sahnedeki `HidingSpot` nesneleri silinmedi. Görünmez bekliyorlar ("park") ve boyanan kapılara atanıyorlar.
- **Yeşil rehber kaldırıldı:**
  - Tablo: yeşil kontur yok, G tuşu yok.
  - Vazo: yeşil hayalet yok.
  - Sahte yalnızca monitördeki görüntüye bakarak yapılır.
- **Güvenlik dikilip kalmıyor:** Şüphelenip yanına gelen güvenlik, karşısındaki ~4 sn boyunca şüpheli bir şey yapmazsa devriyesine döner. Gözden çıkınca şüphe barı söner.
- **Kamyonda mini harita:**
  - Kamyondayken (ya da monitörün yanındayken) sağ üstte müze haritası hep açık: odalar, kayıt yasak odalar, Staff Only, eserler ve adları, arkadaşlar.
  - Atölyede tablo çizerken tuvalin altında da harita var.
  - Büyük ekranda (monitörde E) ve atölyedeki haritada **haritaya tıklayınca** o noktaya (ya da yakındaki esere) ekibin gördüğü **işaret (ping)** konur. Kamyondaki kişi müzedekini böyle yönetir.
  - 3B işlerde (vazo, büst, yüzük) sağ üstteki görüntünün altında da harita görünür.
- **Kayıt optimizasyonu:**
  - Canlı görüntü sadece biri bakarken çizilir:
    - Monitör ekranda görünüyorsa: 3,5 m içinde 15, 8 m içinde 8 kare/sn.
    - Büyük ekran ya da atölye açıksa: 15 kare/sn.
    - Diğer durumlarda hiç çizilmez.
  - Kamera ayarları:
    - Görüntü kamerasında gölge, post-process, HDR, MSAA, derinlik ve renk dokusu kapalı.
    - Uzak çizim mesafesi 45 m.
    - Occlusion culling açık.
  - Daha az tekrarlanan iş:
    - Monitöre bakma ışını ve tabela görünürlüğü saniyede 10 kez hesaplanıyor.
    - Kayıttaki eser hedefi 0,15 sn'de bir aranıyor.
    - Soluk isim maskesi ve harita isimleri önbellekte tutuluyor.
    - Tabelaların ayrı OnGUI'si kaldırıldı; tek yerden çiziliyorlar.
    - Arayüz sadece Repaint ve tıklama olaylarında çiziliyor.
  - Ağ yükü değişmedi: görüntü ağdan gitmiyor, kayıt durumu 2 sn'de bir küçük bir mesaj.

### Dosyalar
- **Değişenler (küçük eklemeler):**
  - `SprayPaint`
  - `HidingSpot` (park / boyalı kapı)
  - `GuardAI` (parkta olanı arama, dikilip kalmama)
  - `MuseumLayout` (dolapları park et)
  - `BodyCam`
  - `PhotoSign`
  - `MuseumLayoutPlan` (`PlanFloorY`)
  - `CanvasPainter` (rehber yok, harita alanı)
  - `WorkbenchUI` (vazo rehberi yok)
  - `PingSystem` (`RequestPingAt`)
  - `Loc`

## Ek 24: Kayıt hemen fark edilmez, H donması giderildi

- **Kayıt yasak yerde süre tanınır:**
  - Kayıt yasak bir odada kayıt yaparken ilk **8 sn** kimse fark etmez. Ekranda geri sayım çıkar: "Burada kayıt yasak — X sn içinde fark edilirsin".
  - Süre dolunca eskisi gibi ziyaretçiler şikayet eder, güvenlik ve kameralar şüphelenir.
  - Yasak odadan çıkınca ya da kaydı kapatınca süre yavaşça (yarı hızla) geri dolar. Aç-kapa yaparak kandırılamaz.
  - Sunucu sayar (`BodyCam.IllegalGraceSeconds`).
- **H'ye ilk basışta donma giderildi:**
  - Kayıt ışığındaki gerçek ışık kaynağı (point light) kaldırıldı. Yerine ışıktan bağımsız parlak kırmızı küre konuldu (monitörle aynı Unlit shader). İlk kayıtta sahnedeki shader'lar yeni ışık için yeniden derleniyordu; donmanın sebebi buydu.
  - Görüntü kamerası ve kayıt ışığı müze kurulunca (yaklaşık 1,5 sn sonra) önceden hazırlanıyor. Müzenin ortasından 4 yöne ve oyuncunun gözünden birkaç görünmez kare çiziliyor. Böylece shader derlemesi yükleme sırasında oluyor, kayıt açılınca değil.
- **Değişen dosyalar:** `BodyCam`, `Loc`.

## Ek 25: Kayıt sayacı baştan sayar, monitör uzaktan da güncellenir, genel optimizasyon

- **8 sn sayacı:** Kayıt kapanınca ya da yasak odadan çıkınca sayaç **sıfırlanır**. Yeniden açınca 8 sn baştan sayar.
- **Kamyon monitörü:** Canlı görüntü uzaktan da güncellenir. Monitör ekranda görünüyorsa:
  - 3,5 m içinde 15 kare/sn
  - 8 m içinde 10 kare/sn
  - 25 m içinde 4 kare/sn
  - Daha uzakta ya da monitör görünmüyorken çizilmez; ekranda son kare kalır.
- **Genel optimizasyon (küçük, güvenli değişiklikler):**
  - `WorldLabel`, `VisitorLook`, `GuardAI` ve `PlayerNetworkSetup` içindeki her nesneye ait `OnGUI` artık sadece Repaint olayında çalışıyor. Her nesne karede iki kez değil bir kez hesaplanıyor.
  - Yeni harita kurulurken `Resources.UnloadUnusedAssets()` çağrılıyor. Önceki haritanın malzeme ve dokuları bellekten atılıyor; uzun oturumlarda bellek birikmesi önleniyor.
  - Fotoğraf çeken ziyaretçinin flaş ışığı sönükken kapalı. Telefon malzemesi tüm ziyaretçilerde ortak.
- **Değişen dosyalar:** `BodyCam`, `VisitorLook`, `WorldLabel`, `GuardAI`, `PlayerNetworkSetup`, `MuseumLayout`.

## Ek 26: Ana karakter modeli (BitirmeChar) ve animasyonları

- **Model:** `Assets/Resources/Characters/BitirmeChar.fbx`
  - Tasarımcının karakteri bütün oyuncuların görünen gövdesi oldu. Eski kodla kurulan tombul gövde artık görünmüyor.
  - Model bulunamazsa otomatik olarak eski gövde kullanılır.
  - Kod `PlayerAvatar` içinde modeli kendisi yüklüyor; sahneye ya da prefab'a bir şey eklemek gerekmiyor.
- **Boy ve yön:**
  - Model 2 m boya ölçekleniyor (CharacterController boyu). Göz hizası kameraya (yaklaşık 1,6 m) denk geliyor.
  - Önü ayak parmaklarından bulunup oyuncunun önüne çevriliyor. Ayaklar yere oturtuluyor.
- **Renk:** Oyuncunun seçtiği renk karakterin **beresine** işleniyor; ekipte kim kim belli oluyor.
- **Animasyonlar kodla kemiklere uygulanıyor:**
  - Animator ya da animasyon dosyası gerekmiyor.
  - Ağdan ek veri gitmiyor: herkes senkron hız, eğilme, eşya, bakış ve baygınlık bilgisinden hesaplıyor.
  - Hareketler:
    - Durma: nefes alma, kollar yanlarda, ara sıra göz kırpma (Blink blend shape).
    - Yürüme: bacak ve kol salınımı, diz bükülmesi, gövde zıplaması.
    - Koşma (Shift): daha büyük adımlar, daha çok öne eğilme, bükük dirsekler.
    - Zıplama: havada dizler toplanıyor, kollar hafif açılıyor.
    - Eğilme ve eğilerek yürüme: kalça iniyor, dizler basit IK ile bükülüyor (ayaklar yerde kalıyor), gövde öne eğiliyor.
    - Tutma: elde eser ya da eşya varken kollar öne uzanıyor, dirsekler bükülüyor, eller önde birleşiyor.
    - Bakış: kafa, oyuncunun yukarı ya da aşağı bakışını izliyor.
    - Baygınlık: sendeleme, kol savurma ve yüzüstü düşme eskisi gibi. Gözler kapalı, ağız açık (MouthOpen), başta yıldızlar.
- **Not:** Kıyafet seçenekleri (şapka, yüz, gövde yapısı) eski gövdeye aitti; yeni modelde şimdilik etkisiz. Renk seçimi çalışıyor.
- **Dosyalar:**
  - Yeni: `Network/PlayerAvatarModel.cs` (partial `PlayerAvatar`), `Resources/Characters/BitirmeChar.fbx`.
  - Değişen: `PlayerAvatar.cs`. Sınıf `partial` yapıldı ve 4 satırlık çağrı eklendi: model kur, renk, animasyon, temizlik.

## Ek 27: Fırça — duvara kapıyı elle çiz, lensi fırçala

- **Fırça herkesin çantasında zorunlu:**
  - Eşya: `Assets/Items/tool_brush.asset` (ItemDatabase'e eklendi, ikon `Items/Icons/tool_brush.png`).
  - Sunucu her oyuncunun çantasında tam bir fırça olmasını sağlıyor. Yeni oyuncuya, sıfırlamadan ve hapisten sonra da veriliyor.
  - Bırakılamaz, satılamaz, çöpe atılamaz, çalınamaz. Ağırlığı 0.
- **B** fırçayı ele alır ya da kaldırır (Tab çantasından tıklayarak da olur).
  - Kendi ekranında sağ altta fırça görünür; çizerken uzanıp sallanır.
  - Diğer oyuncularda karakterin sağ elinde durur, sağ kol öne kalkar.
- **Duvara kapı çizme** (eski "X basılı tut" kaldırıldı):
  - Duvara bak (2,6 m içi). **Sol tık basılı tutup** fareyle çiz; çizgiler duvarda kalır.
  - Kapı sayılması için:
    - Sol ve sağ kenar yere yakından (≤0,6 m) en az ~1,45 m yukarı çizilmeli.
    - Üst kenar çizilmeli.
    - Genişlik 0,55–1,8 m olmalı.
    - Her kenarın en az %70'i çizilmiş olmalı.
  - Ekranda hangi kenarın kaç % olduğu görünür: "Sol kenar / Sağ kenar / Üst / Boy".
  - Tamamlanınca duvarda sahte kapı belirir; E ile içine saklanılır. **Sağ tık** çizimi siler.
  - Yarım çizim sadece çizende görünür; biten kapı herkeste belirir.
- **Kamera lensini boyama:**
  - Kameranın yakınına git (≈3,4 m), lense bak, **sol tık basılı tutup fareyi küçük dairelerle oynat** (fırçala).
  - Lensin üstünde boya lekesi büyür. Dolunca kamera 45 sn kör olur.
- Her biten iş bir **Boya Kutusu** harcar. Eski "Sprey Boya" garajda artık "Boya Kutusu" adıyla satılıyor; boya yoksa uyarı çıkar.
- Çizerken ve boyarken suç işliyorsun: güvenlik ya da ziyaretçi görürse şüphelenir.
- **Dosyalar:**
  - Yeni: `Gameplay/PaintBrush.cs`, `Items/tool_brush.asset`, `Items/Icons/tool_brush.png`.
  - Değişen:
    - `SprayPaint` (X tuşu kaldırıldı, fırça için API)
    - `PlayerInventory` (`ToggleHoldSlot`, fırça çalınamaz)
    - `PlayerAvatar` ve `PlayerAvatarModel` (fırça tek elle tutulur)
    - `Loc`
    - `Resources/ItemDatabase.asset` (fırça sona eklendi)

## Ek 28: Fırça tutuşu, tablo çevirme, kapı görünümü, yana yürüme, düzeltmeler

1. **Fırça elde:**
   - Fırça karakterin sağ avucunda duruyor, parmaklar sapı sarıyor, uç öne-yukarı bakıyor.
   - Boyarken (duvar ya da lens) kol fırçayla sürme hareketi yapıyor; diğer oyuncular da görüyor (`PlayerStealth.IsPaintingAnything`).
2. **Birinci şahısta tablo:** Ekranın ortasına daha yakın ve biraz daha ileride. Eller kenarları orta yükseklikte tutuyor.
3. **R:** Eşya sadece ön / arka çevriliyor (0° ↔ 180°), yanlara dönmüyor. Kısa çevirme animasyonu var; dönerken eşya hafif kalkıyor.
4. **Kapı:** Çizim tamamlanınca eski koyu kapı görünümü (çerçeve, koyu boşluk, kol) beliriyor. Çizgi verisi ağdan gönderilmiyor.
5. **Hapse atma hatası:**
   - Güvenlik ararken rastgele dolap açarken boyalı kapıları açmıyor; boyalı kapı onun için sadece duvar.
   - Seni kapıya girerken kendi gözüyle gördüyse kovalarken yine gelip çıkarıyor.
   - Aramadan ya da yeni alarmdan önce kalmış eski "şurada saklandı" bilgisi siliniyor (`huntSpot`).
6. **Renk:** Oyuncu rengi berenin yanında tuluma da işleniyor (`MAT_Overalls_Yellow`).
7. **Lens ve menzil:**
   - Lens boyarken fırçadan lense boya damlaları uçuyor (18 parçalık sabit havuz, yeni obje üretilmiyor).
   - Lens boyanınca çevresine sıçrayan lekeler ve aşağı akan iki damla ekleniyor.
   - Boya malzemeleri artık önbellekte.
   - Menzil: lens 3,4 → 6 m, duvar 2,6 → 4 m.
8. **Yana yürüme (A / D):** Bacaklar öne-arkaya değil yana açılıp kapanıyor. Gövde yürüdüğü yöne hafif yatıyor, kollar daha az sallanıyor.
9. **Saklanınca gölge:** Saklanırken kendi gövden hiç çizilmiyor; güneşte yere düşen gölge kalmıyor.

**Dosyalar:** `PaintBrush`, `SprayPaint`, `HeldItemView`, `PlayerAvatar`, `PlayerAvatarModel`, `PlayerInventory`, `GuardAI`, `PlayerStealth`.

## Ek 29: Yana yürüme yenilendi, birinci şahısta karakterin kendi kolları, kapı ömrü

- **Yana ve çapraz yürüme:**
  - Kalça ve bacaklar gidilen yöne dönüyor (en fazla 70°), bacaklar o yöne normal adım atıyor.
  - Omurga ters dönerek göğsü ve kafayı öne bakar tutuyor. Geri-çapraz giderken kalça ters yöne dönüyor.
  - Eski "bacak yana açılıp kapanma" kaldırıldı.
- **Birinci şahıs kollar (`FirstPersonArms`):**
  - Tablo tutarken ve fırçada ekranda karakterin GERÇEK kolları var: aynı çizgili kazak ve eldivenler.
  - Ölçek 1,35× (eskisi 1,75×, çok büyük duruyordu); omuzlar ekranın hemen altında.
  - Fırçada sağ el sapı kavrıyor, sol kol ekranın dışında kalıyor.
  - Kimse poz vermediği karede kollar kendiliğinden gizleniyor.
  - Model okunamazsa eski basit eller kullanılıyor.
  - Gerekli ayar: `BitirmeChar.fbx` Read/Write açık (meta: `isReadable: 1`).
- **Fırça (birinci şahıs):** Ekranda biraz daha ileride duruyor, el onu tutuyor.
- **Kapı:**
  - Kapı oluşunca çizgiler hemen siliniyor.
  - İçine hiç girilmeyen kapı 5 sn sonra kendiliğinden siliniyor (`SprayPaint.UnusedDoorSeconds`).
  - Girilip çıkılan kapı 4,5 sn sonra siliniyor (eskisi gibi).

**Dosyalar:** `PlayerAvatar`, `PlayerAvatarModel`, `FirstPersonArms`, `HeldItemView`, `PaintBrush`, `SprayPaint`, `BitirmeChar.fbx.meta`.


## Ek 30: Birinci şahıs kavrama (tablo avuçla, fırça kalem gibi)

- Tutuşlar Blender'da (bpy) BitirmeChar modeliyle birinci şahıs kamerasından görüntü alınarak ayarlandı, sonra aynı hesap koda aktarıldı.
- `FirstPersonArms`: el yönü artık iki eksenle verilir: parmak yönü ve avuç içi yönü (`OrientPalm`). Avuç içi yönü İşaret/Serçe parmak kemiklerinden hesaplanır, işareti dinlenme pozunda bir kez belirlenir (`PalmSign`).
- Parmaklar parmak ve segment başına açı tablolarıyla avuç içine doğru kıvrılır (`CurlTo`, `PaintCurl`, `PenCurl`).
- Tablo: avuç içi tablonun yan kenarına yaslanır, parmaklar arkaya sarılır, başparmak ön yüzde durur.
- Fırça: başparmak ve işaret parmağının uçları sapı metal halkanın altından kıstırır, yüzük ve serçe parmak avuca kıvrılır. Bilek, parmak uçları tam sapa gelecek şekilde kaydırılır.
- Kısa kollar eşyaya yetişsin diye `ShoulderLocal` z değeri 0.33'ten 0.47'ye alındı (omuzlar ekranda görünmez).
- `PaintBrush`: birinci şahıs fırça biraz daha uzağa ve sağ alta alındı (dinlenme: 0.3,-0.24,0.8; boyarken: 0.25,-0.21,0.8). Tutma noktası `TransformPoint(0,0,0)`.


## Ek 31: Boya rengi, boyasız çizim yok, çizimler 10 sn, tablo çevirme

- Tablo R ile çevrilirken eller dönmez. Tablo ellerin arasında kendi etrafında döner (`HeldItemView`: kollara yaw'sız dönüş verilir).
- Boya kutusu yoksa duvara çizgi çekilmez, lens de boyanmaz. Sol tıkta "boya yok" uyarısı çıkar (`PaintBrush`).
- Kapı olmayan çizimler son çizgiden 10 sn sonra duvardan silinir (`DoodleSeconds`). Başka duvara geçince, uzaklaşınca ya da fırçayı kaldırınca da süreleri dolana kadar kalırlar, sonda incelerek kaybolurlar. Sağ tık çizimi hemen siler, kapı tamamlanınca da çizgiler hemen silinir.
- Boya rengi: garaj mağazasında Boya Kutusu → "Satın al" deyince renk kareleri açılır, bir renge tıklayınca o renk seçilir ve kutu alınır.
- `SprayPaint.Palette` 11 renk içerir. `LocalColor` son alınan rengi tutar ve `PlayerPrefs` ile hatırlanır.
- Fırça çizgileri, kapının çerçevesi ve kanadı (aynı rengin koyusu), lens boyası ve damlalar bu renkte olur. Renk, kapı ve lens mesajlarına 1 byte olarak eklendi; herkes aynı rengi görür.
- Malzemeler renk başına bir kez oluşturulur (`ColorMat`, `ColorLineMat`), her boyamada yeni malzeme açılmaz.


## Ek 32: Arkadaşın "yenilik^^" commit'i (d2afb67) ile birleştirme

Arkadaşın commit'i aynı fikirleri (kayıt kamerası + kamyon ekranı, boya / sahte kapı, soluk tabelalar, kat planı, eser başında dikilme) ayrıca yazmıştı. Örtüşen yerlerde birini seçtik; seçilmeyenin kodu SİLİNMEDİ, `Assets/Script/Gameplay/FeatureMerge.cs` içindeki anahtarlarla kapatıldı.

- **Kayıt / monitör / soluk etiket / dikilme:** BodyCam (H, kamyon monitörü, E büyük ekran). Arkadaşın karşılıkları kapalı: `FriendCamcorder` (PlayerStealth.Camcorder: H kaydı, kayıt ışığı, dikilme), `FriendLiveFeed` (LiveFeed) ve `FriendFadedNames` (ArtNames).
- **Boya / sahte kapı / lens:** fırça (B, PaintBrush + SprayPaint). Arkadaşın T spreyi (HeistControls) `FriendSpray` ile kapalı.
- **Fırça kapıları arkadaşın HidingSpot altyapısını kullanır:**
  - Açılış `ServerPaint`, silme `ServerErase` ile yapılır. Konum ve süre herkese senkronlanır.
  - E ipucunda kalan süre görünür. İçindeyken kapı silinmez; çıkınca 4,5 sn, hiç girilmezse 5 sn sonra silinir.
  - Görüntüyü SprayPaint çizer (seçilen renkte); `HidingSpot.ExternalDoorVisual` = true.
  - Eski `LocalPark` / `LocalPlaceAsPaintedDoor` / `Parked` / `IsPaintedDoor` uyumluluk için duruyor.
- **Ortak sayaçlar:** boya kutusu arkadaşın `sprayCount`'u, kamera pili `batteryLevel` (`UpgradeType.CameraBattery`). BodyCam pili `UpgradeCatalog.BatteryCapacity` kullanır (60 sn + 30 sn / seviye). Mağaza, fiyatlar ve sınırlar arkadaşın `UpgradeCatalog`'undan gelir. Boya kutusu alırken renk seçimi duruyor.
- **Açık kalan yenilikleri:**
  - M ile kat planı (kamyonda). Sağdaki ekranda BodyCam görüntüsü çıkar, kayıt yapanın yanında ●REC görünür.
  - Eser tabelaları (ArtPlaque). Adlar BodyCam'in soluk etiketlerini izler.
  - Kalitesiz sahtenin birkaç sn sonra fark edilmesi.
  - SecurityManager kör kamera altyapısı (RoomCamera hem onu hem fırça boyasını sayar).
- **İsim çakışmaları:**
  - Bizim kat planı API'si: `PlanArea`, `PlanDoorPoints`, `PlanRoomAt` (arkadaşınki: `PlanBounds`, `PlanDoors`, `RoomAt`).
  - `PlanRoom` ortak; `noPhoto` alanı eklendi.
  - Bizim alarmsız şüphe ekleme `ServerAddSuspicionNoAlarm` adını aldı.
  - Lens boyamızın objesi "BrushPaintBlob" (arkadaşın RoomCamera'sındaki "PaintBlob" ile karışmasın).
- **Loc:** `upg.spraypaint.*` iki kez tanımlıydı (açılışta çökme). Arkadaşın satırları yorum satırına alındı (fırça metni geçerli).
- **Geri açmak:** FeatureMerge'deki ilgili anahtarı `true` yapın. Bir sistemi açarken karşılığını kapatın (aynı tuşa iki sistem bağlanmasın).


## Ek 33: Kamyonda harita ekranı, brifing hologramı, boya = karakter rengi

- **Harita ekranı:** Müze haritası artık ekranın köşesinde değil, kamyondaki monitörün sağındaki ikinci ekranda. Kat planı bir dokuya çizilir (`BodyCam.RenderMapScreen`). Sadece ekran görünürken ve 12 m içindeyken, saniyede 4 kez çizilir. Bu ekrana bakıp E'ye basınca da büyük ekran açılır.
- **Brifing:** `BriefingTerminal.ShowOriginals = true`. Panoda görevdeki eserlerin dönen hologramı ve listede resimleri yine görünür.
- **Boya rengi:** Boya karakter renginde gelir (`PlayerNetworkSetup.Colors` / `ColorIndex`). Mağazadaki renk seçimi kaldırıldı; GarageShop arkadaşın sürümüne döndü. Sunucu, boyayan oyuncunun karakter rengini kullanır.

## Ek — Paket A (bug + çizim/kamera/kayıt)
- Çizim: kare/çizgi fırça (R), silgi (E), geri alma (Z / Ctrl+Z, 20 adım), toleranslı benzerlik; sinyal yokken skor "--".
- Takas: benzerlik < %35 ile tablo değiştirilemez (ArtworkDisplay.MinSwapScore); kötü sahte eşiği %45, fark edilme şansla (FakeSpotter.NoticeChance).
- Brifing: ShowOriginals=false — hologram ve eser resimleri yok, sadece ad/değer.
- BodyCam: kayıt bitince monitör/önizleme temizlenir; kayıt kamerası kayıt yapanın avatarını gizler (beginCameraRendering).
- Duvar kapısı yalnız dikdörtgen + kapı oranında (1.6–3.2) oluşur. Hapis hücresi kat planlarında çizilir.
- Arkadaştan alma: gürültü/alarm/suç yok; 3 m'den uzaklaşınca veya görüş kesilince iptal (sunucu doğrular).
- Renk: colorIndex artık sunucu yazar (RequestColorRpc), dolu renk seçilemez. Ziyaretçi itilince sendeler.
- Ekonomi: 1. seviye kotası ortalama 3 eser × 0.5, 6. seviyede eski formül. Galeri masası ganimetleri her görevde yeniden doğar.
- Atılan WorldItem'lar sunucu kapanırken silinir; uyuyan fizik uyandırılır (fırça havada kalmasın).

## Ek — Paket B + C (tüm liste)
- Fırça: sağ tık boya topu fırlatır; isabet sunucuda (PaintProjectile.Trace, SprayPaint KindThrow). Yakından sürterek boyama kaldırıldı.
- Bodrum (MuseumLayoutBasement.cs): seed'li "stairs" odasından rampa, altta hapishane; kat planında B1 kutusu, güvenlikler + tıklayınca ping (PingSystem.RequestGuardPing).
- Ganimet: layout bittikten sonra masaya ışınlanır (WorldItem.ServerPlaceAt, NetworkTransform.Teleport). Küçük ganimet (loot_*) satışı yalnız yetenek puanı verir (TeamBank.skillPoints).
- İkinci tezgah ForgeryWorkbench_2: SceneAutoSetup bir kez ekler (ProjectSettings/SahteSaheserBench2.txt); elle: Tools/Sahte Şaheser/Add Second Workbench.
- Tezgah: BenchGuide.cs (yüzük adımları sağda), büst detayları (saç/bıyık/gözlük/kulak/sakal), tür kuralları, H ile öğretici kutusu.
- Aletler (PlayerStealth.Gadgets.cs, GadgetWorld.cs): X bozuk para (5), J muz kabuğu (2), arkadan E basılı yankesicilik. Fark edilme sohbet yerine ekranda (SpottedRpc); AlarmFeedback.cs vinyet/efekt.
- Ses: SoundSynth yeniden yazıldı, 8 yeni SoundId (sona eklendi); Ambience.cs müze/alarm/kamyon ortamı.
- Menü: Tek Oyunculu (SessionManager.StartSinglePlayer, 127.0.0.1 host, kota x0.6), HeistLobbyUI (F3 hazır), GameFlow/ScreenFade geçişleri (tek sahne, aşamalar).
- TutorialUI (19 adım, F2 ilerler, Esc basılı atlar), ObjectiveHUD sağ üst; SecurityMirror.cs seed'li güvenlik aynaları (E basılı yakın bakış).

## Ek 34: Ayrı garaj sahnesi, boy aynası, yeni arayüz, hızlı envanter, bozuk para sersemletmesi

**Garaj artık ayrı Unity sahnesi (`Assets/Scenes/Garage.unity`).**
- Unity kodu derledikten sonra `GarageSceneBuilder` **bir kez kendiliğinden** çalışır (SampleScene açıksa): TestMuseum/Garage yeni sahneye taşınır (x +400), garajın kuzey ağzı "KAMYONA" kapısıyla kapanır, 4 garaj doğma noktası, `GarageArea` ve boy aynası eklenir, kamyonun batı duvarına "Garaja dön" kapısı konur, iki sahne kaydedilir ve Garage.unity Build Settings'e eklenir. Elle: **Tools → Sahte Şaheser → Garajı Ayrı Sahneye Taşı**. Yapıldığı `ProjectSettings/SahteSaheserGarageScene.txt` ile işaretlenir.
- Oyunda sunucu garajı NGO ile **ek sahne (additive)** olarak yükler (`GarageSceneLink`); istemciler ve sonradan katılanlar otomatik yükler. Oyuna ve her yeni göreve garajda başlanır. Garajın kapısında [E] → kamyon; kamyondaki kapıda [E] → garaj (alarm çalarken / kaçış sırasında / bir şey taşırken kapalı).
- Karaborsa ara sokakta kaldı. Garage.unity yoksa her şey eski düzende çalışır.
- **Commit'lerken** `Assets/Scenes/Garage.unity` (+ .meta), `SampleScene.unity`, `ProjectSettings/EditorBuildSettings.asset` ve işaret dosyasını da ekleyin; arkadaş çekince araç tekrar çalışmaz.

**Boy aynası** (`GarageMirror`): garajın doğu duvarında, kozmetik dolabının yanında. Gerçek düzlem yansıması (eğik izdüşüm); kendi gövden aynada görünür, birinci şahıs kollar/fırça gizlenir. Yalnızca önündeyken, 12 m içinde ve ekrandayken çizilir (tek kamera, 512×1024).

**Yeni arayüz teması** (`UIUtil` + `UITheme`): galeri çerçevesi — koyu mürdüm zemin, altın kenar, krem yazı, bordo düğmeler, yuvarlak köşeler (dokular bir kez üretilir). Varsayılan GUI kutu/düğme/yazı kutusu da temaya çevrilir.
**Çakışmalar:** `UIUtil.HudRect(köşe, öncelik, w, h)` — aynı köşedeki kutular önceliğe göre dizilir, asla üst üste binmez:
- Sol üst: oda kodu → görev paneli → garaj hazırlık paneli
- Sağ üst: ping → para → hedef
- Sol alt (aşağıdan): bozuk para ipucu → kamyon ipucu → kamera pili → sohbet
- Alt orta (aşağıdan): ses kutusu → **hızlı envanter** → fırça bilgisi → model durumu → öğretici
- Sağ alt: çanta kilosu → ipuçları

**Hızlı envanter:** ses kutusunun üstünde 3 yuva (çantanın ilk 3 satırı). **1/2/3** ile doğrudan ele al / kaldır; çanta açıkken yuvaya tıklanabilir.

**Bozuk para:** yalnızca **Q** (X ve J kaldırıldı, muz kabuğu kaldırıldı). Para güvenliğin kafasına/gövdesine gelirse 2.5 sn yerinde sersemler (başında dönen yıldızlar), duvara düşerse eskisi gibi ses çıkarır.
**Küçük eşya çalmak:** müzede küçük eşya alırken seni gören ziyaretçi tepki verir, şüphe artar (alarm çalmaz).

## Ek 35: Ressam-hırsız arayüz görselleri, hızlı envanter düzeltmeleri, paralar dolaptan, sersemletme düzeltmesi

- **Arayüz görselleri (Assets/Resources/UI):** koyu tuval zeminli + el boyaması altın çerçeveli + yaldızlı köşeli paneller (`ui_panel`, küçükler için `ui_panel_small`), bordo fırça darbesi düğmeler (`ui_button*`), palet çukuru yuvalar (`ui_slot`, seçili: altın boya halkası `ui_slot_sel`), yazı kutusu, etkileşim yazılarının arkasında koyu fırça darbesi (`ui_stroke`), önemli panellerde maskeleme bandı (`ui_tape`), simgeler (`icon_brush`, `icon_lockpick`, `icon_paint`). `UIUtil` bunları yükler; dosya yoksa aynı renklerde yedek dokular üretir. 64 px'ten küçük kutular otomatik sade panelle çizilir (köşe süsleri taşmaz).
- **Hızlı envanter:** fırça yuvada fırça simgesiyle görünür; seçili yuva altın halka; tuş numarası köşede rozet. Boş yuvaya (1/2/3) basınca / tıklayınca el boşalır. Maymuncuk ve boya kutusu (çantada yer kaplamayan aletler) yuvaların sağında rozet olarak, çanta panelinde "Aletler" satırında görünür.
- **Bozuk para:** görev başı bedava 5 para kaldırıldı; paralar kamyondaki dolaptan [E] ile alınır, Q çantadakini atar. Sol alttaki para satırı kalktı. Atılan para uçarken güvenliğin kafasına / gövdesine değerse (`CoinDazer`) 2.5 sn sersemler. Eski ışınlı atıştaki isabet hatası da düzeltildi (ışın güvenliğin kapsülüne çarpınca isabet sayılmıyordu).
- **Garaja dönüş:** kamyon içindeki "Garaja dön" kapısı kaldırıldı (oyunda kendini siler; editörde SampleScene açılınca da silinir — sahneyi kaydedin). Garaja görev bitince, kamyon [K] ile kapanınca dönülür.
- **Ayna** 3.2 × 2.1 m (4 kişi yan yana).
- `Ambience` uzak adım sesi kaynağı yokken her karede hata veriyordu (Console'da Error Pause açıksa oyun duruyordu) → korundu.

## Ek 36: Tek sahte masası, aynı anda birden fazla kayıt + herkes kendi kaydını seçer, hapis kamerası

- **Tek masa:** kamyonda tek sahte masası var; iki (ya da daha fazla) kişi aynı masada AYNI ANDA çalışır, her biri kendi seçtiği eseri çizer (çalışma zaten yerel). Eski kurulumdan kalan `ForgeryWorkbench_2` oyunda görünmez + etkileşimsizdir; editörde SampleScene açılınca sahneden de silinir → **sahneyi kaydedin (Ctrl+S)**. `TestMuseumBuilder` artık ikinci masa kurmaz.
- **Birden fazla kayıt:** aynı anda en fazla 4 kişi H ile kayıt açabilir (eskiden tek kişi; "arkadaşın zaten yayında" kalktı). Herkesin göğsünde kayıt ışığı yanar.
- **Kaydı herkes kendisi seçer:** monitöre bakıp **E** → büyük ekranda **"Kayıt seç:"** satırı: canlı yayınlar (● CANLI: isim) ve eserlerin son kayıtları (eser adı). Atölyede tuvalin altındaki harita panelinde de aynı satır var. Seçim sadece o bilgisayarda: ben A'yı seçince bende (monitör + atölye) A, arkadaşım B'yi seçince onda B görünür. Aynı düğmeye tekrar tıklamak seçimi bırakır (otomatik).
- **Optimizasyon:** her bilgisayar yalnızca izlediği tek canlı yayını çizer (tek kamera, 640x360, 4–15 kare/sn, yalnız biri bakarken). İzlenmeyen kayıt yapanlar için yalnızca "son kayıt" anında tek kare çizilir (aynı kamera, ayrı doku). Son kayıt seçiliyken canlı yayın hiç çizilmez. Ağdan görüntü gitmez (ağda yalnızca kayıttakilerin listesi).
- **Hapis kamerası:** hapis hücresinin önünü gören güvenlik kamerası (`RoomCamera_Jail`) — bodrumdaki hapiste de, köşe yerleşiminde de. Kurtarma girişimi kameraya yakalanabilir.
