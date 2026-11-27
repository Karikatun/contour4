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
        // The room frame owns the open sweep and the air beneath the broken deck.
        private const float DropDepth = RaidExtraction.ShaftTop;
        private const float EdgeAir = RaidQuotaRotaryCabin.HalfWidth;
        private const float UpperAir = RaidQuotaRotaryCabin.HalfHeight + FarHalfWidth + EdgeAir;
        internal static readonly Bounds RoomField = new Bounds(
            new Vector3((FarStart + FarLength + EdgeAir) / 2, (UpperAir - DropDepth) / 2, 0),
            new Vector3(FarStart + FarLength + EdgeAir, UpperAir + DropDepth, 2 * (NearHalfWidth + EdgeAir)));
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
            room.Deck("ground", 0, 2.5f, 0, -NearHalfWidth, NearHalfWidth);
            // A shallow, continuous depressed approach rises flush to the cabin deck.
            // The low slope keeps the full width available for a cart.
            void ApproachRamp(string name, float start, float end, float y0, float y1)
            {
                float length = Mathf.Sqrt((end - start) * (end - start) + (y1 - y0) * (y1 - y0));
                var forward = N3.Normalize(new N3(end - start, y1 - y0, 0));
                var ramp = new ModulePose(new N3(start, y0, 0), forward, new N3(-forward.Y, forward.X, 0));
                room.Module.Geometry.Add(Block.Box(name, BlockRole.Floor, ramp, 0, length, -Skirt, 0, -NearHalfWidth, NearHalfWidth));
            }
            ApproachRamp("approach-ramp-down", 2.5f, 4.5f, 0, -.12f);
            ApproachRamp("approach-ramp-up", 4.5f, NearEnd, -.12f, 0);
            room.MapRectangle("ground", 0, NearEnd, 0, -NearHalfWidth, NearHalfWidth);
            room.Wall("entry-wall", Pose(0, 0, 0, -N3.UnitX), NearHalfWidth, Height, entry);
            // Tall damaged returns retain the feeling of a room around the mechanism.
            // Their missing middle panels are real apertures, not dark decals.
            room.Box("approach-left-base", BlockRole.Shell, 0, NearEnd, 0, .55f, -NearHalfWidth, -NearHalfWidth + Skirt);
            room.Box("approach-left-return", BlockRole.Shell, 0, 2.45f, .55f, 3.75f, -NearHalfWidth, -NearHalfWidth + Skirt);
            room.Box("approach-left-upper", BlockRole.Shell, 2.45f, 4.55f, 2.55f, 3.45f, -NearHalfWidth, -NearHalfWidth + Skirt);
            room.Box("approach-left-pier", BlockRole.Shell, 4.55f, 6.2f, .55f, 4.45f, -NearHalfWidth, -NearHalfWidth + Skirt);
            room.Box("approach-right-base", BlockRole.Shell, 0, NearEnd, 0, .45f, NearHalfWidth - Skirt, NearHalfWidth);
            room.Box("approach-right-return", BlockRole.Shell, 0, 1.8f, .45f, 3.4f, NearHalfWidth - Skirt, NearHalfWidth);
            room.Box("approach-right-pier", BlockRole.Shell, 3.4f, 6.2f, .45f, 4.25f, NearHalfWidth - Skirt, NearHalfWidth);
            room.Box("approach-right-upper", BlockRole.Shell, 1.8f, 3.4f, 2.7f, 3.25f, NearHalfWidth - Skirt, NearHalfWidth);

            // Facade stands immediately in front of the fixed ring: the circular
            // sweep is left genuinely empty, while local holes break the masonry.
            const float faceStart = 6.17f, faceEnd = 6.42f;
            room.Box("near-rupture-left", BlockRole.Shell, faceStart, faceEnd, 0, 1.40f, -5.5f, -3.25f);
            room.Box("ring-facade-left-outer", BlockRole.Shell, faceStart, faceEnd, 1.40f, 4.8f, -5.5f, -4.85f);
            room.Box("ring-facade-left-inner", BlockRole.Shell, faceStart, faceEnd, 1.40f, 4.8f, -4.05f, -3.25f);
            room.Box("ring-facade-left-over-hole", BlockRole.Shell, faceStart, faceEnd, 2.50f, 4.8f, -4.85f, -4.05f);
            room.Box("ring-facade-right-base", BlockRole.Shell, faceStart, faceEnd, 0, 1.15f, 3.25f, 5.5f);
            room.Box("ring-facade-right-inner", BlockRole.Shell, faceStart, faceEnd, 1.15f, 4.8f, 3.25f, 4.05f);
            room.Box("ring-facade-right-outer", BlockRole.Shell, faceStart, faceEnd, 1.15f, 4.45f, 4.85f, 5.5f);
            room.Box("ring-facade-right-over-hole", BlockRole.Shell, faceStart, faceEnd, 2.25f, 4.45f, 4.05f, 4.85f);
            room.Box("ring-facade-crown", BlockRole.Shell, faceStart, faceEnd, 4.86f, 5.65f, -5.5f, 5.5f);
            room.Box("ring-facade-crown-left", BlockRole.Shell, faceStart, faceEnd, 5.65f, 6.25f, -5.5f, -1.25f);
            room.Box("ring-facade-crown-right", BlockRole.Shell, faceStart, faceEnd, 5.65f, 6.0f, 1.6f, 5.5f);
            // Broken wall ends step down towards a broad open middle.
            room.Box("near-cover-left-base", BlockRole.Obstacle, 3.2f, 3.65f, -.12f, .40f, -4.9f, -2.75f);
            room.Box("near-cover-left-high", BlockRole.Obstacle, 3.2f, 3.65f, .40f, 1.12f, -4.9f, -4.25f);
            room.Box("near-cover-left-mid", BlockRole.Obstacle, 3.2f, 3.65f, .40f, .72f, -4.25f, -3.55f);
            room.Box("near-cover-left-low", BlockRole.Obstacle, 3.2f, 3.65f, .40f, .53f, -3.55f, -2.75f);
            room.Box("near-cover-right-base", BlockRole.Obstacle, 4.6f, 5.05f, -.12f, .33f, 2.75f, 5.0f);
            room.Box("near-cover-right-high", BlockRole.Obstacle, 4.6f, 5.05f, .33f, .96f, 4.35f, 5.0f);
            room.Box("near-cover-right-mid", BlockRole.Obstacle, 4.6f, 5.05f, .33f, .63f, 3.55f, 4.35f);
            room.Box("near-cover-right-low", BlockRole.Obstacle, 4.6f, 5.05f, .33f, .46f, 2.75f, 3.55f);
            // Loose-looking static fragments sit off the cargo approach.
            room.Box("ruin-fragment-left-a", BlockRole.Obstacle, 2.65f, 3.05f, -.18f, .19f, -5.15f, -4.68f);
            room.Box("ruin-fragment-left-b", BlockRole.Obstacle, 5.25f, 5.83f, -.18f, .24f, -5.12f, -4.7f);
            room.Box("ruin-fragment-right-a", BlockRole.Obstacle, 2.75f, 3.28f, -.18f, .23f, 4.58f, 5.13f);
            room.Box("ruin-fragment-right-b", BlockRole.Obstacle, 5.45f, 6.00f, -.18f, .16f, 4.65f, 5.07f);

            // The cabin sweep is empty space, not an invisible shaft or floor.
            const float wellRadius = 3.18f;
            float low = Center.y - wellRadius, high = Center.y + wellRadius;
            room.Box("cabin-sweep", BlockRole.Free, NearEnd, FarStart, low, high, -wellRadius, wellRadius);

            void FarBox(string name, float from, float to, float lower, float upper, float left, float right) =>
                room.Module.Geometry.Add(Block.Box(name, BlockRole.Shell, far, from, to, lower, upper, left, right));
            // Keep the neck and quota bay on one support collider. Two flush boxes produce
            // a forward-facing contact edge that can stop a cart as its nose leaves the neck.
            FarBox("far-extraction-deck", 0, FarLength, -Skirt, 0, -FarHalfWidth, FarHalfWidth);
            // Ruined foundations leave the genuine machine and its landing unobstructed.
            FarBox("far-foundation-left", Neck, FarLength, 0, .38f, -FarHalfWidth, -FarHalfWidth + Skirt);
            FarBox("far-foundation-right", Neck, FarLength, 0, .25f, FarHalfWidth - Skirt, FarHalfWidth);
            FarBox("far-foundation-back", FarLength - Skirt, FarLength, 0, .32f, -FarHalfWidth, FarHalfWidth);
            FarBox("far-broken-pier", FarLength - 1.2f, FarLength, .32f, 1.15f, -FarHalfWidth, -4.75f);
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
