using System;

namespace CompanyGame.World.Maps
{
    [Serializable]
    public sealed class TransitDestination
    {
        public string scenePath;
        public string displayName;
        public string description;

        public TransitDestination() { }

        public TransitDestination(string scenePath, string displayName, string description = "")
        {
            this.scenePath = scenePath;
            this.displayName = displayName;
            this.description = description;
        }
    }
}
