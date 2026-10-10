using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>Marks a staircase of a multi-floor interior; the UI offers floor moves while the player is near.</summary>
    [DisallowMultipleComponent]
    public sealed class FloorStairs : MonoBehaviour
    {
        public int floor = 1;
        public int topFloor = 4;
        public bool allowSpace;
        [Tooltip("Optional separate upper-floor map. Empty keeps the existing in-scene floor transfer.")]
        public string targetScenePath;
        public string targetSpawnId = "stairs_arrival";
        [Min(.5f)] public float radius = 2f;
        const float HeightTolerance = 1.5f;
        static readonly HashSet<FloorStairs> active = new HashSet<FloorStairs>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterLoaded()
        {
            foreach(var stairs in FindObjectsByType<FloorStairs>())
                if(stairs.isActiveAndEnabled)active.Add(stairs);
        }
        void OnEnable() { active.Add(this); }
        void OnDisable() { active.Remove(this); }
        public bool IsInRange(Transform player)
        {
            if (!player || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
            Vector3 d = player.position - transform.position;
            return Mathf.Abs(d.y) <= HeightTolerance && d.x*d.x+d.z*d.z <= radius*radius;
        }
        public static FloorStairs FindNearest(Transform player)
        {
            FloorStairs nearest = null; float best = float.PositiveInfinity;
            foreach(var stairs in active)
            {
                if (!stairs || !stairs.IsInRange(player)) continue;
                float d = (stairs.transform.position-player.position).sqrMagnitude;
                if(d >= best) continue; best=d; nearest=stairs;
            }
            return nearest;
        }
        void OnDrawGizmosSelected()
        { Gizmos.color=Color.green; Gizmos.DrawWireSphere(transform.position, radius); }
    }
}
