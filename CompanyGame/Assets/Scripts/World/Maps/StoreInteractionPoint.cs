using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    public enum StoreAction { Door, Shopkeeper }

    /// <summary>A scene-local door or customer-side position in front of a clerk.</summary>
    [DisallowMultipleComponent]
    public sealed class StoreInteractionPoint : MonoBehaviour
    {
        public StoreAction action;
        public string prompt = "들어가기";
        public string targetScenePath;
        public string targetSpawnId;
        [Min(.1f)] public float radius = 1.4f;
        [Min(.1f)] public float heightTolerance = 1f;
        public StoreOffer[] offers = System.Array.Empty<StoreOffer>();
        static readonly HashSet<StoreInteractionPoint> active = new HashSet<StoreInteractionPoint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { active.Clear(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterLoaded()
        {
            foreach(var point in FindObjectsByType<StoreInteractionPoint>(FindObjectsSortMode.None))
                if(point.isActiveAndEnabled)active.Add(point);
        }
        void OnEnable() { active.Add(this); }
        void OnDisable() { active.Remove(this); }
        public bool IsInRange(Transform player)
        {
            if (!player || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
            Vector3 d = player.position - transform.position;
            return Mathf.Abs(d.y) <= heightTolerance && d.x*d.x+d.z*d.z <= radius*radius;
        }
        public static StoreInteractionPoint FindNearest(Transform player)
        {
            StoreInteractionPoint nearest = null; float best = float.PositiveInfinity;
            foreach(var point in active)
            {
                if (!point || !point.IsInRange(player)) continue;
                float d = (point.transform.position-player.position).sqrMagnitude;
                if(d >= best) continue; best=d; nearest=point;
            }
            return nearest;
        }
        void OnDrawGizmosSelected()
        { Gizmos.color=Color.cyan; Gizmos.DrawWireSphere(transform.position, radius); }
    }

    [System.Serializable]
    public sealed class StoreOffer
    {
        public ItemData item;
        [Min(1)] public int price;
    }
}
