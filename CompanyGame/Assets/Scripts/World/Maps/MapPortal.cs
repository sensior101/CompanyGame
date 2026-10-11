using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>Walk within range and press Space to enter another map scene.</summary>
    [DisallowMultipleComponent]
    public sealed class MapPortal : MonoBehaviour
    {
        [Tooltip("Full Assets/...unity path. Enable this scene in the build scene list.")]
        public string targetScenePath;
        [Tooltip("MapSpawnPoint.spawnId in the destination scene.")]
        public string targetSpawnId = "default";
        public string displayName = "Next map";
        [Min(.5f)] public float interactionRadius = 2.5f;

        static MapPortal focused;
        static readonly HashSet<MapPortal> active = new HashSet<MapPortal>();
        static int selectionFrame = -1;
        static float closestDistance;
        Behaviour player;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            focused = null;
            active.Clear();
            selectionFrame = -1;
            closestDistance = float.PositiveInfinity;
        }

        public static bool HasNearbyPortal(Transform player)
        {
            if (!player) return false;
            foreach (var portal in active)
                if (portal && portal.isActiveAndEnabled && portal.gameObject.scene == SceneLoadManager.CurrentMap &&
                    (portal.transform.position - player.position).sqrMagnitude <= portal.interactionRadius * portal.interactionRadius)
                    return true;
            return false;
        }

        void OnEnable() => active.Add(this);

        void Update()
        {
            if (selectionFrame != Time.frameCount)
            {
                selectionFrame = Time.frameCount;
                focused = null;
                closestDistance = float.PositiveInfinity;
            }
            if (SceneLoadManager.IsLoading || InputFocus.GameplayBlocked()) return;
            player = SceneLoadManager.Traveller;
            if (!player || !player.isActiveAndEnabled) return;
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance > interactionRadius * interactionRadius || distance >= closestDistance) return;
            closestDistance = distance;
            focused = this;
        }

        void LateUpdate()
        {
            // Every portal has completed Update, so overlapping portal ranges
            // choose the nearest one, regardless of component update order.
            if (focused != this || SceneLoadManager.IsLoading || InputFocus.GameplayBlocked()) return;
            if (GameInput.InteractPressed) SceneLoadManager.TryLoadMap(targetScenePath, targetSpawnId, player);
        }

        void OnDisable() { active.Remove(this); if (focused == this) focused = null; }

        void OnValidate()
        {
            targetScenePath = (targetScenePath ?? string.Empty).Trim().Replace('\\', '/');
            targetSpawnId = (targetSpawnId ?? string.Empty).Trim();
            interactionRadius = Mathf.Max(.5f, interactionRadius);
        }

        void OnGUI()
        {
            if (focused != this || SceneLoadManager.IsLoading) return;
            float width = Mathf.Min(480f, Screen.width - 32f);
            var style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect((Screen.width - width) * .5f, Screen.height - 100f, width, 58f),
                "[Space]  " + displayName, style);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(.3f, .8f, 1f, .9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(1.4f, 2f, .2f));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, "[Space] " + displayName);
#endif
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.3f, .8f, 1f, .55f);
            Gizmos.DrawWireSphere(transform.position, interactionRadius);
        }
    }
}
