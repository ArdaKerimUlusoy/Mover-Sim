# UI_DESIGN — Sahte Şaheser arayüz sistemi

Uygulayıcı ajanların TEK rehberi. Teknik: yalnızca OnGUI (IMGUI) + `UIUtil` / `UITheme`. uGUI / UI Toolkit / TMP yok.
Referans uygulama: `Assets/Script/Network/SessionMenuUI.cs` (ana menü, çok oyunculu, ayarlar, yapımcılar, duraklat, HUD).

## 1. Sanat yönü
**Müze sergisi × eskiz defteri × arşiv kataloğu × sergi etiketi**, hafif muzip soygun tonu (katalog numarası
"koleksiyona dahil değil", "Tuval üzerine sahte, karışık teknik"). Oyun içi yüzeyler **koyu plaket** (kömür + ince
pirinç çizgi), belgeler **fildişi katalog sayfası**, tezgah notları **eskiz kâğıdı**, kimlik / istem kartları **sergi etiketi**.
Keskin köşe (1–2 px pah), ince çizgiler, tipografiyle hiyerarşi; dekor az ve amaçlı.

**YASAK:** neon, glassmorphism/bulanık cam, mor-mavi gradient, her yere yuvarlak kart, kalın/çoklu gölge
(gölge yalnızca Dialog + ana menü açık kartları), rastgele ikon paketleri, her panele kâğıt dokusu (yalnızca Sketch),
emoji, yazı içeren doku, `GUI.backgroundColor` ile renk boyama (yeni kodda).

## 2. Token'lar (`UITheme.*`)
| Token | Hex | Kullanım |
|---|---|---|
| Ivory | F2EAD8 | koyu üstünde ana yazı; Catalog zemini |
| IvoryDim | D9CDB4 | koyu üstünde ikincil yazı |
| Paper | EFE6D2 | Sketch zemini |
| Charcoal | 1E1B19 | Plaque zemini; açık üstünde ana yazı |
| CharSoft / CharDeep | 2C2724 / 141210 | yuva, araç düğmesi / oluk, yazı alanı |
| Graphite | 4A443F | hatlar; açık üstünde ikincil yazı |
| Bordeaux | 7A1F2B | birincil eylem, açık üstünde bölüm başlığı, odak çubuğu |
| Forest | 1F3A2E | güvenli alan (kamyon / garaj) vurgusu, harita zemini |
| Sage | 7FB58F | olumlu / hazır / başarı |
| Brass / BrassLight | B08D57 / D8B97A | çerçeve, koyu üstünde bölüm başlığı / vurgu, seçili, sayılar, odak halkası |
| Terracotta | B8664A | eskiz işaretleri, sıcak ikincil vurgu |
| Ochre | D9A441 | uyarı, şüphe |
| Danger / DangerLight | C2483D / E58A7F | alarm, hata, tehlike düğmesi / koyu üstünde tehlike yazısı |
| Scrim | 141210 @62% | diyalog / duraklat perdesi |

Eski adlar korunur ve yeni paletten değer alır: `Gold=BrassLight, Cream=Ivory, Muted=IvoryDim, Wine=Bordeaux,
Mint=Sage, Coral=DangerLight, Ink=Charcoal@93%, Frame=Brass`. Yeni kodda `UITheme.*` kullan.
Kontrast: açık yüzeyde yazı yalnızca Charcoal/Graphite/Bordeaux; koyu yüzeyde Ivory/IvoryDim/Brass(Light)/Sage/Ochre/DangerLight.
`UITheme.TextOn(kind)`, `MutedOn(kind)`, `SectionOn(kind)` doğru rengi verir.

**Boşluk ölçeği** `S1..S8 = 4, 8, 12, 16, 24, 32, 48, 64` px (@1080p). İç boşluk panelde S5 (24), satır arası S2–S3.
**Ölçek** `UITheme.Scale = clamp(Screen.height/1080, 0.85, 1.6)` — yeni ekranlarda boyutlar `* sc`. HUD köşe kutuları (HudRect) sabit px kalır.

