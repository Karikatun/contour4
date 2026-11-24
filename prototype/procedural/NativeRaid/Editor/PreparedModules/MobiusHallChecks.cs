using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Unity.Collections;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    internal static class MobiusHallChecks
    {
        internal static void Verify(GameObject prefab)
        {
            Transform room = prefab.transform;
            MeshCollider ribbon = room.Find("Geometry/Obstacles/" + MobiusHall.Ribbon).GetComponent<MeshCollider>();
            MeshCollider ground = room.Find("Geometry/Floors/ground").GetComponent<MeshCollider>();
            VerifyTopology(ribbon);
            VerifyInterior(room, ribbon, ground);
            VerifyRailBodies(room);
            VerifyField(room,ribbon);
            VerifyExpandedRoute(room);
            VerifyPostNormals();
            VerifyCeilingSettling(room);
            VerifySeparatedAccess(room);
            Debug.Log("CONTOUR4 PREPARED MOBIUS PASSED: scaled x1.5, exact normal rails, physical floor-to-ceiling route");
        }
        private static void VerifyExpandedRoute(Transform room)
        {
            var plugs=room.Find("Geometry/Conditional plugs").GetComponentsInChildren<Collider>();
            foreach(var c in plugs)c.enabled=false;
            int count=0;
            try
            {
                var path=MobiusHall.FloorToCeilingRoute();
                foreach(int direction in new[]{-1,1})
                foreach(float radius in new[]{.34f,.86f})
                {
                    float height=radius>.5f?2.86f:1.8f;
                    var ordered=direction>0?path:path.Reverse().ToArray();
                    for(int j=1;j<ordered.Length;j++)
                    {
                        var a=ordered[j-1];var b=ordered[j];
                        int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(V(a.Position),V(b.Position))/.045f));
                        for(int k=0;k<=steps;k++)
                        {
                            float t=k/(float)steps;Vector3 feet=Vector3.Lerp(V(a.Position),V(b.Position),t);
                            Vector3 up=Vector3.Slerp(V(a.Up),V(b.Up),t).normalized;
                            var hits=Physics.OverlapCapsule(feet+up*(radius+.045f),feet+up*(height-radius+.045f),radius,~0,QueryTriggerInteraction.Ignore);
                            Check(hits.Length==0,$"Full oriented route capsule r={radius} h={height} blocked segment={j} t={t:F3} at {feet}: {string.Join(",",hits.Select(c=>c.name))}");
                            if(Mathf.Abs(feet.x)<MobiusHall.HalfInteriorX-.1f && Mathf.Abs(feet.z)<MobiusHall.HalfInteriorZ-.1f)
                            {
                                bool supported=Physics.RaycastAll(feet+up*.15f,-up,.32f,~0,QueryTriggerInteraction.Ignore).Any(hit=>Vector3.Dot(hit.normal,up)>.97f);
                                Check(supported,$"Oriented support missing segment={j} t={t:F3} feet={feet} up={up}");
                            }
                            count++;
                        }
                    }
                }
            }
            finally {foreach(var c in plugs)c.enabled=true;}
            var loot=room.Find("Loot candidates");
            foreach(string id in new[]{"ribbon-west-spares","ribbon-east-instrument","west-instrument-spares","east-field-spares","ceiling-west-spares","ceiling-east-instrument"})Check(loot.Find("loot__"+id),"Lost scoped loot "+id);
            Debug.Log($"CONTOUR4 PREPARED MOBIUS EXPANDED actual oriented capsule poses={count}; both directions, r0.34/h1.8+r0.86/h2.86; six retained/new loot IDs");
        }

        private static void VerifySeparatedAccess(Transform room)
        {
            var floor=room.Find("Geometry/Floors/"+MobiusHall.Landing).GetComponent<Collider>();
            var ceiling=room.Find("Geometry/Floors/"+MobiusHall.CeilingLanding).GetComponent<Collider>();
            float separation=Mathf.Abs(floor.bounds.center.z-ceiling.bounds.center.z);
            Check(separation>18f,"Floor and ceiling accesses must be geographically opposite, not adjacent");
            float arc=0;var previous=MobiusHall.Surface(0,0).Position;
            for(int i=1;i<=MobiusHall.RibbonSections/2;i++){var next=MobiusHall.Surface(i/(float)MobiusHall.RibbonSections,0).Position;arc+=System.Numerics.Vector3.Distance(previous,next);previous=next;}
            Check(arc>25f,"Intended half-ribbon route is too short");
            foreach(float turn in new[]{.46f,.48f,.5f,.52f,.54f})Check(MobiusHall.Surface(turn,0).Up.Y<-.9999f,"Ceiling landing patch must be flat");
            var oldExit=MobiusHall.Surface(1,-MobiusHall.HalfWidth+MobiusHall.RailInset);
            Vector3 origin=V(oldExit.Position)+V(oldExit.Up)*.505f-Vector3.forward*.25f;
            var rails=new[]{MobiusHall.RailInner,MobiusHall.RailOuter}.Select(id=>room.Find("Geometry/Obstacles/"+id).GetComponent<Collider>());
            Check(rails.Any(rail=>rail.Raycast(new Ray(origin,Vector3.forward),out _,.95f)),"Old adjacent reverse-face shortcut is still open");
            Vector3 unsupported=Vector3.Lerp(floor.bounds.center,ceiling.bounds.center,.5f);
            Check(!Physics.Raycast(unsupported+Vector3.up*.2f,Vector3.down,.4f),"Unexpected immediate bridge joins the two access aprons");
            Debug.Log($"CONTOUR4 PREPARED MOBIUS SEPARATE ACCESS apron-separation={separation:F3}m intended-ribbon-arc={arc:F3}m, rear flat patch, old adjacent reverse opening blocked by real rail");
        }

        private static void VerifyPostNormals()
        {
            var module=MobiusHall.Create();int count=0;float worst=1;
            foreach(int edge in new[]{-1,1})
            {
                float perimeter=4*3*MobiusHall.Scale+2*Mathf.PI*4.6f*MobiusHall.Scale;
                float xPerTurn=Mathf.Abs(MobiusHall.Surface(.001f,0).Position.X-MobiusHall.Surface(0,0).Position.X)/.001f;
                float gap=(MobiusHall.LandingHalfLength+.06f)/xPerTurn;
                float start=(edge>0?0:.5f)+gap,finish=(edge>0?2:2.5f)-gap;
                int frames=2+Enumerable.Range(1,3*MobiusHall.RibbonSections/2-1).Count(i=>i/(float)(MobiusHall.RibbonSections/2)>start&&i/(float)(MobiusHall.RibbonSections/2)<finish);
                var turns=new List<float>{start};
                for(int j=1;j<3*MobiusHall.RibbonSections/2;j++){float turn=j/(float)(MobiusHall.RibbonSections/2);if(turn>start&&turn<finish)turns.Add(turn);}turns.Add(finish);
                var all=turns.Select(turn=>MobiusHall.Surface(turn,edge*(MobiusHall.HalfWidth-MobiusHall.RailInset))).ToArray();
                var posts=new List<ModulePose>{all[0]};float since=0;
                for(int j=1;j<all.Length;j++){since+=System.Numerics.Vector3.Distance(all[j-1].Position,all[j].Position);if(since<1.35f)continue;posts.Add(all[j]);since=0;}posts.Add(all.Last());
                int postIndex=0;
                var vertices=module.Geometry.Single(b=>b.Id==(edge>0?MobiusHall.RailOuter:MobiusHall.RailInner)).Vertices;
                Check((vertices.Length-8*frames)%12==0,"Unrecognized actual rail mesh post layout");
                for(int i=8*frames;i<vertices.Length;i+=12)
                {
                    var foot=(vertices[i]+vertices[i+1]+vertices[i+2]+vertices[i+3])/4;
                    var middle=(vertices[i+4]+vertices[i+5]+vertices[i+6]+vertices[i+7])/4;
                    var top=(vertices[i+8]+vertices[i+9]+vertices[i+10]+vertices[i+11])/4;
                    var axis=System.Numerics.Vector3.Normalize(top-foot);
                    var normal=posts[postIndex++].Up;
                    float dot=System.Numerics.Vector3.Dot(axis,normal);worst=Mathf.Min(worst,dot);
                    Check(dot>.999999f,$"Rail post not perpendicular to local surface at {middle}: dot={dot}");count++;
                }
            }
            Debug.Log($"CONTOUR4 PREPARED MOBIUS POST NORMALS actual mesh posts={count}, minimum normal dot={worst:F7}; no outboard flare");
        }
        private static void VerifyCeilingSettling(Transform room)
        {
            var prior=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
            try
            {
                foreach(string id in new[]{"ceiling-west-spares","ceiling-east-instrument"})
                {
                    var marker=room.Find("Loot candidates/loot__"+id);
                    var actor=new GameObject("Ceiling loot settling witness");
                    try
                    {
                        actor.transform.rotation=marker.rotation;var body=actor.AddComponent<Rigidbody>();body.mass=17;
                        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                        var box=actor.AddComponent<BoxCollider>();box.size=id.Contains("west")?Vector3.one*.5f:new Vector3(.825f,1,.825f);
                        var start=marker.position+marker.forward*(box.size.z/2)+marker.up*(box.size.y/2);body.position=start;
                        float first=0,max=0;
                        for(int tick=0;tick<150;tick++)
                        {
                            body.AddForce(MobiusWalk.TakeoffGravityCorrection(marker.up,Physics.gravity),ForceMode.Acceleration);
                            Physics.SyncTransforms();Physics.Simulate(.01f);
                            float drift=Vector3.Distance(body.position,start);if(tick==0)first=drift;max=Mathf.Max(max,drift);
                        }
                        Check(first<.003f&&max<.025f&&body.linearVelocity.magnitude<.02f&&Quaternion.Angle(marker.rotation,body.rotation)<1,$"Ceiling loot failed settle {id}: first={first}, max={max}");
                        Debug.Log($"CONTOUR4 PREPARED MOBIUS CEILING SETTLED {id}: first={first:F6}m max={max:F6}m, 150ticks, stock-equivalent box surrogate");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(actor);}
                }
            }
            finally{Physics.simulationMode=prior;Physics.SyncTransforms();}
        }

        private static void VerifyTopology(MeshCollider ribbon)
        {
            var triangles = ribbon.sharedMesh.triangles;
            var allFaces = Enumerable.Range(0, triangles.Length / 3)
                .Select(i => (a: triangles[3 * i], b: triangles[3 * i + 1], c: triangles[3 * i + 2])).ToArray();
            Check(Edges(allFaces).All(entry => entry.Value == 2), "Physical ribbon has an open edge.");
            var middle = allFaces.Select(face => new[] { face.a / 2, face.b / 2, face.c / 2 }.OrderBy(index => index).ToArray())
                .Where(face => face[0] != face[1] && face[1] != face[2])
                .Select(face => (a: face[0], b: face[1], c: face[2])).Distinct().ToArray();
            var adjacency = Edges(middle).Where(entry => entry.Value == 1).Select(entry => entry.Key)
                .SelectMany(pair => new[] { (start: pair.a, end: pair.b), (start: pair.b, end: pair.a) })
                .ToLookup(pair => pair.start, pair => pair.end);
            Check(adjacency.Count == 2 * MobiusHall.RibbonSections && adjacency.All(node => node.Count() == 2),
                "Center surface does not expose one traversable outer edge.");
            int start = adjacency.First().Key, current = start, prior = -1, steps = 0;
            do
            {
                int next = adjacency[current].First(point => point != prior);
                prior = current;
                current = next;
                steps++;
            } while (current != start && steps <= adjacency.Count);
            Check(current == start && steps == adjacency.Count, "Ribbon edge splits into two loops.");
            Check(Vector3.Dot(V(MobiusHall.Surface(0, 0).Up), V(MobiusHall.Surface(1, 0).Up)) < -.999f &&
                Vector3.Distance(V(MobiusHall.Surface(0, 0).Position), V(MobiusHall.Surface(2, 0).Position)) < .001f,
                "One circuit must reverse walking up and the second must return to the start.");

            static Dictionary<(int a, int b), int> Edges(IEnumerable<(int a, int b, int c)> faces)
            {
                var result = new Dictionary<(int a, int b), int>();
                foreach (var (a, b, c) in faces)
                foreach (var (from, to) in new[] { (a, b), (b, c), (c, a) })
                {
                    var edge = (Math.Min(from, to), Math.Max(from, to));
                    result.TryGetValue(edge, out int count);
                    result[edge] = count + 1;
                }
                return result;
            }
        }

        private static void VerifyRailBodies(Transform room)
        {
            SimulationMode previous = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            int runs = 0;
            try
            {
                foreach (int edge in new[] { -1, 1 })
                foreach (float turn in new[] { .125f, .375f, .625f, .875f, 1.125f, 1.375f, 1.625f, 1.875f })
                {
                    var frame = MobiusHall.Surface(turn, edge * MobiusHall.HalfWidth);
                    Vector3 up = room.TransformDirection(V(frame.Up));
                    Vector3 point = room.TransformPoint(V(frame.Position));
                    Vector3 inward = room.TransformPoint(V(MobiusHall.Surface(turn, edge * (MobiusHall.HalfWidth - .40f)).Position));
                    Vector3 across = (point - inward).normalized;
                    var actor = new GameObject("Mobius guardrail 35kg witness");
                    try
                    {
                        var body = actor.AddComponent<Rigidbody>();
                        body.mass = 35;
                        body.useGravity = false;
                        body.constraints = RigidbodyConstraints.FreezeRotation;
                        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        var sphere = actor.AddComponent<SphereCollider>();
                        sphere.radius = .32f;
                        body.position = point + up * .505f - across * .65f;
                        body.linearVelocity = across * 2;
                        float furthest = float.NegativeInfinity;
                        for (int tick = 0; tick < 60; tick++)
                        {
                            Physics.SyncTransforms();
                            Physics.Simulate(.01f);
                            furthest = Mathf.Max(furthest, Vector3.Dot(body.position - point, across));
                        }
                        Check(furthest < MobiusHall.RailFlare + .10f, $"35kg body escaped the real rail at edge={edge}, turn={turn}, outboard={furthest:F3}.");
                        runs++;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(actor); }
                }
            }
            finally { Physics.simulationMode = previous; Physics.SyncTransforms(); }
            Debug.Log($"CONTOUR4 PREPARED MOBIUS RAIL PHYSICS {runs} 35kg/.32m sphere pushes, " +
                "60 PhysX ticks each at 2m/s, both edges/both walking faces; no escape.");
        }

        private static void VerifyFloorLootAndHeadman(Transform room)
        {
            var loot = room.Find("Loot candidates");
            var expected = new[] { "ribbon-west-spares", "ribbon-east-instrument", "west-instrument-spares", "east-field-spares" };
            Check(loot.childCount == 4 && expected.All(id => loot.Find("loot__" + id)),
                "Guardrails must retain both ribbon and both floor loot identities.");
            var headman = room.Find("Monster paths/Headman body geometry (r0.86 h2.86)");
            Check(headman && headman.Find("Path ground-through"),
                "The full original Headman envelope lost its ground-through route.");
            Transform path = headman.Find("Path ground-through");
            int headmanPoses = 0;
            for (int segment = 1; segment < path.childCount; segment++)
            {
                Vector3 a = path.GetChild(segment - 1).position, b = path.GetChild(segment).position;
                int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / .065f);
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 feet = Vector3.Lerp(a, b, i / (float)steps);
                    Collider[] contacts = Physics.OverlapCapsule(feet + room.up * .895f,
                        feet + room.up * 2.035f, .86f, ~0, QueryTriggerInteraction.Ignore);
                    Check(contacts.Length == 0, $"Headman full-body route overlaps at {feet}: " +
                        string.Join(",", contacts.Select(c => c.name)));
                    headmanPoses++;
                }
            }
            SimulationMode previous = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (string id in new[] { "west-instrument-spares", "east-field-spares" })
                {
                    Transform marker = loot.Find("loot__" + id);
                    var actor = new GameObject("Mobius floor Medium witness " + id);
                    try
                    {
                        actor.transform.rotation = marker.rotation;
                        var body = actor.AddComponent<Rigidbody>();
                        body.mass = 17;
                        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        var box = actor.AddComponent<BoxCollider>();
                        box.size = new Vector3(.825f, 1f, .825f);
                        Vector3 bottom = marker.position + marker.forward * (.825f / 2);
                        Vector3 start = bottom + marker.up * .5f;
                        body.position = start;
                        for (int tick = 0; tick < 150; tick++) { Physics.SyncTransforms(); Physics.Simulate(.01f); }
                        Vector3 delta = body.position - start;
                        float vertical = Vector3.Dot(delta, marker.up);
                        float lateral = (delta - marker.up * vertical).magnitude;
                        Check(lateral < .015f && Mathf.Abs(vertical + .008f) < .015f && body.linearVelocity.magnitude < .02f,
                            $"Restored Medium floor site {id} did not settle: lateral={lateral:F4}, vertical={vertical:F4}.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(actor); }
                }
            }
            finally { Physics.simulationMode = previous; Physics.SyncTransforms(); }
            Debug.Log($"CONTOUR4 PREPARED MOBIUS REGRESSION four original loot IDs, two Medium floor boxes/150 PhysX ticks, " +
                $"full Headman r0.86/h2.86 ground-through retained; actual capsule poses={headmanPoses}.");
        }

        private static void VerifyRailings(Transform room)
        {
            var rails = new[] { MobiusHall.RailInner, MobiusHall.RailOuter }
                .Select(id => room.Find("Geometry/Obstacles/" + id)?.GetComponent<MeshCollider>()).ToArray();
            Check(rails.All(rail => rail && rail.enabled && !rail.isTrigger && !rail.convex),
                "Both catalog boundary rails must be solid static MeshColliders.");
            Check(rails.Sum(rail => rail.sharedMesh.triangles.Length / 3) < 24000,
                "Ribbon guardrails exceeded the constrained low-poly triangle budget.");
            int guarded = 0, open = 0;
            // Midpoints and vertices exercise every chord and both one-lap joins;
            // two laps per edge are needed to test both physical walking faces.
            for (int edge = -1; edge <= 1; edge += 2)
            for (int i = 0; i < 4 * MobiusHall.RibbonSections; i++)
            {
                float turn = i / (2f * MobiusHall.RibbonSections);
                var frame = MobiusHall.Surface(turn, edge * (MobiusHall.HalfWidth - MobiusHall.RailInset));
                Vector3 point = V(frame.Position), up = V(frame.Up);
                Vector3 across = (point - V(MobiusHall.Surface(turn, edge * (MobiusHall.HalfWidth - .40f)).Position)).normalized;
                bool access = up.y > .99f && Mathf.Abs(point.z - MobiusHall.LandingInnerEdge) < .12f &&
                    Mathf.Abs(point.x - MobiusHall.EntranceX) < MobiusHall.LandingHalfLength;
                // Boundary posts are just outside the stock opening; the exact endpoint
                // is separately tested by the unchanged three-lane ramp/capsule check.
                bool openingMargin = up.y > .99f && Mathf.Abs(point.z - MobiusHall.LandingInnerEdge) < .12f &&
                    Mathf.Abs(point.x - MobiusHall.EntranceX) < MobiusHall.LandingHalfLength + .10f;
                foreach (float height in new[] { .505f, 1.025f })
                {
                    var ray = new Ray(point + up * height - across * .25f, across);
                    bool hit = rails.Any(rail => rail.Raycast(ray, out _, .95f));
                    if (access) { Check(!hit, $"Rail blocks the upper landing opening at turn={turn:F5}."); open++; }
                    else if (!openingMargin) { Check(hit, $"Missing rail at edge={edge}, turn={turn:F5}, height={height:F3}."); guarded++; }
                }
            }
            // The same physical edge on the lower face has NO access opening.
            foreach (float x in new[] { -1.2f, 0, 1.2f })
            {
                var upper = MobiusHall.Surface(0, MobiusHall.HalfWidth - MobiusHall.RailInset);
                var lower = MobiusHall.Surface(1, -MobiusHall.HalfWidth + MobiusHall.RailInset);
                Vector3 start = V(lower.Position) + Vector3.right * x + V(lower.Up) * .505f - Vector3.forward * .25f;
                Check(rails.Any(rail => rail.Raycast(new Ray(start, Vector3.forward), out _, .95f)),
                    "The ramp opening leaked onto the reverse walking face.");
            }
            Debug.Log($"CONTOUR4 PREPARED MOBIUS RAILS guarded-rays={guarded} access-rays={open}; " +
                $"2 solid catalog colliders, two laps/both edges, top/mid bars, reverse-side landing closed; " +
                $"triangles={rails.Sum(rail => rail.sharedMesh.triangles.Length / 3)}.");
        }

        private static void VerifyInterior(Transform room, MeshCollider ribbon, Collider ground)
        {
            const float tolerance = .002f;
            Vector3[] vertices = ribbon.sharedMesh.vertices;
            Bounds measured = new Bounds(ribbon.transform.TransformPoint(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices) measured.Encapsulate(ribbon.transform.TransformPoint(vertex));
            Check(Vector3.Distance(measured.min, ribbon.bounds.min) < tolerance &&
                Vector3.Distance(measured.max, ribbon.bounds.max) < tolerance,
                $"Ribbon collider and outer mesh bounds differ: mesh {measured}, collider {ribbon.bounds}.");
            Check(Mathf.Abs(measured.size.y - 6f) < tolerance, $"Ribbon mesh height is {measured.size.y:F4} m.");

            // Above both 4 m door openings, cast against each named shell collider.
            Vector3 reference = new Vector3(0, 5f, 0);
            RaycastHit floor = HitPlane(ground, Vector3.down);
            Check(Mathf.Abs(floor.point.y) < tolerance, "Hall floor elevation changed.");
            var gaps = new[] {
                measured.min.x - HitPlane(Shell("west-wall-lintel"), Vector3.left).point.x,
                HitPlane(Shell("east-wall-lintel"), Vector3.right).point.x - measured.max.x,
                measured.min.z - HitPlane(Shell("near-wall"), Vector3.back).point.z,
                HitPlane(Shell("far-wall"), Vector3.forward).point.z - measured.max.z,
                measured.min.y - floor.point.y,
                HitPlane(Shell("roof"), Vector3.up).point.y - measured.max.y,
            };
            Check(gaps.All(gap => Mathf.Abs(gap - 3f) < tolerance),
                "Ribbon must clear each inner shell plane by 2 m, measured W/E/N/S/floor/ceiling " +
                string.Join("/", gaps.Select(gap => gap.ToString("F4"))) + " m.");
            Debug.Log(FormattableString.Invariant(
                $"CONTOUR4 PREPARED MOBIUS RIBBON envelope={measured.size.x:F3}x{measured.size.y:F3}x{measured.size.z:F3} m"));
            var interior = measured.size + new Vector3(gaps[0] + gaps[1], gaps[4] + gaps[5], gaps[2] + gaps[3]);
            Debug.Log(FormattableString.Invariant(
                $"CONTOUR4 PREPARED MOBIUS INTERIOR xyz={interior.x:F3},{interior.y:F3},{interior.z:F3} m gaps west/east/near/far/floor/ceiling={gaps[0]:F3}/{gaps[1]:F3}/{gaps[2]:F3}/{gaps[3]:F3}/{gaps[4]:F3}/{gaps[5]:F3} m"));

            Collider Shell(string part) => room.Find("Geometry/Shell/" + part).GetComponent<Collider>();

            RaycastHit HitPlane(Collider plane, Vector3 direction)
            {
                Check(plane.Raycast(new Ray(reference, direction), out RaycastHit hit, 40f),
                    $"Missing collision plane on {plane.name}.");
                return hit;
            }
        }

        private static void VerifyField(Transform room, Collider ribbon)
        {
            int positions = 0;
            var walk = new MobiusWalk(room, ribbon);
            var access = room.GetComponentsInChildren<Collider>().Where(c =>
                c.name == MobiusHall.Landing || c.name == MobiusHall.CeilingLanding ||
                c.name.StartsWith(MobiusHall.Ramp + "-", StringComparison.Ordinal) ||
                c.name.StartsWith(MobiusHall.CeilingRamp + "-", StringComparison.Ordinal)).ToArray();
            foreach (float lap in new[] { 0f, .25f, .50f, .75f, 1f, 1.25f, 1.50f, 1.75f })
            {
                ModulePose pose = MobiusHall.Surface(lap, 0);
                Vector3 normal = V(pose.Up);
                Vector3 center = V(pose.Position);
                Check(ribbon.Raycast(new Ray(center + normal * .10f, -normal), out RaycastHit hit, .15f),
                    $"Field sample at {lap} misses the physical surface.");
                Check(walk.TryPassiveGravity(hit.point + .99f * normal, out Vector3 up) && Vector3.Dot(up, normal) > .98f &&
                    !walk.TryPassiveGravity(hit.point + 1.01f * normal, out _), $"Field reach or direction changed at {lap} turns.");
                positions++;
            }

            foreach (float lap in new[] { 0f, .25f, .5f, .75f, 1f, 1.25f, 1.5f, 1.75f })
            foreach (int side in new[] { -1, 1 })
            {
                ModulePose edge = MobiusHall.Surface(lap, side * MobiusHall.HalfWidth);
                Vector3 width = side * V(edge.Right);
                Vector3 center = V(edge.Position) + .5f * V(edge.Up);
                Vector3 inside = center - .02f * width, outside = center + .02f * width;
                Check(Strip(inside, out _) && !Strip(outside, out _),
                    $"Strip field spills past its own edge at {lap} turns/{side}.");
                Vector3 normal = V(edge.Up);
                bool composed = walk.TryPassiveGravity(outside, out var combinedUp);
                Check(walk.TryPassiveGravity(inside, out _) && !composed,
                    $"Ribbon field spills past the true ribbon at {lap} turns/{side}: composed={composed}.");
            }
            foreach (bool ceiling in new[] { false, true })
            {
                ModulePose frame = MobiusHall.AccessPose(ceiling, .5f);
                Vector3 point = V(frame.Position), normal = V(frame.Up);
                RaycastHit contact = default;
                Check(access.Any(c => c.Raycast(new Ray(point + normal * .1f, -normal), out contact, .2f)),
                    "Access field probe has no exact physical ramp support.");
                Vector3 inner = contact.point + normal * .99f, outer = contact.point + normal * 1.01f;
                Check(!walk.IsWalkSurface(contact.collider), "An access ramp still attaches ribbon gravity.");
                Vector3 expected = ceiling ? -room.up : room.up;
                Check(Vector3.Dot(walk.RoomUp(inner), expected) > .999f &&
                    walk.TryPassiveGravity(inner, out _) == Strip(inner, out _) &&
                    walk.TryPassiveGravity(outer, out _) == Strip(outer, out _),
                    "Physical access ramps must use their ordinary room half and only the true ribbon skin.");
            }
            Debug.Log($"CONTOUR4 PREPARED MOBIUS FIELD {positions} ribbon and two physical ramp probes at 0.99/1.01 m; clipped ribbon skin, no access field or attachment.");

            bool Strip(Vector3 point, out System.Numerics.Vector3 up) => MobiusHall.TryGravity(N(point), out up);
            System.Numerics.Vector3 N(Vector3 point) => new System.Numerics.Vector3(point.x, point.y, point.z);
        }

        private static void VerifyRamp(Collider ribbon)
        {
            foreach (float lane in new[] { -.55f, 0, .55f })
            {
                float previous = 0;
                for (float x = MobiusHall.RampFoot - .10f; x < MobiusHall.EntranceX; x += .025f)
                    previous = Trace(x, MobiusHall.RampCenter + lane, previous);
                Check(Mathf.Abs(previous - MobiusHall.EntranceHeight) < .002f, "Ramp does not reach the landing.");
            }
            foreach (float lane in new[] { -1.2f, 0, 1.2f })
            {
                float previous = MobiusHall.EntranceHeight;
                for (float z = MobiusHall.RampCenter; z > MobiusHall.LandingInnerEdge - MobiusHall.HalfWidth - .05f; z -= .025f)
                    previous = Trace(MobiusHall.EntranceX + lane, z, previous);
            }

            float Trace(float x, float z, float previous)
            {
                Check(Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out RaycastHit hit, 3.1f) &&
                    Mathf.Abs(hit.point.y - previous) < .015f, $"Lost continuous access support at {x:F2}/{z:F3}.");
                if (z < MobiusHall.LandingInnerEdge - .04f) Check(hit.collider == ribbon, "Access reaches an unrelated surface.");
                FreeCapsule(hit.point, Vector3.up);
                return hit.point.y;
            }
        }

        private static int VerifyWalking(Transform room, Collider ribbon)
        {
            int count = 0;
            try
            {
                foreach (int direction in new[] { -1, 1 })
                foreach (float lane in new[] { -1.65f, 0, 1.65f })
                {
                    var player = new MobiusWalk(room, ribbon);
                    room.rotation = Quaternion.identity;
                    for (int i = 0; i <= 2 * MobiusHall.RibbonSections; i++)
                    {
                        var frame = MobiusHall.Surface(direction * i / (float)MobiusHall.RibbonSections, lane);
                        Physics.SyncTransforms();
                        Vector3 expected = room.TransformDirection(V(frame.Up));
                        Vector3 feet = room.TransformPoint(V(frame.Position));
                        Check(ribbon.Raycast(new Ray(feet + expected * .17f, -expected), out RaycastHit hit, .25f) &&
                            Vector3.Dot(hit.normal, expected) > .99f && Vector3.Distance(feet, hit.point) < .014f,
                            $"Collider mismatch at lane={lane}, sample={direction * i}.");
                        FreeCapsule(hit.point, expected, $"lane={lane}, sample={direction * i}");
                        player.Observe(feet, hit.point, hit.normal);
                        Vector3 up = player.Step(feet, Vector3.zero, null, out var change);
                        Check(player.Attached && Vector3.Dot(up, expected) > .997f &&
                            change == (i == 0 ? MobiusWalk.Change.Landing : MobiusWalk.Change.None),
                            $"Walk detaches or flips at lane={lane}, sample={direction * i}.");
                        room.rotation = Quaternion.FromToRotation(up, Vector3.up) * room.rotation;
                        count++;
                    }
                }
            }
            finally
            {
                room.rotation = Quaternion.identity;
                Physics.SyncTransforms();
            }
            return count;
        }

        private static void VerifyEdge(Transform room, Collider ribbon)
        {
            var walk = new MobiusWalk(room, ribbon);
            Vector3 feet = V(MobiusHall.Surface(0, -2.18f).Position);
            walk.Observe(feet, feet, Vector3.up);
            walk.Step(feet, Vector3.zero, null, out _);
            Check(walk.Attached, "Player did not acquire inner edge.");
            walk.Step(feet + Vector3.back * .18f, Vector3.back, null, out var departure);
            Check(!walk.Attached && departure == MobiusWalk.Change.Release, "Leaving the edge did not release ribbon gravity.");
            for (int i = 0; i < 5; i++) walk.Step(feet + Vector3.up * .07f, Vector3.down, null, out _);
            Check(!walk.Attached, "A ray hit without physical foot contact recaptured the player.");
        }

        private static void VerifyJumps(Transform room, Collider ribbon, Collider ground)
        {
            SimulationMode simulation = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                VerifyJump(0f, true);
                VerifyJump(.12f, true);
                VerifyJump(.23f, true);
                VerifyJump(1f, false);
            }
            finally
            {
                Physics.simulationMode = simulation;
                room.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Physics.SyncTransforms();
            }

            void VerifyJump(float lap, bool returnsToRibbon)
            {
                room.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var pose = MobiusHall.Surface(lap, 0);
                room.rotation = Quaternion.FromToRotation(V(pose.Up), Vector3.up);
                var actor = new GameObject("Mobius physics witness");
                var body = actor.AddComponent<Rigidbody>();
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.sleepThreshold = 0;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                CapsuleCollider capsule = actor.AddComponent<CapsuleCollider>();
                capsule.radius = .34f;
                capsule.height = 1.8f;
                capsule.center = Vector3.up * .9f;
                capsule.providesContacts = true;
                Vector3 feet = room.TransformPoint(V(pose.Position)) + Vector3.up * .024f;
                actor.transform.position = feet;
                body.position = feet;
                bool sawRibbon = false;
                Collider lastGround = null;
                int airSteps = 0;
                var walk = new MobiusWalk(room, ribbon);
                Physics.ContactEvent += Contacts;
                try
                {
                    for (int i = 0; i < 50 && !walk.Attached; i++)
                    {
                        Tick();
                        Vector3 up = walk.Step(body.position, body.linearVelocity, lastGround, out _);
                        if (walk.Attached) Align(up, MobiusWalk.Change.Landing);
                    }
                    Check(walk.Attached && sawRibbon, $"PhysX capsule did not settle on the ribbon at lap={lap}.");
                    float angle = Vector3.Angle(Vector3.up, room.up);
                    Check(walk.Jump(), "Accepted stock impulse did not change the ribbon state.");
                    Check(!walk.TryPassiveGravity(body.position + Vector3.up * .25f, out _),
                        "Intentional takeoff still receives passive ribbon gravity.");
                    Vector3 launch = Vector3.up * 4.65f;
                    Vector3 expectedFirst = body.linearVelocity + launch - room.up * Physics.gravity.magnitude * .01f;
                    body.AddForce(launch * body.mass, ForceMode.Impulse);
                    body.AddForce(MobiusWalk.TakeoffGravityCorrection(room.up, Physics.gravity), ForceMode.Acceleration);
                    Tick();
                    float firstStepError = Vector3.Distance(expectedFirst, body.linearVelocity);
                    Check(firstStepError < .008f, $"First airborne tick at {angle:F1}° used different gravity before the frame change. " +
                        $"Expected {expectedFirst}, actual {body.linearVelocity}, error {firstStepError:F4} m/s.");
                    Vector3 originalCenter = room.InverseTransformPoint(body.worldCenterOfMass);
                    Vector3 localLaunch = room.InverseTransformDirection(body.linearVelocity);
                    Vector3 release = walk.Step(body.position, body.linearVelocity, lastGround, out var departure);
                    Check(!walk.Attached && departure == MobiusWalk.Change.Release && Vector3.Dot(release, room.up) > .999f,
                        "Accepted jump did not release to room gravity.");
                    Align(release, departure);
                    Check(Vector3.Distance(originalCenter, room.InverseTransformPoint(body.worldCenterOfMass)) < .002f &&
                        Vector3.Distance(localLaunch, room.InverseTransformDirection(body.linearVelocity)) < .002f,
                        "Room correction moved the center or changed the takeoff vector.");

                    bool landed = false;
                    for (int i = 0; i < 250 && !landed; i++)
                    {
                        float before = body.linearVelocity.y;
                        Tick();
                        Vector3 up = walk.Step(body.position, body.linearVelocity, lastGround, out var change);
                        if (walk.Attached)
                        {
                            Check(returnsToRibbon && change == MobiusWalk.Change.Landing && sawRibbon && airSteps > 0,
                                $"Jump at {angle:F1}° recaptured without a supported ribbon landing.");
                            Align(up, change);
                            landed = true;
                        }
                        else
                        {
                            Check(Vector3.Dot(up, room.up) > .999f, "Airborne step selected ribbon gravity.");
                            if (!sawRibbon && !lastGround && Mathf.Abs(body.linearVelocity.y - before - Physics.gravity.y * .01f) < .01f)
                                airSteps++;
                            if (!returnsToRibbon && lastGround == ground) landed = true;
                        }
                    }
                    Check(landed && airSteps > 2, $"Jump at {angle:F1}° did not land as expected (ribbon={returnsToRibbon}).");
                    Debug.Log($"CONTOUR4 PREPARED MOBIUS JUMP {angle:F1} degrees | first-tick-error={firstStepError:F4} m/s | " +
                        $"room-gravity-steps={airSteps} | " +
                        $"landed={(returnsToRibbon ? "ribbon" : "hall floor")}");
                }
                finally
                {
                    Physics.ContactEvent -= Contacts;
                    UnityEngine.Object.DestroyImmediate(actor);
                }

                void Align(Vector3 up, MobiusWalk.Change change)
                {
                    Quaternion turn = Quaternion.FromToRotation(up, Vector3.up);
                    Vector3 pivot = change == MobiusWalk.Change.Release
                        ? MobiusWalk.ReleasePivot(body.position, body.worldCenterOfMass, turn) : body.position;
                    room.SetPositionAndRotation(body.position + turn * (room.position - pivot), turn * room.rotation);
                    body.linearVelocity = turn * body.linearVelocity;
                    Physics.SyncTransforms();
                }

                void Tick()
                {
                    sawRibbon = false;
                    lastGround = null;
                    Physics.SyncTransforms();
                    Physics.Simulate(.01f);
                }

                void Contacts(PhysicsScene _, NativeArray<ContactPairHeader>.ReadOnly headers)
                {
                    foreach (var header in headers)
                    for (int j = 0; j < header.pairCount; j++)
                    {
                        ref readonly ContactPair pair = ref header.GetContactPair(j);
                        if (pair.isCollisionExit || (pair.collider != capsule && pair.otherCollider != capsule)) continue;
                        bool first = pair.collider == capsule;
                        Collider other = first ? pair.otherCollider : pair.collider;
                        for (int k = 0; k < pair.contactCount; k++)
                        {
                            ref readonly ContactPairPoint point = ref pair.GetContactPoint(k);
                            Vector3 normal = first ? point.normal : -point.normal;
                            if (other == ribbon)
                            {
                                sawRibbon = true;
                                walk.Observe(body.position, point.position, normal);
                            }
                            else if (normal.y > .5f && Mathf.Abs(body.position.y - point.position.y) < .39f)
                                lastGround = other;
                        }
                    }
                }
            }
        }

        private static void FreeCapsule(Vector3 feet, Vector3 up, string location = "access")
        {
            Collider[] blockers = Physics.OverlapCapsule(feet + up * .40f, feet + up * 1.49f, .34f);
            Check(blockers.Length == 0,
                $"Walking capsule obstructed at {feet}, {location}, colliders={string.Join(",", blockers.Select(c => c.name))}.");
        }

        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("Mobius hall: " + message);
        }

        private static Vector3 V(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
    }
}

