using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    public enum TransitKind { Subway, Bus }

    /// <summary>Place the boarding point on accessible pavement in front of the facility.</summary>
    [DisallowMultipleComponent]
    public sealed class TransitStop : MonoBehaviour
    {
        public TransitKind kind;
        public Transform boardingPoint;
        [Min(.5f)] public float interactionRadius = 3f;
        [Min(.25f)] public float verticalTolerance = 1.5f;

        static readonly HashSet<TransitStop> activeStops = new HashSet<TransitStop>();

        public Vector3 BoardingPosition => boardingPoint ? boardingPoint.position : transform.position;
        public string Prompt => kind == TransitKind.Subway ? "지하철 타기" : "버스 타기";
        public string ArrivalSpawnId => kind == TransitKind.Subway ? "subway" : "bus";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { activeStops.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterLoadedStops()
        {
            // Also works when Enter Play Mode has both domain and scene reload disabled.
            foreach (var stop in FindObjectsByType<TransitStop>(FindObjectsSortMode.None))
                if (stop.isActiveAndEnabled) activeStops.Add(stop);
        }

        void OnEnable() { activeStops.Add(this); }
        void OnDisable() { activeStops.Remove(this); }

        public bool IsInRange(Transform player)
        {
            if (!player || gameObject.scene != SceneLoadManager.CurrentMap) return false;
            Vector3 offset = player.position - BoardingPosition;
            if (Mathf.Abs(offset.y) > verticalTolerance) return false;
            offset.y = 0f;
            return offset.sqrMagnitude <= interactionRadius * interactionRadius;
        }

        public static TransitStop FindNearest(Transform player)
        {
            TransitStop nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var stop in activeStops)
            {
                if (!stop || !stop.isActiveAndEnabled || !stop.IsInRange(player)) continue;
                float candidate = (stop.BoardingPosition - player.position).sqrMagnitude;
                if (candidate >= distance) continue;
                nearest = stop;
                distance = candidate;
            }
            return nearest;
        }

        void OnValidate()
        {
            interactionRadius = Mathf.Max(.5f, interactionRadius);
            verticalTolerance = Mathf.Max(.25f, verticalTolerance);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == TransitKind.Subway ? new Color(.2f, .9f, .75f) : new Color(.3f, .65f, 1f);
            Gizmos.DrawWireSphere(BoardingPosition, interactionRadius);
            Gizmos.DrawRay(BoardingPosition, Vector3.up * verticalTolerance);
        }
    }
}