## 3. Tipografi (`UITheme.Style(role[, color, anchor])` — paylaşılan stil; kalıcı değişiklik için kopyala)
| Rol | Boyut @1080 | Yazı tipi | Kullanım |
|---|---|---|---|
| Display | 54 kalın | serif (Display) | oyun adı, görev sonu başlığı |
| Title | 28 kalın | serif | ekran / panel başlığı, ana menü öğeleri |
| Section | 13 kalın, BÜYÜK HARF (`UITheme.Caps`) | gövde | bölüm başlığı (Brass koyu / Bordeaux açık) |
| Body / BodyStrong | 16 | gövde | metin / istem etiketi |
| Button | 17 kalın | gövde | düğmeler (`UIUtil.Button` çizer) |
| Caption | 12 | gövde | küçük etiket, açıklama, ipucu |
| Numeric | 20 kalın | mono | para, yüzde, sayaç, oda kodu |
| Mono | 14 | mono | katalog no, tuş kapağı, kod |

Yazı tipi önceliği: `Resources/UI/Fonts/{Display,Body,Mono}.ttf` → OS fontu (Display: Georgia/Constantia…, Mono: Consolas/Cascadia…)
→ Unity yerleşik. Gövde bilerek yerleşik (Türkçe karakterler güvenli). BÜYÜK HARF için `UITheme.Caps()` (tr-TR: i→İ).

## 4. İkon dili
29 ikon, `Resources/UI/Icons/icon_*.png`, 64×64, **beyaz 4 px çizgi**, yuvarlak uç, canlı alan 6–58, dolgu yok (yalnız küçük nokta).
`UIUtil.Icon(rect, IconId.X, renk)`. Boyutlar: 16/20 (HUD satırı), 24 (düğme), 32–48 (kart). Renk anlamı taşır (Sage/Ochre/Danger).
Liste: Interact, Eye, Camera, Brush, Eraser, Undo, Bag, Money, Lock, Lockpick, Door, Painting, Vase, Bust, Ring, Mic, MicOff,
Speaker, Check, Warning, Alarm, Jail, Map, Gear, Players, Chevron, Close, Clock, Truck. Yeni ikon → `generate_ui.ps1`'e aynı kurallarla ekle.

## 5. Durumlar ve hareket
| Durum | Görsel |
|---|---|
| hover | dolgu bir ton açık + pirinç iç hat (düğme), yazı Ivory/beyaz |
| pressed | koyu dolgu, alt dudak yok, içerik 1 px aşağı |
| selected | Tool: pirinç dolgu + kömür içerik; yuva: açık pirinç hat + arşiv köşeleri; renk: 2 px halka |
| disabled | grafit dolgu, yazı %45–50, etkileşim yok; yuva: soluk + çapraz tarama |
| focus (klavye) | `FocusRing`: 3 px dışarıda açık pirinç hat, 2.2 rad/sn hafif nabız + kalıcı hover dokusu; Enter tetikler |

Hareket (`Time.unscaledTime`, `UIUtil.Tween/Ease/Pulse`): hover/odak 0.12 sn (MotionFast), panel giriş 0.18 sn (fade + 8–16 px kayma,
OutCubic), ekran geçişi 0.30 sn, toast 0.22 giriş / 3.5 kalış / 0.30 çıkış. Sıçrama yok (OutBack yalnızca rozet "pop").
Sürekli animasyon yalnızca durum bildirir (alarm, kayıt, meşgul). Uzun nabız yok; okunurluk her zaman önce.

## 6. Varlıklar (tek kaynak: `Assets/Resources/UI/<Klasör>/`, üretici `Assets/Art/UI/Tools/generate_ui.ps1`)
Neden Resources: OnGUI `Resources.Load` ile yükler (`UITheme.Tex("Panels/panel_plaque")`, önbellekli). Art/UI'da kopya YOK.
İçe aktarma: `Assets/Script/Editor/UIAssetPostprocessor.cs` (alt klasörler: mipmap kapalı, sıkıştırmasız, clamp — `Textures/tex_*` repeat —,
bilinear, alphaIsTransparency, NPOT none). Gerekirse menü: **Tools ▸ UI ▸ Reimport UI Textures**. Kökteki eski `ui_*.png` yedek olarak kalır.

