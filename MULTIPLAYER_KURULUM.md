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

## Ek — Test düzeltmeleri (27.10)
- SoundSynth.MakeLoop: döngü karışımı dizinin sonunu aşmıyor (IndexOutOfRange giderildi).
- Tezgah mini haritası: BodyCam yazı stilleri tek satır + kırpma; başlıktaki tıklama ipucu yalnız sığarsa. Araç düğmeleri dar sütunda 2x2.
- Brifing: roller ve plan bölümleri kaldırıldı (sadece hedef listesi).
- Güvenlik aynaları tamamen kaldırıldı (SecurityMirror.cs silindi).
- VisionConeVisual: zemin ışını gözün hemen üstünden atılıyor; bodrum hapis kamerasının konisi artık bodrum zeminine çiziliyor.

## Ek — UI/UX yeniden tasarım (27.10)
- Tasarım rehberi: UI_DESIGN.md (palet, tipografi rolleri, bileşenler, ekran düzenleri). Sistem: UI/UITheme.cs + UI/UIUtil.cs (OnGUI kalır).
- Varlıklar: Assets/Resources/UI/{Panels,Buttons,Frames,Icons,Backgrounds,Textures} — yeniden üretim: Assets/Art/UI/Tools/generate_ui.ps1.
- İçe aktarma: Editor/UIAssetPostprocessor.cs; dokular bozuk görünürse Tools > UI > Reimport UI Textures.
- Font: Assets/Resources/UI/Fonts/ altına Display.ttf / Body.ttf / Mono.ttf konursa otomatik yüklenir (yoksa Georgia/Consolas/yerleşik).
- Yeni ortak çizim: UI/PromptHud.cs (istemler), ObjectiveHUD.HudKit, BlackMarket.EconomyPanelUI. Mantık/ağ değişmedi.

## Ek 37: Duvara kapı çizme yazısı, aynada fırça, daha kolay kapı çizimi

- **Duvar yazısı:** "[Sol tık basılı] Duvara kapı çiz…" uzun tuş ifadesi tuş kapağına sığmadığı için soluk düz metin + boş "basılı tut" çubuğu olarak çiziliyordu. Artık tuş kapağı ("Sol tık basılı") ve açık metin ayrı verilir (`brush.wall.key` / `brush.wall.label`).
- **Aynada fırça:** kendi elindeki üçüncü şahıs fırça da kurulur ama kendi ekranında yalnızca gölge olarak çizilir; boy aynası çizerken görünür yapar → aynada fırça elinde, el boş görünmez. (`PaintBrush.LocalHandBrush`)
- **Kapı tanıma daha affedici:** en / boy oranı 1.05–4.5 (eskiden 1.6–3.2), en 0.3–2.6 m, boy ≥ 0.6 m; kenar kapsaması %60 (eskiden %70) ve noktalara ±9 cm tolerans; yanların alt %12'si sayılmaz (iki yan aynı yerde bitmek zorunda değil); alt kenarı da çizilmiş (kapalı dikdörtgen) kapılar artık "içi karalanmış" sayılıp reddedilmez.

## Ek 38: Playtest düzeltmeleri (16.11)

- **Öğreticide Esc:** öğretici açıkken Esc'yi basılı tutmak öğreticiyi atlar; ayarlar/menü artık basınca değil, Esc kısa basılıp BIRAKILINCA (0,3 sn'den kısa) açılır. Menü, çanta, kat planı açıkken Esc eskisi gibi onları kapatır. (`SessionMenuUI`, `TutorialUI`)
- **Küçük ganimetler (kökten yeniden yazıldı):** artık sahnedeki eşyalar yakınlıkla "yakalanmıyor". Ganimet listesi ItemDatabase'den (`loot_` eşyaları) alınır; HER görevde (ilki dahil) müze yerleşimi kurulduktan sonra eski masa ganimetleri silinir ve her `LootTable_` masasının üst yüzeyine 2 yeni eşya doğar. Masadaki eşyalar sabitlenir (kinematik): yere düşmez/kaymaz. Oyuncunun yere bıraktığı ganimetlere dokunulmaz. (`MuseumSession`, `WorldItem.SetPinned`)
- **Kayıt kapalıyken referans yok:** takım oyununda müzedeki bir arkadaş kayıtta (H) değilken atölyede ne canlı görüntü ne de "son kayıt" karesi görünür; tek başına oyunda son kayıt kalır (canlı yayın imkânsız). Kayıt seçim listesindeki son kayıtlar da yalnızca kayıt açıkken seçilebilir. (`BodyCam.ReferenceAllowed`)
- **Benzerlik yüzdesi:** kayıt kapalıyken hiç gösterilmez; yerine "Gizli — arkadaşın kayıt açınca görünür" bloğu çıkar (tablo, vazo, büst, yüzük). (`BenchKit.SimilarityOff`)
- **Yüzükte kamera ekranı:** atölyedeki kamera görüntüsü artık atölye panelinin ÜSTÜNDE çizilir (altında kalıp kaybolmaz) ve küçük eserler için **Yakınlaştır 1x/2x/3x** düğmesi var (yalnızca senin ekranın; büst/vazo/yüzükte).
- **Büst / ağır hedef uyarısı:** çantaya sığmayan hedef varsa (4. seviyede 12 kg Mermer Büst, başlangıç çantası 10 kg) garajda ve kamyonda sol üstte uyarı kartı, seviye başında bildirim, brifingde uyarı bandı ve satırda "önce Büyük Çanta al" notu. (`UI/BagWarning.cs` — yeni)
- **Cepten para çalmak için eğilme:** ziyaretçinin arkasında **eğilmiş** (Ctrl / C) olmadan cep soyulamaz; ayaktayken "arkasında eğil" ipucu çıkar. (`PlayerInteractor`)
- **Hapiste anahtar çizerek kaçış:** hapisteyken fırçayı al [B], hücre duvarına ANAHTAR çiz (yuvarlak baş + uzun ince gövde + diş; yön serbest) → **2 boya kutusu** harcanır ve hücrenin önüne çıkarsın. Kaçarken güvenlik/kamera görürse alarm çalar ve aranırsın (arkadaş kurtarmasıyla aynı kural). Hapiste ekranda nasıl kaçılacağı yazar (boya / fırça yoksa o da). Kapı, düz çizgi, tek halka, "B" anahtar sayılmaz. (`PaintBrush.LooksLikeKey`, `JailCell.KeyEscapeRpc`)

## Ek 39: Tek oyunculu mod — fotoğraf makinesi + sınırlı film

Yalnızca **Tek Oyunculu** (ana menü → Tek Oyunculu / Öğretici; `SessionManager.IsSinglePlayer`) oyunda çalışır. Çok oyunculu oyun hiç değişmedi (H kaydı, monitör, kayıt seçimi aynen).

- **[H]** fotoğraf makinesini kaldırır / indirir (vizör: kenarlar kararır, köşe çerçeveleri, odak işareti). **[Sol tık]** çeker. Bakılan eser bulunursa makine ona **otomatik odaklanır** (kadraj eseri doldurur — yüzük gibi küçük eserler de net çıkar). Odakta eser yoksa uyarı verir (o kare sahte için işe yaramaz).
- **Film sınırlı:** görev başına 6 kare; garajdaki **Kamera Pili** her seviyede +2 film (tek oyunculuda açıklaması da öyle yazar). Yeni görevde film ve fotoğraflar yenilenir.
- **Fotoğraf yasak odalar:** vizörde kırmızı uyarı; çekerken güvenlik / kamera görürse şüphe artar (alarm doğrudan çalmaz).
- **Atölye:** çizim o eserin fotoğrafına bakarak yapılır. Fotoğraf yoksa görüntü yok ve benzerlik gizli. Büst / vazo / yüzükte sağ üstteki panelin altında **Fotoğraflar** albümü: küçük resme tıklayınca o fotoğraf referans olur (yeşil yazı = bu eserin fotoğrafı). Tabloda o eserin son fotoğrafı kullanılır.
- **Kamyon monitörü:** son çekilen fotoğrafı gösterir. Sol altta kayıt pili kartı yerine "Fotoğraf makinesi [H] · Film x/y" kartı. Çekince ekran kısa flaş + sağda polaroid önizleme.
- Hedef paneli, ipuçları ve öğretici (Kayıt al → Fotoğraf çek) tek oyunculuda fotoğraf metinleriyle gösterilir (`SoloPhotoCamera.K`: anahtarın `.photo` sürümü).
- Dosyalar: `Gameplay/SoloPhotoCamera.cs` (yeni), `BodyCam` (tek oyunculuda H kaydı kapalı, referans = fotoğraf, monitör = son fotoğraf), `ArtworkDisplay.Visual` (odak için), `PaintBrush` (vizör açıkken fırça çizmez), `ObjectiveHUD`, `TutorialUI`, `UpgradeCatalog`, `CanvasPainter`, `Loc`.

## Ek 40: Vazo boyama, harita ekranı, menü müzesi

