/// <summary>
/// BİRLEŞTİRME ANAHTARLARI (23 Ekim 2026). İki ekip aynı fikirleri ayrı ayrı yazdı; örtüşen kısımlarda biri seçildi.
/// Seçilmeyen tarafın kodu SİLİNMEDİ, sadece buradan kapatıldı: true yapınca geri açılır (ikisini birden açmak
/// aynı tuşa iki sistem bağlar, şüpheyi iki kez doldurur).
///  - Kayıt kamerası + kamyon monitörü + kat planı + soluk etiketler + "eser başında dikilme": BodyCam (açık).
///    Arkadaşın karşılıkları: PlayerStealth.Camcorder (H kaydı, kayıt ışığı, dikilme), LiveFeed, ArtNames (soluk tabela).
///  - Boya / sahte kapı / lens: fırça (PaintBrush + SprayPaint, B). Arkadaşın karşılığı: HeistControls T spreyi.
///    Kapılar arkadaşın HidingSpot sunucu altyapısını (ServerPaint / ServerErase) kullanır; boya kutusu sayacı ortak (sprayCount).
///  - Arkadaşın örtüşmeyen yenilikleri açık: M ile kat planı (FloorPlanUI, kamyonda), kalitesiz sahtenin fark edilmesi,
///    SecurityManager kör kamera altyapısı, kamera pili upgrade'i (BodyCam pili bunu kullanır).
/// </summary>
public static class FeatureMerge
{
    /// <summary>Arkadaşın H kayıt kamerası (PlayerStealth.Camcorder): kayıt, kayıt ışığı, pil, eser başında dikilme şüphesi.</summary>
    public static readonly bool FriendCamcorder = false;
    /// <summary>Arkadaşın T boya spreyi (HeistControls): lense / duvara basılı tutarak sık.</summary>
    public static readonly bool FriendSpray = false;
    /// <summary>Arkadaşın canlı yayını (LiveFeed): kamyondaki ekran + atölyede orijinal yerine yayın.</summary>
    public static readonly bool FriendLiveFeed = false;
    /// <summary>Arkadaşın soluk tabelaları (ArtNames). Kapalıyken eser adları BodyCam'in soluk etiketlerini izler.</summary>
    public static readonly bool FriendFadedNames = false;
}
