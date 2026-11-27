using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    internal static partial class PreparedModuleLoot
    {
        private const float Lift = .008f;
        private const float RibbonLane = -1.1f;
        private const string MarkerPrefix = "loot__";

        // Match the dimensions and bottom/front pivot used by RaidLootSites in 0.2.19.
        private enum Size { Tiny, Small, Medium, Big, Wide, Tall, VeryTall }

        private static Vector3 Envelope(Size size) => size switch
        {
            Size.Tiny => new(.25f, .25f, .25f),
            Size.Small => new(.50f, .50f, .50f),
            Size.Medium => new(.825f, 1f, .825f),
            Size.Big => new(1.25f, 1.50f, 1.25f),
            Size.Wide => new(3f, 2.4f, 1.2f),
            Size.Tall => new(2.4f, 3f, 1.2f),
            Size.VeryTall => new(1.25f, 3.5f, 1.25f),
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };

        private readonly struct Site
        {
            internal readonly string Id;
            internal readonly string Floor;
            internal readonly string Support;
            internal readonly LootSurfaceKind Kind;
            internal readonly Size Capacity;
            internal readonly Quaternion Rotation;
            internal readonly Vector3 Bottom;
            internal readonly Vector3 RibbonLaneCenter;
            internal readonly RoomDressing Cabinet;
            internal readonly string Case;

            internal Site(string id, string floor, string support, LootSurfaceKind kind, Size capacity,
                Vector3 bottom, Vector3 forward, Vector3 up, RoomDressing cabinet = null, string cabinetCase = null,
                float lift = Lift, Vector3 ribbonLaneCenter = default)
            {
                Id = id;
                Floor = floor;
                Support = support;
                Kind = kind;
                Capacity = capacity;
                Bottom = bottom + up * lift;
                RibbonLaneCenter = ribbonLaneCenter;
                Rotation = Quaternion.LookRotation(forward, up);
                Cabinet = cabinet;
                Case = cabinetCase;
            }

            internal Vector3 Dimensions => Envelope(Capacity);
            internal Vector3 Up => Rotation * Vector3.up;
            internal Vector3 Forward => Rotation * Vector3.forward;
            internal Vector3 Right => Rotation * Vector3.right;
            internal Vector3 Pivot => Bottom - Forward * (Dimensions.z * .5f);
            internal Volume Item => new(Bottom + Up * (Dimensions.y * .5f), Dimensions * .5f, Rotation);
            internal bool OnRibbon => Support == MobiusHall.Ribbon;
        }

        private static readonly Size[] BenchSizes = { Size.Medium, Size.Small, Size.Tiny };
        private static readonly Size[] RaisedSizes = { Size.Big, Size.Medium, Size.Small, Size.Tiny };
        private static readonly Size[] ShelfSizes = { Size.Small, Size.Tiny };

        internal static int Add(GrayModule module, Transform parent) => Add(module, parent, out _);

        internal static int Add(GrayModule module, Transform parent, out string description)
        {
            if (parent.childCount != 0) throw new InvalidOperationException($"{module.Id}: loot parent must be empty.");
            var sites = Collect(module, parent);
            var report = new ModuleDescription { moduleId = module.Id, transit = Exclusions(module.Id),
                sites = sites.Select(entry => new SiteDescription(entry.Site, entry.Stance, entry.Front)).ToArray() };
            foreach (var (site, stance, front) in sites)
            {
                var marker = new GameObject(MarkerPrefix + site.Id).transform;
                marker.SetParent(parent, false);
                marker.SetLocalPositionAndRotation(site.Pivot, site.Rotation);
                Debug.Log($"CONTOUR4 LOOT SITE {module.Id}|{site.Id}|{site.Floor}|{site.Support}|{site.Kind}|{site.Capacity}|" +
                    $"{Vector(site.Dimensions)}|{Vector(site.Pivot)}|{Vector(site.Up)}|{Vector(site.Forward)}|" +
                    $"{Vector(stance)}|{site.Case ?? "-"}|{front ?? "-"}");
            }
            description = JsonUtility.ToJson(report, true);
            Debug.Log($"CONTOUR4 LOOT MODULE {module.Id}: {sites.Count} candidates. {Exclusions(module.Id)}");
            return sites.Count;
        }

        [Serializable]
        private sealed class ModuleDescription
        {
            public string moduleId;
            public string transit;
            public SiteDescription[] sites;
        }

        [Serializable]
        private sealed class SiteDescription
        {
            public string id, marker, floor, support, surface, capacity, mounting, cabinet, leaf;
            public Vector3 envelope, pivot, up, forward, retrievalFeet;

            internal SiteDescription(Site site, Vector3 stance, string front)
            {
                id = site.Id;
                marker = MarkerPrefix + site.Id;
                floor = site.Floor;
                support = site.Support;
                surface = site.Kind.ToString();
                capacity = site.Capacity.ToString();
                mounting = front == null ? "Loose on fixed support" : "Loose on fixed shelf, closed leaf opened for retrieval";
                cabinet = site.Cabinet?.Name(site.Case);
                leaf = front;
                envelope = site.Dimensions;
                pivot = site.Pivot;
                up = site.Up;
                forward = site.Forward;
                retrievalFeet = stance;
            }
        }

        internal static void Verify(GrayModule module, Transform parent)
        {
            var sites = Collect(module, parent);
            var markers = parent.Cast<Transform>().ToDictionary(marker => marker.name);
            if (markers.Count != sites.Count)
                throw new InvalidOperationException($"{module.Id}: expected {sites.Count} loot markers, found {markers.Count}.");
            foreach (var (site, _, _) in sites)
            {
                if (!markers.TryGetValue(MarkerPrefix + site.Id, out Transform marker) ||
                    Vector3.Distance(marker.localPosition, site.Pivot) > .001f ||
                    Quaternion.Angle(marker.localRotation, site.Rotation) > .03f ||
                    marker.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException($"{module.Id}: invalid loot marker {site.Id}.");
            }
            if (module.Id == MobiusHall.Id)
                VerifyRibbonSpawn(parent, sites.Where(entry => entry.Site.OnRibbon).Select(entry => entry.Site), markers);
            int sphereMask = SphericalChamber.GetMask(module.Id);
            if (sphereMask != 0)
                VerifySphereSpawn(parent.parent, sites.Select(entry => entry.Site), markers, sphereMask);
        }

        private static List<(Site Site, Vector3 Stance, string Front)> Collect(GrayModule module, Transform frame)
        {
            Physics.SyncTransforms();
            var query = new RoomQuery(module, frame);
            var result = new List<(Site Site, Vector3 Stance, string Front)>();
            var accepted = new List<Volume>();
            foreach (var choices in Candidates(module, query))
            foreach (Site site in choices)
            {
                if (accepted.Any(box => box.Intersects(site.Item)) || result.Any(existing =>
                    RoomQuery.Stance(existing.Stance, existing.Site.Up).Intersects(site.Item)) || !query.Fits(site) ||
                    !query.CanRetrieve(site, accepted, out Vector3 stance, out string front)) continue;
                result.Add((site, stance, front));
                accepted.Add(site.Item);
                break;
            }

            // Recheck paths with every accepted spot filled. Removal only clears paths
            // for the entries already reviewed, so a single reverse pass is sufficient.
            for (int i = result.Count - 1; i >= 0; i--)
            {
                Site site = result[i].Site;
                var otherItems = accepted.Where((_, index) => index != i).ToArray();
                if (query.CanRetrieve(site, otherItems, out Vector3 stance, out string front))
                    result[i] = (site, stance, front);
                else
                {
                    result.RemoveAt(i);
                    accepted.RemoveAt(i);
                }
            }

            var missingFloors = module.Floors.Select(floor => floor.Id).Where(floor =>
                !TraversalOnly(module.Id, floor) && result.All(entry => entry.Site.Floor != floor)).ToArray();
            if (missingFloors.Length != 0)
                throw new InvalidOperationException($"{module.Id}: no verified loot sites on {string.Join(", ", missingFloors)}.");
            if (module.Id == MobiusHall.Id)
            {
                if (result.Count(entry => entry.Site.OnRibbon) != 2)
                    throw new InvalidOperationException($"{module.Id}: both ribbon loot sites need support and a clear retrieval path.");
                query.VerifyRibbonCenterLane(accepted);
            }
            int sphereMask = SphericalChamber.GetMask(module.Id);
            if (sphereMask != 0)
                VerifySphereSites(result.Select(entry => entry.Site), sphereMask);
            if (module.Id == MaintenanceTrench.Id)
            {
                var missing = new[] { MaintenanceTrench.SampleSiteId, MaintenanceTrench.SampleReserveSiteId }
                    .Where(id => result.All(entry => entry.Site.Id != id)).ToArray();
                if (missing.Length > 0)
                    throw new InvalidOperationException($"{module.Id}: sample sites failed support, clearance or retrieval: {string.Join(", ", missing)}.");
            }
            return result;
        }

        private static bool TraversalOnly(string module, string floor) =>
            module is "quarter-turn-bridge" or "spiral-corridor" || floor is "bridge" or "plank" ||
            floor.EndsWith("-bridge", StringComparison.Ordinal) ||
            module == "upper-exit-corridor" && floor == "upper-landing";

        private static string Exclusions(string module) => module switch
        {
            "quarter-turn-bridge" or "spiral-corridor" => "The central transit and dock margins occupy the curved deck and both end leads.",
            "upper-exit-corridor" => "The turn is transit only and the upper landing is a full-width dock approach.",
            "u-shaft" => "Both curved decks serve cargo transit. Loot is on the lower floor and fixed side arms.",
            MaintenanceTrench.Id => "The plank crossing, shutter and freight detour remain clear. Two sample sites sit behind the shutter.",
            SphericalChamber.Id => "Two service bays, the outer edges of both branch decks, and two radial shell locations hold loot. The center lanes and dock passages stay clear.",
            _ when SphericalChamber.IsVariant(module) => "Installed service bays and branch decks and two radial shell locations hold loot. The center lanes and dock passages stay clear.",
            MobiusHall.Id => "Small valuables rest on both faces of the flat lower ribbon beside the walking lane. The access ramp stays clear.",
            _ => "",
        };

        private static IEnumerable<IEnumerable<Site>> Candidates(GrayModule module, RoomQuery query)
        {
            if (module.Id == MobiusHall.Id)
            {
                yield return new[] { RibbonSite("ribbon-west-spares", .9875f, -RibbonLane, Size.Tiny) };
                yield return new[] { RibbonSite("ribbon-east-instrument", .0125f, RibbonLane, Size.Small) };
                yield return new[]{ new Site("ceiling-west-spares","ceiling","roof",LootSurfaceKind.Floor,Size.Small,new Vector3(9,12,-5),Vector3.forward,Vector3.down) };
                yield return new[]{ new Site("ceiling-east-instrument","ceiling","roof",LootSurfaceKind.Floor,Size.Medium,new Vector3(9,12,5),Vector3.forward,Vector3.down) };
            }
            int sphereMask = SphericalChamber.GetMask(module.Id);
            if (sphereMask != 0)
                foreach (Site site in SphereSites(query, sphereMask))
                    yield return new[] { site };
            if (module.Id == MaintenanceTrench.Id)
            {
                yield return new[] { new Site(MaintenanceTrench.SampleSiteId, "main", MaintenanceTrench.Bridge,
                    LootSurfaceKind.Floor, Size.Small, V(MaintenanceTrench.SampleSite), Vector3.right, Vector3.up) };
                yield return new[] { new Site(MaintenanceTrench.SampleReserveSiteId, "main", "east-deck-near",
                    LootSurfaceKind.Floor, Size.Small, V(MaintenanceTrench.SampleReserveSite), Vector3.right, Vector3.up) };
            }
            foreach (Bay bay in FloorBays(module.Id))
            {
                var floor = module.Floors.Single(item => item.Id == bay.Floor);
                var point = V(floor.Pose.Point(bay.Along, 0, bay.Across));
                var up = V(floor.Pose.Up);
                string support = query.DeckAt(point, up);
                if (support != null)
                    yield return new[] { new Site(bay.Id, floor.Id, support, LootSurfaceKind.Floor, bay.Capacity,
                        point, V(floor.Pose.Right) * bay.Side, up) };
            }

            foreach (RoomDressing dressing in module.Dressing)
            {
                foreach (var surface in dressing.LootSurfaces)
                {
                    var part = dressing.Parts.Single(item => item.Id == surface.Support);
                    string cabinet = surface.Kind == LootSurfaceKind.Cabinet ?
                        part.Id.Substring(0, part.Id.LastIndexOf("-shelf-", StringComparison.Ordinal)) : null;
                    foreach (var choice in SurfaceChoices(module, dressing, part, surface.Kind, surface.WallSide, cabinet))
                        yield return choice;
                }

                foreach (var part in dressing.Parts.Where(item => item.Id.EndsWith("-body-top", StringComparison.Ordinal)))
                {
                    string body = part.Id.Substring(0, part.Id.Length - "-body-top".Length);
                    var back = dressing.Parts.Single(item => item.Id == body + "-body-back");
                    int side = Math.Sign(back.Center.Z - part.Center.Z);
                    foreach (var choice in SurfaceChoices(module, dressing, part, LootSurfaceKind.Raised, side))
                        yield return choice;
                }

                // PartsRack shelves have a lip, upright and sometimes a retained bin. The
                // matching lip gives the retrieval side and preserves the organizer's shape.
                foreach (var part in dressing.Parts.Where(item => item.Id.Contains("-shelf-", StringComparison.Ordinal)))
                {
                    string lipId = part.Id.Replace("-shelf-", "-lip-");
                    var lip = dressing.Parts.FirstOrDefault(item => item.Id == lipId);
                    if (lip.Id == null) continue;
                    int side = Math.Sign(part.Center.Z - lip.Center.Z);
                    foreach (var choice in SurfaceChoices(module, dressing, part, LootSurfaceKind.Raised, side))
                        yield return choice;
                }
            }
            if (module.Id is "sample-storage" or "sample-storage-narrow")
            {
                var (min, max) = module.Geometry.Single(block => block.Id == "large-containers").Bounds;
                var rack = new RoomDressing("", GrayBuilder.Level);
                var part = new RoomDressing.Part("large-containers", (min + max) / 2, max - min, DressingSurface.Worktop);
                foreach (var choice in SurfaceChoices(module, rack, part, LootSurfaceKind.Raised, -1))
                    yield return choice;
            }
        }

        private static Site RibbonSite(string id, float turns, float across, Size size)
        {
            // This flat patch is on the side opposite the entry. Its two faces keep
            // the stock pivot at contact height and leave the center of the walk free.
            var pose = MobiusHall.Surface(turns, across);
            Vector3 lane = V(MobiusHall.Surface(turns, 0).Position);
            Vector3 bottom = V(pose.Position);
            return new Site(id, "ground", MobiusHall.Ribbon, LootSurfaceKind.Raised, size, bottom,
                bottom - lane, V(pose.Up), lift: 0, ribbonLaneCenter: lane);
        }

        private static IEnumerable<IEnumerable<Site>> SurfaceChoices(GrayModule module, RoomDressing dressing,
            RoomDressing.Part part, LootSurfaceKind kind, int side, string cabinet = null)
        {
            var up = V(dressing.Frame.Up);
            var forward = V(dressing.Frame.Right) * side;
            string support = string.IsNullOrEmpty(dressing.Id) ? part.Id : dressing.Name(part.Id);
            var deck = module.Floors.Where(floor => !TraversalOnly(module.Id, floor.Id))
                .Where(floor => Vector3.Dot(V(floor.Pose.Up), up) > .999f)
                .Select(floor => (Floor: floor, Height: Vector3.Dot(V(dressing.Point(part.Center)) - V(floor.Pose.Position), up)))
                .Where(entry => entry.Height >= 0 && entry.Height < 2.4f)
                .OrderBy(entry => entry.Height).Select(entry => entry.Floor).FirstOrDefault();
            if (deck == null) yield break;

            var categories = kind == LootSurfaceKind.Cabinet ? ShelfSizes :
                kind == LootSurfaceKind.Table ? BenchSizes : RaisedSizes;
            float span = part.Size.X - .08f;
            int divisions = Mathf.Clamp(Mathf.FloorToInt(span / (kind == LootSurfaceKind.Cabinet ? .64f : .87f)), 1, 3);
            for (int index = 0; index < divisions; index++)
            {
                string id = divisions == 1 ? support : $"{support}-{index + 1}";
                float width = span / divisions;
                float middle = -span * .5f + width * (index + .5f);
                yield return FromCut();

                IEnumerable<Site> FromCut()
                {
                    foreach (Size size in categories)
                    {
                        Vector3 bounds = Envelope(size);
                        float availableX = width - bounds.x - .02f;
                        float availableZ = part.Size.Z - bounds.z - .08f;
                        if (availableX < 0 || availableZ < 0) continue;
                        foreach (float lateral in new[] { 0, -.42f, .42f })
                        foreach (float depth in new[] { -.28f, .26f })
                        {
                            var shift = new System.Numerics.Vector3(middle + availableX * lateral, part.Size.Y / 2,
                                availableZ * side * depth);
                            yield return new Site(id, deck.Id, support, kind, size, V(dressing.Point(part.Center + shift)),
                                forward, up, cabinet == null ? null : dressing, cabinet);
                        }
                    }
                }
            }
        }

        private readonly struct Bay
        {
            internal readonly string Id, Floor;
            internal readonly float Along, Across;
            internal readonly int Side;
            internal readonly Size Capacity;

            internal Bay(string id, string floor, float along, float across, int side, Size capacity = Size.Small)
            {
                Id = id;
                Floor = floor;
                Along = along;
                Across = across;
                Side = side;
                Capacity = capacity;
            }
        }

        private static IEnumerable<Bay> FloorBays(string module) => module switch
        {
            "service-cupboard" => new Bay[] { new("rear-spares-bay", "ground", 6.5f, -2.42f, -1) },
            "calibration-sector" => new Bay[] {
                new("freight-north-west", "ground", 1.8f, 6.53f, 1, Size.Wide),
                new("freight-north-gauge", "ground", 5.2f, 6.51f, 1, Size.Tall),
                new("freight-north-east", "ground", 21.4f, 6.38f, 1, Size.Big),
                new("freight-south-west", "ground", 1.35f, -6.54f, -1, Size.VeryTall),
                new("freight-regulator-south", "ground", 6.25f, -1.75f, -1, Size.Big),
                new("freight-regulator-north", "ground", 6.25f, 1.75f, 1, Size.Big) },
            "sample-storage" or "sample-storage-narrow" => new Bay[] {
                new("freight-south-west", "ground", 1.30f, -6.52f, -1, Size.VeryTall),
                new("freight-north-pack", "ground", 11.02f, 6.60f, 1, Size.Tall),
                new("freight-south-east", "ground", 13.78f, -4.12f, -1, Size.Big),
                new("freight-intake-wide", "ground", 1.75f, -4.50f, -1, Size.Wide),
                new("freight-intake-tall", "ground", 1.50f, 3.75f, 1, Size.Tall),
                new("freight-intake-very-tall", "ground", 1.00f, 5.75f, 1, Size.VeryTall),
                new("freight-archive-wall", "ground", 2.75f, -6.50f, -1, Size.Big),
                new("freight-rack-approach", "ground", 4.25f, -1.25f, -1, Size.Big) },
            "observation-lab" or "observation-lab-broken-floor" => new Bay[] {
                new("freight-north-wall", "lower", 10.82f, 6.51f, 1, Size.Tall),
                new("freight-lower-east", "lower", 12.95f, -2.66f, -1, Size.VeryTall),
                new("gallery-rail-spares", "gallery", 12.8f, -3.83f, 1),
                new("freight-optics-wide", "lower", 4.50f, -.75f, -1, Size.Wide),
                new("freight-entrance-tall", "lower", 1.50f, 3.75f, 1, Size.Tall),
                new("freight-stair-foot", "lower", 1.00f, -6.50f, -1, Size.VeryTall),
                new("freight-optics-crates", "lower", 2.25f, -3.25f, -1, Size.Big),
                new("freight-stand-crates", "lower", 5.00f, 1.25f, 1, Size.Big) },
            "straight-corridor-short" => new Bay[] { new("north-wall-spares", "ground", 3.75f, 2.12f, 1) },
            "straight-corridor-long" => new Bay[] {
                new("north-wall-spares", "ground", 5.15f, 2.12f, 1),
                new("south-wall-spares", "ground", 9.15f, -2.10f, -1) },
            "elbow-corridor" => new Bay[] {
                new("outside-elbow-spares", "ground", 7.55f, -2.42f, -1),
                new("inside-elbow-spares", "ground", 3.55f, 5.96f, 1, Size.Tiny) },
            "junction-corridor" => new Bay[] {
                new("south-wall-spares", "ground", 7.55f, -2.96f, -1),
                new("branch-wall-spares", "ground", 4.75f, 6.42f, 1, Size.Tiny) },
            "upper-exit-corridor" => new Bay[] {
                new("lower-east-spares", "lower", 18.65f, -2.85f, -1),
                new("lower-west-spares", "lower", 3.65f, 2.90f, 1) },
            "shaft-plant" => new Bay[] {
                new("mid-landing-service-kit", "middle-landing", 10.38f, -7.02f, -1, Size.Tiny),
                new("upper-landing-spares", "upper-landing", 19.95f, -7.02f, -1),
                new("upper-guard-spares", "upper-landing", 17.65f, -.37f, 1, Size.Small) },
            "gallery-chamber" => new Bay[] { new("gallery-rail-spares", "lab-gallery", 12.8f, -3.83f, 1) },
            _ when SphericalChamber.IsVariant(module) => SphereBays(SphericalChamber.GetMask(module)),
            MobiusHall.Id => new Bay[] {
                new("west-instrument-spares", "ground", -MobiusHall.HalfInteriorX + 1.25f,
                    -MobiusHall.HalfInteriorZ + 1, -1, Size.Medium),
                new("east-field-spares", "ground", MobiusHall.HalfInteriorX - 1.25f,
                    -MobiusHall.HalfInteriorZ + 1, -1, Size.Medium), },
            MaintenanceTrench.Id => new Bay[] {
                new("freight-bypass-west", "main", 2.0f, 5.70f, 1, Size.Wide),
                new("freight-bypass-mid", "main", 6.48f, 5.78f, 1, Size.Tall),
                new("freight-bypass-east", "main", 13.45f, 5.68f, 1, Size.VeryTall) },
            _ => Array.Empty<Bay>(),
        };

        private static Vector3 V(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

        private static string Vector(Vector3 vector) => FormattableString.Invariant($"{vector.x:F4},{vector.y:F4},{vector.z:F4}");
    }
}


