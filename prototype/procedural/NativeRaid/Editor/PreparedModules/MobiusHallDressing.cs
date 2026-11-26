using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.Procedural.Catalog
{
    // Only the Mobius hall owns these fixed, non-interactive workstations.
    internal static class MobiusHallDressing
    {
        const string Dir = "Assets/Contour4/PreparedModules/mobius-hall";
        static Material[] mats;
        static Mesh cube;
        static readonly Dictionary<string, Mesh> old = new Dictionary<string, Mesh>();
        static bool initialized;
        static Transform group;
        static List<CombineInstance>[] pieces;
        static Material Material(string name, Color color)
        {
            string path = Dir + "/institute-" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Standard")); m.name = "Mobius institute " + name; AssetDatabase.CreateAsset(m, path); }
            m.color = color; m.SetFloat("_Glossiness", .2f); m.SetFloat("_Metallic", 0); EditorUtility.SetDirty(m); return m;
        }
        static void Box(int m, Vector3 p, Vector3 s) => pieces[m].Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(p, Quaternion.identity, s) });
        static void Start(string name)
        {
            group = new GameObject(name).transform; group.SetParent(parent, false);
            pieces = Enumerable.Range(0, mats.Length).Select(_ => new List<CombineInstance>()).ToArray();
        }
        static Transform parent;
        static void Finish()
        {
            for (int i=0;i<mats.Length;i++) if (pieces[i].Count>0)
            {
                string name = group.name + " " + i; var mesh = new Mesh { name = name }; mesh.CombineMeshes(pieces[i].ToArray(), true, true);
                if (old.TryGetValue(name, out var prev)) { EditorUtility.CopySerialized(mesh, prev); Object.DestroyImmediate(mesh); mesh=prev; old.Remove(name); EditorUtility.SetDirty(mesh); }
                else if (!initialized) { AssetDatabase.CreateAsset(mesh, Dir + "/mobius-dressing-geometry.asset"); initialized=true; }
                else AssetDatabase.AddObjectToAsset(mesh, Dir + "/mobius-dressing-geometry.asset");
                var o = new GameObject("Finish " + i); o.transform.SetParent(group, false); o.AddComponent<MeshFilter>().sharedMesh=mesh; o.AddComponent<MeshRenderer>().sharedMaterial=mats[i];
            }
        }
        static void Bench(string name, float x)
        {
            Start(name); float z=-7.65f; bool registry=x>0;
            Box(registry?2:1,new Vector3(x,.88f,z),new Vector3(3.2f,.10f,.85f));
            foreach(float d in new[]{-1.43f,1.43f}) Box(0,new Vector3(x+d,.43f,z),new Vector3(.12f,.86f,.72f));
            Box(0,new Vector3(x,1.18f,-8.00f),new Vector3(3.1f,.55f,.13f));
            for(int k=0;k<4;k++) Box(1,new Vector3(x-1.15f+k*.75f,1.2f,-7.92f),new Vector3(.16f,.18f,.035f));
            Box(0,new Vector3(x,2.24f,-8.05f),new Vector3(2.7f,.12f,.16f));
            Box(1,new Vector3(x,2.18f,-7.96f),new Vector3(2.35f,.05f,.08f));
            foreach(float d in new[]{-1.2f,1.2f}) Box(3,new Vector3(x+d,1.72f,-8.05f),new Vector3(.07f,.95f,.08f));
            if(registry) {
                // Recorder is mounted at the back edge, leaving the desk surface usable.
                Box(0,new Vector3(x+1.15f,1.42f,-7.98f),new Vector3(.66f,.94f,.32f));
                Box(2,new Vector3(x+1.15f,1.52f,-11.80f),new Vector3(.50f,.50f,.035f));
                Box(1,new Vector3(x+1.15f,1.52f,-7.77f),new Vector3(.37f,.28f,.02f));
                Box(3,new Vector3(x+1.15f,1.12f,-7.77f),new Vector3(.34f,.035f,.02f));
            } else {
                Box(0,new Vector3(x-1.15f,1.56f,-8.04f),new Vector3(.62f,.98f,.18f));
                Box(2,new Vector3(x-1.15f,1.58f,-7.93f),new Vector3(.48f,.72f,.04f));
                for(int k=0;k<4;k++) Box(1,new Vector3(x-1.15f,1.35f+k*.15f,-7.90f),new Vector3(.30f,.045f,.02f));
            }
            // Stool tucked under the table, leaving a usable approach.
            Box(2,new Vector3(x,.48f,-7.5f),new Vector3(.48f,.12f,.45f));
            Box(0,new Vector3(x,.24f,-7.5f),new Vector3(.10f,.48f,.10f));
            Box(0,new Vector3(x,.04f,-7.5f),new Vector3(.50f,.08f,.48f));
            Finish();
            group.localPosition = new Vector3(x*(MobiusHall.Scale-1),0,(-7.65f-MobiusHall.WorktableSetback)*MobiusHall.Scale+7.65f);
        }
        static void Instrument()
        {
            Start("Central ribbon monitoring and control");
            Box(0,new Vector3(0,1.04f,-7.75f),new Vector3(2.45f,2.08f,.72f));
            Box(2,new Vector3(0,1.55f,-7.375f),new Vector3(2.13f,.82f,.04f));
            for(int i=0;i<3;i++) { float x=-.65f+i*.65f; Box(1,new Vector3(x,1.63f,-7.345f),new Vector3(.46f,.38f,.025f)); Box(3,new Vector3(x,1.2f,-7.34f),new Vector3(.17f,.11f,.03f)); }
            Box(1,new Vector3(0,.78f,-7.36f),new Vector3(2.15f,.42f,.09f));
            for(int i=0;i<5;i++) Box(2,new Vector3(-.8f+i*.4f,.78f,-7.30f),new Vector3(.18f,.12f,.03f));
            for(int i=0;i<3;i++) { float x=-.65f+i*.65f; Box(2,new Vector3(x,1.63f,-7.32f),new Vector3(.32f,.32f,.025f)); for(int j=0;j<12;j++) { float a=j*Mathf.PI/6; Box(3,new Vector3(x+.14f*Mathf.Cos(a),1.63f+.14f*Mathf.Sin(a),-7.30f),new Vector3(.032f,.032f,.025f)); } Box(1,new Vector3(x,1.63f,-7.28f),new Vector3(.02f,.20f,.02f)); }
            Finish();
            group.localPosition = Vector3.forward * MobiusHall.MonitoringOffsetZ;
        }
        static void Diagnostics()
        {
            Start("West field diagnostics");
            Box(0,new Vector3(-11.35f,1.15f,-5.1f),new Vector3(.52f,2.3f,1.65f));
            Box(2,new Vector3(-11.07f,1.48f,-5.1f),new Vector3(.04f,1.15f,1.35f));
            for(int i=0;i<3;i++) { float z=-5.5f+i*.4f; Box(1,new Vector3(-11.04f,1.60f,z),new Vector3(.025f,.45f,.28f)); Box(3,new Vector3(-11.02f,1.31f,z),new Vector3(.02f,.06f,.18f)); }
            Box(3,new Vector3(-11.63f,2.7f,-5.1f),new Vector3(.08f,1.0f,.08f));
            Box(3,new Vector3(-11.45f,3.2f,-5.1f),new Vector3(.40f,.08f,.08f));
            Finish();
            group.localPosition = new Vector3(-11.35f * (MobiusHall.Scale - 1), 0,
                (-5.1f - MobiusHall.WallEquipmentSetback) * MobiusHall.Scale + 5.1f);
        }
        static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Quaternion rotation)
        {
            var o = new GameObject(name); o.transform.SetParent(parent, false);
            o.transform.localPosition=position; o.transform.localRotation=rotation; o.transform.localScale=scale;
            o.AddComponent<MeshFilter>().sharedMesh=cube; o.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        static void Tripod(Transform root, string name, Vector3 floor, float turn)
        {
            var fixture = new GameObject(name).transform; fixture.SetParent(root,false); fixture.localPosition=floor;
            Part(fixture,"Mast",new Vector3(0,.80f,0),new Vector3(.08f,1.50f,.08f),mats[2],Quaternion.identity);
            for(int i=0;i<3;i++) {
                float a=i*2*Mathf.PI/3; var foot=new Vector3(.44f*Mathf.Cos(a),.045f,.44f*Mathf.Sin(a));
                var top=new Vector3(0,.68f,0); var d=top-foot;
                Part(fixture,"Splayed leg " + (i+1),(top+foot)*.5f,new Vector3(.065f,d.magnitude,.065f),mats[2],Quaternion.FromToRotation(Vector3.up,d));
                Part(fixture,"Foot " + (i+1),foot,new Vector3(.12f,.09f,.12f),mats[2],Quaternion.identity);
            }
            var s=MobiusHall.Surface(turn,0).Position; var target=new Vector3(s.X,s.Y,s.Z);
            var head=new GameObject("Adjustable spotlight head").transform; head.SetParent(fixture,false);
            head.localPosition=new Vector3(0,1.35f,0); head.localRotation=Quaternion.LookRotation(target-(floor+head.localPosition));
            Part(head,"Black lamp housing",new Vector3(0,0,-.15f),new Vector3(.42f,.38f,.38f),mats[2],Quaternion.identity);
            Part(head,"Ivory lens",new Vector3(0,0,.055f),new Vector3(.32f,.28f,.025f),mats[1],Quaternion.identity);
            var emitter=new GameObject("Spot emitter").transform; emitter.SetParent(head,false); emitter.localPosition=new Vector3(0,0,.075f);
            var light=emitter.gameObject.AddComponent<Light>(); light.type=LightType.Spot; light.color=new Color(1f,.88f,.70f);
            light.intensity=12f; light.range=Vector3.Distance(floor+head.localPosition,target)*2.2f; light.spotAngle=70f; light.innerSpotAngle=42f;
            light.shadows=LightShadows.None; light.renderMode=LightRenderMode.ForcePixel;
        }
        internal static void Add(Transform root)
        {
            mats = new[]{Material("enamel",new Color(.25f,.32f,.25f)),Material("ivory",new Color(.70f,.67f,.53f)),Material("bakelite",new Color(.075f,.09f,.08f)),Material("brass",new Color(.48f,.42f,.26f)),Material("plaster",new Color(.58f,.58f,.49f)),Material("floor",new Color(.19f,.24f,.21f))};
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (MobiusHall.WorkstationHulls.Any(h => h.id == r.name) || MobiusHall.TripodHulls.Any(h => h.id == r.name)) { r.enabled=false; continue; }
                if (r.name.StartsWith(MobiusHall.EastStorage + "-", StringComparison.Ordinal))
                {
                    r.sharedMaterial = r.name.Contains("-shelf-", StringComparison.Ordinal) ? mats[1] : mats[0];
                    continue;
                }
                var n=r.name; var m = n==MobiusHall.Ribbon || n==MobiusHall.RailInner || n==MobiusHall.RailOuter ? mats[0] : n==MobiusHall.Ramp || n==MobiusHall.Landing ? mats[5] : r.transform.parent.name=="Shell" || r.transform.parent.name=="Conditional plugs" ? mats[4] : r.transform.parent.name=="Floors" ? mats[5] : null;
                if(m) r.sharedMaterial=m;
            }
            parent=new GameObject("Mobius dressing").transform; parent.SetParent(root,false);
            string path=Dir+"/mobius-dressing-geometry.asset"; foreach(var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()) old[mesh.name]=mesh; initialized=old.Count>0;
            var temp=GameObject.CreatePrimitive(PrimitiveType.Cube); cube=temp.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(temp);
            Bench("Worktable west",-4.5f); Bench("Worktable east",4.5f); Instrument(); Diagnostics();
            // Existing four emitters become warm ceiling-mounted practical fixtures.
            var lights=root.Find("Ribbon work lights").GetComponentsInChildren<Light>();
            for(int i=0;i<lights.Length;i++) { var l=lights[i]; l.transform.localPosition=i<2 ? new Vector3(i==0?-6.75f:6.75f,2.16f,(-7.85f-MobiusHall.WorktableSetback)*MobiusHall.Scale) : new Vector3(i==2?-6:6,11.65f,6.45f); l.color=new Color(1f,.91f,.76f); l.intensity=i<2?3.5f:4f; l.range=i<2?14f:18f; l.shadows=LightShadows.None; l.renderMode=LightRenderMode.ForcePixel; }
            Start("Ceiling practical housings"); foreach(var l in lights.Skip(2)) { var p=l.transform.localPosition; Box(0,new Vector3(p.x,11.91f,p.z),new Vector3(1.55f,.18f,.50f)); Box(1,new Vector3(p.x,11.80f,p.z),new Vector3(1.36f,.06f,.35f)); } Finish();
            Tripod(root,"Ribbon tripod west",new Vector3(-9.4f*MobiusHall.Scale,0,-3f*MobiusHall.Scale),.75f);
            Tripod(root,"Ribbon tripod east",new Vector3(9.4f*MobiusHall.Scale,0,-3f*MobiusHall.Scale),.20f);
            Tripod(root,"Ribbon tripod north",new Vector3(9.2f*MobiusHall.Scale,0,7.7f*MobiusHall.Scale),.04f);
            // A wall practical illuminates the downward-facing upper approach. It has
            // no collider and remains outside the walking lane at the near wall.
            var middle=MobiusHall.AccessPose(true,.5f).Position;
            var practicalPosition=new Vector3(middle.X,middle.Y-2.5f,MobiusHall.CeilingRampOuterEdge+.2f);
            Start("Ceiling ramp wall practical");
            Box(0,practicalPosition+new Vector3(0,0,-.14f),new Vector3(1.55f,.50f,.16f));
            Box(1,practicalPosition+new Vector3(0,0,-.035f),new Vector3(1.36f,.35f,.025f));
            Finish();
            var practical=new GameObject("Ceiling ramp practical emitter").AddComponent<Light>();
            practical.transform.SetParent(group,false);practical.transform.localPosition=practicalPosition;
            practical.type=LightType.Point;practical.color=new Color(1f,.91f,.76f);
            practical.intensity=2.5f;practical.range=12f;practical.shadows=LightShadows.None;
            practical.renderMode=LightRenderMode.ForcePixel;
            foreach(var stale in old.Values) Object.DestroyImmediate(stale,true); old.Clear();
        }
    }
}


