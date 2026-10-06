using UnityEngine;

/// <summary>
/// İsteğe bağlı: Taşınabilir objeye ekranda görünecek bir isim vermek
/// veya kaldırılmasını geçici olarak kapatmak için kullanılır.
/// Bu bileşen olmadan da Rigidbody'si olan her obje (kütle sınırının altındaysa) kaldırılabilir.
/// Kütleyi Rigidbody > Mass alanından ayarla: ağır objeler oyuncuyu yavaşlatır.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Pickupable : MonoBehaviour
{
    [SerializeField] private string displayName = "";
    [SerializeField] private bool canBePickedUp = true;

    public string DisplayName => displayName;
    public bool CanBePickedUp
    {
        get => canBePickedUp;
        set => canBePickedUp = value;
    }
}
