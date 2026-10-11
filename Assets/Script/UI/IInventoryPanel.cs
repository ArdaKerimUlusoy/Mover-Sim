/// <summary>
/// Çanta paneli yerine açılan başka paneller (karaborsa, garaj mağazası...).
/// PlayerInventory.OpenExternal(panel) ile açılır; böylece imleç, Esc/Tab ile kapatma ve
/// "panel açıkken hareket etme" kuralları otomatik çalışır.
/// </summary>
public interface IInventoryPanel
{
    /// <summary>OnGUI içinde çağrılır.</summary>
    void DrawPanel(PlayerInventory inventory);

    /// <summary>false dönerse panel kapanır (ör. oyuncu uzaklaştı).</summary>
    bool StillValid(PlayerInventory inventory);
}
