using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Oyun başlarken NavMesh'i (güvenliklerin yürüyebileceği alan) oluşturur.
/// Aynı objede NavMeshSurface olmalı. Böylece NavMesh dosyasını elle bake edip commit'lemeye gerek kalmaz.
/// </summary>
[DefaultExecutionOrder(-500)]
[RequireComponent(typeof(NavMeshSurface))]
public class RuntimeNavMeshBaker : MonoBehaviour
{
    public static bool Ready { get; private set; }

    private void Awake()
    {
        Ready = false;
        NavMeshSurface surface = GetComponent<NavMeshSurface>();
        surface.BuildNavMesh();
        Ready = true;
    }

    private void OnDestroy()
    {
        Ready = false;
    }
}
