using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>A reusable, physically moving lift. Floor UI and the persistent player remain owned by their existing systems.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-150)]
    public sealed class ElevatorController : MonoBehaviour
    {
        [Serializable]
        public sealed class Floor
        {
            public string label;
            [Tooltip("Cabin root position at this stop; all stops share the same world X/Z.")]
            public Transform stop;
            public Transform callPoint;
            public LobbyAutomaticDoor landingDoor;
        }

        public Transform cabin;
        [Tooltip("Trigger volume strictly inside the cabin. Its lower face is the cabin floor.")]
        public BoxCollider cabinArea;
        public LobbyAutomaticDoor cabinDoor;
        public Floor[] floors = Array.Empty<Floor>();
        [Min(0)] public int initialFloor;
        [Min(.1f)] public float speed = 2f;
        [Min(.1f)] public float acceleration = 2f;
        [Min(.5f)] public float callRadius = 1.6f;
        [Min(.2f)] public float openSeconds = 2.5f;

        enum MotionState { Idle, Opening, Open, Closing, Moving }
        MotionState state;
        readonly Queue<int> requests = new Queue<int>();
        static readonly HashSet<ElevatorController> active = new HashSet<ElevatorController>();
        Collider[] cabinSolids = Array.Empty<Collider>();
        float velocity, openUntil;
        bool ready, suspended, obstructed;
        int currentFloor, targetFloor;

        public int FloorCount => floors == null ? 0 : floors.Length;
        public int CurrentFloorIndex => currentFloor;
        public int TargetFloorIndex => targetFloor;
        public bool IsMoving => state == MotionState.Moving;
        public bool IsReady => ready;
        public string StatusText => !ready ? "엘리베이터를 사용할 수 없습니다." :
            !GameSession.IsAuthority ? "현재 클라이언트의 승강기 요청은 지원되지 않습니다." :
            obstructed ? "통로가 막혀 있습니다. 문과 승강실 안쪽을 확인해주세요." :
            IsMoving ? GetFloorLabel(targetFloor) + "으로 이동 중" :
            state == MotionState.Closing ? "문이 닫힙니다." : GetFloorLabel(currentFloor) + " · 대기 중";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterLoaded()
        {
            foreach (var lift in FindObjectsByType<ElevatorController>())
                if (lift.isActiveAndEnabled) active.Add(lift);
        }
        void OnEnable() => active.Add(this);
        void Awake() => Initialize();

        void Initialize()
        {
            ready = false;
            if (!cabin || !cabinArea || !cabinArea.isTrigger || !cabinArea.transform.IsChildOf(cabin) ||
                !cabinDoor || !cabinDoor.transform.IsChildOf(cabin) || FloorCount < 2 ||
                initialFloor < 0 || initialFloor >= FloorCount)
            { Debug.LogError("[Elevator] Cabin, trigger volume, cabin door and at least two stops are required.", this); return; }
            for (int i = 0; i < FloorCount; i++)
            {
                var floor = floors[i];
                if (floor == null || !floor.stop || !floor.callPoint || !floor.landingDoor ||
                    !floor.landingDoor.HasSolidLeaves || floor.landingDoor.transform.IsChildOf(cabin) ||
                    (i > 0 && (floor.stop.position.y <= floors[i - 1].stop.position.y ||
                        Vector2.Distance(new Vector2(floor.stop.position.x, floor.stop.position.z),
                            new Vector2(floors[0].stop.position.x, floors[0].stop.position.z)) > .01f)))
                { Debug.LogError("[Elevator] Each ascending stop needs its own solid landing doors and a call point, on the same shaft X/Z.", this); return; }
                for (int j = 0; j < i; j++)
                    if (floor.landingDoor == floors[j].landingDoor || floor.stop == floors[j].stop)
                    { Debug.LogError("[Elevator] Stops and landing doors must be distinct.", this); return; }
            }
            if (!cabinDoor.HasSolidLeaves)
            { Debug.LogError("[Elevator] Both cabin door leaves require solid colliders.", this); return; }
            var solids = new List<Collider>();
            foreach (var col in cabin.GetComponentsInChildren<Collider>(true))
                if (!col.isTrigger) solids.Add(col);
            cabinSolids = solids.ToArray();
            if (cabinSolids.Length < 3)
            { Debug.LogError("[Elevator] Cabin floor/walls and door collision are required.", this); return; }
            currentFloor = targetFloor = initialFloor;
            if (Vector3.Distance(cabin.position, floors[initialFloor].stop.position) > .02f)
            { Debug.LogError("[Elevator] Author the cabin at its initial floor stop.", this); return; }
            requests.Clear(); velocity = 0; state = MotionState.Idle; ready = true;
            SetDoors(false, true);
        }

        public string GetFloorLabel(int index) => index >= 0 && index < FloorCount && floors[index] != null &&
            !string.IsNullOrWhiteSpace(floors[index].label) ? floors[index].label : (index + 1) + "층";

        public bool TryGetInteraction(Transform traveller, out bool inside, out int callFloor)
        {
            inside = false; callFloor = -1;
            if (!ready || !traveller || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap || SceneLoadManager.IsLoading) return false;
            var motor = traveller.GetComponent<CharacterController>();
            inside = motor && ContainsPassenger(motor, false);
            if (inside) return true;
            float best = callRadius * callRadius;
            for (int i = 0; i < FloorCount; i++)
            {
                Vector3 offset = traveller.position - floors[i].callPoint.position;
                if (Mathf.Abs(offset.y) > 1.25f) continue;
                float distance = offset.x * offset.x + offset.z * offset.z;
                if (distance > best) continue;
                best = distance; callFloor = i;
            }
            return callFloor >= 0;
        }

        public static ElevatorController FindNearest(Transform traveller, out bool inside, out int callFloor)
        {
            ElevatorController nearest = null; inside = false; callFloor = -1;
            float best = float.PositiveInfinity;
            foreach (var lift in active)
            {
                if (!lift || !lift.TryGetInteraction(traveller, out bool passenger, out int floor)) continue;
                float distance = passenger ? 0 : (traveller.position - lift.floors[floor].callPoint.position).sqrMagnitude;
                if (distance >= best) continue;
                nearest = lift; best = distance; inside = passenger; callFloor = floor;
            }
            return nearest;
        }

        public bool TryRequestFloor(int index, Transform requester, out string message)
        {
            message = string.Empty;
            if (!GameSession.IsAuthority) { message = "현재 클라이언트의 승강기 요청은 지원되지 않습니다."; return false; }
            if (index < 0 || index >= FloorCount || !TryGetInteraction(requester, out bool inside, out int callFloor) ||
                (!inside && index != callFloor)) { message = "승강실 또는 해당 층 호출 버튼에서 이용해주세요."; return false; }
            if (inside)
            {
                var motor = requester.GetComponent<CharacterController>();
                if (!motor || !motor.enabled || !ContainsPassenger(motor, true))
                { message = "승강실 안쪽으로 이동해주세요."; return false; }
            }
            if (index == currentFloor && !IsMoving && state != MotionState.Closing)
            {
                state = MotionState.Opening;
                openUntil = Time.time + openSeconds;
                message = GetFloorLabel(index) + " 문을 엽니다.";
                return true;
            }
            if (index != targetFloor || state == MotionState.Idle)
                if (!requests.Contains(index)) requests.Enqueue(index);
            message = GetFloorLabel(index) + " 이동이 예약되었습니다.";
            return true;
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float elapsed)
        {
            if (!ready) return;
            bool unavailable = !GameSession.IsAuthority || SceneLoadManager.IsLoading || gameObject.scene != SceneLoadManager.CurrentMap;
            if (unavailable)
            {
                requests.Clear(); velocity = 0; suspended = true; SetDoors(false, true); return;
            }
            if (suspended)
            {
                suspended = false;
                // A suspended car may be between stops. Complete its existing trip with the doors locked.
                if (Vector3.Distance(cabin.position, floors[currentFloor].stop.position) > .01f) state = MotionState.Moving;
                else state = MotionState.Idle;
            }
            obstructed = false;
            switch (state)
            {
                case MotionState.Idle:
                    SetDoors(false, true);
                    if (requests.Count > 0)
                    {
                        targetFloor = requests.Dequeue();
                        state = targetFloor == currentFloor ? MotionState.Opening : MotionState.Closing;
                    }
                    break;
                case MotionState.Opening:
                    SetDoors(true, false);
                    if (cabinDoor.IsFullyOpen && floors[currentFloor].landingDoor.IsFullyOpen)
                    { state = MotionState.Open; openUntil = Time.time + openSeconds; }
                    break;
                case MotionState.Open:
                    SetDoors(true, false);
                    if (Time.time < openUntil) break;
                    targetFloor = requests.Count > 0 ? requests.Dequeue() : currentFloor;
                    state = MotionState.Closing;
                    break;
                case MotionState.Closing:
                    SetDoors(false, false);
                    obstructed = cabinDoor.IsBlocked || floors[currentFloor].landingDoor.IsBlocked;
                    if (!AllDoorsClosed()) break;
                    if (!CanCarryCurrentPassenger())
                    { obstructed = true; state = MotionState.Opening; break; }
                    if (targetFloor == currentFloor) state = MotionState.Idle;
                    else { SetDoors(false, true); velocity = 0; state = MotionState.Moving; }
                    break;
                case MotionState.Moving:
                    SetDoors(false, true);
                    if (!AllDoorsClosed()) { velocity = 0; obstructed = true; break; }
                    float distance = Mathf.Abs(floors[targetFloor].stop.position.y - cabin.position.y);
                    if (distance <= .001f)
                    { cabin.position = floors[targetFloor].stop.position; currentFloor = targetFloor; state = MotionState.Opening; velocity = 0; break; }
                    float dt = Mathf.Clamp(elapsed, 0, .05f);
                    velocity = Mathf.MoveTowards(velocity, Mathf.Min(speed, Mathf.Sqrt(2 * acceleration * distance)), acceleration * dt);
                    Vector3 delta = Vector3.up * Mathf.Sign(floors[targetFloor].stop.position.y - cabin.position.y) * Mathf.Min(distance, velocity * dt);
                    if (!MoveCabin(delta)) { velocity = 0; obstructed = true; }
                    break;
            }
        }

        void SetDoors(bool openAtCurrent, bool lockCabin)
        {
            if (cabinDoor) cabinDoor.SetExternalState(openAtCurrent, lockCabin);
            for (int i = 0; i < FloorCount; i++)
                if (floors[i] != null && floors[i].landingDoor)
                {
                    bool aligned = ready && i == currentFloor && Vector3.Distance(cabin.position, floors[i].stop.position) < .015f && !IsMoving;
                    floors[i].landingDoor.SetExternalState(aligned && openAtCurrent, !aligned || lockCabin);
                }
        }

        bool AllDoorsClosed()
        {
            if (!cabinDoor.IsFullyClosed) return false;
            foreach (var floor in floors) if (!floor.landingDoor.IsFullyClosed) return false;
            return true;
        }

        bool CanCarryCurrentPassenger()
        {
            var traveller = SceneLoadManager.Traveller;
            var motor = traveller ? traveller.GetComponent<CharacterController>() : null;
            return !motor || !ContainsPassenger(motor, false) || motor.enabled && ContainsPassenger(motor, true);
        }

        bool ContainsPassenger(CharacterController motor, bool entireCapsule)
        {
            if (!motor || !cabinArea) return false;
            Vector3 center = cabinArea.transform.InverseTransformPoint(motor.transform.TransformPoint(motor.center)) - cabinArea.center;
            Vector3 half = cabinArea.size * .5f;
            if (!entireCapsule) return Mathf.Abs(center.x) <= half.x && Mathf.Abs(center.z) <= half.z && Mathf.Abs(center.y) <= half.y;
            Vector3 scale = cabinArea.transform.lossyScale;
            float radius = motor.radius * Mathf.Max(Mathf.Abs(motor.transform.lossyScale.x), Mathf.Abs(motor.transform.lossyScale.z));
            float height = motor.height * Mathf.Abs(motor.transform.lossyScale.y);
            float contactTolerance = motor.skinWidth * Mathf.Max(Mathf.Abs(motor.transform.lossyScale.x), Mathf.Abs(motor.transform.lossyScale.z));
            return Mathf.Abs(center.x) + radius / Mathf.Abs(scale.x) <= half.x + contactTolerance / Mathf.Abs(scale.x) + .01f &&
                Mathf.Abs(center.z) + radius / Mathf.Abs(scale.z) <= half.z + contactTolerance / Mathf.Abs(scale.z) + .01f &&
                Mathf.Abs(center.y) + height * .5f / Mathf.Abs(scale.y) <= half.y + .08f;
        }

        bool MoveCabin(Vector3 delta)
        {
            if (delta.sqrMagnitude <= .00000001f) return true;
            var traveller = SceneLoadManager.Traveller;
            var motor = traveller ? traveller.GetComponent<CharacterController>() : null;
            // Test current occupancy every step: no stale parenting or teleporting an actor back into the car.
            if (!motor || !ContainsPassenger(motor, false)) { cabin.position += delta; Physics.SyncTransforms(); return true; }
            if (!motor.enabled || !ContainsPassenger(motor, true)) return false;
            var ignored = new List<Collider>();
            Vector3 before = motor.transform.position;
            bool wasGrounded = motor.isGrounded;
            bool moved = false;
            try
            {
                // The old cabin floor must not block descent. Keep all shaft, landing and world collisions active.
                foreach (var solid in cabinSolids)
                    if (solid && solid.enabled && !Physics.GetIgnoreCollision(motor, solid))
                    { Physics.IgnoreCollision(motor, solid, true); ignored.Add(solid); }
                CollisionFlags collision = motor.Move(delta);
                Vector3 travelled = motor.transform.position - before;
                bool blocked = delta.y > 0 ? (collision & CollisionFlags.Above) != 0 : (collision & CollisionFlags.Below) != 0;
                if (blocked || (travelled - delta).sqrMagnitude > .000001f)
                {
                    motor.Move(-travelled);
                    return false;
                }
                cabin.position += delta;
                Physics.SyncTransforms();
                moved = true;
                return true;
            }
            finally
            {
                foreach (var solid in ignored) if (motor && solid) Physics.IgnoreCollision(motor, solid, false);
                // Carry Move clears isGrounded. Restore contact against the new floor so the existing
                // player motor resets gravity normally instead of accumulating fall speed during a trip.
                if (moved && wasGrounded && motor && motor.enabled)
                    motor.Move(Vector3.down * Mathf.Max(.001f, motor.skinWidth * .5f));
            }
        }

        void OnDisable()
        {
            active.Remove(this); requests.Clear(); velocity = 0;
            if (ready) SetDoors(false, true);
        }
    }
}
