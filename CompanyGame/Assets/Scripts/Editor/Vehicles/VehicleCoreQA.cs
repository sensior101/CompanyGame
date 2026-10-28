using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode checks for the car core: VehicleGrade.Step acceleration and VehicleBounds measurement. Throws on failure.</summary>
public static class VehicleCoreQA
{
    const float Dt = .02f, Kmh100 = 100f / 3.6f;

    [MenuItem("Tools/Company Game/Vehicles/Validate Vehicle Core")]
    static void MenuRun() { Run(); Debug.Log("VehicleCoreQA passed"); }

    public static void Run()
    {
        foreach (var spec in new[] { (120f, 4f), (140f, 6f), (200f, 9f), (300f, 14f) }) CheckGrade(spec.Item1, spec.Item2);
        CheckSlowGrade();
        CheckBounds();
        CheckInputMap();
        CheckDrivingVisibility();
    }

    // Mounts nothing for real: drives the private visibility helpers on a throwaway player/car pair.
    static void CheckDrivingVisibility()
    {
        var player = new GameObject("QA player", typeof(PlayerVehicle));
        var car = GameObject.CreatePrimitive(PrimitiveType.Cube); car.transform.SetParent(player.transform, false);
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); body.transform.SetParent(player.transform, false);
        player.AddComponent<CompanyGame.Daldongne.DaldongnePlayerAppearance>().female = body;   // the camera-owned body look
        var trail = new GameObject("QA trail", typeof(LineRenderer)); trail.transform.SetParent(player.transform, false);
        var trailRenderer = trail.GetComponent<LineRenderer>(); trailRenderer.enabled = false;  // not part of the body: keeps its own state
        var cam = new GameObject("QA cam", typeof(PlayerCameraController));
        try
        {
            var vehicle = player.GetComponent<PlayerVehicle>(); var controller = cam.GetComponent<PlayerCameraController>();
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            void Set(string name, object value) => typeof(PlayerVehicle).GetField(name, F).SetValue(vehicle, value);
            void Call(string name, params object[] args) => typeof(PlayerVehicle).GetMethod(name, F).Invoke(vehicle, args);
            var carRenderer = car.GetComponent<Renderer>(); var bodyRenderer = body.GetComponent<Renderer>();
            Set("instance", car); Set("cameraController", controller);
            Call("CaptureRenderers");
            controller.firstPerson = false; Call("ApplyCarVisibility", false);
            Expect(carRenderer.enabled && !bodyRenderer.enabled && carRenderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On, "third person: only the car");
            bodyRenderer.enabled = true; Call("ApplyCarVisibility", false);   // camera toggle re-shows the body
            Expect(!bodyRenderer.enabled, "third person: body stays hidden after the camera re-shows it");
            controller.firstPerson = true; Call("ApplyCarVisibility", true);
            Expect(!carRenderer.enabled && !bodyRenderer.enabled && carRenderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off, "first person: nothing visible, no shadow");
            Call("RestoreCarVisibility");
            Expect(carRenderer.enabled && !bodyRenderer.enabled && carRenderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On, "dismount in first person: car back, body stays hidden");
            controller.firstPerson = false; Set("instance", car); Call("CaptureRenderers"); Call("ApplyCarVisibility", false); Call("RestoreCarVisibility");
            Expect(bodyRenderer.enabled && carRenderer.enabled, "dismount in third person: body visible");
            Expect(!trailRenderer.enabled, "dismount: a non-body renderer keeps its pre-ride state");
        }
        finally { UnityEngine.Object.DestroyImmediate(player); UnityEngine.Object.DestroyImmediate(cam); }
    }
    static void Expect(bool ok, string label) { if (!ok) throw new Exception("driving visibility: " + label); }

    static void CheckInputMap()
    {
        void Case(Vector2 move, float speed, float throttle, bool reverse, float steer, string name)
        {
            PlayerVehicle.MapCarInput(move, speed, out float t, out bool r, out float s);
            if (!Mathf.Approximately(t, throttle) || r != reverse || !Mathf.Approximately(s, steer)) throw new Exception($"input map {name}: got {t},{r},{s}");
        }
        Case(new Vector2(0, 1), 0f, 1f, false, 0f, "W");
        Case(new Vector2(0, -1), 0f, 1f, true, 0f, "S standing reverses");
        Case(new Vector2(0, -1), 10f, 0f, false, 0f, "S while moving brakes");
        Case(new Vector2(0, 1), -3f, 0f, false, 0f, "W while reversing brakes");
        Case(new Vector2(-1, -1), -2f, 1f, true, -1f, "A+S reversing");
        Case(Vector2.zero, 5f, 0f, false, 0f, "idle");
    }

    static VehicleGrade MakeGrade(float top, float zeroTo100)
    {
        var grade = ScriptableObject.CreateInstance<VehicleGrade>();
        grade.topSpeedKmh = top; grade.zeroTo100Sec = zeroTo100; grade.brakeMps2 = 9f;
        return grade;
    }

    static void CheckGrade(float top, float zeroTo100)
    {
        var grade = MakeGrade(top, zeroTo100);
        try
        {
            float speed = 0f, reached = -1f;
            for (int i = 1; i <= (int)(60f / Dt); i++)
            {
                speed = grade.Step(speed, 1f, Dt);
                if (speed > grade.TopSpeedMps + 1e-4f) throw new Exception($"top {top}: speed {speed} exceeded top {grade.TopSpeedMps}");
                if (reached < 0f && speed >= Kmh100) reached = i * Dt;
            }
            if (reached < 0f || Mathf.Abs(reached - zeroTo100) > zeroTo100 * .02f)
                throw new Exception($"top {top}: 0-100 took {reached}s, expected {zeroTo100}s +-2%");
            if (Mathf.Abs(speed - grade.TopSpeedMps) > 1e-3f) throw new Exception($"top {top}: never settled at top speed ({speed})");
            float braked = grade.Step(speed, 0f, Dt);
            if (Mathf.Abs((speed - braked) - grade.brakeMps2 * Dt) > 1e-4f) throw new Exception($"top {top}: brake step wrong");
            if (grade.Step(0f, .5f, 100f) > grade.TopSpeedMps * .5f + 1e-4f) throw new Exception("half throttle exceeded half of top speed");
        }
        finally { UnityEngine.Object.DestroyImmediate(grade); }
    }

    // Top speed below 100 km/h: same acceleration rate, so top is reached after top/100 * zeroTo100 seconds.
    static void CheckSlowGrade()
    {
        var grade = MakeGrade(60f, 12f);
        try
        {
            float speed = 0f, reached = -1f;
            for (int i = 1; i <= (int)(30f / Dt) && reached < 0f; i++)
            {
                speed = grade.Step(speed, 1f, Dt);
                if (speed >= grade.TopSpeedMps - 1e-4f) reached = i * Dt;
            }
            if (Mathf.Abs(reached - 7.2f) > 7.2f * .02f) throw new Exception($"60 km/h grade reached top in {reached}s, expected 7.2s");
        }
        finally { UnityEngine.Object.DestroyImmediate(grade); }
    }

    static void CheckBounds()
    {
        var root = new GameObject("VehicleCoreQA root");
        try
        {
            root.transform.SetPositionAndRotation(new Vector3(5f, 3f, 2f), Quaternion.Euler(0f, 30f, 0f));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(0f, .5f, 1f);
            cube.transform.localScale = new Vector3(2f, 1f, 4f);
            var bounds = VehicleBounds.Measure(root);
            Near(bounds.center, new Vector3(0f, .5f, 1f), "center");
            Near(bounds.size, new Vector3(2f, 1f, 4f), "size");
            // Rotating the root must not change the root-local result.
            root.transform.rotation = Quaternion.Euler(10f, 80f, 0f);
            Near(VehicleBounds.Measure(root).size, new Vector3(2f, 1f, 4f), "size after rotation");
            if (VehicleBounds.Measure(null).size.sqrMagnitude < .01f) throw new Exception("null root must return the non-empty fallback");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Near(Vector3 actual, Vector3 expected, string label)
    {
        if ((actual - expected).magnitude > .01f) throw new Exception($"VehicleBounds {label}: {actual} != {expected}");
    }
}
