using UnityEngine;

/// <summary>
/// Sağ üstte o anki HEDEF (aşamaya ve role göre) ve bağlama göre tuş ipuçları.
/// Mevcut durumları okur: GameFlow.Phase, BodyCam, MuseumSession, SecurityManager, PlayerStealth, WorkbenchUI.
/// Kendini kurar; sahneye bir şey eklemek gerekmez.
/// </summary>
public class ObjectiveHUD : MonoBehaviour
{
    private static ObjectiveHUD instance;
    private GUIStyle title, body, keys;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("ObjectiveHUD");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ObjectiveHUD>();
    }

    /// <summary>Hedef metninin çeviri anahtarı + varsa argümanlar; ipucu anahtarı.</summary>
    private void Evaluate(out string objKey, out object[] objArgs, out string hintKey)
    {
        objArgs = null;
        PlayerStealth me = PlayerStealth.Local;
        MuseumSession ms = MuseumSession.Instance;
        GamePhase phase = GameFlow.Phase;
        bool solo = GameFlow.Solo;

        if (me != null && me.IsJailed) { objKey = "obj.jail"; hintKey = "hint.jail"; return; }
        if (ms != null && ms.SequenceActive) { objKey = "obj.sequence"; hintKey = ""; return; }
        if (ms == null || !ms.Ready) { objKey = "obj.wait"; hintKey = ""; return; }

        SecurityManager sec = SecurityManager.Instance;
        bool alarm = sec != null && sec.AlarmActive;

        if (ms.QuotaMet)
        {
            if (phase == GamePhase.Truck) { objKey = "obj.close"; hintKey = "hint.truck"; }
            else { objKey = "obj.return"; hintKey = "hint.museum"; }
            return;
        }

        objArgs = new object[] { Mathf.Max(0, ms.Earned), ms.Quota };

        switch (phase)
        {
            case GamePhase.Garage:
                objKey = "obj.garage"; hintKey = "hint.garage";
                break;
            case GamePhase.Truck:
                if (WorkbenchUI.IsOpen) { objKey = "obj.draw"; hintKey = "hint.workbench"; }
                else if (BodyCam.Live && !BodyCam.LocalRecording) { objKey = "obj.truck.live"; hintKey = "hint.truck"; }
                else { objKey = solo ? "obj.truck.solo" : "obj.truck.team"; hintKey = "hint.truck"; }
                break;
            case GamePhase.Museum:
                hintKey = "hint.museum";
                if (alarm) objKey = "obj.alarm";
                else if (BodyCam.LocalRecording) objKey = "obj.recording";
                else objKey = solo ? "obj.museum.solo" : "obj.museum.team";
                break;
            default:
                objKey = "obj.wait"; hintKey = "";
                break;
        }
    }

    private void OnGUI()
    {
        if (!GameFlow.LocalSpawned || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BriefingUI.IsOpen || FloorPlanUI.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            title.normal.textColor = UIUtil.Gold;
            body = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
            body.normal.textColor = UIUtil.Cream;
            keys = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            keys.normal.textColor = UIUtil.Muted;
        }

        Evaluate(out string objKey, out object[] args, out string hintKey);
        string obj = args != null ? Loc.T(objKey, args) : Loc.T(objKey);
        string hint = string.IsNullOrEmpty(hintKey) ? "" : Loc.T(hintKey);

        float w = 320f;
        float bodyH = body.CalcHeight(new GUIContent(obj), w - 30f);
        float hintH = hint.Length > 0 ? keys.CalcHeight(new GUIContent(hint), w - 30f) : 0f;
        // Sağ üst köşe: ping ve para kutularının altına dizilir (çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopRight, UIUtil.HudOrder.Objective, w, 30f + bodyH + hintH + 12f);

        UIUtil.Panel(r, UIUtil.Gold);
        UIUtil.Tape(r);
        GUI.Label(new Rect(r.x + 16f, r.y + 6f, w - 30f, 20f), Loc.T("obj.title"), title);
        GUI.Label(new Rect(r.x + 16f, r.y + 25f, w - 30f, bodyH), obj, body);
        if (hintH > 0f) GUI.Label(new Rect(r.x + 16f, r.y + 28f + bodyH, w - 30f, hintH), hint, keys);
    }
}
