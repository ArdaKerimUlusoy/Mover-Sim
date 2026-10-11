using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Sahibi (owner) kim ise pozisyonu o gönderir.
/// Oyuncu kendi karakterini kendi bilgisayarında hareket ettirdiği için gecikme hissedilmez.
/// Taşınabilir objelerde de kaldıran oyuncu sahip olur, böylece taşıma pürüzsüz olur.
/// </summary>
[DisallowMultipleComponent]
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
