using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CompanyGame.Daldongne
{
    [DisallowMultipleComponent]
    public sealed class DaldongnePlayerAppearance : MonoBehaviour
    {
        public enum Variant { Female, Male }
        public Variant selected;
        public GameObject female;
        public GameObject male;

        void Awake() { Select(selected); }
        public void Select(Variant value)
        {
            selected = value;
            if (female) female.SetActive(value == Variant.Female);
            if (male) male.SetActive(value == Variant.Male);
        }
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.digit1Key.wasPressedThisFrame) Select(Variant.Female);
            if (keys.digit2Key.wasPressedThisFrame) Select(Variant.Male);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(Variant.Female);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(Variant.Male);
#endif
        }
    }
}
