using UnityEngine;

/// <summary>Oyunun aşamaları: ana ekran, garaj / lobi, kamyon, müze. Aynı sahne içinde konuma göre belirlenir.</summary>
public enum GamePhase { Menu, Garage, Truck, Museum }

/// <summary>
/// Yerel oyuncunun hangi aşamada olduğunu söyleyen ortak yardımcı (ScreenFade, ObjectiveHUD, TutorialUI, HeistLobbyUI kullanır).
/// Sadece mevcut durumları OKUR: TruckZone (kamyon), MuseumLayout.PlanBounds (müze), geri kalanı garaj.
/// </summary>
public static class GameFlow
{
    private static int cacheFrame = -1;
    private static GamePhase cachePhase;

    /// <summary>Yerel oyuncu doğdu mu (menüden oyuna geçildi mi)?</summary>
    public static bool LocalSpawned
    {
        get
        {
            PlayerStealth me = PlayerStealth.Local;
            return me != null && me.IsSpawned;
        }
    }

    /// <summary>Yerel oyuncunun aşaması (karede bir kez hesaplanır). Oyuncu yoksa Menu.</summary>
    public static GamePhase Phase
    {
        get
        {
            if (cacheFrame == Time.frameCount) return cachePhase;
            cacheFrame = Time.frameCount;
            PlayerStealth me = PlayerStealth.Local;
            cachePhase = me != null && me.IsSpawned ? PhaseAt(me.transform.position) : GamePhase.Menu;
            return cachePhase;
        }
    }

    /// <summary>Dünya konumuna göre aşama: önce kamyon, sonra müze (kat planı sınırı), kalanı garaj.
    /// Garaj ayrı sahnedeyse (GarageArea) garaj kendi sınırından tanınır; müzenin dışındaki sokak da müze aşaması sayılır.</summary>
    public static GamePhase PhaseAt(Vector3 pos)
    {
        GarageArea ga = GarageArea.Instance;
        if (ga != null && ga.Contains(pos)) return GamePhase.Garage;

        TruckZone tz = TruckZone.Instance;
        if (tz != null && tz.Contains(pos)) return GamePhase.Truck;

        MuseumLayout ml = MuseumLayout.Instance;
        if (ml != null)
        {
            Rect b = ml.PlanBounds;
            if (b.width > 1f && b.height > 1f)
            {
                b.xMin -= 1.5f; b.yMin -= 1.5f; b.xMax += 1.5f; b.yMax += 1.5f;
                if (b.Contains(new Vector2(pos.x, pos.z))) return GamePhase.Museum;
            }
        }
        return ga != null ? GamePhase.Museum : GamePhase.Garage;
    }

    /// <summary>Odada yalnızca ben mi varım (tek oyunculu ya da henüz kimse katılmadı)?</summary>
    public static bool Solo => PlayerNetworkSetup.All.Count <= 1;

    /// <summary>Tek oyunculu mod seçilmiş mi?</summary>
    public static bool SinglePlayerMode => SessionManager.Instance != null && SessionManager.Instance.IsSinglePlayer;

    /// <summary>Aşama ve görev seviyesi için ekranda yazılacak başlık (ör. "MÜZE — Seviye 2"). Müze hazır değilse seviye yazılmaz.</summary>
    public static string StageTitle(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Museum:
            {
                MuseumSession ms = MuseumSession.Instance;
                return ms != null && ms.Ready ? Loc.T("stage.museum", ms.Level) : Loc.T("stage.museum.plain");
            }
            case GamePhase.Truck: return Loc.T("stage.truck");
            case GamePhase.Garage: return Loc.T("stage.garage");
            default: return "";
        }
    }

    /// <summary>Başlığın altına yazılan kısa açıklama.</summary>
    public static string StageSubtitle(GamePhase phase)
    {
        MuseumSession ms = MuseumSession.Instance;
        switch (phase)
        {
            case GamePhase.Museum:
                return ms != null && ms.Ready ? Loc.T("stage.museum.sub", ms.Theme.name, ms.Theme.City) : Loc.T("stage.museum.sub.none");
            case GamePhase.Truck: return Loc.T("stage.truck.sub");
            case GamePhase.Garage: return Loc.T("stage.garage.sub");
            default: return "";
        }
    }
}