- **Vazo:** şekil eşiğine (çizgi) ulaşmadan da boyamaya geçilebilir: sol panelde **"Boyamaya geç (SPACE)"** düğmesi; eşiğin altındayken "şekil düşük, sahte zayıf olur" notu. Boyarken **"Şekle geri dön (SPACE)"** ile tekrar şekillendirilir (boya korunur). Boyadıktan sonra Vazgeç onay ister, Bitir düğmesi kalır.
- **Kamyon harita ekranı:** müze GİRİŞİ (yeşil kapı + ok + "GİRİŞ") artık monitördeki, büyük ekrandaki ve atölyedeki planda görünür. Harita ekranına bakıp **E** büyük ekranı açar (eskiden yalnızca soldaki monitörde çalışıyordu; ekran yazısı "Ekrana bakıp [E]: büyük ekran").
- **Ana menü arka planı:** sahnedeki eski sabit test müzesi yerine oyunun gerçek müze düzeni (rastgele tohum + tema: odalar, cephe, ışık) kurulur; menüde iç tavanlar gizlenir (yukarıdan görünüm). Oyundan menüye dönünce son oynanan müze görünür. (`SessionMenuUI.EnsureMenuMuseum`)

## Ek 41: Yeni güvenlik modeli + animasyonlar, harita ekranında E

- **Güvenlik modeli:** `securıty_modelıngkop.blend` → `Assets/Resources/Characters/GuardChar.fbx`. Blend dosyasında iskelet, animasyon, blend shape ve malzeme YOKTU (yalnızca statik model); hepsi eklendi:
  - İskelet (19 kemik: Hips, Spine, Chest, Neck, Head, kollar, bacaklar, ayak/parmak), ağırlıklar bölgeye göre verildi; elinde jop (yeni parça).
  - Animasyonlar (Legacy, FBX içinde): **Idle, Idle_Kizgin** (eller belde, öne eğik, ayak vurur), **Walk, Run, Jop_Vurus** (jopu kaldırıp vurur), **Pushed** (geriye savrulur, toparlanır). Blender'da kare kare çizilip kontrol edildi.
  - Yüz: **Kizgin** (kaşlar çatılır) ve **Saskin** (kaşlar kalkar, gözler büyür) blend shape'leri.
  - Renkler: açık mavi gömlek, lacivert kravat/pantolon/şapka, altın arma ve toka, siyah ayakkabı/kemer; ten rengi her güvenlikte farklı. ~33k üçgen.
- **Oyunda (`GuardLook`):** model varsa eski parça parça bekçi yerine kullanılır (yoksa eskisi çalışır). Hıza göre Idle / Walk / Run; kovalarken, alarmda ya da vurduktan / itildikten sonra Idle_Kizgin + kızgın yüz; şüphelenince şaşkın yüz. Yakalayınca ve itene sabrı taşınca **Jop_Vurus**, itilince **Pushed** (sunucu `GuardAI.ServerPlayAction` → herkeste oynar). Muz kabuğu kayması ve sersemleme (yıldızlar + kafa sallanması) modelde de çalışır. Görünmeyen güvenlik animasyon hesaplamaz.
- **Harita ekranında E:** bakış artık ekranın dikdörtgeniyle matematiksel olarak kontrol edilir (önünde çarpıştırıcı olsa da çalışır).

## Ek 42: Güvenlik modeli — tasarımcının dosyası (secur_ty_boyali.blend)

- Ek 41'deki model yanlış (boyasız, iskeletsiz) dosyadandı; yerine **secur_ty_boyali.blend** kullanıldı: tasarımcının Rigify iskeleti, animasyonları (Idle, Idle_Kizgin, Walk, Run, Jop_Vurus, Pushed), Kizgin / Saskin yüz ifadeleri, boyaları (SG_*) ve siyah konturu (SG_Kontur).
- Rigify'ın kontrol kemikleri oyuna gerekmediği için animasyonlar yalnızca deform kemiklerinden oluşan temiz bir iskelete kare kare aktarıldı (44 kemik; Head, Hips, Foot.L/Toes.L adlandırıldı). Orijinal ve oyun sürümü yan yana çizilip karşılaştırıldı: aynı.
- Poligon 165k → ~37k üçgen (kaş ve gözler — ifadeli kısım — olduğu gibi, gerisi sadeleştirildi). 30 fps.
- Kod tarafı değişmedi (GuardLook aynı animasyon / ifade adlarını kullanıyor); yalnızca boy ölçüsü yeni dosyaya göre.

## Ek 43: Jop vuruşu ile bayılma aynı anda

- Güvenliği 2. kez itince bayılma artık jopun İNDİĞİ anda olur (`GuardAI.HitImpactSeconds` = 0,42 sn; Jop_Vurus animasyonundan ölçüldü, 30 fps ~13. kare). Eskiden bayılma hemen oluyor, animasyon arkadan geliyordu.
- Vurmadan önce güvenlik iten oyuncuya döner ve vuruş boyunca yerinde durur (`GuardAI.ServerFaceForHit`).

## Ek 44: Küçük ganimetler doğmuyordu (düzeltme)

- Sebep: ganimet masaları müze kökünün doğrudan altında değil, `TestMuseum/Museum/LootTable_*` altında. Ek 38'deki yeni sistem yalnızca kökün ilk seviyesine bakıyordu → hiç masa bulamıyor, hiçbir şey doğmuyordu. Artık tüm alt hiyerarşide aranır.
- Masa ya da ganimet tanımı bulunamazsa Console'a `[Loot]` uyarısı düşer (sorun hemen görünsün).

## Ek 45: Hapisten çıkış kuralları (süre kaldırıldı)

- **Çok oyunculu:** hapisten YALNIZCA arkadaş kapıda [E basılı] kurtarır. Anahtar çizme kapalı. Herkes içerideyse koşu biter (artık odada tek kişi kalsa da).
- **Tek oyunculu:** süreyle çıkış YOK; fırçayla hücre duvarına ANAHTAR çizilir (2 boya). Fırça ya da 2 boya yoksa kaçış imkânı kalmaz → ~4 sn sonra koşu biter (sonsuza kadar hapiste kalınmaz).
- `JailCell.KeyEscapeAllowed` (= `SessionManager.IsSinglePlayer`), `JailCell.CanKeyEscape`; `JailCell.Update`'teki süre sayacı kaldırıldı; `MuseumSession.ServerCheckAllCaught` yeni kurala göre. Hedef paneli / öğretici / hapis yazıları moda göre.

## Ek 46: Tek oyunculu hapis süresi, yeniden doğma güvencesi, küçük ganimet parası

- **Tek oyunculu hapis:** anahtar çizmenin YANINDA 30 sn sonra kendiliğinden çıkış geri geldi (ekranda geri sayım + "ya da anahtar çizip hemen kaç"). Fırça / boya yoksa da süre dolunca çıkılır; koşu bitmez. **Çok oyunculu değişmedi** (yalnızca arkadaş kurtarır, herkes içerideyse koşu biter).
- **Yakalanıp koşu sıfırlanınca boşlukta doğma:** yeni görev başında ışınlanmadan kısa süre sonra garajda / kamyonda olmayan oyuncu tekrar başlangıç noktasına ışınlanır (`MuseumSession.ServerVerifySpawns`); ayrıca haritanın altına (y < -40) düşen oyuncu başlangıca döner (`PlayerStealth`).
- **Küçük ganimetler para eder:** karaborsada satınca eserler gibi para verir (ekip / kişisel paylaşılır, kotaya sayılır; çalıntı "sıcak" fiyat) ve üstüne eskisi gibi ortak yetenek puanı. Satış satırında "$X +Y puan".
- Sahnedeki ganimetler kaldırılırken NGO'nun "in-scene object destroy" uyarısı giderildi (`WorldItem.ServerRemoveSceneItem`).

## Ek 47: Yeni ana karakter — Soygun (tasarımcının animasyonlarıyla)

- Oyuncu karakteri artık **Soygun** (`Assets/Resources/Characters/SoygunChar.fbx`, dokular `Characters/Textures/`). Dosya bulunamazsa eski BitirmeChar (kodla animasyon) devreye girer; BitirmeChar silinmedi (ikonlar da onu kullanıyor).
- Model 183k → ~46k üçgen (yüz en az sadeleştirildi). Tasarımcının 10 animasyon dosyası tek FBX'e aktarıldı ve tasarımcının kontrol görüntüleriyle karşılaştırıldı: pozlar aynı. Kemikler oyunun adlarına çevrildi (Hips, Spine, Chest, Neck, Head, UpperArm.L, Hand.R, Index_Proximal.L, Foot.L, Toes.L...; taşıma noktaları CarryPainting / CarryBig / Grip.R, gözler Eye.L/R).
- **Animasyonlar** (`PlayerAvatarClips.cs`, Legacy Animation): Idle, Walk (geri yürürken ters), Run (Shift), Itme (F ile itince, yalnızca üst gövde — herkes görür: `PlayerStealth.PushAnimRpc`), Tasima_Tablo_Idle/Walk (tablo), Tasima_Buyuk_Idle/Walk (vazo, büst...), Tasima_Kucuk_Idle/Walk (yüzük, küçük ganimet, alet — sağ elde). Adım hızı gerçek hıza göre.
- Animasyonun üstüne: eğilme, zıplama, yana yürüme, fırça kolu ve boyama hareketi, baygınlık (eski düşme pozu).
- **Kafa / göz bakışı:** biri sesli konuşunca (12 m) kafa hafifçe ona döner, gözler kalan açıyı kapatır (konuşma bitince 2 sn daha); yoksa yakındaki (6 m) oyuncuya göz atar; kimse yoksa oyuncunun baktığı yöne (yukarı / aşağı) eğilir. Ayarlar tasarımcının SoygunBakis.cs'inden (%35, en çok 35°, göz ±5,5° / ±6°, görüş 110°). `VoiceChat.IsSpeaking(clientId)` eklendi.
- Oyuncu rengi berede (bere ayrı malzeme `G_Bere`). Birinci şahıs kollar da yeni karakterin kolları (`FirstPersonArms`, meta Read/Write açık).

