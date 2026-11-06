using UnityEngine;

/// <summary>
/// Marks a prop GameObject placed by the Game Grid item brush so items can be
/// found and erased by cell coordinates.
/// </summary>
public sealed class GameGridItemInstance : MonoBehaviour
{
    public Vector3Int cell;
    public GameObject prefab;
}
