using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RoomFinish
    {
        // Match the authored V2_floor and V2_plaster Standard materials exactly.
        internal static readonly Color FloorColor = new(.19f, .24f, .21f);
        internal static readonly Color WallColor = new(.58f, .58f, .49f);
        internal const float Glossiness = .20f;

        internal static Material CreateFloor(Shader standard) => Create(standard, "V2_floor", FloorColor);
        internal static Material CreateWall(Shader standard) => Create(standard, "V2_plaster", WallColor);

        private static Material Create(Shader standard, string name, Color color)
        {
            var material = new Material(standard) { name = name, color = color };
            material.SetFloat("_Glossiness", Glossiness);
            return material;
        }
    }
}
