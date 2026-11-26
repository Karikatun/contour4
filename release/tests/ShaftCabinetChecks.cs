using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class ShaftCabinetChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("CONTOUR4_PAIR_RACK_OUTPUT");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundles = RoomPrefabBundle.Load(Path.Combine(output, "contour4-rooms.bundle"),
                Path.Combine(output, "contour4-prepared-modules.bundle"), requireWear: true);
            var rows = new List<string>();
            foreach (bool prepared in new[] { true, false })
            {
                var host = new GameObject("Closed switchgear fixture");
                try
                {
                    var raid = host.AddComponent<RaidAssembly>();
                    var module = ShaftPlant.Create();
                    var pose = new Pose(Vector3.zero, Quaternion.identity);
                    var plan = new RaidPlan.Room(module, pose, pose, 0);
                    var shader = Shader.Find("Standard");
                    var fitout = (RaidInteriorFitout)Call("CreateFitout", shader);
                    BakedRoom room;
                    if (prepared)
                    {
                        if (!bundles.Prefabs.TryGetPrepared(plan, out var prefab)) throw new InvalidOperationException("Missing shaft prefab.");
                        room = raid.PlacePrepared(plan, 0, prefab, null, fitout);
                    }
                    else
                    {
                        var (floor, wall) = ((Material, Material))Call("Finishes", shader);
                        var obstacle = (Material)Call("Material", shader, new Color(.39f, .33f, .25f));
                        var sight = new RaidSightStrips(shader, Field<List<Material>>("materials"), Field<List<Mesh>>("meshes"));
                        room = (BakedRoom)Call("PlaceRoom", plan, 0, floor, wall, obstacle, obstacle, sight, null, fitout);
                    }
                    Field<Dictionary<RaidPlan.Room, BakedRoom>>("rooms").Add(plan, room);
                    var interactions = RaidRoomInteractions.Attach(raid);
                    Physics.SyncTransforms();
                    var fronts = interactions.Cabinets.Where(front => front.Body.name.StartsWith("plant-feed-switchgear-", StringComparison.Ordinal)).ToArray();
                    if (fronts.Length != 2) throw new InvalidOperationException("Expected two mounted switchgear doors.");
                    foreach (var front in fronts)
                    {
                        var leaf = front.Parts.First();
                        Vector3 ray = room.Root.forward;
                        if (!Physics.Raycast(leaf.bounds.center - ray * 1.2f, ray, out var closed, 1.35f, ~0, QueryTriggerInteraction.Ignore) ||
                            closed.collider != leaf || closed.rigidbody != front.Body ||
                            !front.Body.TryGetComponent<HingeJoint>(out var hinge) || !hinge.useLimits || !hinge.connectedBody.isKinematic)
                            throw new InvalidOperationException("Closed leaf failed first-hit and anchored hinge check: " + leaf.name);
                        rows.Add($"{(prepared ? "prepared" : "fallback")}\t{closed.collider.name}\tanchored hinge and closed leaf first hit");
                    }
                    T Field<T>(string key) => (T)typeof(RaidAssembly).GetField(key, Private).GetValue(raid);
                    object Call(string key, params object[] args) => typeof(RaidAssembly).GetMethod(key, Private).Invoke(raid, args);
                }
                finally { Object.DestroyImmediate(host); }
            }
            File.WriteAllLines(Path.Combine(output, "shaft-cabinet.tsv"), rows);
            Debug.Log($"CONTOUR4 SHAFT CLOSED SWITCHGEAR PASSED: {rows.Count} mounted closed leaf observations.");
        }
    }
}
