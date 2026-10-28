using UnityEngine;

/// <summary>
/// Written by CitizenNavBuilder next to the baked NavMesh: points on walkable ground that a person can really
/// reach (islands of useful size that contain a spawn point or touch ground level). The spawner draws start points
/// from here, so roof islands and tiny scraps are never used and no runtime path search is needed.
/// </summary>
public sealed class CitizenNavInfo : MonoBehaviour
{
    public Vector3[] starts = System.Array.Empty<Vector3>();
}
