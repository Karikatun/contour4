using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace Contour4.NativeRaid.Runtime
{
    // Production-only dressing: inserted before both native and query navigation collect colliders.
    internal static class RaidCatalogSites
    {
        private readonly struct Site
        {
            internal readonly string Key, Support;
            internal readonly Vector3 Bottom, Size, Approach;
            internal Site(string key, string support, Vector3 bottom, Vector3 size, Vector3 approach)
            { Key = key; Support = support; Bottom = bottom; Size = size; Approach = approach; }
        }
        private static readonly Site[] Calibration = {
            new("portable-gravimeter", "Catalog side table", new Vector3(3,.912f,-5.5f), new Vector3(.46f,.34f,.32f), new Vector3(3,0,-4)),
            new("gimbal-gyroscope", "Catalog wide measuring table", new Vector3(17.5f,.912f,5.8f), new Vector3(1.18f,.76f,.55f), new Vector3(17.5f,0,3.5f)),
            new("gravity-lens", "Catalog low plinth", new Vector3(7,.252f,3.5f), new Vector3(1.35f,.95f,.52f), new Vector3(4.7f,0,3.5f)),
        };
        private static readonly Site[] Storage = {
            new("mobius-strip", "Catalog demonstration shelf", new Vector3(3,.852f,-5.5f), new Vector3(.52f,.43f,.22f), new Vector3(3,0,-4)),
        };
        private static readonly Site[] Shaft = {
            new("field-core", "ground", new Vector3(14,.012f,1.35f), new Vector3(2.35f,1.35f,1.55f), new Vector3(11.5f,0,1.35f)),
        };
        private static IEnumerable<Site> Definitions(string id) => id == "calibration-sector" ? Calibration :
            id.StartsWith("sample-storage", StringComparison.Ordinal) ? Storage :
            id == "shaft-plant" ? Shaft : Array.Empty<Site>();

        internal static void Dress(GrayModule module)
        {
            foreach (var site in Definitions(module.Id))
            {
                if (site.Key == "field-core") continue; // Native ground support; never a table or pedestal.
                Vector3 size = site.Key == "gravity-lens" ? new Vector3(1.65f,.24f,.85f) :
                    site.Key == "gimbal-gyroscope" ? new Vector3(1.65f,.08f,.9f) : new Vector3(1.1f,.08f,.7f);
                Vector3 center = site.Bottom - Vector3.up * (.012f + size.y / 2);
                Add(module, site.Support, center, size);
                if (site.Key == "gravity-lens") continue;
                float height = center.y - size.y / 2;
                foreach (float x in new[] { -.5f, .5f })
                foreach (float z in new[] { -.5f, .5f })
                    Add(module, site.Support + " leg " + x + " " + z,
                        new Vector3(center.x + x * (size.x - .12f), height / 2, center.z + z * (size.z - .12f)),
                        new Vector3(.06f,height,.06f));
            }
        }
        private static void Add(GrayModule module, string name, Vector3 center, Vector3 size)
        {
            var min = center - size / 2; var max = center + size / 2;
            module.Geometry.Add(Block.Box(name, BlockRole.Fixture, GrayBuilder.Level,
                min.x,max.x,min.y,max.y,min.z,max.z));
        }
        internal static void Mark(RaidAssembly raid)
        {
            foreach (var room in raid.Rooms)
            {
                var sites = Definitions(room.Plan.Module.Id).ToArray();
                if (sites.Length == 0) continue;
                var root = RaidAssembly.Child(room.Root, "Catalog sites");
                foreach (var site in sites) RaidAssembly.Child(root, "valuable__" + site.Key).localPosition = site.Bottom;
            }
        }
        internal static IEnumerable<Transform> Sites(RaidAssembly raid) => raid.Rooms
            .Where(room => room.Root.Find("Catalog sites"))
            .SelectMany(room => room.Root.Find("Catalog sites").Cast<Transform>());

        internal static void Validate(RaidAssembly raid)
        {
            Physics.SyncTransforms();
            if (Sites(raid).Count() != 5) throw new InvalidOperationException("Procedural catalog must contain five distinct sites including the lower-shaft field core.");
            foreach (var room in raid.Rooms)
            foreach (var site in Definitions(room.Plan.Module.Id))
            {
                Clear(room.Root, site.Key, site.Bottom + Vector3.up * site.Size.y / 2, site.Size);
                var marker = room.Root.Find("Catalog sites/valuable__" + site.Key);
                if (!Physics.Raycast(marker.position + marker.up * .025f, -marker.up, out var hit, .1f, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider.name != site.Support) throw new InvalidOperationException("Missing catalog support " + site.Key);
                Clear(room.Root, site.Key + " approach", site.Approach + Vector3.up * .97f, new Vector3(1.7f,1.85f,1.7f));
                foreach (var landmark in room.Plan.Module.Landmarks.Where(entry => entry.Key is "equipment" or "containers" or "hand-samples"))
                {
                    var p = RaidPlan.Vector(landmark.Value) + Vector3.up * .65f;
                    var hits = Physics.OverlapBox(room.Root.TransformPoint(p), Vector3.one * .624f, room.Root.rotation, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(c => c.name.StartsWith("Catalog ", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Catalog furniture overlaps original loot volume " + landmark.Key);
                }
                Debug.Log($"[Contour4 Catalog] seed={raid.Plan.Seed} {site.Key}: footprint/support/approach/ordinary loot clear.");
            }
            // Lens eligibility is deliberate: only the audited physical cart spine, never the door portal shortcut.
            ValidateLensRoute(raid);
            ValidateFieldCoreRoute(raid);
        }
        private static void Clear(Transform frame, string label, Vector3 center, Vector3 size)
        {
            var hits = Physics.OverlapBox(frame.TransformPoint(center), size / 2 - Vector3.one * .002f,
                frame.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length > 0) throw new InvalidOperationException(label + " intersects " + string.Join(",", hits.Select(c => c.name)));
        }
        internal static List<(Vector3 Point, Quaternion Rotation)> LensRoute(RaidAssembly raid)
        {
            var calibration = raid.Rooms.Single(room => room.Plan.Module.Id == "calibration-sector");
            var result = new List<(Vector3,Quaternion)>();
            void Point(RaidPlan.Room room, Vector3 point, Vector3 forward) => result.Add((room.Frame.position + room.Frame.rotation * point,
                room.Frame.rotation * Quaternion.LookRotation(forward, Vector3.up)));
            // The loaded cart backs away from the stand into a full turning bay before travelling to the entry.
            Point(calibration.Plan,new Vector3(4.7f,0,3.5f),Vector3.left);
            Point(calibration.Plan,new Vector3(3,0,3.5f),Vector3.left);
            Point(calibration.Plan,new Vector3(3,0,0),Vector3.left);
            Point(calibration.Plan,new Vector3(1.8f,0,0),Vector3.left);
            AppendFreightToEntrance(raid, calibration.Plan, result);
            return result;
        }
        private static void AppendFreightToEntrance(RaidAssembly raid, RaidPlan.Room current, List<(Vector3 Point, Quaternion Rotation)> result)
        {
            while (current != raid.Plan.Entrance.Room)
            {
                var passage = raid.Plan.Freight.Single(edge => edge.To.Room == current);
                // Sample both actual dock frames, including the room boundary, rather than trusting coincident names.
                result.Add((passage.To.Pad(1.5f), Quaternion.LookRotation(passage.To.Outward, passage.To.Up)));
                if (passage.Fold != null)
                    foreach (var knot in passage.Fold.Module.Routes[0].Knots.Reverse())
                        result.Add((passage.Fold.Point(knot.Position), Quaternion.LookRotation(
                            -passage.Fold.Direction(knot.Forward), passage.Fold.Direction(knot.Up))));
                result.Add((passage.From.Pad(1.5f), Quaternion.LookRotation(-passage.From.Outward, passage.From.Up)));
                current = passage.From.Room;
                var arrival = raid.Plan.Arrival(current);
                var route = RaidPlan.FreightRoute(current, arrival.Dock, passage.From.Dock);
                bool reverse = route.From == arrival.Dock.Id;
                var knots = reverse ? route.Knots.Reverse() : route.Knots.AsEnumerable();
                foreach (var knot in knots)
                {
                    var position = current.Point(knot.Position);
                    // Catalog routes include exterior margins for pairwise checks. The assembled
                    // route already includes the actual adjacent dock or swept bridge instead.
                    if (Vector3.Dot(position - passage.From.Sill, passage.From.Outward) > .001f ||
                        (current != raid.Plan.Entrance.Room && Vector3.Dot(position - arrival.Sill, arrival.Outward) > .001f))
                        continue;
                    result.Add((position, Quaternion.LookRotation(current.Direction(knot.Forward) * (reverse ? -1f : 1f),
                        current.Direction(knot.Up))));
                }
                result.Add((arrival.Pad(1.5f), Quaternion.LookRotation(arrival.Outward, arrival.Up)));
            }
        }
        internal static List<(Vector3 Point, Quaternion Rotation)> FieldCoreRoute(RaidAssembly raid)
        {
            var shaft = raid.Rooms.Single(room => room.Plan.Module.Id == "shaft-plant");
            var result = new List<(Vector3,Quaternion)>();
            var local = shaft.Plan.Module.Routes.Single(route => route.Id == "freight-access");
            foreach (var knot in local.Knots.Reverse())
            {
                // End at the actual lower freight dock; the assembled neighbour owns the exterior.
                if (knot.Position.X < 1.5f) continue;
                result.Add((shaft.Plan.Point(knot.Position), Quaternion.LookRotation(-shaft.Plan.Direction(knot.Forward), shaft.Plan.Direction(knot.Up))));
            }
            AppendFreightToEntrance(raid, shaft.Plan, result);
            return result;
        }
        private static void ValidateFieldCoreRoute(RaidAssembly raid)
        {
            var route = FieldCoreRoute(raid);
            int samples = 0;
            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i-1]; var b = route[i];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a.Point,b.Point) / .15f), Mathf.CeilToInt(Quaternion.Angle(a.Rotation,b.Rotation) / 5f));
                for (int n = 0; n <= count; n++)
                {
                    float t = n/(float)count;
                    Vector3 feet = Vector3.Lerp(a.Point,b.Point,t);
                    Quaternion rotation = Quaternion.Slerp(a.Rotation,b.Rotation,t);
                    Vector3 up = rotation * Vector3.up;
                    // Carry-clearance test, with the catalog envelope lifted 0.25m over the deck.
                    // Native grip, mass and dynamic delivery remain separate in-game checks.
                    var hits = Physics.OverlapBox(feet + up * .925f, new Vector3(1.175f,.675f,.775f), rotation, ~0, QueryTriggerInteraction.Ignore)
                        .Where(c => c.transform.IsChildOf(raid.transform)).ToArray();
                    if (hits.Length > 0) throw new InvalidOperationException($"Field-core carry route seed={raid.Plan.Seed} sample={samples}: " + string.Join(",",hits.Select(c => c.name)));
                    if (Vector3.Dot(feet-raid.Plan.Entrance.Sill,raid.Plan.Entrance.Outward) < -.01f &&
                        !Physics.RaycastAll(feet + up * .55f, -up, 1.1f, ~0, QueryTriggerInteraction.Ignore)
                            .Any(hit => hit.collider.transform.IsChildOf(raid.transform) && Vector3.Dot(hit.normal,up) >= .6f))
                        throw new InvalidOperationException("Field-core route lacks deck support at " + feet);
                    samples++;
                }
            }
            Debug.Log($"[Contour4 Catalog] seed={raid.Plan.Seed}: field-core catalog 2.35x1.35x1.55m carry clearance at 0.25m deck offset to stock entrance PASS, {samples} samples. Native grip/delivery is checked in game.");
        }
        private static void ValidateLensRoute(RaidAssembly raid)
        {
            var route = LensRoute(raid);
            int samples = 0;
            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i-1]; var b = route[i];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a.Point,b.Point) / .15f), Mathf.CeilToInt(Quaternion.Angle(a.Rotation,b.Rotation) / 5f));
                for (int n = 0; n <= count; n++)
                {
                    float t = n/(float)count;
                    Vector3 feet = Vector3.Lerp(a.Point,b.Point,t);
                    Quaternion rotation = Quaternion.Slerp(a.Rotation,b.Rotation,t);
                    Vector3 up = rotation * Vector3.up;
                    // The chassis has wheel clearance. On a compact concave deck, a tangent
                    // box extended into that space would hit the rise in front of the wheels.
                    var hits = Physics.OverlapBox(feet + up * 1.20f, new Vector3(.825f,.95f,1.25f), rotation, ~0, QueryTriggerInteraction.Ignore)
                        .Where(c=>c.transform.IsChildOf(raid.transform)).ToArray();
                    if (hits.Length > 0) throw new InvalidOperationException($"Lens cart path seed={raid.Plan.Seed} sample={samples} at {feet}: " + string.Join(",",hits.Select(c=>c.name)));
                    // Sample every wheel on the authored side of the actual entrance plane.
                    // Beyond that plane support belongs to the stock starter and is verified in game.
                    foreach (float x in new[] { -.65f,.65f }) foreach(float z in new[] { -.85f,.85f })
                    {
                        Vector3 wheel=feet+rotation*new Vector3(x,.55f,z);
                        if(Vector3.Dot(wheel-raid.Plan.Entrance.Sill,raid.Plan.Entrance.Outward)>=-.01f) continue;
                        if (!Physics.Raycast(wheel, -up, out var floor, 1.1f, ~0, QueryTriggerInteraction.Ignore) ||
                            Vector3.Dot(floor.normal, up) < .6f)
                            throw new InvalidOperationException("Lens cart lacks floor support at " + feet);
                    }
                    samples++;
                }
            }
            Debug.Log($"[Contour4 Catalog] seed={raid.Plan.Seed}: lens loaded-cart 1.65x1.90x2.50m physical route to stock entrance PASS, {samples} samples. Native starter/quota is checked in game.");
        }
    }
}