## Ek 48: Soygun karakteri düzeltmeleri (fırça, tutma, birinci şahıs kollar, ayaklar, dış çizgi, yürüyüş)

- **Birinci şahıs kollar görünmüyordu:** kol / ön kol ara kemikleri (UpperArmTwist, LowerArmTwist, UpperLegTwist, LowerLegTwist) yanlış adlanmıştı → kol kesme kodu kolları bulamıyordu. Adlar düzeltildi; kollar (kazak kolu + el) artık görünür.
- **Animasyon sırası:** klipler artık LateUpdate'te elle örnekleniyor (`PoseNow`); Animation bileşeni kendi kendine poz yazmıyor. Sıra her karede aynı: klip → itme (üst gövde) → baygınlık / fırça kolu → eğilme / zıplama / yana yürüme → eşya tutma → bakış. Fırça / eşya konumunu okuyan scriptler de önce bu pozu kurdurur (bir kare geride kalmaz).
- **Fırça:** sağ el fırçayı tutar (kol öne kalkar, parmaklar sapı sarar).
- **Tablo / vazo (üçüncü şahıs):** eşya tasarımcının taşıma noktasında (CarryPainting / CarryBig); taşıma animasyonunun üstüne eller eşyanın iki kenarını kavrar (IK + parmaklar). Yüksek eşya yüzü kapatmasın diye üst kenarı çene hizasında tutulur. Yassı eşyalar (tuval / çerçeve) tablo gibi taşınır.
- **Ayaklar yerde:** yürüme / koşu kliplerinde ayakkabının en alt noktası her karede yere oturtuldu (Blender'da, Root kemiği); oyunda zemine ışın atılıp karakter yere indirilir (CharacterController'ın deri payı yüzünden havada duruyordu).
- **Yürüyüş / koşu:** kollar gövdeye yaklaştırıldı (Idle, Walk, Run, Itme: ~50° → ~30° açıklık); yürürken / koşarken üst gövdenin sallanması yarıya indirildi.
- **Dış siyah çizgi** (güvenlikteki gibi, ters kabuk `G_Kontur`): sadeleştirilmiş kopya, ~14k üçgen. Toplam ~62k üçgen.

## Ek 49: Soygun — tutuş, birinci şahıs kollar, dış çizgi, boy, koşu

- **Tablo tutma (üçüncü şahıs):** bilekler tablonun iki yan kenarında (alt yarıda), parmaklar kenarı arkadan sarar, başparmak önde. Büyük eşyada (vazo, büst) avuçlar yanlardan sarar. Bilekler animasyondaki halinden en çok 70° bükülür (kol ile el arasında boşluk / yırtık kalmaz); fırça tutan bilek en çok 55°.
- **Birinci şahıs kollar:** dış çizgi kabuğu birinci şahısta çizilmez (kameranın dibinde siyah lekeler / bilekte "boşluk" görünüyordu); eller gerçek boyunda (küçültülünce kol ucuyla el arasında açıklık kalıyordu); kollar biraz küçük çizilir → elde tutulan tablo / vazo orantılı görünür.
- **Dış çizgi:** omuzlarda / kollarda görünmüyordu (çok sadeleştirilmiş kabuk kabarık kolların içinde kalıyordu). Kabuk daha az sadeleştirildi ve biraz kalınlaştı (~31k üçgen; toplam ~77k).
- **Boy:** Soygun artık 1,75 m (insanlardan biraz kısa). Kamera / oyun mantığı değişmedi.
- **Koşu:** yürüyüşün hızlısı gibi durmasın diye ayrı stil: gövde öne eğik (15°), kollar daha geniş savrulur, dirsekler bükük; oynatma hızı düşürüldü.

## Ek 50: Birinci şahıs kolda açıklık (düzeltme)

- Kamera kolun boyunca baktığı için kabarık kazak kolunun ağzından içi görünüyordu; malzeme tek yüzlü olduğundan iç taraf çizilmiyor, kolda delik / açıklık kalıyordu. Birinci şahıs kolların malzemeleri artık çift yüzlü kopyalar (`FirstPersonArms`, `_Cull = Off`); karakterin kendisi değişmedi.

## Ek 51: Birinci şahıs kazak kolunda delik (asıl sebep)

- Soygun'un kazak kolunda bazı köşeler omuz kemiğine (Shoulder.L/R) boyalı; kol kesme kodu "üç köşesi de kolda olan üçgeni al" kuralıyla bu üçgenleri atıyordu → kolun ortasında dikdörtgen delikler. Yeni kural: üç köşesi de kol + omuz bölgesinde ve en az biri kolda olan üçgen alınır (gövde yine gelmez). Blender'da kesilen kol her açıdan çizildi: delik yok.

## Ek 52: Birinci şahıs tutuş eski haline döndü

- Kendi ekranında eşya / fırça tutan kollar yeniden eski karakterin (BitirmeChar) kolları; `FirstPersonArms.cs` Soygun'dan önceki haline birebir geri alındı (aynı ölçek, aynı eldivenli eller, aynı kavrama). Soygun karakteri yalnızca dışarıdan (diğer oyuncular, F5, ayna) görünür; birinci şahıs görüntü eski oyundaki gibi.

## Ek 53: Birinci şahıs kollar yeniden Soygun'un kolları (deliksiz)

- Kendi ekranındaki kollar yine Soygun'un kolları; yerleşim ve kavrama eski oyundaki gibi (omuz noktası, IK, parmak kavrayışı aynı). Ölçek: kol 1.0, el 1.25 (çıplak eller eskisindeki eldivenler kadar dolgun dursun).
- Kolda delik: sadeleştirmede kazak kolunda omuza kadar uzanan ince üçgenler oluşmuş; kol kesme kuralı bunları atınca delik kalıyordu. Yeni kural (yalnızca Soygun): iki köşesi kolda olan üçgen de alınır, omuzdaki parçalar gelmez. Blender'da birinci şahıs görüntü taklit edilip kontrol edildi: delik yok.
- Dış çizgi birinci şahısta çizilmez; malzemeler çift yüzlü kopya. Modelde 7 köşenin hatalı (komşularından çok farklı) ağırlığı düzeltildi.

## Ek 54: Göz hizası, F3 ile kamyon, ganimet hayaletleri, tutuşlar, baygınlık, itme (sol tık)

