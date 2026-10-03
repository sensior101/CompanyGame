using UnityEngine;

namespace CompanyGame.Daldongne
{
    /// <summary>
    /// Legacy serialized-type adapter. All movement lives in PlayerMovement.
    /// Kept so older external maps/packages still load; current scenes and
    /// player prefabs use PlayerMovement directly. Never add both components.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class DaldongneVillageWalker : PlayerMovement
    {
    }
}
