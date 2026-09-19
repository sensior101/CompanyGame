using CompanyGame.Daldongne;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CompanyGame.World.Maps
{
    /// <summary>Walk within range and press E to enter another map scene.</summary>
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
        static int selectionFrame = -1;
        static float closestDistance;
        PlayerMovement player;
        float nextPlayerLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            focused = null;
            selectionFrame = -1;
            closestDistance = float.PositiveInfinity;
        }

        void Update()
        {
            if (selectionFrame != Time.frameCount)
            {
                selectionFrame = Time.frameCount;
                focused = null;
                closestDistance = float.PositiveInfinity;
            }
            if (SceneLoadManager.IsLoading) return;
            if ((!player || !player.isActiveAndEnabled) && Time.unscaledTime >= nextPlayerLookup)
            {
                nextPlayerLookup = Time.unscaledTime + .5f;
                SceneLoadManager.TryGetScenePlayer(gameObject.scene, out player, out _);
            }
            if (!player || !player.isActiveAndEnabled || !player.walking) return;
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance > interactionRadius * interactionRadius || distance >= closestDistance) return;
            closestDistance = distance;
            focused = this;
        }

        void LateUpdate()
        {
            // Every portal has completed Update, so overlapping portal ranges
            // choose the nearest one, regardless of component update order.
            if (focused != this || SceneLoadManager.IsLoading) return;
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            pressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKeyDown(KeyCode.E);
#endif
            if (pressed) SceneLoadManager.TryLoadMap(targetScenePath, targetSpawnId, player);
        }

        void OnDisable() { if (focused == this) focused = null; }

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
                "[E]  " + displayName, style);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(.3f, .8f, 1f, .9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(1.4f, 2f, .2f));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, "[E] " + displayName);
#endif
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.3f, .8f, 1f, .55f);
            Gizmos.DrawWireSphere(transform.position, interactionRadius);
        }
    }
}
