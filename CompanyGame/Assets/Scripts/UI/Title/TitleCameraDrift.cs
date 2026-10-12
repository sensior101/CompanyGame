using UnityEngine;

/// <summary>Slowly circles the camera around the title backdrop. Pure decoration.</summary>
public sealed class TitleCameraDrift : MonoBehaviour
{
    public Vector3 center = new Vector3(0f, 18f, 0f);
    public float radius = 92f, height = 34f, degreesPerSecond = 2.2f, startAngle = 25f, bob = 2f, lookOffset = 24f;

    void LateUpdate()
    {
        float angle = (startAngle + Time.time * degreesPerSecond) * Mathf.Deg2Rad;
        float y = height + Mathf.Sin(Time.time * 0.2f) * bob;
        transform.position = center + new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius);
        // Aim a little to the side of the tower so the menu in the middle of the screen does not cover it.
        var toCenter = Quaternion.LookRotation(center - transform.position);
        transform.rotation = toCenter;
        transform.LookAt(center + transform.right * lookOffset);
    }
}
