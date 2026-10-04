using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>A box-shaped ownable area (room or land). Ownership lives in PropertyRegistry, keyed by propertyId.</summary>
    [DisallowMultipleComponent]
    public sealed class PropertyZone : MonoBehaviour
    {
        public string propertyId;
        public string displayName;
        /// <summary>Box size; the bottom face center is transform.position.</summary>
        public Vector3 size = new Vector3(3f, 2.6f, 3f);
        /// <summary>Test owner seeded on first sight. Empty = unowned.</summary>
        public string seedOwner;
        static readonly HashSet<PropertyZone> active = new HashSet<PropertyZone>();
        public static IReadOnlyCollection<PropertyZone> Active => active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterLoaded()
        {
            foreach(var zone in FindObjectsByType<PropertyZone>())
                if(zone.isActiveAndEnabled)active.Add(zone);
        }
        void OnEnable() { active.Add(this); }
        void OnDisable() { active.Remove(this); }
        public bool Contains(Vector3 worldPos)
        {
            if (!isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
            Vector3 p = transform.InverseTransformPoint(worldPos);
            return Mathf.Abs(p.x) <= size.x*.5f && Mathf.Abs(p.z) <= size.z*.5f && p.y >= 0f && p.y <= size.y;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color=Color.yellow; Gizmos.matrix=transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f,size.y*.5f,0f), size);
        }
    }
}
