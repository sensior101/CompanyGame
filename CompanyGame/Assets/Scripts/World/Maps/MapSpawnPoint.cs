using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompanyGame.World.Maps
{
    /// <summary>A named arrival/reset location. IDs are unique within one scene.</summary>
    [DisallowMultipleComponent]
    public sealed class MapSpawnPoint : MonoBehaviour
    {
        [Tooltip("Stable ID referenced by an incoming MapPortal. Unique within this scene.")]
        public string spawnId = "default";

        public static bool TryFind(Scene scene, string id, out MapSpawnPoint spawnPoint, out string error)
        {
            spawnPoint = null;
            error = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "The destination scene is not loaded.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                error = "The destination spawn ID is empty.";
                return false;
            }
            foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<MapSpawnPoint>(true))
            {
                if (!string.Equals(candidate.spawnId, id, StringComparison.Ordinal)) continue;
                if (spawnPoint)
                {
                    spawnPoint = null;
                    error = "Map '" + scene.name + "' has duplicate spawn ID '" + id + "'.";
                    return false;
                }
                spawnPoint = candidate;
            }
            if (spawnPoint && spawnPoint.isActiveAndEnabled) return true;
            spawnPoint = null;
            error = "Map '" + scene.name + "' has no active spawn point with ID '" + id + "'.";
            return false;
        }

        void OnValidate() { spawnId = (spawnId ?? string.Empty).Trim(); }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(.15f, 1f, .55f, .9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * .9f, .35f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
            Vector3 origin = transform.position + Vector3.up * .15f;
            Gizmos.DrawRay(origin, transform.forward * 1.3f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, "Spawn: " + spawnId);
#endif
        }
    }
}
