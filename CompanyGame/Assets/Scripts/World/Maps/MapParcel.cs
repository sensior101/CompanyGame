using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>Persistent map address on the parcel, independent of the building occupying it.</summary>
    [DisallowMultipleComponent]
    public sealed class MapParcel : MonoBehaviour
    {
        public string parcelId;
        public string district;
        [Min(1)] public int number;
        [Tooltip("Existing LandPlot ID, when this parcel is connected to the purchase system.")]
        public string landPlotId;
        public Vector3 localCenter;
        public Vector2 size;
        public Vector3 Center => transform.TransformPoint(localCenter);
        public string Address => district + " " + number + "번지";
    }
}