| Dosya | Boyut | 9-dilim kenarı (L,R,T,B) |
|---|---|---|
| Panels/panel_plaque · panel_plaque_small | 48² · 24² | 12 · 6 (küçük: <32 px kutular, otomatik) |
| Panels/panel_catalog · panel_sketch · panel_label | 64² · 96² · 32² | 16 · 24 · 8 |
| Panels/panel_shadow | 48² | 16 (rect −12/−8, +24/+22 büyütülerek) |
| Panels/field · field_focus | 32² | 8 |
| Panels/slot · slot_hover · slot_selected | 48² | 12 |
| Panels/prog_track · prog_fill (beyaz, renklenir) | 24×12 | 4 · (4,4,3,3) |
| Panels/keycap | 24×26 | 7,7,6,9 |
| Panels/badge (beyaz, renklenir) | 24×16 | 8,4,4,4 |
| Frames/frame_brass · frame_focus · frame_corners | 32² · 16² · 32² | 6 · 5 · 10 |
| Buttons/btn_{primary,secondary,danger}_{normal,hover,pressed,disabled} | 40×32 | 10 |
| Buttons/btn_tool_{normal,hover,pressed,selected,disabled} | 40² | 10 |
| Buttons/slider_track · slider_thumb(_hover) · toggle_off/on | 16×12 · 10×12 · 16² | 4,4,0,0 · 0 · (16,0,16,0) |
| Backgrounds/bg_vignette · bg_scrim_left · bg_scrim_bottom | 256² · 256×4 · 4×256 | tam ekran gerilir |
| Textures/tex_paper_grain · tex_sketch_grid · tex_hatch · ornament_diamond | 128² · 32² · 16² · 12² | döşenir (`UIUtil.Tile`) |

## 7. Bileşen kuralları (`UIUtil`)
- **Panel(rect, PanelKind)** — `Plaque`: oyun içi her şey (HUD, istem, toast, duraklat). `Catalog`: belge/okuma (brifing, yapımcılar,
  görev sonu raporu, diyalog). `Sketch`: tezgah notları / öğretici / rehber — ızgara+kâğıt otomatik. `Label`: küçük kimlik kartı,
  eser etiketi, hedef kartı. Eski `Panel(rect[,accent][,alpha])` = Plaque (+solda 3 px renk şeridi). `Tape()` artık arşiv köşeleri.
- **Button(rect, text, ButtonKind, enabled, focused, icon, selected)** — Ekranda tek Primary (asıl eylem). Secondary: geri/iptal/alternatif.
  Danger: geri alınamaz (at, ayrıl, çöpe at, çıkış). Tool: ikonlu kare/sekme (fırça, renk, dil), `selected` ile. Min yükseklik 28 (Tool 32),
  dokunma hedefi ≥ 32×32. Etiket fiil ("Oda Kur", "Katıl"). GUILayout içinde `LayoutButton`.
- **Prompt(topCenter, key, label, color, hold)** — plaket şerit + tuş kapağı + etiket; basılı tutmada altta pirinç dolum. Eski `DrawPrompt`
  "[E] ..." metnini otomatik tuş kapağına çevirir; yeni kodda Loc metnini `[E]` ile başlat ya da Prompt kullan. Yer: `CenterSlot()`.
- **KeyCap(rect, key)** — tuş adları her yerde tuş kapağıyla (ipucu satırları dahil).
- **ShowToast(text, ToastKind, sn, icon)** — her yerden çağrılır; sağ kenar, ekranın %28'inden aşağı, en çok 4, aynı metin tazelenir.
  Info=Brass, Success=Sage, Warning=Ochre, Danger=Danger. Kalıcı/kritik durum (alarm, hapis) toast DEĞİL → TopSlot bandı.
