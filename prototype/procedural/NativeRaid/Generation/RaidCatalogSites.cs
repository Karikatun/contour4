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
        // New pools are physically distinct from the five established sites. Sizes are Unity XYZ.
        private static readonly Site[] SmallLab = {
            new("corner-cube", "Catalog optical spares table", new Vector3(18,.912f,-5.9f), new Vector3(.08f,.04365f,.07968f), new Vector3(18,0,-3.9f)),
            new("free-fall-photogate", "Catalog optical spares table", new Vector3(18,.912f,-5.9f), new Vector3(.13227f,.07448f,.14f), new Vector3(18,0,-3.9f)),
            new("pirani-gauge-head", "Catalog optical spares table", new Vector3(18,.912f,-5.9f), new Vector3(.12001f,.05557f,.05490f), new Vector3(18,0,-3.9f)),
            new("bullseye-level", "Catalog optical spares table", new Vector3(18,.912f,-5.9f), new Vector3(.09f,.01772f,.08991f), new Vector3(18,0,-3.9f)),
        };
        private static readonly Site[] MediumLab = {
            new("atwood-machine", "Catalog low lab table", new Vector3(5.8f,.512f,-5.5f), new Vector3(.52118f,.80001f,.46213f), new Vector3(5.8f,0,-3.8f)),
            new("kater-pendulum", "Catalog low lab table", new Vector3(5.8f,.512f,-5.5f), new Vector3(.53758f,.95f,.53904f), new Vector3(5.8f,0,-3.8f)),
            new("vertical-seismometer", "Catalog low lab table", new Vector3(5.8f,.512f,-5.5f), new Vector3(.5f,.27881f,.37610f), new Vector3(5.8f,0,-3.8f)),
            new("hydrostatic-balance", "Catalog low lab table", new Vector3(5.8f,.512f,-5.5f), new Vector3(.55001f,.48840f,.28275f), new Vector3(5.8f,0,-3.8f)),
        };
        private static readonly Site[] LargeLab = {
            new("vacuum-drop-column", "ground", new Vector3(19,.012f,-2.5f), new Vector3(1.25976f,1.75f,1.13585f), new Vector3(20.7f,0,-2.5f)),
            new("cavendish-balance", "ground", new Vector3(19,.012f,-2.5f), new Vector3(1.80001f,1.08443f,.82291f), new Vector3(20.7f,0,-2.5f)),
        };
        private static readonly Site Cryostat = new("anomaly-cryostat", "Catalog cryostat cradle",
            new Vector3(3,.092f,-2.2f), new Vector3(1.25f,2.55f,1.10f), new Vector3(3,0,-.6f));
        internal static readonly string[] LaboratoryKeys = SmallLab.Concat(MediumLab).Concat(LargeLab)
            .Select(site => site.Key).Append(Cryostat.Key).ToArray();
        private static int Choice(int seed, uint salt, int count) {
            uint x = unchecked((uint)seed) ^ salt;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (int)(x % (uint)count);
        }
        private static IEnumerable<Site> Selected(string id, int seed) {
            foreach (var site in Established(id)) yield return site;
            if (id == "calibration-sector") {
                yield return SmallLab[Choice(seed,0x2C41u,SmallLab.Length)];
                yield return MediumLab[Choice(seed,0x27A9u,MediumLab.Length)];
                int pick = Choice(seed,0x30C4u,16);
                if (pick < LargeLab.Length) yield return LargeLab[pick]; // one large lab item in 2/16 seeds.
            }
            if (id.StartsWith("sample-storage", StringComparison.Ordinal) && Choice(seed,0x10C4u,16)==0)
                yield return Cryostat; // rare separate cradle; original storage display is retained.
        }
        private static IEnumerable<Site> Definitions(string id) => Established(id).Concat(
            id == "calibration-sector" ? SmallLab.Concat(MediumLab).Concat(LargeLab) :
            id.StartsWith("sample-storage", StringComparison.Ordinal) ? new[] { Cryostat } : Array.Empty<Site>());
        private static IEnumerable<Site> Established(string id) => id == "calibration-sector" ? Calibration :
            id.StartsWith("sample-storage", StringComparison.Ordinal) ? Storage :
            id == "shaft-plant" ? Shaft : Array.Empty<Site>();

        internal static void Dress(GrayModule module)
        {
            foreach (var site in Definitions(module.Id).GroupBy(site => site.Support).Select(group => group.First()))
            {
                if (site.Support == "ground") continue; // Native ground support; never a table or pedestal.
                Vector3 size = site.Key == "anomaly-cryostat" ? new Vector3(1.5f,.08f,1.35f) : site.Support == "Catalog low lab table" ? new Vector3(1.1f,.08f,.8f) : site.Key == "gravity-lens" ? new Vector3(1.65f,.24f,.85f) :
                    site.Key == "gimbal-gyroscope" ? new Vector3(1.65f,.08f,.9f) : new Vector3(1.1f,.08f,.7f);
                Vector3 center = site.Bottom - Vector3.up * (.012f + size.y / 2);
                Add(module, site.Support, center, size);
                if (site.Key is "gravity-lens" or "anomaly-cryostat") continue;
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
                var sites = Selected(room.Plan.Module.Id,raid.Plan.Seed).ToArray();
                if (sites.Length == 0) continue;
                var root = RaidAssembly.Child(room.Root, "Catalog sites");
                foreach (var site in sites) RaidAssembly.Child(root, "valuable__" + site.Key).localPosition = site.Bottom;
            }
        }
        internal static IEnumerable<Transform> Sites(RaidAssembly raid) => raid.Rooms
            .Where(room => room.Root.Find("Catalog sites"))
            .SelectMany(room => room.Root.Find("Catalog sites").Cast<Transform>());

        internal static RaidLootPlacement Placement(RaidAssembly raid, Transform marker, string key)
        {
            var room = raid.Rooms.Single(room => marker.parent.parent == room.Root);
            var site = Selected(room.Plan.Module.Id, raid.Plan.Seed).Single(site => site.Key == key);
            var supports = room.Colliders.Where(collider => collider.name == site.Support);
            var interior = RaidLootPlacement.StructuralBounds(room);
            foreach (var collider in supports)
            {
                var placement = new RaidLootPlacement(room.Root, interior, collider, room.Floor.FloorId);
                if (placement.Supports(marker.position, marker.up)) return placement;
            }
            throw new InvalidOperationException("Missing catalog support " + key);
        }

        internal static bool OverlapsReservedLoot(RaidLootSlot slot) => slot.Room.Root.Find("Catalog sites") &&
            Definitions(slot.Room.Plan.Module.Id).Any(site =>
                slot.LocalBounds.Intersects(new Bounds(site.Bottom + Vector3.up * (site.Size.y / 2f), site.Size)));

        internal static void Validate(RaidAssembly raid)
        {
            Physics.SyncTransforms();
            var expected = raid.Rooms.SelectMany(room => Selected(room.Plan.Module.Id,raid.Plan.Seed)).Select(site => "valuable__"+site.Key).OrderBy(key=>key).ToArray();
            if (!Sites(raid).Select(site=>site.name).OrderBy(key=>key).SequenceEqual(expected) || expected.Distinct().Count()!=expected.Length)
                throw new InvalidOperationException("Procedural catalog selected sites differ from deterministic unique definitions.");
            foreach (var room in raid.Rooms)
            foreach (var site in Definitions(room.Plan.Module.Id))
            {
                Clear(room.Root, site.Key, site.Bottom + Vector3.up * site.Size.y / 2, site.Size);
                var point = room.Root.TransformPoint(site.Bottom);
                if (!Physics.Raycast(point + room.Root.up * .025f, -room.Root.up, out var hit, .1f, ~0, QueryTriggerInteraction.Ignore) ||
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
            ValidateLaboratoryRoutes(raid);
        }
        internal static List<(Vector3 Point, Quaternion Rotation)> LaboratoryRoute(RaidAssembly raid, string key) {
            bool cryo = key == Cryostat.Key;
            var room = raid.Rooms.Single(r=>cryo ? r.Plan.Module.Id.StartsWith("sample-storage",StringComparison.Ordinal) : r.Plan.Module.Id=="calibration-sector");
            var result = new List<(Vector3,Quaternion)>();
            void Point(Vector3 p, Vector3 f) => result.Add((room.Root.TransformPoint(p),room.Root.rotation*Quaternion.LookRotation(f,Vector3.up)));
            if (cryo) {
                Point(new Vector3(3,0,-2.2f),Vector3.left);
                Point(new Vector3(3,0,0),Vector3.left);
                Point(new Vector3(1.8f,0,0),Vector3.left);
            } else if (LargeLab.Any(site=>site.Key==key)) {
                Point(new Vector3(19,0,-2.5f),Vector3.back);
                Point(new Vector3(19,0,-4.5f),Vector3.left);
                Point(new Vector3(7.25f,0,-4.5f),Vector3.left);
                Point(new Vector3(3,0,0),Vector3.left);
                Point(new Vector3(1.8f,0,0),Vector3.left);
            } else if (SmallLab.Any(site=>site.Key==key)) {
                Point(new Vector3(18,0,-3.9f),Vector3.left);
                Point(new Vector3(7.25f,0,-4.5f),Vector3.left);
                Point(new Vector3(3,0,0),Vector3.left);
                Point(new Vector3(1.8f,0,0),Vector3.left);
            } else {
                Point(new Vector3(5.8f,0,-3.8f),Vector3.left);
                Point(new Vector3(3,0,0),Vector3.left);
                Point(new Vector3(1.8f,0,0),Vector3.left);
            }
            AppendFreightToEntrance(raid,room.Plan,result);
            return result;
        }
        internal static void ValidateLaboratoryRoutes(RaidAssembly raid, bool allEligible = false) {
            var sites = allEligible ? SmallLab.Concat(MediumLab).Concat(LargeLab).Append(Cryostat) :
                raid.Rooms.SelectMany(room=>Selected(room.Plan.Module.Id,raid.Plan.Seed)).Where(site=>LaboratoryKeys.Contains(site.Key));
            foreach (var site in sites) {
                var route=LaboratoryRoute(raid,site.Key); int samples=0;
                for(int i=1;i<route.Count;i++) {
                    var a=route[i-1];var b=route[i];
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a.Point,b.Point)/.15f),Mathf.CeilToInt(Quaternion.Angle(a.Rotation,b.Rotation)/5f));
                    for(int n=0;n<=count;n++) {
                        float t=n/(float)count;var feet=Vector3.Lerp(a.Point,b.Point,t);var rotation=Quaternion.Slerp(a.Rotation,b.Rotation,t);var up=rotation*Vector3.up;
                        var hits=Physics.OverlapBox(feet+up*(.25f+site.Size.y/2),site.Size/2,rotation,~0,QueryTriggerInteraction.Ignore)
                            .Where(c=>c.transform.IsChildOf(raid.transform)).ToArray();
                        if(hits.Length>0)throw new InvalidOperationException($"Lab route {site.Key} seed={raid.Plan.Seed} sample={samples} feet={feet}: "+string.Join(",",hits.Select(c=>c.name)));
                        if(Vector3.Dot(feet-raid.Plan.Entrance.Sill,raid.Plan.Entrance.Outward)<-.01f &&
                            !Physics.RaycastAll(feet+up*.55f,-up,1.1f,~0,QueryTriggerInteraction.Ignore).Any(hit=>hit.collider.transform.IsChildOf(raid.transform)&&Vector3.Dot(hit.normal,up)>=.6f))
                            throw new InvalidOperationException("Lab route lacks support "+site.Key+" "+feet);
                        samples++;
                    }
                }
                Debug.Log($"[Contour4 Catalog] seed={raid.Plan.Seed}: {site.Key} upright XYZ={site.Size} carry route to stock entrance PASS, {samples} samples; native grip/sale pending.");
            }
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
