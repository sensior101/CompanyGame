using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>Two leaves slide away from the centre while the persistent player is near.</summary>
    [DisallowMultipleComponent]
    public sealed class LobbyAutomaticDoor : MonoBehaviour
    {
        public Transform leftLeaf;
        public Transform rightLeaf;
        [Min(1f)] public float openRadius = 3.2f;
        [Min(1f)] public float closeRadius = 4.2f;
        [Min(.2f)] public float slideDistance = 1.55f;
        [Min(.1f)] public float speed = 3.5f;
        [Tooltip("False preserves normal proximity operation. A lift can explicitly control and interlock the doors.")]
        public bool externallyControlled;
        [Tooltip("Optional trigger box covering the doorway, for obstruction checks while closing.")]
        public BoxCollider obstructionZone;

        Vector3 leftClosed;
        Vector3 rightClosed;
        float openness;
        bool initialized, externalOpen, lockedClosed;
        Vector3 clearanceCenter, clearanceSize;

        public bool IsFullyClosed => initialized && openness <= .0001f;
        public bool IsFullyOpen => initialized && openness >= .9999f;
        public bool IsBlocked { get; private set; }
        public bool HasSolidLeaves => HasSolidCollider(leftLeaf) && HasSolidCollider(rightLeaf);

        void Awake() => Initialize();

        void Initialize()
        {
            if (initialized || !leftLeaf || !rightLeaf) return;
            if (leftLeaf) leftClosed = leftLeaf.localPosition;
            if (rightLeaf) rightClosed = rightLeaf.localPosition;
            bool first = true;
            Bounds bounds = default;
            foreach (var leaf in new[] { leftLeaf, rightLeaf })
                foreach (var col in leaf.GetComponentsInChildren<Collider>(true))
                {
                    if (col.isTrigger) continue;
                    var box = col.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = transform.InverseTransformPoint(new Vector3((i & 1) == 0 ? box.min.x : box.max.x,
                            (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z));
                        if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                    }
                }
            clearanceCenter = first ? new Vector3(0, 1.2f, 0) : bounds.center;
            clearanceSize = first ? new Vector3(2.8f, 2.4f, .7f) : bounds.size + new Vector3(.08f, .04f, .7f);
            initialized = true;
        }

        public void SetExternalState(bool open, bool lockClosed)
        {
            Initialize();
            externallyControlled = true;
            externalOpen = open;
            lockedClosed = lockClosed;
        }

        static bool HasSolidCollider(Transform leaf)
        {
            if (!leaf) return false;
            foreach (var col in leaf.GetComponentsInChildren<Collider>(true))
                if (col.enabled && !col.isTrigger) return true;
            return false;
        }

        bool DoorwayOccupied()
        {
            var space = obstructionZone ? obstructionZone.transform : transform;
            Vector3 center = obstructionZone ? obstructionZone.center : clearanceCenter;
            Vector3 size = obstructionZone ? obstructionZone.size : clearanceSize;
            Vector3 scale = space.lossyScale;
            Vector3 half = Vector3.Scale(size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            foreach (var hit in Physics.OverlapBox(space.TransformPoint(center), half, space.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit || hit.transform.IsChildOf(transform)) continue;
                var motor = hit as CharacterController ?? hit.GetComponentInParent<CharacterController>();
                if (motor && motor.enabled && motor.gameObject.activeInHierarchy) return true;
                if (hit.attachedRigidbody && !hit.attachedRigidbody.isKinematic) return true;
            }
            return false;
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float elapsed)
        {
            if (!leftLeaf || !rightLeaf) return;
            Initialize();
            var traveller = SceneLoadManager.Traveller;
            float distance = traveller ? Vector3.Distance(traveller.transform.position, transform.position) : float.PositiveInfinity;
            float target = externallyControlled ? externalOpen && !lockedClosed ? 1f : 0f :
                distance <= (openness > .01f ? closeRadius : openRadius) ? 1f : 0f;
            IsBlocked = target < .5f && !lockedClosed && DoorwayOccupied();
            if (IsBlocked) target = 1;
            float worldDistance = slideDistance * (leftLeaf.parent ? leftLeaf.parent.TransformVector(Vector3.right).magnitude : 1f);
            openness = Mathf.MoveTowards(openness, target, speed * Mathf.Max(0, elapsed) / Mathf.Max(.01f, worldDistance));
            leftLeaf.localPosition = leftClosed + Vector3.left * slideDistance * openness;
            rightLeaf.localPosition = rightClosed + Vector3.right * slideDistance * openness;
        }
    }
}