- **Slot(rect, SlotState)** — Empty/Filled/Hover/Selected/Disabled; içerik ikonu 60%, adet sağ altta Numeric küçük, kısayol sol üstte Caption.
- **Dialog(title, body, confirm, cancel, danger)** — modal; Enter onay, Esc vazgeç; açıkken arkadaki kontroller `GUI.enabled=false`.
- **ProgressBar(rect, oran, renk, marker, ticks)** — eşikli işlerde `marker` (ör. vazo %75). Eski `Bar()` buna yönlenir. Yükseklik ≥ 8.
- **Badge(pos, text, fill, textColor)** — envanter etiketi biçimi: durum (HAZIR, KAYIT, x3), rol, yeni.
- **SectionHeader / Divider(ornament) / BrassFrame / Corners / FocusRing / Scrim / Vignette / ScrimLeft / Hatch / Text / Measure**.
- **Klavye**: `NavigateVertical`, `NavigateHorizontal`, `EscapePressed`; liste ekranlarında ↑/↓ + Enter + Esc zorunlu, alt solda KeyCap ipuçları.
- HUD yerleşimi değişmez: `HudRect(köşe, HudOrder, w, h)`, `TopSlot`, `CenterSlot`.

## 8. Ekran tarifleri (ekran → dosya → durumlar → yapılacak)
**Ana menü** — `Network/SessionMenuUI.cs` ✅ (referans). Gerçek sahne (menü kamerası ±3° salınım) + vinyet + soldan perde; solda katalog no,
Display başlık, bordo+pirinç çizgi, slogan, numaralı dizin (01 Tek Oyunculu … 06 Çıkış; odakta bordo çubuk + chevron + açıklama);
sağ üstte dil sekmeleri; sağ altta sanatçı sergi etiketi (isim, renk, kıyafet); altta durum şeridi (meşgul: saat, nabız) + KeyCap ipuçları.
Alt ekranlar: Çok oyunculu (Oda Kur Primary / ya da / kod alanı mono / Katıl), Ayarlar (Ses, Kontroller, Dil, Öğretici), Yapımcılar (Catalog sayfası). Çıkış → Dialog.
**Duraklat** ✅ — perde + vinyet + ortada Plaque (480) + arşiv köşeleri; Devam Et (odaklı Primary), kaydırmalı bölümler, altta Kodu kopyala (Secondary + toast) / Ayrıl (Danger).
**Bağlantı geri bildirimi** ✅ — "Bağlanılıyor" plaket + nabızlı noktalar; `sm.Status` durum şeridi; kick/hata metni durum şeridinde.
**HUD (oda kodu, ping)** ✅ — Plaque, Players ikonu, kod Mono BrassLight; ping rengi Sage/Ochre/DangerLight.
**Görev / hedef HUD** — `UI/ObjectiveHUD.cs`, `Forgery/MuseumSession.cs` (kota paneli): sağ üst Plaque, Section başlık + Body hedef, kota ProgressBar(marker=kota).
**Gizlilik / alarm** — `Stealth/PlayerStealth.cs` (şüphe çubuğu: Ochre→Danger, Eye ikonu), `Stealth/AlarmFeedback.cs`, `Stealth/SecurityManager.cs`
(alarm bandı: TopSlot, Plaque + Danger şerit + Alarm ikonu; kamera kapalı bandı Forest şerit + Camera ikonu), hapis: Jail ikonu.
**Kayıt / alet ipuçları** — `Gameplay/HeistControls.cs` (kayıt rozeti Badge "REC" Danger + Camera ikonu, pil ProgressBar), `Gameplay/PaintBrush.cs`, `Gameplay/SprayPaint.cs`.
**Etkileşim istemleri** — `Interaction/PlayerInteractor.cs`, `ObjectCarrier.cs` (nişangah: 2 px Ivory nokta + hedefte 6 px pirinç halka),
`Gameplay/GarageTravel.cs`, `Gameplay/StaffDoor.cs`, `Gameplay/LockpickSystem.cs` (kilit mini oyunu: Plaque + Lockpick ikonu, pim çubukları ProgressBar),
`Gameplay/PushSystem.cs`, `Social/PingSystem.cs` (dünya işareti: pirinç baklava + mesafe Mono), `Gameplay/VisitorLook.cs`, `Stealth/GuardAI.cs` (kafa üstü ikonları: Eye/Warning/Alarm, renk anlamlı).
**Dünya etiketleri** — `UI/WorldLabel.cs`, `UI/ArtPlaque.cs` (Label kind küçük kart, Charcoal yazı), `Network/PlayerNetworkSetup.cs` (isim: Caption, oyuncu rengi 3 px alt çizgi, gölgesiz).
**Tezgah (tablo/vazo/büst/yüzük)** — `Forgery/WorkbenchUI.cs`, `Forgery/CanvasPainter.cs`, `Forgery/BenchGuide.cs`: sol araç sütunu Tool düğmeleri (Brush, Eraser, Undo…, seçili pirinç),
referans görüntü BrassFrame + Section "ORİJİNAL", çalışma alanı ortada, sağ/alt Sketch panelde benzerlik (Numeric % + ProgressBar marker), ölçü kaydırıcıları temalı skin;
hedef seçimi: 4 Label kartı (Painting/Vase/Bust/Ring ikonu + eser adı + Corners seçili); altbilgi KeyCap ipuçları + Esc "Bitir" Primary. Rehber/öğretici kutu = Sketch.
**Çanta / envanter** — `Inventory/PlayerInventory.cs`: hotbar Slot durumları + kısayol Caption; ağırlık/para HUD Plaque + Bag/Money ikonu + Numeric;
çanta paneli Plaque ızgara + sağda bilgi kartı Label; alet rozetleri Badge.
**Ekonomi** — `Interaction/BlackMarket.cs`, `Interaction/GarageShop.cs`, `Gameplay/TrashBin.cs`: Catalog sayfası (satır: ikon, ad Body, fiyat Numeric Bordeaux, eylem Primary/Danger).
**Brifing** — `Forgery/BriefingTerminal.cs` (BriefingUI): Catalog sayfası; hedef eserler Label kartları + rol/plan numaralı liste (Mono numara).
**Kat planı / BodyCam / canlı yayın** — `Gameplay/FloorPlanUI.cs`, `Gameplay/BodyCam.cs`, `Gameplay/LiveFeed.cs`: plan zemini Forest @90% + Graphite duvar çizgileri,
odalar Ivory hat, eserler pirinç baklava + Caption ad, oyuncular renk noktası, güvenlikler Danger üçgen; yayın BrassFrame + "SİNYAL YOK" tarama (Hatch) + Camera ikonu.
**Lobi / garaj** — `UI/HeistLobbyUI.cs`: sol Plaque, oyuncu satırı (renk, ad, konum Caption, HAZIR Badge Sage / bekliyor Graphite), F3 KeyCap.
**Öğretici** — `UI/TutorialUI.cs`: alt orta Sketch kartı (adım no Mono Terracotta, Title başlık, Body metin, KeyCap'li ipucu), ilerleme noktaları.
**Bildirimler / sohbet** — `Network/ChatUI.cs` (sol alt Plaque, isim oyuncu renginde, alan temalı field); geçici olaylar → `UIUtil.ShowToast`.
**Aşama geçişi** — `UI/ScreenFade.cs`: Charcoal karartma + ortada Section üst satır + Display aşama adı + ornament Divider.
**Kaçış / görev sonu** — `Gameplay/TruckEscapeUI.cs`: ScrimBottom + Truck ikonu; sonuç kartı Catalog (Display başlık, kazanç Numeric, eser listesi Label kartları); yakalandı: Jail + Danger.
**Ses göstergesi** — `Network/VoiceChat.cs` + `Network/VoiceKeywordListener.cs`: **DOKUNMA** (kural). Görünümü UIUtil.Panel/DrawRect üzerinden zaten yeni temaya geçer.
İleride yalnızca çizim değişikliği izinli olursa: DrawMicWidget/DrawMicIcon yerine `Icon(Mic/MicOff/Speaker)`.

## 9. Uygulama grupları (dosya-çakışmasız; hiçbir grup UIUtil.cs / UITheme.cs / SessionMenuUI.cs / Loc.cs'yi DÜZENLEMEZ)
| Grup | Dosyalar |
|---|---|
| 0 ✅ Sistem + menü | `UI/UIUtil.cs`, `UI/UITheme.cs`, `Network/SessionMenuUI.cs`, `Editor/UIAssetPostprocessor.cs`, `Art/UI/Tools/generate_ui.ps1` |
| A Durum & görev HUD | `UI/ObjectiveHUD.cs`, `Forgery/MuseumSession.cs`*, `Stealth/PlayerStealth.cs`*, `Stealth/AlarmFeedback.cs`, `Stealth/SecurityManager.cs`*, `UI/ScreenFade.cs`, `Gameplay/TruckEscapeUI.cs`, `Gameplay/HeistControls.cs` |
| B İstemler & dünya | `Interaction/PlayerInteractor.cs`, `ObjectCarrier.cs`, `Gameplay/GarageTravel.cs`, `Gameplay/StaffDoor.cs`, `Gameplay/LockpickSystem.cs`*, `Gameplay/PushSystem.cs`*, `Social/PingSystem.cs`*, `Gameplay/VisitorLook.cs`, `Stealth/GuardAI.cs`*, `UI/WorldLabel.cs`, `UI/ArtPlaque.cs`, `Network/PlayerNetworkSetup.cs`*, `Gameplay/PaintBrush.cs`*, `Gameplay/SprayPaint.cs`* |
| C Tezgah | `Forgery/WorkbenchUI.cs`, `Forgery/CanvasPainter.cs`, `Forgery/BenchGuide.cs` |
| D Çanta & ekonomi | `Inventory/PlayerInventory.cs`*, `Interaction/BlackMarket.cs`, `Interaction/GarageShop.cs`, `Gameplay/TrashBin.cs` |
| E Kamyon istihbaratı & sosyal | `Gameplay/BodyCam.cs`*, `Gameplay/FloorPlanUI.cs`, `Gameplay/LiveFeed.cs`, `Forgery/BriefingTerminal.cs`, `UI/HeistLobbyUI.cs`, `UI/TutorialUI.cs`, `Network/ChatUI.cs` |

\* = NetworkBehaviour / oynanış dosyası: yalnızca `OnGUI` ve `Draw*` çizim metotlarını değiştir; RPC, NetworkVariable, durum mantığı, girdi akışı aynı kalır.
Yeni metin gerekirse Loc anahtarlarını rapora yaz (Loc.cs'yi tek bir ajan toplu ekler) ya da grup başına ayrı bir sıra ile ekle; `{ "anahtar", new[] { "EN", "TR" } },` biçimi, önce Grep ile tekrar kontrolü.
UIUtil'de eksik yardımcı varsa kendi dosyanda `private static` yaz ve raporda öner.

## 10. Kabul ölçütleri (her ekran)
1. Yalnızca token renkleri; sabit `new Color(...)` yok (oyuncu renkleri hariç). 2. Panel türü kurala uygun; yazı rengi `TextOn(kind)`.
3. Tuş adları KeyCap/`[E]` biçiminde. 4. Liste/menüde klavye odağı + FocusRing. 5. 1280×720 ve 2560×1440'ta taşma yok.
6. Türkçe karakterler (ğüşıöçİ) doğru; büyük harfte `UITheme.Caps`. 7. `Event.current.type == Repaint` dışında doku çizme yok (Panel/Icon zaten kontrol eder).
8. Oynanış davranışı değişmedi; derleme `Editor.log`'da "error CS" yok.

## 11. Yazı tipi önerisi (indirilmedi)
OFL: **Cormorant Garamond SemiBold** veya **Libre Caslon Text Bold** → `Assets/Resources/UI/Fonts/Display.ttf`; **IBM Plex Sans** → `Body.ttf`;
**IBM Plex Mono** → `Mono.ttf`. Üçü de Türkçe glifleri içerir; dosya konunca `UITheme` otomatik kullanır (kod değişikliği gerekmez).
