using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using static Contour4.NativeRaid.Runtime.RaidPlan;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed partial class RaidInteriorDetails
    {
        // Coordinates on these faces are in metres. The render mesh is owned by
        // RaidInteriorDetails and never supplies support, collision or clearance.
        private static Face Surface(Vector3 center, Vector3 up, Vector3 inward) =>
            new(center, Vector3.Cross(inward, up), up, inward, 1f, 1f);

        private static Face Deck(ModulePose frame) =>
            Surface(Vector(frame.Position), Vector(frame.Forward), Vector(frame.Up));

        private static Face Shift(Face face, float right, float up) =>
            Surface(face.Origin + face.Right * right + face.Up * up, face.Up, face.Normal);

        private static void Fill(Batch batch, Face face, Ink ink, float w, float h, float lift = 0f) =>
            batch.Rect(face, ink, -w / 2, -h / 2, w / 2, h / 2, lift);

        private static void Outline(Batch batch, Face face, Ink ink, float width, float height, float line)
        {
            float x = width / 2, y = height / 2;
            batch.Rect(face, ink, -x, -y, x, -y + line);
            batch.Rect(face, ink, -x, y - line, x, y);
            batch.Rect(face, ink, -x, -y + line, -x + line, y - line);
            batch.Rect(face, ink, x - line, -y + line, x, y - line);
        }

        private static void Code(Batch batch, Face face, string code, float width, float height, Ink edge)
        {
            Fill(batch, face, Ink.Rubber, width, height);
            batch.Rect(face, edge, -width / 2, height * .32f, width / 2, height / 2, .001f);
            batch.Text(face.Sized(width, height), code, Ink.Letter);
        }

        private static void Stroke(Batch batch, Face face, Ink ink, float width, float lift, params Vector2[] points)
        {
            for (int i = 1; i < points.Length; i++)
                batch.Line(face, ink, points[i - 1], points[i], width, lift);
        }

        private static void ShuttleApron(Batch batch, bool service, Vector2 station, Vector2 truck, float depth, float wall)
        {
            PressureFrame(batch, Surface(new Vector3(0, 0, -wall), Vector3.up, Vector3.back),
                station, "A01", aboveHeader: true);
            PressureFrame(batch, Surface(new Vector3(0, 0, wall - depth), Vector3.up, Vector3.forward),
                truck, "V01", aboveHeader: true);

            var apron = Surface(new Vector3(0, .012f, -depth / 2), Vector3.forward, Vector3.up);
            foreach (float side in new[] { -1f, 1f })
            {
                var track = Shift(apron, side * .89f, 0f);
                Fill(batch, track, Ink.Conduit, .165f, depth - 1.30f, .001f);
                Fill(batch, Shift(track, side * .045f, 0), Ink.Rim, .034f, depth - 1.30f, .002f);
                for (float y = -depth / 2 + .8f; y < depth / 2 - .7f; y += .67f)
                    Fill(batch, Shift(track, 0, y), Ink.BlueSteel, .12f, .033f, .003f);
            }
            // The right flank contains stationary machinery in both bays.
            var parking = Shift(apron, -4.7f, -1.25f);
            Ink trim = service ? Ink.Amber : Ink.Teal;
            Outline(batch, parking, trim, 3.2f, 3.75f, .065f);
            Code(batch, Shift(parking, .55f, 1.25f), "P01", 1.4f, .55f, trim);
            foreach (float side in new[] { -1f, 1f })
                Fill(batch, Shift(parking, side * .77f, .27f), Ink.Rim, .065f, 1.32f);
        }

        private static void PressureFrame(Batch batch, Face face, Vector2 opening, string code, bool aboveHeader)
        {
            float half = opening.x / 2, top = opening.y;
            foreach (float side in new[] { -1f, 1f })
            {
                var jamb = Shift(face, side * (half + .265f), top / 2);
                Fill(batch, jamb, Ink.BlueSteel, .46f, top - .10f, .008f);
                Fill(batch, Shift(jamb, side * -.183f, 0), Ink.Rubber, .060f, top - .08f, .013f);

                // Visible actuator bracket plus two hinge blocks sit on the stationary jamb.
                foreach (float y in new[] { .83f, top - 1.02f })
                {
                    var hinge = Shift(face, side * (half + .22f), y);
                    Fill(batch, hinge, Ink.Rubber, .23f, .29f, .028f);
                    Fill(batch, hinge, Ink.Rim, .17f, .18f, .034f);
                    Fill(batch, hinge, Ink.Amber, .042f, .20f, .037f);
                }
                var ram = Shift(face, side * (half + .39f), top * .56f);
                Fill(batch, ram, Ink.Rubber, .122f, 1.18f, .024f);
                Fill(batch, Shift(ram, 0, .21f), Ink.Rim, .043f, .64f, .032f);
                Fill(batch, Shift(ram, 0, -.36f), Ink.BlueSteel, .086f, .33f, .035f);

                Fill(batch, Shift(face, side * (half + .23f), .492f), Ink.Rubber, .38f, .77f, .012f);
                for (int stripe = 0; stripe < 6; stripe++)
                {
                    float y = .16f + stripe * .115f;
                    batch.Line(face, Ink.Amber, new Vector2(side * (half + .09f), y),
                        new Vector2(side * (half + .37f), y + .09f), .078f, .015f);
                }
            }

            if (aboveHeader)
            {
                // The curve moves inward only after it has cleared the top of the opening.
                var bends = new[] { new Vector2(-half - .084f, top - .05f),
                    new Vector2(-half - .057f, top + .057f), new Vector2(-half + .006f, top + .114f),
                    new Vector2(-half + .14f, top + .140f), new Vector2(half - .14f, top + .140f),
                    new Vector2(half - .006f, top + .114f), new Vector2(half + .057f, top + .057f),
                    new Vector2(half + .084f, top - .05f) };
                Stroke(batch, face, Ink.BlueSteel, .094f, .007f, bends);
                Stroke(batch, face, Ink.Rubber, .044f, .015f, bends);
            }
            Code(batch, Shift(face, half + .87f, top * .71f), code, .95f, .62f, Ink.Amber);
        }

        private static Face EntryFace(Dock dock, float recess)
        {
            var frame = dock.Threshold;
            return Surface(Vector(frame.Point(-recess, 0)), Vector(frame.Up), -Vector(frame.Forward));
        }

        private static void TransferRooms(Batch batch, GrayModule module)
        {
            var airlock = module.Docks.Single(dock => dock.Id == "airlock-entry");
            PressureFrame(batch, EntryFace(airlock, CornerSlice.Thickness),
                new Vector2(airlock.Opening.X, airlock.Opening.Y), "A02", aboveHeader: false);
            var apron = Shift(Deck(module.Floors.Single(f => f.Id == airlock.FloorId).Pose), 0f, -13.1f);
            foreach (float side in new[] { -1f, 1f })
            {
                var rail = Shift(apron, side * .89f, 0);
                Fill(batch, rail, Ink.Conduit, .14f, 3.00f);
                Fill(batch, rail, Ink.Rim, .041f, 3.00f, .002f);
            }
            var thresholdLamp = Surface(apron.Origin + apron.Normal * CornerSlice.Headroom + apron.Up * -1.56f,
                apron.Up, -apron.Normal);
            Fill(batch, thresholdLamp, Ink.BlueSteel, airlock.Opening.X + .72f, .20f);
            Fill(batch, thresholdLamp, Ink.CoolLens, airlock.Opening.X - .36f, .05f, .004f);

            var hall = module.Floors.Single(f => f.Id == "distribution-hall").Pose;
            var distribution = module.Docks.Single(dock => dock.Id == "distribution-hall-entry");
            var entry = EntryFace(distribution, CornerSlice.Thickness);
            for (int side = -1; side <= 1; side += 2)
            {
                var sign = Shift(entry, side * (distribution.Opening.X / 2 + 1.55f), 2.76f);
                Code(batch, sign, side < 0 ? "D01" : "D02", 2.02f, 1.30f, Ink.CoolLens);
                Fill(batch, Shift(sign, 0, -.79f), Ink.Amber, 1.92f, .044f);
            }

            // The first ceiling slab spans the full hall. The next two are wings
            // beside the rotation aperture and cannot host the middle of the arc.
            var ceiling = Surface(Vector(hall.Point(-13.83f, CornerSlice.Headroom)), Vector(hall.Forward), -Vector(hall.Up));
            var arc = Enumerable.Range(0, 17).Select(index =>
            {
                float t = index / 16f;
                return new Vector2(Mathf.Lerp(-6.55f, 6.55f, t), .95f * Mathf.Sin(t * Mathf.PI));
            }).ToArray();
            Stroke(batch, ceiling, Ink.BlueSteel, .27f, .015f, arc);
            for (int index = 1; index < arc.Length; index++)
            {
                var start = arc[index - 1];
                var end = arc[index];
                batch.Line(ceiling, index < 4 || index > 13 ? Ink.WarmLens : Ink.CoolLens,
                    Vector2.Lerp(start, end, .10f), Vector2.Lerp(start, end, .91f), .11f, .022f);
            }
        }

        private static void QuotaApron(Batch batch, RaidPlan.Room room)
        {
            // The stock quota reserves x >= 6 m. All signs and mechanical
            // dressing here terminate at the loading-bay roof's inner side.
            var floor = Deck(room.Module.Floors[0].Pose);
            var turn = Shift(floor, 0, 4.38f);
            Outline(batch, turn, Ink.Amber, 4.97f, 2.48f, .059f);
            for (int side = -1; side <= 1; side += 2)
            {
                var track = Shift(floor, side * 1.81f, 1.79f);
                Fill(batch, track, Ink.Rubber, .115f, 2.71f);
                Fill(batch, track, Ink.Amber, .052f, 2.71f, .001f);
                var head = Shift(track, 0, .72f);
                Stroke(batch, head, Ink.Amber, .054f, .001f,
                    new Vector2(-.20f, -.09f), new Vector2(0, .12f), new Vector2(.20f, -.09f));
            }

            string station = $"Q{room.Index % 100:D2}";
            Code(batch, Shift(turn, 0, .32f), station, 1.75f, .87f, Ink.Amber);
            float sideWall = RaidExtraction.Width / 2 - GrayBuilder.Skirt;
            var wall = Surface(new Vector3(3.44f, 4.15f, sideWall), Vector3.up, Vector3.back);
            Code(batch, wall, station, 2.38f, 1.50f, Ink.Amber);
            var trunk = Surface(new Vector3(3.0f, 2.82f, sideWall), Vector3.up, Vector3.back);
            Fill(batch, trunk, Ink.Rubber, 4.74f, .34f);
            Fill(batch, trunk, Ink.Conduit, 4.71f, .258f, .011f);
            Fill(batch, Shift(trunk, 0, .067f), Ink.Rim, 4.65f, .031f, .014f);
            foreach (float x in new[] { -2.13f, -.74f, .74f, 2.13f })
                Fill(batch, Shift(trunk, x, 0), Ink.Amber, .046f, .27f, .018f);
        }

        private static void LabelDocks(Batch batch, GrayModule module)
        {
            int index = 1;
            foreach (Dock dock in module.Docks.Where(d => (d.Use & DockUse.Physical) != 0))
            {
                var frame = dock.Threshold;
                var floor = Surface(Vector(frame.Point(-.75f, 0)), -Vector(frame.Forward), Vector(frame.Up));
                Code(batch, floor, index.ToString("D2"), .80f, .47f, Ink.CoolLens);
                foreach (float side in new[] { -1f, 1f })
                    Fill(batch, Shift(floor, side * (dock.Opening.X * .5f - .32f), 0), Ink.Amber, .095f, .67f);
                index++;
            }
        }

        private static void StraightPassage(Batch batch, GrayModule module)
        {
            float length = module.Plan.Y;
            float wall = module.Plan.X / 2 - GrayBuilder.Skirt;
            LabelDocks(batch, module);
            foreach (float side in new[] { -1f, 1f })
            {
                var baseboard = Surface(new Vector3(length / 2, .70f, side * wall), Vector3.up, -side * Vector3.forward);
                Fill(batch, baseboard, Ink.Rubber, length - 1.06f, .38f);
                Fill(batch, baseboard, Ink.BlueSteel, length - 1.12f, .286f, .004f);
                Fill(batch, Shift(baseboard, 0, -.055f), Ink.Rim, length - 1.19f, .028f, .009f);
                int bays = length > 10f ? 6 : 3;
                float spacing = (length - 1.29f) / bays;
                for (int panel = 0; panel < bays; panel++)
                {
                    float offset = (panel - (bays - 1) / 2f) * spacing;
                    Face recess = Shift(baseboard, offset, .026f);
                    Fill(batch, recess, Ink.Rubber, spacing - .16f, .209f, .007f);
                    Fill(batch, recess, panel == 0 && side < 0 ? Ink.WarmLens : Ink.CoolLens,
                        spacing - .26f, .116f, .009f);
                    Fill(batch, Shift(recess, 0, .044f), Ink.Plaster, spacing - .30f, .019f, .010f);
                }

                var roof = Surface(new Vector3(length / 2, GrayBuilder.Height, side * wall * .64f),
                    Vector3.right, Vector3.down);
                Fill(batch, roof, Ink.BlueSteel, .11f, length - 1.24f);
                for (float station = 1.52f; station < length - 1.28f; station += 2.36f)
                {
                    var lamp = Shift(roof, 0, station - length / 2);
                    Fill(batch, lamp, Ink.Rubber, .35f, .55f, .008f);
                    Fill(batch, lamp, station < 2f ? Ink.WarmLens : Ink.CoolLens, .23f, .39f, .015f);
                }
            }
        }

        private static void BranchPassage(Batch batch, GrayModule module, ILookup<string, Collider> shapes)
        {
            LabelDocks(batch, module);
            switch (module.Id)
            {
                case "elbow-corridor":
                    // One connected cable run turns with the outside of the bend.
                    CableRun(batch, new Vector3(4.66f, 3.17f, -3), Vector3.forward, 8.68f);
                    CableRun(batch, new Vector3(9, 3.17f, 3.375f), Vector3.left, 12.75f);
                    break;
                case "junction-corridor":
                    CableRun(batch, new Vector3(7.5f, 3.16f, -3.5f), Vector3.forward, 13.6f);
                    ServiceSpur(batch);
                    break;
                case "upper-exit-corridor":
                    CableRun(batch, new Vector3(12, 3.16f, 3.5f), Vector3.back, 22.1f);
                    CableRun(batch, new Vector3(9, 3.16f, -9), Vector3.forward, 8.9f);
                    Sweep(batch, module.Geometry.Where(b => shapes[b.Id].Any(VisibleShape)), "turn-");
                    break;
            }
        }

        private static void CableRun(Batch batch, Vector3 center, Vector3 inward, float length)
        {
            var face = Surface(center, Vector3.up, inward);
            Fill(batch, face, Ink.Rubber, length, .225f);
            Fill(batch, face, Ink.Conduit, length, .172f, .008f);
            Fill(batch, Shift(face, 0, .052f), Ink.Rim, length, .028f, .012f);
            for (float x = -length / 2 + .36f; x < length / 2 - .14f; x += 1.45f)
            {
                var clamp = Shift(face, x, 0);
                Fill(batch, clamp, Ink.Rubber, .066f, .244f, .014f);
                Fill(batch, clamp, Ink.Tile, .061f, .201f, .019f);
            }
        }

        private static void ServiceSpur(Batch batch)
        {
            const float innerLeft = 4.25f, innerRight = 10.75f;
            const float start = 3.75f, end = 10.5f;
            foreach (float x in new[] { innerLeft, innerRight })
            {
                var inward = x == innerLeft ? Vector3.right : Vector3.left;
                float length = end - start - .06f;
                // The structural walls carry the plaster. Keep the service run and vault fittings.
                CableRun(batch, new Vector3(x, 2.22f, (start + end) / 2), inward, length - .19f);
            }

            foreach (float z in new[] { 4.77f, 7.25f, 9.75f })
            {
                // The shoulder returns stay outside the 4.5 m freight opening.
                // At the crown the rib rides on the existing ceiling face.
                var vault = Surface(new Vector3(7.5f, 0, z), Vector3.up, Vector3.back);
                var profile = new[] { new Vector2(-3.24f, 3.20f),
                    new Vector2(-3.16f, 3.59f), new Vector2(-2.96f, 3.82f),
                    new Vector2(-2.70f, 3.95f), new Vector2(-2.31f, 3.975f),
                    new Vector2(2.31f, 3.975f), new Vector2(2.70f, 3.95f),
                    new Vector2(2.96f, 3.82f), new Vector2(3.16f, 3.59f), new Vector2(3.24f, 3.20f) };
                Stroke(batch, vault, Ink.Plaster, .065f, 0f, profile);
                var obverse = Surface(vault.Origin, Vector3.up, Vector3.forward);
                Stroke(batch, obverse, Ink.Plaster, .065f, 0f, profile);
                var ceiling = Surface(new Vector3(7.5f, GrayBuilder.Height, z), Vector3.forward, Vector3.down);
                Fill(batch, ceiling, Ink.Plaster, 4.62f, .14f);
                Fill(batch, ceiling, z < 6 ? Ink.WarmLens : Ink.CoolLens, 1.48f, .052f, .009f);
            }
        }

        private static void Trench(Batch batch, GrayModule module)
        {
            LabelDocks(batch, module);
            foreach (Block block in module.Geometry.Where(b => b.Id is "west-deck-near" or "east-deck-near"))
            {
                var (min, max) = block.Bounds;
                float centerZ = (min.Z + max.Z) / 2;
                float gratingX = block.Id.StartsWith("west", StringComparison.Ordinal) ?
                    max.X - .99f : min.X + .99f;
                var grate = Surface(new Vector3(gratingX, max.Y, centerZ), Vector3.right, Vector3.up);
                Fill(batch, grate, Ink.Rubber, .83f, 1.42f);
                Outline(batch, grate, Ink.Rim, .88f, 1.47f, .035f);
                for (int slot = -6; slot <= 6; slot++)
                    Fill(batch, Shift(grate, 0, slot * .102f), Ink.BlueSteel, .76f, .023f, .004f);
                foreach (float x in new[] { -.34f, -.17f, 0, .17f, .34f })
                    Fill(batch, Shift(grate, x, 0), Ink.Rim, .021f, 1.37f, .006f);
            }

            // The line and accessible isolation wheels are on the near wall;
            // the shutter guides and the inspection plank retain their silhouettes.
            const float serviceWall = -6.75f;
            foreach (float x in new[] { 2.63f, 15.85f })
            {
                var wall = Surface(new Vector3(x, 1.26f, serviceWall), Vector3.up, Vector3.forward);
                Fill(batch, wall, Ink.BlueSteel, 1.29f, .74f);
                Outline(batch, wall, Ink.Rim, 1.37f, .82f, .029f);
                Fill(batch, wall, Ink.Conduit, 1.18f, .105f, .010f);
                for (int flange = -1; flange <= 1; flange += 2)
                {
                    var collar = Shift(wall, flange * .40f, 0);
                    Fill(batch, collar, Ink.Rubber, .11f, .215f, .015f);
                    Fill(batch, collar, Ink.Amber, .05f, .196f, .022f);
                }
                var wheel = Enumerable.Range(0, 17).Select(i =>
                {
                    float angle = i * 2 * Mathf.PI / 16;
                    return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .228f;
                }).ToArray();
                Stroke(batch, wall, Ink.Amber, .047f, .024f, wheel);
                Fill(batch, wall, Ink.Amber, .36f, .042f, .026f);
                Fill(batch, wall, Ink.Amber, .044f, .36f, .026f);
                Fill(batch, wall, Ink.Rim, .12f, .11f, .030f);
                Fill(batch, Shift(wall, 0, .55f), Ink.WarmLens, .64f, .054f, .012f);
            }

            var bypass = Surface(new Vector3(MaintenanceTrench.Length / 2, 2.48f, 6.75f),
                Vector3.up, Vector3.back);
            Fill(batch, bypass, Ink.BlueSteel, 6.8f, .20f);
            Fill(batch, bypass, Ink.CoolLens, 6.65f, .064f, .007f);
        }

        private static void SphereGuidance(Batch batch, GrayModule module)
        {
            LabelDocks(batch, module);
            // Keep the authored shell segmentation. Deck cues are keyed to the
            // installed physical pieces, so disabled openings have no false route.
            Block diameter = module.Geometry.Single(block => block.Id == "diameter-deck");
            var (min, max) = diameter.Bounds;
            int mask = SphericalChamber.GetMask(module.Id);
            for (int side = -1; side <= 1; side += 2)
            {
                int branchMask = side > 0 ? SphericalChamber.UpperMask : SphericalChamber.LowerMask;
                for (float x = min.X + 1.35f; x < max.X - .69f; x += 1.47f)
                {
                    if ((mask & branchMask) != 0 && Mathf.Abs(x) < SphericalChamber.HalfWidth + .45f)
                        continue;
                    var guide = Surface(new Vector3(x, max.Y, side * (SphericalChamber.HalfWidth - .225f)),
                        Vector3.right, Vector3.up);
                    Fill(batch, guide, Ink.BlueSteel, .14f, .71f);
                    Fill(batch, guide, Ink.CoolLens, .066f, .585f, .006f);
                }
            }

            foreach (Block sill in module.Geometry.Where(block => block.Id.EndsWith("-sill-deck", StringComparison.Ordinal)))
            {
                SpanStripe(batch, sill, true, .07f, .090f, Ink.BlueSteel);
                SpanStripe(batch, sill, true, .075f, .084f, Ink.CoolLens, .003f);
                SpanStripe(batch, sill, true, .910f, .930f, Ink.BlueSteel);
                SpanStripe(batch, sill, true, .916f, .925f, Ink.CoolLens, .003f);
            }
        }

        private static void RibbonStations(Batch batch, GrayModule module)
        {
            LabelDocks(batch, module);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int column = -1; column <= 1; column++)
                {
                    float x = column * 10.58f;
                    float lowerY = side < 0 ? 4.12f : 5.28f;
                    float upperY = side < 0 ? 8.85f : 9.22f;
                    var wall = Surface(new Vector3(x, lowerY, side * MobiusHall.HalfInteriorZ),
                        Vector3.up, -side * Vector3.forward);
                    WallPractical(batch, wall, side < 0 ? Ink.WarmLens : Ink.CoolLens);
                    WallPractical(batch, Shift(wall, 0, upperY - lowerY), Ink.CoolLens);
                }
            }

            // Binders are marked in front of the existing instrument hulls.
            // The authored warm tripods continue to separate the twist from the room.
            foreach (var (id, center, span) in MobiusHall.WorkstationHulls)
            {
                if (id is not ("mobius-west-worktable" or "mobius-east-worktable" or "mobius-monitoring")) continue;
                bool monitor = id == "mobius-monitoring";
                float z = center.Z + (monitor ? -1 : 1) * (span.Z / 2 + .66f);
                var face = Surface(new Vector3(center.X, 0, z), monitor ? Vector3.forward : Vector3.back, Vector3.up);
                string code = monitor ? "M03" : center.X < 0 ? "M01" : "M02";
                Outline(batch, face, Ink.BlueSteel, 2.10f, .84f, .044f);
                Code(batch, face, code, 1.37f, .55f, monitor ? Ink.Teal : Ink.CoolLens);
            }
        }

        private static void WallPractical(Batch batch, Face face, Ink lens)
        {
            Fill(batch, face, Ink.BlueSteel, 3.64f, .71f);
            Fill(batch, face, Ink.Rubber, 3.48f, .57f, .006f);
            Fill(batch, face, lens, 3.32f, .44f, .011f);
            foreach (float slot in new[] { -1.10f, 0, 1.10f })
                Fill(batch, Shift(face, slot, 0), Ink.BlueSteel, .028f, .48f, .016f);
            Fill(batch, Shift(face, 0, -.22f), Ink.Rim, 3.33f, .027f, .017f);
        }

        private static void CurvedPassage(Batch batch, GrayModule module, ILookup<string, Collider> shapes)
        {
            LabelDocks(batch, module);
            Sweep(batch, module.Geometry.Where(b => shapes[b.Id].Any(VisibleShape)),
                "bridge-", "helix-", "west-bridge-", "east-bridge-");
        }

        private static void Sweep(Batch batch, IEnumerable<Block> geometry, params string[] prefixes)
        {
            int roofIndex = 0;
            foreach (Block span in geometry)
            {
                if (span.Vertices.Length != 8 ||
                    !prefixes.Any(prefix => span.Id.StartsWith(prefix, StringComparison.Ordinal))) continue;
                if (span.Id.EndsWith("-deck", StringComparison.Ordinal))
                {
                    SpanStripe(batch, span, true, .045f, .070f, Ink.BlueSteel);
                    SpanStripe(batch, span, true, .930f, .955f, Ink.BlueSteel);
                }
                else if (span.Id.EndsWith("-roof", StringComparison.Ordinal))
                {
                    SpanStripe(batch, span, false, .237f, .287f, Ink.Conduit);
                    SpanStripe(batch, span, false, .282f, .294f, Ink.Rim, .005f);
                    if (roofIndex++ % 5 < 2)
                    {
                        SpanStripe(batch, span, false, .723f, .793f, Ink.BlueSteel);
                        SpanStripe(batch, span, false, .741f, .775f, Ink.CoolLens, .008f);
                    }
                }
            }
        }

        private static void SpanStripe(Batch batch, Block span, bool floor, float near, float far, Ink ink, float lift = 0f)
        {
            // Use the original span's diagonal on twisted surfaces. Bilinear
            // interpolation can bury one side of a strip below the actual face.
            Vector3 a = Vector(span.Vertices[floor ? 1 : 3]);
            Vector3 b = Vector(span.Vertices[floor ? 5 : 7]);
            Vector3 c = Vector(span.Vertices[floor ? 6 : 4]);
            Vector3 d = Vector(span.Vertices[floor ? 2 : 0]);
            Vector3 inward = (a - Vector(span.Vertices[floor ? 0 : 2])).normalized;
            if (!floor) (near, far) = (1 - far, 1 - near);
            Vector3 diagonalNear = Vector3.Lerp(a, c, near);
            Vector3 diagonalFar = Vector3.Lerp(a, c, far);
            Quad(Vector3.Lerp(a, d, near), diagonalNear, diagonalFar, Vector3.Lerp(a, d, far));
            Quad(diagonalNear, Vector3.Lerp(b, c, near), Vector3.Lerp(b, c, far), diagonalFar);

            void Quad(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
            {
                Vector3 normal = Vector3.Cross(q - p, r - p).normalized;
                if (Vector3.Dot(normal, inward) < 0) normal = -normal;
                var offset = normal * (.004f + lift);
                batch.Append(p + offset, q + offset, r + offset, s + offset, normal, ink);
            }
        }
    }
}
