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

        Vector3 leftClosed;
        Vector3 rightClosed;
        float openness;

        void Awake()
        {
            if (leftLeaf) leftClosed = leftLeaf.localPosition;
            if (rightLeaf) rightClosed = rightLeaf.localPosition;
        }

        void Update()
        {
            if (!leftLeaf || !rightLeaf) return;
            var traveller = SceneLoadManager.Traveller;
            float distance = traveller ? Vector3.Distance(traveller.transform.position, transform.position) : float.PositiveInfinity;
            float target = distance <= (openness > .01f ? closeRadius : openRadius) ? 1f : 0f;
            openness = Mathf.MoveTowards(openness, target, speed * Time.deltaTime / slideDistance);
            leftLeaf.localPosition = leftClosed + Vector3.left * slideDistance * openness;
            rightLeaf.localPosition = rightClosed + Vector3.right * slideDistance * openness;
        }
    }
}
