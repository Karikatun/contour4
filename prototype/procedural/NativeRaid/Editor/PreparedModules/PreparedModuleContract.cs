using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Numerics = System.Numerics;
using static Contour4.Procedural.Catalog.BuildPreparedModules;

namespace Contour4.Procedural.Catalog
{
    internal static class PreparedModuleContract
    {
        internal static void Save(GrayModule module, string path)
        {
            var geometry = module.Geometry.Where(block => block.Role != BlockRole.Free).ToArray();
            var catalog = new Catalog
            {
                id = module.Id,
                units = "meters in module space",
                transformConvention = "Unity forward/up match catalog forward/up. Unity local +X is negative catalog lateral.",
                dockDefault = "Plugs are closed. Disable the named plug when creating an appropriate connection.",
                difficulty = module.Difficulty,
                planWidth = module.Plan.X,
                planLength = module.Plan.Y,
                boundsMin = V(geometry.Select(block => block.Bounds.Min).Aggregate(Numerics.Vector3.Min)),
                boundsMax = V(geometry.Select(block => block.Bounds.Max).Aggregate(Numerics.Vector3.Max)),
                floors = module.Floors.Select(floor => new FloorData
                {
                    id = floor.Id,
                    frame = new FrameData(floor.Pose),
                    mapContour = floor.MapContour.Select(V2).ToArray(),
                }).ToArray(),
                docks = module.Docks.Select(dock => new DockData
                {
                    id = dock.Id,
                    floor = dock.FloorId,
                    use = dock.Use.ToString(),
                    cargo = dock.MaxCargo.ToString(),
                    plug = dock.PlugId,
                    external = module.ExternalDocks.Contains(dock),
                    width = dock.Opening.X,
                    height = dock.Opening.Y,
                    frame = new FrameData(dock.Threshold),
                    contactStart = V(dock.ContactStart),
                    contactEnd = V(dock.ContactEnd),
                    mapPoint = V2(dock.MapPoint),
                }).ToArray(),
                internalPortals = module.InternalPortals.Select(pair => new Pair { a = pair.A, b = pair.B }).ToArray(),
                haulRoutes = module.Routes.Select(route => new HaulData
                {
                    id = route.Id,
                    from = route.From,
                    to = route.To,
                    cargo = route.Cargo.ToString(),
                    condition = route.Condition,
                    envelope = V(route.Envelope),
                    stepClearance = route.StepClearance,
                    hasFlight = route.Flight.HasValue,
                    flightStart = route.Flight.HasValue ? V(route.Flight.Value.Start) : default,
                    flightEnd = route.Flight.HasValue ? V(route.Flight.Value.End) : default,
                    knots = route.Knots.Select(knot => new FrameData(knot)).ToArray(),
                }).ToArray(),
                reserveVolumes = module.Approaches.Concat(module.Geometry.Where(block => block.Role == BlockRole.Free))
                    .Select(Volume).ToArray(),
                fallVolumes = module.FallVolumes.Select(Volume).ToArray(),
                mechanisms = module.Dressing.SelectMany(dressing => dressing.MovingFronts.Select(front => new Mechanism
                {
                    id = dressing.Name(front.Panel),
                    kind = front.Angle != 0 ? "cabinet hinge" : "drawer slide",
                    pivot = dressing.Name(front.Panel) + " pivot",
                    panel = dressing.Name(front.Panel),
                    handle = dressing.Name(front.Handle),
                    tray = front.Tray == null ? null : dressing.Name(front.Tray),
                    openLocalRotationDegrees = Vector3.up * front.Angle,
                    openLocalTranslation = Vector3.forward * front.Travel,
                })).ToArray(),
            };
            if (module.Id == MaintenanceTrench.Id)
                catalog.mechanisms = catalog.mechanisms.Append(new Mechanism
                {
                    id = MaintenanceTrench.Gate,
                    kind = "inspection shutter",
                    pivot = "Inspection shutter slide",
                    panel = MaintenanceTrench.Gate,
                    closeLocalTranslation = Vector3.down * MaintenanceTrench.GateTravel,
                }).ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(catalog, prettyPrint: true) + "\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Vector2 V2(Numerics.Vector2 v) => new(v.X, v.Y);
        private static VolumeData Volume(Block block) => new()
        {
            id = block.Id,
            vertices = block.Vertices.Select(V).ToArray(),
            triangles = block.Faces,
        };

        [Serializable] private sealed class Catalog
        {
            public int schemaVersion = 1;
            public string id, units, transformConvention, dockDefault;
            public int difficulty;
            public float planWidth, planLength;
            public Vector3 boundsMin, boundsMax;
            public FloorData[] floors;
            public DockData[] docks;
            public Pair[] internalPortals;
            public HaulData[] haulRoutes;
            public VolumeData[] reserveVolumes, fallVolumes;
            public Mechanism[] mechanisms;
        }

        [Serializable] private sealed class FrameData
        {
            public Vector3 position, forward, up;

            public FrameData(ModulePose pose)
            {
                position = V(pose.Position);
                forward = V(pose.Forward);
                up = V(pose.Up);
            }
        }

        [Serializable] private sealed class FloorData
        {
            public string id;
            public FrameData frame;
            public Vector2[] mapContour;
        }

        [Serializable] private sealed class DockData
        {
            public string id, floor, use, cargo, plug;
            public bool external;
            public float width, height;
            public FrameData frame;
            public Vector3 contactStart, contactEnd;
            public Vector2 mapPoint;
        }

        [Serializable] private sealed class Pair { public string a, b; }

        [Serializable] private sealed class HaulData
        {
            public string id, from, to, cargo, condition;
            public Vector3 envelope;
            public float stepClearance;
            public bool hasFlight;
            public Vector3 flightStart, flightEnd;
            public FrameData[] knots;
        }

        [Serializable] private sealed class VolumeData
        {
            public string id;
            public Vector3[] vertices;
            public int[] triangles;
        }

        [Serializable] private sealed class Mechanism
        {
            public string id, kind, pivot, panel, handle, tray;
            public Vector3 openLocalRotationDegrees, openLocalTranslation, closeLocalTranslation;
        }
    }
}
