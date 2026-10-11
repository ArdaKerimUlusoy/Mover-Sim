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