- **Göz hizası:** karakter 1,75 m'ye küçülünce kamera eski 1,6 m'de kalmıştı → oyuncular birbirini kendi göz hizasının altında görüyordu. Kamera artık Soygun'un gerçek göz hizasında (`PlayerAvatar.ModelEyeY` → `PlayerController.EyeOffset`, ~1,35 m). Kayıt kamerası (uzaktaki kayıt yapan) da aynı yükseklikten çeker.
- **F3 = hazır + kamyona bin:** garajda [F3] → 1,5 sn geri sayım ([F3] ile iptal) → kamyona ışınlanma (`GarageTravel.TryTravelLocal`). Tek oyunculuda da geçerli. Kapıya bakıp E ile geçiş kaldırıldı (kapı yalnızca bilgi istemi gösterir). Metinler güncellendi.
- **Küçük ganimetler (arkadaşta havada):** müzeye elle konmuş eski masa ganimetleri istemcide ağa hiç bağlanmadan sahnede kalıyordu; masalar yeni yerlerine taşındığı için havada görünüyordu. `WorldItemGhosts` (yeni): bağlı istemcide spawn edilmemiş sahne eşyalarını gizler, oturumdan çıkınca geri açar; `WorldItem.OnNetworkDespawn` istemcide kaldırılan sahne eşyasını gizler.
- **Tablo çevirme:** elde gösterilen eşya artık pivotu değil yatay ORTASI tutulan noktaya konur (`HeldItemView.centerOff`); R ile çevirince içeri / dışarı kaymaz.
- **Bozuk para / küçük eşya (birinci şahıs):** iki elle ortada değil, sağ elin avucunda (sağ altta, avuç yukarı, parmaklar hafif kıvrık; `FirstPersonArms.ShowSmall`). Blender'da birinci şahıs görüntüyle denendi.
- **Birinci şahıs kollar:** ayrı kol modeli `SoygunArms.fbx` (Soygun'un kazak kolu + elleri, omuzda temiz kesilmiş, yalnızca kol kemiklerine bağlı) → kıyafette yırtık / delik yok; tabloyu tutarken başparmaklar önde.
- **Çanta:** sol el çantanın içine giriyordu → çanta biraz geriye / dışa alındı, sol kol 8° açıldı (taşıma klipleri hariç), Blender'da.
- **Bakış:** kafa önce ve belirgin döner (kalan açının %60'ı, en çok 45°), gövde biraz sonra ve az döner (en çok 22°).
- **Baygınlık:** yatan gövde havada kalıyordu → baygınken 0,2 sn'de bir mesh pişirilip en alt noktası zemine oturtulur (`KnockGroundFit`). Kendi kameran karakterle aynı zamanlamayla sendeler, yere yığılır (yerden ~20 cm, yan yatmış), sekip kalkar.
- **Kayıt ışığı:** eski sabit yer büyük kafanın içinde kalıyordu → Soygun'un sol göğsünde, gövdeyle hareket eder.
- **Kayıtta kendi gövden:** kaydı izleyen kişi kendi bilgisayarında yalnızca gölge olarak çizildiği için kayıtta görünmüyordu → kayıt kamerası için geçici olarak çizilir; birinci şahıs kollar / önündeki eşya kayıtta gizlenir.
- **"Çal" troll:** güvenlik bir hırsızlık kelimesini duyarsa konuşanın 4 m yakınındaki oyuncular da "duyuldu" sayılır.
- **İtme = SOL TIK** (fırça / fotoğraf vizörü / fizik objesi eldeyken F).

## Ek 55: Birinci şahıs tutuşlar Blender'da tasarlandı, garaj dolabı (renk)

- **Tablo ve bozuk para tutuşu artık Blender'da tasarlanmış pozlar:** `SoygunArms.fbx` içinde klipler `FP_Tablo_Dar`, `FP_Tablo_Genis` (yan kenarlardan kavrama: avuç kenara yaslanır, parmaklar kenarı dolanıp arkaya geçer, başparmak ön yüzde) ve `FP_Kucuk` (sağ avuç yukarı, bozuk para avuçta). Ara genişlikteki tablolarda iki poz karıştırılır. `FirstPersonArms` bu klipleri elle örnekler (`ShowPainting`, `ShowSmall`).
- **Kolların kameraya göre yeri Blender'dakiyle birebir:** modelde kamera kemikleri var (`FP_Cam`, 1 m ileri `FP_CamF`, 1 m yukarı `FP_CamU`); Unity kökü bunlara göre yerleştirir. Avuçtaki eşya noktası `FP_ItemR` (+ yukarısı `FP_ItemRUp`). Blender'da FBX yeniden içe aktarılıp aynı yerleştirmeyle çizildi: tasarımla aynı görüntü.
- **Kamera yakın kesme düzlemi** 0,3 → 0,05 m (birinci şahıs kollar gösterilirken): kameraya yakın kazak kolları kesik görünüyordu.
- **Elde küçük eşyalar gerçek boyunda:** bozuk para ~5,5 cm, diğer küçük eşyalar en çok ~10 cm (dünyadaki görünüşleri bilerek büyük).
- **Garaj dolabı** (`GarageWardrobe`, yeni): garajdaki kozmetik dolabı (`Garage_CosmeticLocker`) çalışma anında kapak çizgisi / kulp / korniş ile dolaba çevrilir; bakıp [E] → DOLAP paneli: karakter rengi (herkes görür, dolu renkler seçilemez). "Kıyafetler: Yakında" bölümü hazır. PromptHud'a 7. kanal (garaj dolabı).
- Ayarlar → tuşlar listesinde itme artık "Mouse 1".

## Ek 56: PromptHud hata düzeltmesi

- 7. istem kanalı (garaj dolabı) eklenince `drawFrame` dizisi 6 elemanlı kalmıştı → `IndexOutOfRangeException` (PromptHud.cs:88). Dizi artık kanal sayısından üretiliyor.

## Ek 57: Yazı-tura para, tablo çevirirken el animasyonu, ince avuç, dışarıdan fırça tutuşu

- **Bozuk para (birinci şahıs):** yazı-tura tutuşu (`FP_Para_Tut`: yumruk, para işaret parmağının üstünde) ve fiske pozu (`FP_Para_Fiske`: başparmak açılır). [R] ile (ya da 6–11 sn'de bir kendiliğinden) başparmakla fiske: para dönerek (3 tur) ~20 cm havalanır ve ele geri düşer (0,75 sn). Para noktası `FP_CoinR` / `FP_CoinRUp` kemikleri. `FirstPersonArms.ShowCoin`, `HeldItemView` (isCoin).
- **Tablo çevirirken ([R]):** eller tabloyu bırakıp hafif açılır ve dışa çekilir (`FP_Tablo_Dar_Acik` / `FP_Tablo_Genis_Acik`), tablo dönünce yeniden kavrar (ağırlık = dönüşün sinüsü).
- **Tablo tutuşu daha ince:** eller %88 boyutta, başparmaklar çerçeve kenarı boyunca (avuç içi daha az görünür).
- **Fırça (dışarıdan bakınca):** ana karaktere Blender'da `Firca` pozu: sağ kol öne uzanır, fırça kalem gibi başparmak–işaret parmağı arasında. Fırçanın yeri `BrushGrip` / `BrushTip` kemiklerinden (`PlayerAvatar.BrushGripIsRoot`). Boyarken kol küçük dairelerle fırçayı sürer. Eski kodla kurulan fırça kolu yalnızca bu poz yoksa kullanılır.
- Blender iş akışı: `soybuild2.py` → `soybrush.py save` → `soyexp.py` (karakter); `soyarms.py` → `fpauthor.py export` (birinci şahıs kollar).

## Ek 58: Birinci şahısta el yok, dışarıdan fırça yumrukla tutulur

- **Birinci şahıs: el / kol hiç çizilmez.** Tablo, vazo, büst ekranın alt ortasında, küçük eşyalar (bozuk para, yüzük, ganimet, alet) sağ altta kendi başına durur; fırça da elsiz (`PaintBrush` artık `FirstPersonArms.ShowBrush` çağırmaz, `HeldItemView` kol pozlarını kullanmaz). Bozuk parada [R] ile yazı-tura kalır (para dönerek havalanır, yerine düşer; kendiliğinden atma kaldırıldı). `FirstPersonArms` / `SoygunArms.fbx` dosyada duruyor ama kullanılmıyor.
- **Fırça (dışarıdan):** kalem tutuşunda fırçanın sapı ön kola giriyordu → Blender'da yeni `Firca` pozu: fırça yumruğun içinden geçer (parmaklar sapı sarar), uç başparmak tarafından öne-yukarı çıkar, sapın arkası kolun altında serbest kalır. `BrushGrip` = fırçanın kökü (sapın ortası yumrukta).

## Ek 59: Tek görüntü — birinci şahısta kendi gövdenin kolları tutar (Lethal Company tarzı)

- **Ayrı FPS el / kol yok.** Birinci şahısta karakterin kendi gövdesi ve kolları görünür; eşyayı, fırçayı, bozuk parayı diğer oyuncuların gördüğü aynı kollar, aynı pozla tutar (`HeldItemView` artık kameraya bağlı ayrı yerleşim kullanmaz, herkes için `TryGetHoldPoint` + kol IK).
- **Karakter iki mesh oldu (Blender, `soyhead.py`):** `Soygun` (gövde, kollar, eller, kıyafet) + `SoygunHead` (yüz, saç, bere, kafa konturu, yakanın içindeki ten rengi boyun dibi). Normaller ayırmadan önce sabitlendi → boyunda dikiş / renk geçişi farkı yok; dışarıdan görünüm birebir aynı (eski/yeni render karşılaştırması). Kazağın yaka ağzı içten yaka beyazıyla kapatıldı (sadece aşağı bakınca görünür, dışarıdan kafanın altında kalır). Animasyonlar, kemikler, `Firca`, `BrushGrip` aynen.
- **Sahibinin ekranı:** yalnızca kafa "sadece gölge" (kamera kafanın içinde); gövde normal çizilir. Gövde 5 cm geride çizilir ki aşağı bakınca yaka kameraya girmesin. Baygınken (kamera yere düşer) gövde yine sadece gölge. F5 önizleme, garaj aynası, kayıt kamerası eskisi gibi çalışır (kayıt görüntüsünde elindeki eşya artık gizlenmez — gerçekten elinde). Eski tek parça karakterde eski davranış (gövde gölge, eşya ekranda).
- **Kollar bakışı izler:** tablo / büyük eşya omuzdan bakışla birlikte iner-kalkar (eğimin %40'ı) ve eşya da o kadar eğilir; küçük eşya ve fırçada sağ kol aynı şekilde (%50, hafif öne kalkık). Sahibinde kameranın eğimi, diğerlerinde senkron `LookPitch` → iki görüntü aynı. Tablo kolların uzandığı kadar önde, üst kenarı çene hizasında (yüzü kapatmaz); birinci şahısta hafif aşağı bakınca tamamı görünür.
- **Bozuk para:** [R] yazı-tura artık herkes için avuçtan dönerek havalanır.
- Değişen: `PlayerAvatar.cs`, `PlayerAvatarModel.cs`, `PlayerAvatarClips.cs`, `HeldItemView.cs`, `PaintBrush.cs`, `BodyCam.cs`, `SoygunChar.fbx`.

## Ek 60: Tutuş daha yukarıda, tablo kolların önünde, avuçta para, envanter ikonu düzeltmesi

- **Eşyalar ekranda:** tablo / vazo / büst artık göz hizasının biraz altından tutulur (üst kenar tabloda ~17 cm, büyük eşyada ~20 cm aşağıda). Düz bakarken ekranın altında görünür, aşağı bakmak gerekmez; dışarıdan bakınca yüz açıkta kalır (çene / göğüs hizası). Bakışla iniş-kalkış azaltıldı (%25). Ne kadar önde tutulacağı kol boyuna göre hesaplanır (bilekler rahat yetişir).
- **Tablo:** eller tablonun ARKASINDA, yan kenarlara yakın; parmaklar kenarı öne doğru sarar → kollar / kollar tablonun içinden geçmez.
- **Vazo / büst:** iki yandan alt yarısından kavranır (eskiden kafasının / gövdesinin ortasından tutuyordu).
- **Bozuk para / yüzük / küçük eşya:** sağ el öne-yukarı ve biraz dışa uzanır, ön kol dönüp avuç yukarı bakar, eşya avucun içinde durur (birinci şahısta sağ altta görünür, dışarıdan "avucunda gösteriyor"). [R] yazı-tura avuçtan havalanır.
- **Envanter ikonu:** yeni görev / odada sahte kimlikleri 1'den başlıyordu, ikon önbelleği eski sahtenin (ör. tablonun) resmini yeni sahteye (ör. büst) gösteriyordu. Anahtar artık temizleme sayacı + eşya türü + kimlik.
- Blender'da birinci / üçüncü şahıs benzetimiyle (aynı IK hesabı) kontrol edildi.
- Değişen: `PlayerAvatarClips.cs`, `ForgeryData.cs`, `ItemIconRenderer.cs`.

## Ek 61: Parmaklar Blender pozlarından, vazonun içi boş görünmüyor

- **Parmaklar (Blender, `soyhands.py`):** üç el pozu eklendi (yalnız parmaklar, iki el): `El_Tablo` (parmaklar kanca gibi tablonun yan kenarını öne doğru sarar), `El_Kavra` (vazo / büst gövdesini sarar, başparmak karşıda), `El_Avuc` (avuç açık, hafif çukur — bozuk para / yüzük). Oyunda IK eli yerleştirir, parmaklar bu pozları alır (`SetupHandPoses`, `HandPose`); eskiden kod parmakları bir noktaya doğru kıvırıyordu, parmaklar yamuluyor / avucun içine gömülüyordu. Pozlar yoksa eski kıvırma devrede.
- **Küçük eşya:** el yüzün önüne değil, sağ öne-yana uzanır (dışarıdan "avucunda gösteriyor", ağzına götürüyor gibi durmaz); avuç yukarı, eşya avucun üstünde.
- **Vazo:** döner gövde mesh'lerinin (`Lathe`, `GridLathe`) üst ve alt kapaklarının yüz yönü tersti (yukarıdan bakınca kapak çizilmiyor, vazonun içi boş / arkası yok görünüyordu). Düzeltildi — müzedeki vazolar ve atölyede yapılan sahte vazolar dahil.
- Blender'da aynı IK + parmak pozlarıyla birinci / üçüncü şahıs kontrol edildi.
- Değişen: `PlayerAvatarClips.cs`, `ArtworkLook.cs`, `ArtworkLookExtra.cs`, `SoygunChar.fbx`.

## Ek 62: Eşyanın şekline göre tutuş (eller yüzeye oturur, parmaklar yüzeye değince durur)

- **Şekil (`HoldShape`, HeldItemView.cs):** eşya alınınca görselinin köşe noktalarından bir kez kurulur. Tablo = kutu (genişlik, yükseklik, kalınlık). Vazo / büst / heykel / küçük eşya = dikey eksen etrafında 14 yükseklik × 16 açı hücresinde yüzeyin eksene uzaklığı (yarıçap zarfı). Okunamayan mesh'te kutuya düşer. `Sdf` (işaretli uzaklık), `Radius`, `BestGripY`.
- **Eller:** vazo / büstte iki yanın en geniş olduğu yükseklik seçilir (alt-orta bölge: vazonun karnı, büstün omuz altı); ince boyunlu ya da şişman vazoda eller farklı yere oturur. Avucun ortası yüzeyin üstüne konur (yüzey eğimine göre avuç da eğilir), iki geçişli IK ile bilek yerleşir. Tabloda avuç arka yüzde, yan kenarın hemen içinde.
- **Parmaklar:** her parmak açık avuç pozundan (`El_Avuc`) yeni yumruk pozuna (`El_Kapali`, Blender) doğru eklem eklem 6 adımda kapanır; o eklemden sonraki boğumlar / parmak ucu yüzeye değince durur → parmaklar eşyanın şeklini sarar (şişman vazoda az, ince boyunda çok bükülür; tabloda kalınlığa göre kanca). Açık pozda bile eşyanın içine giren parmak (ör. tablonun içinden çıkan başparmak) önce dışarı açılır. Küçük eşyada parmaklar çukur avuca kadar kapanır, yüzüğe / ganimete değince durur; yazı-tura sırasında düz avuç.
- **El yönü düzeltmesi:** el çevrilirken başparmak kökü kullanılıyordu; bu kök avucun altına doğru da kaydığı için avuç ~45° yanlış bakıyordu (parmaklar eşyaya değil yana kapanıyordu). Artık işaret − serçe parmak kökü ekseni kullanılır (`ThumbSide`). Bozuk para avucu artık düz yukarı bakar, para avucun ortasında.
- Eşya herkeste gecikmesiz konur (parmaklar eşyanın o karedeki yerine oturur). Hesap her bilgisayarda yerel; ağ trafiği yok. Maliyet: eşya tutan oyuncu başına karede ~10 parmak × 3 eklem × 6 adım uzaklık sorgusu.
- Blender'da aynı hesapla şişman / ince vazo ve tablo için kontrol edildi.
- Değişen: `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs`, `HeldItemView.cs`, `SoygunChar.fbx`.

## Ek 63: Tablo insan gibi tutulur (avuçlar yan kenarlarda, parmaklar arkada)

- Tablo kollar yetişecek genişlikteyse (`EdgeGrip`): avuçlar iki yan kenara yaslanır ve birbirine bakar; parmaklar kenarı dolanıp tablonun karşı (arka) yüzüne kapanır, yüzeye değince durur; başparmak sana bakan yüzde. Eller tablonun içinden geçmez. Kollar kenarlara yetişmeyecek kadar geniş tablolarda eski arka yüz tutuşu kalır.
- Blender'da aynı hesapla birinci / üçüncü şahıs kontrol edildi.
- Değişen: `PlayerAvatarClips.cs`.

## Ek 64: Bozuk para yazı-tura tutuşu (başparmakla fiske)

- **Tutuş (Blender `El_Yazitura`):** yumruk öne bakar (avuç içe, işaret parmağı tarafı yukarı); işaret parmağı biraz açık kıvrım yapar, başparmak onun altında öne uzanır; para başparmak tırnağı ile işaret kıvrımının üstünde düz yatar (görseldeki gibi). Ön kol kendi ekseninde döner, bilek burulmaz.
- **Atış [R]:** başparmak ilk ~0,06 sn'de yukarı fiske vurur (`El_Fiske`), para havadayken öyle kalır, para düşerken geri kıvrılır. Para başparmağın üstünden dönerek (1080°) ~20 cm havalanır, aynı yere düşer. Herkes görür (yerel hesap, `HeldItemView.CoinFlipProgress`).
- Diğer küçük eşyalar (yüzük, ganimet): avuç yukarı, şekle göre çukur avuç (Ek 62) aynen.
- Değişen: `PlayerAvatarClips.cs`, `HeldItemView.cs`, `SoygunChar.fbx`.

## Ek 65: Eller ekranda daha yukarıda, vazo daha önde, vazoların içi boş

- **Birinci şahısta görünürlük:** kendi ekranında gövde 12 cm geride çizilir (önceden 5 cm) → eller ve tutulan eşya kameradan biraz uzakta, ekranda daha yukarıda kalır (diğer oyunculara etkisi yok). Bakışla iniş-kalkış %12'ye indi (aşağı bakınca eşya daha az iner). Bozuk para / küçük eşya eli göz hizasının ~19 cm altında (önceden 24). Tablo yan kenarlardan ortanın biraz üstünden tutulur, üst kenar gözün ~14 cm altında.
- **Vazo / büst:** gövdeden daha önde (eşya yarıçapı + 24 cm), ortada; eller vazonun orta-üst bölgesinin en geniş yerinden kavrar (gövdenin %35–78'i).
- **Vazoların içi boş:** döner gövde mesh'leri (`ArtworkLook.HollowLathe` — `Lathe` ve `GridLathe` bunu kullanır) artık ağızlı: üstte yukarı bakan ağız halkası, et kalınlığı kadar içeride içe bakan iç duvar ve derinde iç taban. Yukarıdan bakınca gerçek vazo gibi içi görünür (müze vazoları + atölyede yapılan sahte vazolar).
- Değişen: `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs`, `ArtworkLook.cs`, `ArtworkLookExtra.cs`.

## Ek 66: Vazo ağzı sana doğru eğik, duvarlar iki yüzlü (ön taraf saydam kalmaz)

- Vazo (ArtKind.Vase) elde ağzı sana doğru 16° eğik tutulur (`HeldItemView.ExtraTilt`; parmak / el hesabı da aynı eğimle) → aşağı bakınca ağız ve içi tam görünür.
- Vazo malzemesi iki yüzlü (`ArtworkLook.TwoSided`, `_Cull` = 0): ince duvarın arka yüzü de çizilir, yakından / tepeden bakınca ön taraf saydam kalmaz (müze ve sahte vazolar).
- Değişen: `ArtworkLook.cs`, `ArtworkLookExtra.cs`, `HeldItemView.cs`, `PlayerAvatarClips.cs`.

## Ek 67: Vazo ağzı yuvarlak kalır, elin dış çizgisi vazonun içinden görünmez

- Vazo eğimi 16° → 6° (fazla eğilince ağzın yakın tarafı büyüyüp oval görünüyordu).
- Elin siyah dış çizgisi (kontur kabuğu) ince vazo duvarının içinden görünüyordu: avuç yüzeyden 3 cm, parmaklar 2 cm (parmak + çizgi kalınlığı) uzakta durur; vazo et kalınlığı 1,5–3 cm.
- Değişen: `PlayerAvatarClips.cs`, `HeldItemView.cs`, `ArtworkLook.cs`.

## Ek 68: Yüzük çimdikle, vazo yukarı bakınca yüze girmez

- **Yüzük (Blender `El_Cimdik`):** işaret parmağı ile başparmağın uçları buluşur, diğer parmaklar gevşek kıvrık; yüzük bandından bu iki uç arasında tutulur ve dik durur (deliği sana bakar). Yüzük elde gerçek boyunda (~3,5 cm). Birinci şahısta sağ altta görünür. `HeldItemView.IsRing` (`forgery_ring`, `original_ring`).
- **Vazo / tablo yukarı bakınca:** eskiden yukarı bakınca eşya kollarla birlikte kalkıyor, vazo kameraya / yüze giriyordu (ağzı ve içi garip görünüyordu). Artık yalnızca aşağı bakınca biraz iner, yukarı bakınca kalkmaz; üst kenar her zaman göz hizasının en az 12 cm altında.
- Para kesesi (`loot_coinpouch`) artık yanlışlıkla bozuk para gibi yazı-tura tutuşuna girmez.
- Değişen: `PlayerAvatarClips.cs`, `HeldItemView.cs`, `SoygunChar.fbx`.

## Ek 69: Eşyalar birinci şahısta daha uzakta (net görünür)

- Kendi ekranında gövde 20 cm geride çizilir (önceden 12) → eller ve tüm tutulan eşyalar kameradan daha uzakta, yakın çekim bozulması (ağzı / kenarı büyümüş görünme) olmaz. Diğer oyuncuların gördüğü değişmez.
- Kollar daha çok uzanır (%94): vazo / büst gövdeden eşya yarıçapı + 28 cm, tablo en az 34 cm önde; küçük eşya eli daha önde.
- Değişen: `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs`.

## Ek 70: Tablo yüzü sana eğik, yüzük çimdik (yazı-tura değil)

- **Tablo:** yüzü sana doğru 30° eğik tutulur (üst kenar sana yakın) ve biraz daha önde (en az 38 cm) → aşağı bakınca resim tam görünür. Eller yan kenarlarda, aynı eğimle.
- **Yüzük:** bozuk paranın yumruk tutuşundan ayrıldı. El yarı yana yatık (avuç aşağı-içe), parmaklar öne-içe uzanır; işaret ve başparmak uçları buluşur, diğer parmaklar gevşek (`El_Cimdik` yeniden: orta / yüzük / serçe az kıvrık). Yüzük parmak uçlarının önünde-üstünde, dik. [R] yüzüğü döndürmez / zıplatmaz.
- Değişen: `PlayerAvatarClips.cs`, `HeldItemView.cs`, `SoygunChar.fbx`.

## Ek 71: Tablo eğimi ters yöne

- Tablo üst kenarı senden UZAĞA 30° eğik tutulur (Ek 70'te sana doğruydu → sana bakan resim aşağı dönüyor, arkası görünüyordu). Artık aşağı bakınca resim yukarı bakar ve tam görünür.
- Değişen: `HeldItemView.cs`.

## Ek 72: Yüzük — R etkisiz (kalkma da yok), daha büyük, yandan çimdik

- [R] yüzükte hiçbir şey yapmaz: dönme animasyonu ve dönerken kalkma (`flipLift`) da kapalı. Bozuk parada ayrıca kalkma yok (kendi yazı-tura atışı var).
- Yüzük elde ~6 cm (ekranda rahat görünür).
- Tutuş (referans görseldeki gibi): el yandan görünür — parmaklar içe (sola) uzanır, işaret parmağı üstte, başparmak altında; yüzük ikisinin uçları arasında dik, deliği sana bakar.
- Unity'de `SoygunChar.fbx` → Reimport gerekebilir (çimdik pozu `El_Cimdik` Ek 70'te değişti).
- Değişen: `PlayerAvatarClips.cs`, `HeldItemView.cs`.

## Ek 73: Tablo / büyük eşya sabit (nefes alma ile oynamaz), daha uzakta

- Tutulan eşyanın yeri her karede bir kez hesaplanır ve oyuncuya göre yumuşatılır (nefes alma / adım sallanması eşyayı aşağı-yukarı oynatmaz); eller ve eşya aynı noktayı kullanır.
- Kollar tam gerilmez (uzanma %88): eller tablonun kenarından kopmaz, boşluk kalmaz. Tablo avucu kenara 1,8 cm (vazoda 3 cm kalır).
- Kendi ekranında gövde 28 cm geride (önceden 20) → eşya ve eller kameradan daha uzakta, görüş açısı daha geniş. Diğer oyuncular etkilenmez.
- Değişen: `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs`.

## Ek 74: Birinci şahıs gövde kaydırması düzeltildi (asıl yakın / basık görünme sebebi)

- Hata: kendi ekranında gövdeyi geri kaydırmak (Ek 65/69/73: 12 → 20 → 28 cm) eşyayı kameradan UZAKLAŞTIRMIYOR, tersine kameranın ALTINA çekiyordu (eller ve eşya omuzlara bağlı, kamera yerinde). 28 cm'de tablo kameranın ~6 cm önünde kalıyordu → dev eller, yukarıdan basık görünen tablo.
- Gövde kaydırması 4 cm'ye indi (yalnızca yaka kameraya girmesin). Eşyalar artık kameranın ~30+ cm önünde: düz bakınca ekranın altında, hafif aşağı bakınca tamamı ve eller görünür.
- Tablo yüzü sana 20° (30° basık görünüyordu).
- Blender'da oyunla aynı kamera (70° görüş, göz hizası) ve aynı IK hesabıyla tablo, vazo, yüzük, bozuk para için 0° / 20° / 40° bakışta kontrol edildi.
- Değişen: `PlayerAvatarModel.cs`, `HeldItemView.cs`.

## Ek 75: Büyük vazo kameraya girmez, yüzük dik ve bandından tutulur

- Tablo / vazo / büst görseli elde taşınabilir boyuta küçültülür (tablo yarı genişlik ≤ 36 cm, vazo / büst ≤ 18 cm, yarı yükseklik ≤ 30 cm). Çok geniş vazonun ağzı birinci şahısta kameranın içinde kalıp kesik / açık görünüyordu. Vazo gövdeden eşya yarıçapı + 20 cm önde (kollar yetişir).
- Yüzük: modelin hangi eksende yattığından bağımsız olarak deliği (en ince ekseni) sana bakacak şekilde dik çevrilir; ~8 cm; işaret ve başparmak halkanın alt bandını tutar, halka parmak uçlarının üstünde (`HeldItemView.RingRadius`).
- Değişen: `HeldItemView.cs`, `PlayerAvatarClips.cs`.

## Ek 76: Vazo / büst kollar uzanmış, çenenin altında

- Vazo / büstün üst kenarı çenenin altında (eskiden gözün 17 cm altı → iri kafada burnun / yüzün önüne giriyordu, birinci şahısta kameraya çok yakındı). Kollar daha çok uzanır (%92): eşya gövdeden olabildiğince önde, eller iki yanından.
- Değişen: `PlayerAvatarClips.cs`.

## Ek 77: Karakter / tutuş düzeltmeleri, siluet çizgisi, el kamerası, F3 ile birlikte çıkış, ayna

- **Elde eşya elden kayıyordu (bozuk para, yüzük, tablo):** yanında oyuncu varken gövde (omurga / göğüs) ona doğru dönüyordu ama bu dönüş el IK'sından ve eşyanın yeri hesaplandıktan SONRA yapılıyordu → başını çevirince eller eşyadan ayrılıyordu. Bakış ikiye bölündü: `LookBody` (gövde, IK'dan önce) ve `LookHead` (kafa / göz, en sonda).
- **Kafalar yüze değil havaya bakıyordu:** hedef "kök + 1,4 m" idi (kök kapsülün ortasında → kafanın ~1 m üstü). Artık hedef karakterin gözleri (`PlayerAvatar.EyeWorldPoint`).
- **Gözler koşarken oynuyordu:** kimseye bakmıyorken gözler her kare dinlenme pozunda tutulur (kliplerdeki göz hareketi görünmez), göz açısı yumuşatılır; koşarken yakındakine göz atma yok (yalnızca konuşana bakar).
- **Tablo dışarıdan çok yatıktı:** ek eğim yalnızca kendi ekranında 16° (aşağı bakınca resmi görürsün), diğerlerinin ekranında 2°; diğerlerinin ekranında tablo biraz daha aşağıda (yüz açık). Her bilgisayar kendi hesabını yapar, eller iki görüntüde de tablonun kenarında.
- **Fırça dışarıdan:** boyamıyorken kol öne dümdüz uzanmaz; dirsek bükük, el göğsün sağ önünde, fırça yumruktan öne-yukarı (`BrushRelaxPose`). Boyarken eski öne uzanan Blender pozu. Kendi birinci şahıs görüntün değişmedi.
- **Yüzük bozuk paradan ayrı (`RingPose`):** el göğsün sağ önünde, parmaklar öne-yukarı, yüzük işaret ile başparmağın uçları arasında alt bandından tutulur, diğer üç parmak avuca kıvrık; yüzüğün deliği tutanın gözüne bakar (birinci şahısta tam daire). Bozuk para tutuşu (yazı-tura) aynen.
- **Vazo / büst parmakları:** avuçlar yanların biraz arkasında, parmaklar yüzey boyunca öne doğru sarar (`El_Kavra`'ya doğru kapanır); eskiden parmaklar havaya öne uzanıyordu.
- **Birinci şahısta vazonun ağzı kesiliyordu:** oyuncu kamerasının yakın kesme düzlemi 0,3 → 0,03 m (`PlayerController.ApplyFieldOfView`).
- **Dış çizgi yalnızca siluette (`SilhouetteOutline.cs`, `Resources/Shaders/SilhouetteMask|SilhouetteOutline.shader`):** karakterin gövdesi stencil'e numarasını yazar, ters kabuk yalnızca o numaranın olmadığı yere çizilir → burun, göz, kulak, bere, çanta içindeki çizgiler kalkar; dış kenar (çanta ve şapka dahil) kalır. Güvenlikte de. FBX'te Read/Write açık olmalı (Soygun'da açık); kapalıysa eski çizgi kalır. Ayna / kayıt dokularının derinliği 16 → 24 bit (stencil için).
- **Kayıt (H):** göğüsteki kırmızı küre kalktı. Kayıtta karakter SOL ELİNDE küçük bir el kamerası tutar (üstünde yanıp sönen küçük REC ışığı); iki elle eşya taşırken kamera göğüste durur. Kendi ekranında da elinde görürsün.
- **Garaj aynası uzakta donuyordu:** 12 m'den uzakta çizim tamamen duruyordu. Artık 60 m'ye kadar çizilir (12 m'den sonra 2 karede bir), uzak düzlem yansıyan kameraya göre.
- **F3 = herkes hazır olunca birlikte kamyona:** garajdaki herkes F3'e basınca sunucu 3 sn ortak geri sayım başlatır, herkes aynı anda ışınlanır. Biri F3'e tekrar basarsa / yeni oyuncu gelirse iptal. Garajda tek kişi kaldıysa (ör. sonradan gelen) kendi F3'ü yeter. Hazır olunca neden gidemeyeceği (elde eşya, hapis…) hemen söylenir. Tek oyunculu değişmedi. (`HeistLobbyUI`, `GarageTravel.LocalBlockReason`, `GarageSceneLink`, `ObjectiveHUD`, Loc)
- Pozlar Blender'da (SoygunChar.fbx) aynı IK hesabıyla birinci / üçüncü şahıstan çizilip kontrol edildi; kod Unity'nin kendi DLL'leriyle derlendi. Unity'de oynanarak denenmedi.
- Değişen: `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs`, `HeldItemView.cs`, `PlayerController.cs`, `BodyCam.cs`, `GarageMirror.cs`, `GuardLook.cs`, `HeistLobbyUI.cs`, `GarageTravel.cs`, `GarageSceneLink.cs`, `ObjectiveHUD.cs`, `Loc.cs`; yeni: `SilhouetteOutline.cs`, iki shader.

## Ek 78: Çanta, ayna çizgisi, fırça parmakları (3. ve 4. tur)

- Çanta: klipteki yatıklık düzeltildi (dik asılır, kalçanın yanında). Boştayken çanta tarafındaki el kapağın üstüne dayanır (dururken / yürürken; koşarken, iki elle eşya taşırken ve kayıtta bırakır).
- Fırça: dışarıdan bakınca (diğer oyuncular, F5, ayna) parmaklar sapı sarar; kendi birinci şahıs görüntün değişmedi. Aynanın önündeyken kendi karakterin de dışarıdan görünen pozu kullanır.
- Aynada siyah dış çizgi: çizgi maskesi her karede sıfırlanır, ayna çizmeden önce kendi dokusunu temizler (`SilhouetteMaskClear.shader`).
- Değişen: `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs`, `SilhouetteOutline.cs`, `GarageMirror.cs`, `Resources/Shaders/SilhouetteMaskClear.shader`.

## Ek 79: Kontroller listesi, küçük ganimet doğrudan çantaya, zıplama kolları, emote çarkı (T)

**Kontroller (Ayarlar → Kontroller):** oyuncunun kullandığı bütün tuşlar gruplu (Hareket / Eşyalar / Soygun / Ekip / Diğer). F5, F7/F8 gibi geliştirici tuşları yazılmaz. Yanlış açıklamalar düzeltildi (Q önce eldekini bırakır; H tek kişide fotoğraf makinesi; B fırçayı çıkarır).

**Ayarlar → Oyun:** renk seçimi kaldırıldı (renk garajdaki dolaptan ve ana menüden seçilir).

**Küçük ganimet:** masalardaki küçük eşyalar (`loot_` ile başlayanlar) 1-2-3 hızlı yuvalarında görünmez; hızlı yuvalar küçük ganimeti atlayıp sıradaki eşyaları gösterir. Bir eşya aldığında (ya da birinden çaldığında) sağ altta, kilo kartının üstünde resmiyle "Çantaya eklendi" kartı çıkar (3 sn; aynı eşya art arda gelirse sayısı artar).

**Zıplama:** havadayken kollar yanlara kalkar (çıkarken biraz öne, düşerken denge için daha açık), inince iner. Fırça tutan kol etkilenmez.

**Yana yürüme:** çanta kalçayla birlikte dönmez, gövdeyle kalır (sağa giderken karnın önüne kaymıyor, onu tutan el de kolu ters bükmüyor).

**Emote çarkı (T):**
- T'yi basılı tut → ekranın ortasında 8 dilimli çark. Fareyi kaydır (imleç görünmez, kamera dönmez) → dilim büyür ve kırmızılaşır. T'yi bırak → emote oynar. Ortada bırakırsan ya da Esc'ye basarsan hiçbir şey olmaz.
- Şimdilik 6 el hareketi: El salla, Başparmak yukarı, OK, Gösterip gül, Orta parmak, Başparmak aşağı. Boş 2 dilim "Yakında" (troll / komik hareketler sonra).
- Herkes görür. Ağda emote başına iki küçük mesaj gider (`SS_EmoteReq` istemci → sunucu, `SS_Emote` sunucu → diğerleri); poz her ekranda zamandan hesaplanır. Sunucu bekleme süresini denetler (spam yok).
- Emote oynarken yenisi başlamaz; eller doluyken (tablo, vazo, fırça, küçük eşya, taşınan nesne) "Ellerin dolu" der.
- Animator kullanılmadı: karakter klip + kod katmanlarıyla oynatılıyor, emote'lar da bu katmanların en üstünde kodla kurulan pozlar (kol IK + parmak bükme). Blender'da her poz önden / yandan / üstten denendi. Bir şey kurmak gerekmez, sahneye bir şey eklemen de gerekmez.
- **İsteğe bağlı ayarlar:** `Assets/Resources` içinde sağ tık → Create → Sahte Şaheser → Emote Settings (adı `EmoteSettings` kalsın). Alanlar: tuş, çark açıkken kamera dursun mu / yürünsün mü, emote sırasında yürünsün mü, bekleme süresi (varsayılan 1,5 sn), çarkın ekrandaki yeri ve boyu, sesler.
- **Ses eklemek:** `Assets/Resources/Emotes/` klasörüne şu adlarla ses koy: `ok`, `middle`, `thumbsup`, `thumbsdown`, `wave`, `pointlaugh` (ya da Emote Settings'teki listeye sürükle). Ses yoksa sessiz oynar.
- **Yeni emote eklemek:** `EmoteCatalog.cs` (kimlik + süre), `Loc.cs` (`emote.<key>`), `PlayerAvatarEmotes.cs` (poz), `EmoteIcons.cs` (ikon). 8'den fazla olunca çarkta fare tekerleği sayfa değiştirir. Kilit açma (`EmoteCatalog.IsUnlocked`) ve oyuncuya özel sıra (`EmoteCatalog.SaveWheelOrder`) için yer hazır.
- Yeni dosyalar: `Social/EmoteCatalog.cs`, `Social/EmoteSettings.cs`, `Social/EmoteIcons.cs`, `Social/EmoteWheel.cs`, `Network/PlayerAvatarEmotes.cs`. Değişen: `PlayerAvatarClips.cs`, `PlayerAvatar.cs`, `PlayerController.cs` (iki satır), `SessionMenuUI.cs` (bir satır), `PlayerInventory.cs`, `SettingsUI.cs`, `UIUtil.cs`, `Loc.cs`.

## Ek 80: Emote'lar yeniden — kıyafet bozulmadan

- Sorun: kol açıları kodla (IK) baştan hesaplanınca gömleğin kolu omuzda sivri / koyu katlanıyor, bilek 180° dönünce kol burgu gibi kıvrılıyordu.
- Çözüm: öne uzanan emote'lar (başparmak yukarı / aşağı, gösterip gül, orta parmak) Blender'daki `Firca` klibinin sağ kol duruşundan başlar (omuz ve burulma kemikleri dahil; bu duruşta kumaş pürüzsüz). Üstüne yalnızca küçük el kaydırması, bilek ve parmak ayarı yapılır. Kolun kendi ekseninde dönmesi `UpperArmTwist` / `LowerArmTwist` kemiklerine paylaştırılır.
- El başın yanında olanlar (el salla, OK): üst kol 55° dışa döner, koltuk altının önündeki koyu kırışık kalkar (Blender'da 0 / ±35 / 55° denendi).
- "Nah" yerine "Orta parmak" (kimlik aynı: 1).
- Her emote Blender'da önden / yandan / üstten yakın çekimle kontrol edildi; çanta eli ve zıplama kolları da.
- Değişen: `PlayerAvatarEmotes.cs`, `PlayerAvatarClips.cs`, `EmoteCatalog.cs`, `EmoteIcons.cs`, `EmoteSettings.cs`, `Loc.cs`.

## Ek 81: Parmaklar düzgün bükülür (parmak iskeleti / ağırlık düzeltmesi)

- Blender'da ölçülen sorunlar (SoygunChar):
  1. Parmak ağırlık boyaması: işaret / serçe parmağının kök boğumu çoğunlukla avuç kemiğine bağlı, orta / yüzük parmağının orta boğumu neredeyse tamamen uç kemiğine bağlı (Ring_Intermediate'e hiç nokta yok), yüzük parmağının kökü %16 serçe parmağına bağlı → parmaklar kırık / ezik bükülüyor, biri bükülünce yanındaki çekiliyor.
  2. `El_Kapali` klibinde orta boğum ters yöne (+80°) bükülüyor → yumruk yerine pençe.
- Düzeltme (FBX'e dokunmadan, oyun açılırken): `FingerReskin.cs` parmak noktalarını en yakın parmağın ekseni boyunca konumuna göre avuç / kök / orta / uç kemiğine bağlar (eklemde yumuşak geçiş; dış çizgi kabuğu da aynı). El pozlarında ters bükülen büyük açılar avuca doğru çevrilir; emote parmakları her kemiğin kendi menteşe ekseninde (El_Kavra'dan okunur) bükülür.
- Eşya tutuşlarındaki (vazo, yüzük, el kamerası) parmaklar da bundan düzelir. Birinci şahıs kol modeline (SoygunArms) dokunulmadı.
- Öne uzanan el hareketleri gövdenin 16 cm dışında tutulur (karşıdan silüette okunur).
- Arkadaşın Blender'da düzeltmek isterse: parmak boğumlarının ağırlıklarını yeniden boya, `El_Kapali`'da orta boğumları avuca doğru çevir. O zaman bu kod zaten düzgün olan ağırlıkları neredeyse aynı bırakır.
- Değişen: `FingerReskin.cs` (yeni), `PlayerAvatarModel.cs` (bir satır), `PlayerAvatarClips.cs`, `PlayerAvatarEmotes.cs`.

## Ek 82: Karakterin eli tam çözünürlüklü (parmaklar gerçekten bükülür)

- Asıl sebep: SoygunChar oluşturulurken model 93.105 noktadan 23.223 noktaya indirilmişti; elde 4.148 nokta 2.076'ya düşmüş, parmakların orta boğum halkaları silinmişti (noktalar yalnızca parmağın dibinde ve ucunda kalmıştı). Parmak bükülünce ezik / yassı uçlar buradan geliyordu. Kemikler zaten doğru yerdeydi.
- Düzeltme (`SoygunChar.blend` → `SoygunChar.fbx`): eski tam sürümdeki (soygun_karakter_procreate.blend, aynı uzayda, aynı kemik konumları) el modeli doku eşlemesi ve ağırlıklarıyla yeni modele kondu; ağırlıklar yeni kemik adlarına çevrildi (DEF-f_index.01 → Index_Proximal, DEF-forearm.001 → LowerArmTwist vb.). Azaltılmış el ve onun dış çizgi kabuğu silindi, kabuk yeni elden aynı kalınlıkta (0,05) yeniden üretildi.
- Dışa aktarılan FBX eskisiyle karşılaştırıldı: 78 kemik ve dinlenme pozu aynı, 18 klip aynı ad ve uzunlukta, nesneler / malzeme sırası aynı, örnek karelerde kemik konum farkı en çok ~2 mm. Değişen yalnızca gövde modelinin nokta sayısı (23.223 → 28.245).
- Arkadaşın `El_*` el pozları ve `Firca` klibi tam elde pürüzsüz; klipler değiştirilmedi.
- Kodda önceki geçici parmak düzeltmeleri kaldırıldı (`FingerReskin` artık çağrılmıyor, dosya silinebilir; El_Kapali'yi çalışma anında değiştirme kaldırıldı). Emote yumrukları tam bükülür (85/95/70).
- Birinci şahıs kol modeline (SoygunArms.fbx) dokunulmadı.
- Değişen: `Assets/Resources/Characters/SoygunChar.fbx`, `PlayerAvatarModel.cs`, `PlayerAvatarClips.cs`, `PlayerAvatarEmotes.cs`.

## Ek 83: Elde tutma düzeltmeleri (para, tablo, vazo, yüzük, büst) ve [R] çevirme animasyonu
- El pozu klipleri (`SoygunChar.blend` → `SoygunChar.fbx`): `El_Kapali`, `El_Yazitura`, `El_Fiske`, `El_Tablo`, `El_Cimdik` kliplerinde bazı parmak eklemleri ters yöne (elin sırtına) bükülüyordu (sağ ve sol, 36 eğri). İşaretleri düzeltildi; artık hepsi avuca doğru kapanıyor. Bozuk paradaki yamuk / bozuk parmaklar buradan geliyordu (para "El_Yazitura" yumruğunun üstünde duruyor).
- Tablo, avuçtaki şişlik: parmaklar eşyaya açık pozda bile değince kod onları 60°'ye kadar geriye açıyordu; başparmak kökü ters bükülüp avuç içi şişiyordu. Bu geriye açma kaldırıldı (parmak açık pozda kalır).
- Vazo, kol kıyafeti: dirsek aşağı-içe düşüyor, bilek tek başına dönüyordu. Artık dirsek taşıma klibinin kendi dirsek yönüne bükülüyor, bilek dönüşünün %60'ını önkol burulma kemiği (LowerArmTwist) alıyor. Blender'da oyundaki hesapla denendi: kol çizgileri kesintisiz, buruşma yok.
- Kalın yüzük: halka parmak uçlarının içinden geçiyordu. Artık işaret parmağı ile başparmak bant kalınlığı kadar aralanıyor (`HeldItemView.RingTube`), halka bandın ortasından tutuluyor.
- Büst: alttaki dikdörtgen kaidenin iki yan yüzünden tutuluyor (`HoldShape.HasBase / BaseCenter / BaseHalf`, büst görselindeki en alttaki küp). Kollar aynı klip dirsek yönünü kullanıyor.
- [R] çevirme: eşya ~0,3 sn'de yumuşakça dönüyor, dönerken 4 cm kadar kalkıp iniyor. Tablo / vazo / büstte eller de birlikte kalkıyor (`HeldItemView.TurnLift`, `TryGetCarryPoint`'e eklenir). Yüzük yine dönmüyor, para yine yazı-tura atıyor.
- Değişen dosyalar: `SoygunChar.fbx`, `PlayerAvatarClips.cs`, `PlayerAvatarModel.cs` (ArmIK'a isteğe bağlı dirsek yönü), `PlayerAvatarEmotes.cs` (sol önkol burulma kemiği), `HeldItemView.cs`.
