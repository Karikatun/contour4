using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using CatalogVector = System.Numerics.Vector3;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidPrefabBranch
    {
        internal const string Entry = "authored-entry";
        internal GrayModule Module { get; }
        internal Action<Transform> Mount { get; }
        internal float ThresholdSpacing { get; }
        internal Bounds Reservation { get; }

        internal RaidPrefabBranch(string id, Bounds reservation, Pose entrance, Vector2 opening, Action<Transform> mount)
        {
            var gray = new GrayBuilder(id, reservation.size.x, reservation.size.z);
            Reservation = reservation;
            Vector3 outward = entrance.rotation * Vector3.forward;
            Vector3 up = entrance.rotation * Vector3.up;
            // Some authored frame trim protrudes past the sill. Reserve it and fill the gap with a short collar.
            var projectedExtents = new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z));
            float overhang = Vector3.Dot(reservation.center - entrance.position, outward) +
                Vector3.Dot(reservation.extents, projectedExtents);
            ThresholdSpacing = Mathf.Max(overhang, 0f) + .02f;
            var authoredSill = new ModulePose(Vector(entrance.position), Vector(outward), Vector(up));
            var collarSill = new ModulePose(Vector(entrance.position + outward * ThresholdSpacing), Vector(outward), Vector(up));
            gray.Door(Entry, "ground", collarSill,
                Cargo.Cart, width: opening.x, height: opening.y);
            gray.Box("authored-group-volume", BlockRole.Free, reservation.min.x, reservation.max.x,
                reservation.min.y, reservation.max.y, reservation.min.z, reservation.max.z);
            gray.Module.Geometry.Add(Block.Span("attached-collar-volume", BlockRole.Free, authoredSill,
                collarSill, -.25f, opening.y, -opening.x / 2 - .25f, opening.x / 2 + .25f));
            Module = gray.Module;
            Mount = mount;
        }

        internal static Bounds Measure(GameObject prefab, string excludedChild)
        {
            Transform root = prefab.transform;
            Transform excluded = root.Find(excludedChild);
            var colliders = root.GetComponentsInChildren<Collider>(true)
                .Where(collider => !collider.isTrigger && (!excluded || !collider.transform.IsChildOf(excluded)));
            return RaidPlan.BoundsOf(colliders.SelectMany(collider =>
            {
                Bounds bounds = collider switch
                {
                    BoxCollider box => new Bounds(box.center, box.size),
                    MeshCollider mesh when mesh.sharedMesh => mesh.sharedMesh.bounds,
                    _ => throw new InvalidOperationException("Unsupported authored branch collider " + collider.name)
                };
                return Corners(bounds).Select(corner => root.InverseTransformPoint(collider.transform.TransformPoint(corner)));
            }));
        }

        private static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
        }

        private static CatalogVector Vector(Vector3 value) => new(value.x, value.y, value.z);
    }
}
