using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RoomFinish
    {
        // Match the authored Common Institute Style materials exactly.
        internal static readonly Color FloorColor = new(.19f, .24f, .21f);
        internal static readonly Color WallColor = new(.58f, .58f, .49f);
        private static readonly Color EnamelColor = new(.25f, .32f, .25f);
        internal const float Glossiness = .20f;

        internal static Material CreateFloor(Shader standard) => Create(standard, "V2_floor", FloorColor);
        internal static Material CreateWall(Shader standard) => Create(standard, "V2_plaster", WallColor);
        internal static Material CreateEnamel(Shader standard) => Create(standard, "V2_enamel", EnamelColor);

        private static Material Create(Shader standard, string name, Color color)
        {
            var material = new Material(standard) { name = name, color = color };
            material.SetFloat("_Glossiness", Glossiness);
            return material;
        }
    }
}
