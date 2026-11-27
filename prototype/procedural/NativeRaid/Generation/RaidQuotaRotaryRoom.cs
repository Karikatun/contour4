using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using static Contour4.Procedural.Catalog.GrayBuilder;
using N2 = System.Numerics.Vector2;
using N3 = System.Numerics.Vector3;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RaidQuotaRotaryRoom
    {
        internal const string Id = "extraction-rotary-transfer";
        private const float CenterX = 10f;
        private const float NearEnd = CenterX - RaidQuotaRotaryCabin.HalfLength;
        private const float FarStart = CenterX + RaidQuotaRotaryCabin.HalfLength;
        private const float FarLength = 15.9f;
        private const float NearHalfWidth = 5.5f;
        private const float FarHalfWidth = 5.5f;
        private const float ThroatHalfWidth = RaidQuotaRotaryCabin.HalfWidth;
        private const float ThroatHeight = 2 * RaidQuotaRotaryCabin.HalfHeight;
        private const float Neck = 2.10f;
        internal static readonly Vector3 Center = new Vector3(CenterX, RaidQuotaRotaryCabin.HalfHeight, 0);
        internal static readonly Quaternion NearRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        internal static readonly Bounds FarField = new Bounds(
            new Vector3(0, (RaidExtraction.ShaftTop - .1f) / 2, FarLength / 2),
            new Vector3(2 * FarHalfWidth, RaidExtraction.ShaftTop + .14f, FarLength + .02f));

        // Viewed from the near approach, the far floor lands on the right, left or ceiling.
        internal static float TurnDegrees(int seed) => (unchecked((uint)seed) % 3u) switch
        {
            0 => 90f,
            1 => -90f,
            _ => 180f,
        };

        internal static GrayModule Create(float degrees)
        {
            var dock = RaidQuotaRotaryCabin.Doorway(Center, NearRotation, true, degrees);
            Vector3 up = dock.rotation * Vector3.up;
            // Exact cardinal axes keep the two support surfaces flush at the threshold.
            var farUp = new N3(0, Mathf.RoundToInt(up.y), Mathf.RoundToInt(up.z));
            var far = new ModulePose(new N3(FarStart, Center.y, 0) - RaidQuotaRotaryCabin.HalfHeight * farUp, N3.UnitX, farUp);
            var room = new GrayBuilder(Id, 2 * NearHalfWidth, FarStart + FarLength);
            var entry = room.Door(RaidExtraction.Entry, "ground", Pose(0, 0, 0, -N3.UnitX), Cargo.Cart);
            room.Floor("ground", 0, NearEnd, 0, -NearHalfWidth, NearHalfWidth);
            room.Wall("entry-wall", Pose(0, 0, 0, -N3.UnitX), NearHalfWidth, Height, entry);
            room.Box("approach-left", BlockRole.Shell, 0, NearEnd, 0, Height, -NearHalfWidth, -NearHalfWidth + Skirt);
            room.Box("approach-right", BlockRole.Shell, 0, NearEnd, 0, Height, NearHalfWidth - Skirt, NearHalfWidth);
            room.Roof(0, NearEnd, Height, -NearHalfWidth, NearHalfWidth);
            room.Box("near-rupture-left", BlockRole.Shell, NearEnd - Skirt, NearEnd, 0, Height, -NearHalfWidth, -ThroatHalfWidth);
            room.Box("near-rupture-right", BlockRole.Shell, NearEnd - Skirt, NearEnd, 0, Height, ThroatHalfWidth, NearHalfWidth);
            room.Box("near-rupture-lintel", BlockRole.Shell, NearEnd - Skirt, NearEnd, ThroatHeight, Height, -ThroatHalfWidth, ThroatHalfWidth);
            room.Box("near-cover-left", BlockRole.Obstacle, 3.2f, 3.65f, 0, 1.05f, -4.9f, -2.75f);
            room.Box("near-cover-right", BlockRole.Obstacle, 4.6f, 5.05f, 0, 1.18f, 2.75f, 5.0f);

            // The deck across this span has been lost. A sealed well encloses the rotating cage.
            const float wellRadius = 3.18f;
            float low = Center.y - wellRadius, high = Center.y + wellRadius;
            room.Box("well-bottom", BlockRole.Shell, NearEnd, FarStart, low - Skirt, low, -wellRadius, wellRadius);
            room.Box("well-top", BlockRole.Shell, NearEnd, FarStart, high, high + Skirt, -wellRadius, wellRadius);
            room.Box("well-left", BlockRole.Shell, NearEnd, FarStart, low, high, -wellRadius - Skirt, -wellRadius);
            room.Box("well-right", BlockRole.Shell, NearEnd, FarStart, low, high, wellRadius, wellRadius + Skirt);
            room.Box("broken-deck-left", BlockRole.Shell, NearEnd, NearEnd + .83f, -.32f, .04f, -3.18f, -2.91f);
            room.Box("broken-deck-right", BlockRole.Shell, NearEnd, NearEnd + 1.38f, -.32f, .04f, 3.01f, 3.18f);
            room.Box("cabin-sweep", BlockRole.Free, NearEnd, FarStart, low, high, -wellRadius, wellRadius);

            void FarBox(string name, float from, float to, float lower, float upper, float left, float right) =>
                room.Module.Geometry.Add(Block.Box(name, BlockRole.Shell, far, from, to, lower, upper, left, right));
            // Keep the neck and quota bay on one support collider. Two flush boxes produce
            // a forward-facing contact edge that can stop a cart as its nose leaves the neck.
            FarBox("far-extraction-deck", 0, FarLength, -Skirt, 0, -FarHalfWidth, FarHalfWidth);
            FarBox("far-neck-left", 0, Neck, -2.0f, 4.9f, -ThroatHalfWidth - Skirt, -ThroatHalfWidth);
            FarBox("far-neck-right", 0, Neck, -2.0f, 4.9f, ThroatHalfWidth, ThroatHalfWidth + Skirt);
            FarBox("far-neck-roof", 0, Neck, ThroatHeight, ThroatHeight + Skirt, -ThroatHalfWidth, ThroatHalfWidth);
            // Fill the corners outside both throats so they cannot become a bypass or a fall trap.
            // Keep the sill beneath the continuous approach deck. Its interior top edge
            // would otherwise catch low loose freight at the approach to the cabin.
            room.Box("near-well-sill", BlockRole.Shell, NearEnd - Skirt, NearEnd, low, -Skirt, -wellRadius, wellRadius);
            room.Box("near-well-cap", BlockRole.Shell, NearEnd - Skirt, NearEnd, Height, high, -wellRadius, wellRadius);
            FarBox("far-well-sill", 0, Skirt, -2.0f, -Skirt, -FarHalfWidth, FarHalfWidth);
            FarBox("far-well-cap", 0, Skirt, ThroatHeight, 4.9f, -FarHalfWidth, FarHalfWidth);
            FarBox("far-well-left", 0, Skirt, -Skirt, ThroatHeight, -FarHalfWidth, -ThroatHalfWidth);
            FarBox("far-well-right", 0, Skirt, -Skirt, ThroatHeight, ThroatHalfWidth, FarHalfWidth);

            FarBox("far-room-left", Neck, FarLength, 0, RaidExtraction.ShaftTop, -FarHalfWidth, -FarHalfWidth + Skirt);
            FarBox("far-room-right", Neck, FarLength, 0, RaidExtraction.ShaftTop, FarHalfWidth - Skirt, FarHalfWidth);
            FarBox("far-extraction-back", FarLength - Skirt, FarLength, 0, RaidExtraction.ShaftTop, -FarHalfWidth, FarHalfWidth);
            FarBox("far-shaft-front", Neck, Neck + Skirt, 3.6f, RaidExtraction.ShaftTop, -FarHalfWidth, FarHalfWidth);
            FarBox("far-shaft-front-left", Neck, Neck + Skirt, 0, 3.6f, -FarHalfWidth, -ThroatHalfWidth);
            FarBox("far-shaft-front-right", Neck, Neck + Skirt, 0, 3.6f, ThroatHalfWidth, FarHalfWidth);
            FarBox("far-shaft-roof", Neck, FarLength, RaidExtraction.ShaftTop, RaidExtraction.ShaftTop + Skirt, -FarHalfWidth, FarHalfWidth);
            room.Module.Geometry.Add(Block.Box("far-quota-working-volume", BlockRole.Free, far,
                Neck, FarLength, 0, RaidExtraction.ShaftTop, -FarHalfWidth, FarHalfWidth));
            room.MapFloor("far-landing", far, new N2(0, -ThroatHalfWidth), new N2(Neck, -ThroatHalfWidth),
                new N2(Neck, -FarHalfWidth), new N2(FarLength, -FarHalfWidth), new N2(FarLength, FarHalfWidth),
                new N2(Neck, FarHalfWidth), new N2(Neck, ThroatHalfWidth), new N2(0, ThroatHalfWidth));

            room.Module.Landmarks.Add(RaidExtraction.Machine, far.Point(10.8f, 0));
            // Two static landing paths joined by the powered cabin, not a continuous static deck.
            room.Module.Routes.Add(new HaulRoute("rotary-quota-route", entry.Id, RaidExtraction.Machine, Cargo.Cart,
                new[] { Pose(0, 0, 0), Pose(NearEnd, 0, 0), far, new ModulePose(far.Point(6.2f, 0), far.Forward, far.Up) },
                condition: "rotary-quota-cabin-powered"));
            return room.Module;
        }
    }
}
