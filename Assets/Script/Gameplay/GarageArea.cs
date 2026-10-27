using UnityEngine;

/// <summary>
/// Ayrı "Garage" sahnesindeki garajın sınırı (GameFlow.PhaseAt garajı buradan tanır).
/// Tools/Sahte Şaheser/Garajı Ayrı Sahneye Taşı aracı kurar. Sahnede yoksa eski düzen (garaj ana sahnede) aynen çalışır.
/// </summary>
public class GarageArea : MonoBehaviour
{
    public static GarageArea Instance { get; private set; }

    [Tooltip("Dünya koordinatında garajın merkezi ve boyu (zemin + duvarlar, biraz payla).")]
    [SerializeField] private Vector3 center;
    [SerializeField] private Vector3 size = new Vector3(15f, 6f, 13f);

    public Bounds WorldBounds => new Bounds(center, size);

    public void Setup(Bounds b)
    {
        center = b.center;
        size = b.size;
    }

    public bool Contains(Vector3 p) => WorldBounds.Contains(p);

    private void OnEnable() => Instance = this;

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.5f);
        Gizmos.DrawWireCube(center, size);
    }
}
