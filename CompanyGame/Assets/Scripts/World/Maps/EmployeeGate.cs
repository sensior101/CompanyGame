using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>One card reader lane. Closed glass wings physically block passage.</summary>
    [DisallowMultipleComponent]
    public sealed class EmployeeGate : MonoBehaviour
    {
        public const string CardItemId = "handae_employee_card";
        public Transform leftWing;
        public Transform rightWing;
        public Renderer[] ledRenderers;
        [Min(0f)] public float wingSlideDistance = .62f;
        public Collider passageBlocker;
        public float openSeconds = 4f;
        public float feedbackSeconds = 2f;
        [Min(.2f)] public float exitApproachDistance = 1.8f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly Color Blue = new Color(.05f, .46f, 1f);
        static readonly Color Green = new Color(.05f, 1f, .32f);
        static readonly Color Red = new Color(1f, .06f, .07f);
        Vector3 leftClosed;
        Vector3 rightClosed;
        float openUntil;
        float feedbackUntil;
        bool success;
        bool wasNearBarrier;
        MaterialPropertyBlock block;

        void Awake()
        {
            if (leftWing) leftClosed = leftWing.localPosition;
            if (rightWing) rightClosed = rightWing.localPosition;
            if (passageBlocker) passageBlocker.enabled = true;
            block = new MaterialPropertyBlock();
            SetLed(Blue);
        }

        void Update()
        {
            var traveller = SceneLoadManager.Traveller;
            Vector3 approach = traveller ? transform.InverseTransformPoint(traveller.transform.position) : Vector3.zero;
            float approachHalfWidth = passageBlocker ? passageBlocker.bounds.extents.x + .1f : .65f;
            // Local +Z faces the secured staff area. Only that side grants automatic exit.
            bool nearExit = traveller && Mathf.Abs(approach.x) < approachHalfWidth &&
                approach.z > .15f && approach.z <= exitApproachDistance && Mathf.Abs(approach.y) < 2.5f;
            if (nearExit) Show(true);
            bool acceptedOpen = Time.time < openUntil;
            float travel = Mathf.Max(0f, wingSlideDistance);
            Vector3 leftOpen = leftClosed + Vector3.left * travel;
            Vector3 rightOpen = rightClosed + Vector3.right * travel;
            if (leftWing) leftWing.localPosition = Vector3.MoveTowards(leftWing.localPosition, acceptedOpen ? leftOpen : leftClosed, Time.deltaTime * 1.8f);
            if (rightWing) rightWing.localPosition = Vector3.MoveTowards(rightWing.localPosition, acceptedOpen ? rightOpen : rightClosed, Time.deltaTime * 1.8f);

            // Keep the full-height barrier in place until both leaves finish retracting.
            // Missing wings or zero travel fail closed; expired access blocks immediately.
            bool passageClear = acceptedOpen && travel > .01f && leftWing && rightWing &&
                (leftWing.localPosition - leftOpen).sqrMagnitude <= .0001f &&
                (rightWing.localPosition - rightOpen).sqrMagnitude <= .0001f;
            if (passageBlocker) passageBlocker.enabled = !passageClear;

            bool nearBarrier = traveller && Mathf.Abs(approach.x) < approachHalfWidth &&
                Mathf.Abs(approach.z) < .85f && Mathf.Abs(approach.y) < 2.5f;
            if (nearBarrier && !wasNearBarrier && Time.time >= openUntil)
                Show(false);
            wasNearBarrier = nearBarrier;
            SetLed(acceptedOpen ? Green : Time.time < feedbackUntil ? (success ? Green : Red) : Blue);
        }

        void OnDisable()
        {
            openUntil = 0f;
            if (passageBlocker) passageBlocker.enabled = true;
        }

        void Show(bool accepted)
        {
            success = accepted;
            feedbackUntil = Time.time + feedbackSeconds;
            openUntil = accepted ? Time.time + openSeconds : 0f;
            if (!accepted && passageBlocker) passageBlocker.enabled = true;
        }

        public bool TryUse(ItemData heldItem)
        {
            bool accepted = heldItem && heldItem.itemId == CardItemId;
            Show(accepted);
            return accepted;
        }

        void SetLed(Color color)
        {
            if (ledRenderers == null) return;
            block.SetColor(BaseColor, color);
            block.SetColor(EmissionColor, color * 3f);
            foreach (var renderer in ledRenderers)
                if (renderer) renderer.SetPropertyBlock(block);
        }

        /// <summary>Called by the existing left-click owner before a punch or item use.</summary>
        public static bool TryHandleClick(Camera camera, Transform player)
        {
            if (!camera || !player || !GameInput.AttackPressed) return false;
            var ray = camera.ViewportPointToRay(new Vector3(.5f, .5f));
            var hits = Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (!hit.collider || hit.transform.IsChildOf(player)) continue;
                var gate = hit.collider.GetComponentInParent<EmployeeGate>();
                if (!gate) return false;
                if (Vector3.Distance(player.position, gate.transform.position) > 3.1f) return false;
                var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
                var stack = inventory?.GetSlot(inventory.SelectedHotbarIndex);
                gate.TryUse(stack != null && !stack.IsEmpty ? stack.Item : null);
                return true;
            }
            return false;
        }
    }
}
