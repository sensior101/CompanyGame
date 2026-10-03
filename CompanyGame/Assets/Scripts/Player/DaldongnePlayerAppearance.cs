using UnityEngine;

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
        int variant = GameInput.AltNumberPressed(2);
        if (variant == 0) Select(Variant.Female);
        if (variant == 1) Select(Variant.Male);
        }
    }
}
