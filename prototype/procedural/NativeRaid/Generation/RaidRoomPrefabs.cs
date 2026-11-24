using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidRoomPrefabs
    {
        internal const string ReceivingAsset = "Assets/Contour4/Rooms/sample-receiving.prefab";
        internal const string ReceivingWithoutResearchAsset = "Assets/Contour4/Rooms/sample-receiving-without-research.prefab";
        private const string PreparedRoot = "Assets/Contour4/PreparedModules";

        private readonly GameObject withResearch;
        private readonly GameObject withoutResearch;
        private readonly IReadOnlyDictionary<string, PreparedModule> prepared;

        internal RaidRoomPrefabs(GameObject withResearch, GameObject withoutResearch,
            IReadOnlyDictionary<string, PreparedModule> prepared = null)
        {
            this.withResearch = withResearch;
            this.withoutResearch = withoutResearch;
            this.prepared = prepared;
        }

        internal static string PreparedAsset(string id) => $"{PreparedRoot}/{id}/{id}";

        internal bool TryGetPrepared(RaidPlan.Room room, out GameObject prefab)
        {
            prefab = null;
            if (prepared == null || !prepared.TryGetValue(room.Module.Id, out var module))
                return false;
            prefab = module.Prefab;
            return true;
        }

        internal IReadOnlyList<RaidLootSlot> PreparedLoot(BakedRoom room) =>
            prepared[room.Plan.Module.Id].Loot.Select(site => site.Bind(room)).ToArray();

        internal GameObject For(RaidPlan.Room room)
        {
            if (room.Module.Id != FreightServiceRoom.SampleReceiving) return null;
            bool research = room.Module.Docks.Any(dock => dock.Id == FreightServiceRoom.ResearchDoor);
            var prefab = research ? withResearch : withoutResearch;
            return prefab ? prefab : throw new InvalidOperationException($"Required room prefab is unavailable (research={research}).");
        }

        internal sealed class PreparedModule
        {
            internal readonly GameObject Prefab;
            private readonly LootSite[] sites;
            internal IReadOnlyList<LootSite> Loot => sites;

            internal PreparedModule(GrayModule module, GameObject prefab, TextAsset catalog, TextAsset loot)
            {
                Prefab = prefab;
                // Unity's player serializer can leave arrays of these plugin types null.
                var serializer = JsonSerializer.Create();
                var contract = ReadMetadata<CatalogMetadata>(serializer, catalog);
                var descriptions = ReadMetadata<LootMetadata>(serializer, loot);
                var root = prefab.transform;
                var markers = root.Find("Loot candidates");
                if (prefab.name != module.Id || contract?.schemaVersion != 1 || contract.id != module.Id ||
                    descriptions?.moduleId != module.Id || descriptions.sites == null || !markers ||
                    !root.Find("Monster paths") || markers.childCount != descriptions.sites.Length)
                    throw new InvalidOperationException($"{module.Id}: invalid prepared asset identity, schema or marker inventory.");

                ValidateFrames("Floor frames", module.Floors.Select(floor => floor.Id), contract.floors);
                ValidateFrames("Docks", module.Docks.Select(dock => dock.Id), contract.docks);
                ValidateFrames("Haul routes", module.Routes.Select(route => route.Id), contract.haulRoutes);
                var supports = new HashSet<string>(root.GetComponentsInChildren<Collider>(true).Select(collider => collider.name),
                    StringComparer.Ordinal);
                var floorIDs = new HashSet<string>(module.Floors.Select(floor => floor.Id), StringComparer.Ordinal);
                var found = new HashSet<string>(StringComparer.Ordinal);
                sites = descriptions.sites.Select(site =>
                {
                    var marker = string.IsNullOrEmpty(site.marker) ? null : markers.Find(site.marker);
                    if (string.IsNullOrEmpty(site.id) || site.marker != "loot__" + site.id || !marker ||
                        !found.Add(site.marker) || !supports.Contains(site.support) || !floorIDs.Contains(site.floor) ||
                        !Enum.TryParse(site.capacity, out RaidLootSize size) || !Enum.IsDefined(typeof(RaidLootSize), site.capacity) ||
                        !Enum.TryParse(site.surface, out LootSurfaceKind surface) || !Enum.IsDefined(typeof(LootSurfaceKind), site.surface) ||
                        !Matches(RaidLootSites.Envelope(size), site.envelope) || !Matches(marker.localPosition, site.pivot) ||
                        !Matches(marker.forward, site.forward) || !Matches(marker.up, site.up))
                        throw new InvalidOperationException($"{module.Id}: invalid prepared loot candidate {site.id}.");
                    return new LootSite(site.marker, site.floor, site.support, size, surface);
                }).ToArray();

                void ValidateFrames(string group, IEnumerable<string> expected, NamedEntry[] sidecar)
                {
                    var entries = root.Find(group);
                    var expectedIDs = new HashSet<string>(expected, StringComparer.Ordinal);
                    if (!entries || sidecar == null || sidecar.Length != expectedIDs.Count ||
                        entries.childCount != expectedIDs.Count || !expectedIDs.SetEquals(sidecar.Select(entry => entry.id)) ||
                        expectedIDs.Any(id => !entries.Find(id)))
                        throw new InvalidOperationException($"{module.Id}: prepared {group} differs from the catalog.");
                }
            }

            private static bool Matches(Vector3 expected, Vector3 actual) =>
                RaidGeometry.Finite(actual) && Vector3.Distance(expected, actual) <= .001f;

            private static T ReadMetadata<T>(JsonSerializer serializer, TextAsset asset)
            {
                var document = JObject.Parse(asset.text,
                    new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                foreach (var entry in document.DescendantsAndSelf().OfType<JObject>())
                {
                    // Json.NET can bind both spellings to the same typed field.
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var duplicate = entry.Properties().FirstOrDefault(property => !names.Add(property.Name));
                    if (duplicate != null)
                        throw new JsonSerializationException($"{asset.name}: duplicate prepared metadata field {duplicate.Path}.");
                }
                return document.ToObject<T>(serializer);
            }
        }

        internal sealed class LootSite
        {
            private readonly string marker, floor, support;
            private readonly RaidLootSize size;
            private readonly LootSurfaceKind surface;

            internal LootSite(string marker, string floor, string support, RaidLootSize size, LootSurfaceKind surface)
            {
                this.marker = marker;
                this.floor = floor;
                this.support = support;
                this.size = size;
                this.surface = surface;
            }

            internal RaidLootSlot Bind(BakedRoom room)
            {
                var frame = room.Root.Find("Loot candidates/" + marker);
                if (!frame || !room.CollidersById.TryGetValue(support, out var supportCollider) || !supportCollider)
                    throw new InvalidOperationException($"{room.Plan.Module.Id}: missing placed loot marker or support {marker} ({support}).");
                Vector3 extent = RaidLootSites.Envelope(size);
                Vector3 center = frame.localPosition + frame.localRotation * new Vector3(0f, extent.y / 2f, extent.z / 2f);
                Vector3 half = extent / 2f;
                Quaternion rotation = frame.localRotation;
                Vector3 boundsHalf = Abs(rotation * Vector3.right) * half.x +
                    Abs(rotation * Vector3.up) * half.y + Abs(rotation * Vector3.forward) * half.z;
                return new RaidLootSlot(room, supportCollider, surface, size, new Pose(frame.localPosition, rotation),
                    new Bounds(center, boundsHalf * 2f), floor);
            }

            private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        // Typed views of the unchanged authoring sidecars.
#pragma warning disable CS0649
        [Serializable] private sealed class CatalogMetadata
        {
            public int schemaVersion;
            public string id;
            public NamedEntry[] floors, docks, haulRoutes;
        }

        [Serializable] private sealed class NamedEntry { public string id; }

        [Serializable] private sealed class LootMetadata
        {
            public string moduleId;
            public LootEntry[] sites;
        }

        [Serializable] private sealed class LootEntry
        {
            public string id, marker, floor, support, surface, capacity;
            public Vector3 envelope, pivot, up, forward;
        }
#pragma warning restore CS0649
    }
}
