using System.IO;
using UnityEngine;

/// <summary>
/// Shares position and yaw (3 floats + u16 yaw = 14 bytes). The owner marks itself dirty only when it moved;
/// other machines snap to the first state and then follow the latest one smoothly.
/// </summary>
public sealed class NetTransform : NetBehaviour
{
    const float Follow = 12f, SnapDistance = 10f, MoveEpsilon = .01f, TurnEpsilon = .5f;

    Vector3 target, sentPosition;
    float targetYaw, sentYaw;
    bool hasTarget;

    public override void Write(BinaryWriter w)
    {
        sentPosition = transform.position;
        sentYaw = transform.eulerAngles.y;
        w.Write(sentPosition.x); w.Write(sentPosition.y); w.Write(sentPosition.z);
        w.Write(EncodeYaw(sentYaw));
    }

    public override void Read(BinaryReader r)
    {
        target = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        targetYaw = DecodeYaw(r.ReadUInt16());
        if (!hasTarget || (transform.position - target).sqrMagnitude > SnapDistance * SnapDistance)
            transform.SetPositionAndRotation(target, Quaternion.Euler(0f, targetYaw, 0f));
        hasTarget = true;
    }

    public static ushort EncodeYaw(float degrees) => (ushort)Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) * 65536f / 360f); // 360 wraps to 0
    public static float DecodeYaw(ushort value) => value * 360f / 65536f;

    void Update()
    {
        if (!Entity.IsSpawned) return;
        if (IsOwner)
        {
            if ((transform.position - sentPosition).sqrMagnitude > MoveEpsilon * MoveEpsilon
                || Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, sentYaw)) > TurnEpsilon) MarkDirty();
            return;
        }
        if (!hasTarget) return;
        float k = 1f - Mathf.Exp(-Follow * Time.deltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, target, k),
            Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), k));
    }
}
