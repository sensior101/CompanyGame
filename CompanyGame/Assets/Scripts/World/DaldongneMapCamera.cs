using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CompanyGame.Daldongne
{
    [RequireComponent(typeof(Camera))]
    public sealed class DaldongneMapCamera : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0, 6, 2);
        public float zoom = 42f;
        public float yaw = -33f;
        public float pitch = 36f;
        Camera mapCamera;

        void OnEnable() { mapCamera = GetComponent<Camera>(); Apply(); }
        void LateUpdate()
        {
            Vector2 delta = Vector2.zero;
            float wheel = 0;
            bool orbit = false, pan = false, reset = false, top = false;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                delta = Mouse.current.delta.ReadValue();
                wheel = Mouse.current.scroll.ReadValue().y / 120f;
                orbit = Mouse.current.rightButton.isPressed;
                pan = Mouse.current.middleButton.isPressed;
            }
            if (Keyboard.current != null)
            {
                reset = Keyboard.current.homeKey.wasPressedThisFrame;
                top = Keyboard.current.tKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10;
            wheel = Input.mouseScrollDelta.y;
            orbit = Input.GetMouseButton(1); pan = Input.GetMouseButton(2);
            reset = Input.GetKeyDown(KeyCode.Home); top = Input.GetKeyDown(KeyCode.T);
#endif
            if (reset) ResetView();
            if (top) SetTopDown(pitch <= 80);
            if (orbit) { yaw += delta.x * .22f; pitch = Mathf.Clamp(pitch - delta.y * .18f, 15, 89.8f); }
            if (pan) focus -= (transform.right * delta.x + Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized * delta.y) * (zoom * .002f);
            zoom = Mathf.Clamp(zoom * Mathf.Exp(-wheel * .12f), 6, 65);
            Apply();
        }
        public void ResetView()
        {
            focus = new Vector3(0, 6, 2); zoom = 42; yaw = -33; pitch = 36; Apply();
        }
        public void SetTopDown(bool enabled)
        {
            pitch = enabled ? 89.8f : 36; yaw = enabled ? 0 : -33; Apply();
        }
        void Apply()
        {
            if (!mapCamera) mapCamera = GetComponent<Camera>();
            if (!mapCamera) return;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = focus - transform.forward * 115;
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = zoom;
        }
    }
}
