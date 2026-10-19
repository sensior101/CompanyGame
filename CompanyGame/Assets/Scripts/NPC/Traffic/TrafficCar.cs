using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An NPC car drawn from the map's <see cref="TrafficPlan"/> (Docs/Multiplayer_Redesign.md 12.14). The spawner gives it
/// a pose every frame; this keeps the body (collider, kinematic rigidbody), the lift that puts the wheels on the road
/// and the turning wheels. <see cref="Delay"/> is how far this PC draws it behind the plan after it waited for someone.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrafficCar : MonoBehaviour
{
    public VehicleGrade grade;
    public float wheelRadius = .34f;
    public string itemId = "";
    public int key;

    public float Speed { get; private set; }
    public Vector3 Position => pos;
    public float Yaw => yaw;
    public Vector3 Forward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    /// <summary>Body bounds in the car's own space (from its meshes).</summary>
    public Bounds Body => body;
    /// <summary>Seconds this PC draws the car behind the plan (it waited for someone in front); 0 = exactly the plan.</summary>
    public double Delay { get; internal set; }

    Vector3 pos;
    float lift, yaw, wheelDegrees;
    Bounds body = new Bounds(new Vector3(0f, .7f, .3f), new Vector3(1.8f, 1.4f, 4.2f));
    readonly List<Transform> wheels = new List<Transform>();
    readonly List<Quaternion> wheelBase = new List<Quaternion>();

    /// <summary>Builds the collider and finds the wheels.</summary>
    public void Init()
    {
        body = VehicleBounds.Measure(gameObject);
        lift = Mathf.Max(0f, -body.min.y); // pivot above the lowest point: raise so the wheels touch the road
        Rigidbody rigid = GetComponent<Rigidbody>();
        if (!rigid) rigid = gameObject.AddComponent<Rigidbody>();
        rigid.isKinematic = true;
        rigid.useGravity = false;
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = body.center;
        box.size = body.size;
        wheels.Clear();
        wheelBase.Clear();
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            // Skip wheels nested inside another wheel so they are not rotated twice.
            if (child == transform || !child.name.StartsWith("Wheel") || child.parent.name.StartsWith("Wheel")) continue;
            wheels.Add(child);
            wheelBase.Add(child.localRotation);
        }
    }

    /// <summary>Puts the car on the road at <paramref name="road"/> (a lane point height) facing <paramref name="forward"/>.</summary>
    internal void Show(Vector3 road, Vector3 forward, float speed)
    {
        Vector3 next = road + Vector3.up * lift;
        if (forward.x * forward.x + forward.z * forward.z > 1e-6f) yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        wheelDegrees += Vector3.Dot(next - pos, Forward) / Mathf.Max(.05f, wheelRadius) * Mathf.Rad2Deg;
        pos = next;
        Speed = speed;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        for (int i = 0; i < wheels.Count; i++)
            wheels[i].localRotation = wheelBase[i] * Quaternion.Euler(wheelDegrees, 0f, 0f);
    }
}
