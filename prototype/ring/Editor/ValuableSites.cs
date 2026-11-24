using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Original, deliberately small dressing slice for the existing ring, in game metres.
// This does not import the independently authored procedural room catalog.
public static class ValuableSites
{
    private static Material metal, wood, marking;
    private static readonly List<object> results = new();

    public static void Build(Dictionary<string, Transform> rooms)
    {
        metal = Material("Site painted steel", new Color(.19f, .25f, .24f));
        wood = Material("Site phenolic", new Color(.25f, .17f, .10f));
        marking = Material("Site numbering", new Color(.79f, .65f, .34f));
        Transform observation = Root(rooms["Loot2"]);
        Transform storage = Root(rooms["Loot3"]);
        Transform calibration = Root(rooms["Loot1"]);

        // Operator station: the cassette lies flat on a waist-high table.
        Box(observation, "Operator table top", new Vector3(4.65f, .86f, 4.9f), new Vector3(1.6f, .08f, .9f), wood);
        foreach (float x in new[] { 3.98f, 5.32f })
        foreach (float z in new[] { 4.58f, 5.22f })
            Box(observation, "Operator table leg", new Vector3(x, .41f, z), new Vector3(.07f, .82f, .07f), metal);
        Site(observation, "experiment-cassette", new Vector3(4.65f, .912f, 4.8f));
        // Shallow sloped console and two analog dial housings make this an operator desk.
        Box(observation, "Analog console", new Vector3(4.65f, 1.03f, 5.17f), new Vector3(.94f, .26f, .30f), metal);
        foreach (float x in new[] { 4.42f, 4.88f })
            Box(observation, "Console dial", new Vector3(x, 1.04f, 5.007f), new Vector3(.18f, .15f, .012f), marking);

        // Open numbered cell, with no door or front rail across the capsule's grab route.
        Box(storage, "Sample shelf base", new Vector3(-3.25f, .80f, 5.65f), new Vector3(1.1f, .08f, .62f), metal);
        foreach (float x in new[] { -3.80f, -2.70f })
            Box(storage, "Sample shelf upright", new Vector3(x, .72f, 5.85f), new Vector3(.06f, 1.44f, .06f), metal);
        Box(storage, "Sample shelf back", new Vector3(-3.25f, 1.11f, 5.96f), new Vector3(1.1f, .54f, .04f), metal);
        Box(storage, "Cell number plate", new Vector3(-3.25f, .76f, 5.33f), new Vector3(.32f, .13f, .015f), metal);
        Label(storage, "Cell 04", "04", new Vector3(-3.25f, .76f, 5.315f));
        Site(storage, "control-sample", new Vector3(-3.25f, .852f, 5.62f));

        // Low pallet beside an identifiable calibration stand, outside through-routes.
        Box(calibration, "Calibration pallet", new Vector3(3.8f, .10f, 5.8f), new Vector3(.8f, .20f, .75f), wood);
        Box(calibration, "Calibration stand foot", new Vector3(5f, .08f, 5.8f), new Vector3(.7f, .16f, .7f), metal);
        Box(calibration, "Calibration stand column", new Vector3(5f, .78f, 5.9f), new Vector3(.17f, 1.4f, .17f), metal);
        Box(calibration, "Calibration stand dial", new Vector3(5f, 1.5f, 5.8f), new Vector3(.55f, .35f, .20f), marking);
        Site(calibration, "calibration-weight", new Vector3(3.8f, .212f, 5.8f));

        // Second approved batch: independent supports, keeping the first four sites exact.
        Table(calibration, "Gravimeter side table", new Vector3(-4.4f,.86f,5.8f), new Vector3(1.1f,.08f,.8f));
        Site(calibration,"portable-gravimeter",new Vector3(-4.4f,.912f,5.8f));
        Table(calibration, "Wide measuring table", new Vector3(-1.8f,.86f,5.8f), new Vector3(1.65f,.08f,.9f));
        Site(calibration,"gimbal-gyroscope",new Vector3(-1.8f,.912f,5.8f));
        Box(calibration,"Lens low plinth",new Vector3(1.2f,.12f,5.8f),new Vector3(1.65f,.24f,.85f),metal);
        Site(calibration,"gravity-lens",new Vector3(1.2f,.252f,5.8f));
        Table(storage, "Demonstration sample shelf",new Vector3(0,.80f,5.65f),new Vector3(1.1f,.08f,.7f));
        Site(storage,"mobius-strip",new Vector3(0,.852f,5.65f));

        // Authentic accessible upper observation gallery, 1.40m above the lower hall.
        Box(observation, "Upper gallery deck", new Vector3(-1f, 1.325f, 5.4f), new Vector3(5.8f, .15f, 3.4f), metal);
        foreach (float x in new[] { -3.7f, 1.7f })
        foreach (float z in new[] { 3.9f, 6.9f })
            Box(observation, "Gallery support", new Vector3(x, .625f, z), new Vector3(.10f, 1.25f, .10f), metal);
        for (int i = 0; i < 8; i++)
        {
            float top = (i + 1) * .175f;
            Box(observation, "Gallery stair " + (i + 1), new Vector3(-2.9f, top * .5f, 1.075f + i * .35f),
                new Vector3(1.4f, top, .35f), metal);
        }
        // Handrail leaves the complete 1.40m stair opening clear.
        Rail(observation, new Vector3(-3.87f, 1.4f, 5.4f), new Vector3(.05f, 1f, 3.4f));
        Rail(observation, new Vector3(1.87f, 1.4f, 5.4f), new Vector3(.05f, 1f, 3.4f));
        Rail(observation, new Vector3(-1f, 1.4f, 7.07f), new Vector3(5.8f, 1f, .05f));
        Rail(observation, new Vector3(-.08f, 1.4f, 3.73f), new Vector3(3.9f, 1f, .05f));
        Box(observation, "Open sensor rack base", new Vector3(-.5f, 1.43f, 6.35f), new Vector3(.62f, .06f, .48f), metal);
        foreach (float x in new[] { -.76f, -.24f })
            Box(observation, "Open sensor rack upright", new Vector3(x, 2.15f, 6.5f), new Vector3(.04f, 1.5f, .04f), metal);
        Box(observation, "Open sensor rack rear brace", new Vector3(-.5f, 2.65f, 6.51f), new Vector3(.56f, .05f, .04f), metal);
        Site(observation, "sensor-rod", new Vector3(-.5f, 1.472f, 6.30f));

        Physics.SyncTransforms();
        CheckSite(observation, "experiment-cassette", new Vector3(.24f, .08f, .16f), "Operator table top");
        CheckSite(storage, "control-sample", new Vector3(.12f, .30f, .12f), "Sample shelf base");
        CheckSite(calibration, "calibration-weight", new Vector3(.33f, .30f, .30f), "Calibration pallet");
        CheckSite(observation, "sensor-rod", new Vector3(.22f, 1.65f, .22f), "Open sensor rack base");
        CheckSite(calibration,"portable-gravimeter",new Vector3(.46f,.34f,.32f),"Gravimeter side table");
        CheckSite(calibration,"gimbal-gyroscope",new Vector3(1.18f,.76f,.55f),"Wide measuring table");
        CheckSite(calibration,"gravity-lens",new Vector3(1.35f,.95f,.52f),"Lens low plinth");
        CheckSite(storage,"mobius-strip",new Vector3(.52f,.43f,.22f),"Demonstration sample shelf");
        foreach(float x in new[] {-4.4f,-1.8f,1.2f})
            ClearBox(calibration,"second batch approach "+x,new Vector3(x,.97f,3.8f),new Vector3(1.7f,1.85f,1.7f));
        ClearBox(storage,"demonstrator approach",new Vector3(0,.97f,4.3f),new Vector3(.8f,1.85f,.8f));
        for(int i=0;i<=18;i++)
            ClearBox(calibration,"loaded lens cart turn "+i,new Vector3(1.2f,.98f,3.8f),new Vector3(1.65f,1.90f,2.50f),Quaternion.Euler(0,i*5,0));
        ClearBox(observation, "rod approach and turn bay", new Vector3(-.5f, 2.345f, 4.85f), new Vector3(1.9f, 1.85f, 1.9f));
        ClearSphere(observation, "rod horizontal rotation", new Vector3(-.5f, 2.38f, 5.05f), .86f);
        ClearBox(observation, "operator approach", new Vector3(4.65f, .94f, 3.8f), new Vector3(.7f, 1.85f, .7f));
        ClearBox(storage, "sample approach", new Vector3(-3.25f, .94f, 4.75f), new Vector3(.7f, 1.85f, .7f));
        ClearBox(calibration, "pallet approach", new Vector3(3.8f, .94f, 4.7f), new Vector3(1.4f, 1.85f, 1.4f));
        // Walked stair dimensions and continuity, including full player envelope/headroom.
        for (int i = 0; i < 8; i++)
            ClearBox(observation, "stair headroom " + (i + 1), new Vector3(-2.9f, (i + 1) * .175f + .937f, 1.075f + i * .35f),
                new Vector3(.65f, 1.85f, .22f));
        Directory.CreateDirectory("ValidationOutput");
        File.WriteAllText("ValidationOutput/catalog-sites.txt", string.Join("\n", results));
        Debug.Log("[Contour4Sites] PASS: eight catalog envelopes/supports, second batch approaches, loaded lens cart turn and original gallery checks.");
    }

    private static Transform Root(Transform room)
    {
        var root = Child(room, "Catalog sites", Vector3.zero);
        root.localScale = Vector3.one / room.localScale.x;
        // Pick the solid wall opposite the south portal rather than assuming FBX axis sign.
        var portal = room.Find("Markers").Cast<Transform>().First(node => node.name.Contains("__South_"));
        if (portal.localPosition.z > 0) root.localRotation = Quaternion.Euler(0, 180, 0);
        return root;
    }

    public static void CheckOrdinaryVolume(Transform marker, Vector3 size)
    {
        var hits = Physics.OverlapBox(marker.position, size * .5f - Vector3.one * .003f, marker.parent.parent.rotation,
            ~0, QueryTriggerInteraction.Ignore).Where(hit => hit.transform.IsChildOf(marker.parent.parent.Find("Catalog sites"))).ToArray();
        if (hits.Length != 0)
            throw new InvalidOperationException("Original volume " + marker.name + " overlaps new site furniture: " + string.Join(",", hits.Select(hit => hit.name)));
        Debug.Log("[Contour4Sites] Original random loot volume clear: " + marker.name);
    }

    public static void CheckLensRoute(Dictionary<string,Transform> parts)
    {
        var path=new List<Pose>();
        void At(Vector3 p,Vector3 forward,Vector3 up) => path.Add(new Pose(p,Quaternion.LookRotation(forward,up)));
        var calibration=parts["Loot1"].Find("Catalog sites");
        var quota=parts["Quota"];
        var exit=parts["Loot1"].Find("Markers/Loot1__West_Quota");
        var truck=quota.Find("Markers/Quota__South_Truck");
        Vector3 start=calibration.TransformPoint(new Vector3(1.2f,0,3.8f));
        Vector3 center=calibration.TransformPoint(Vector3.zero);
        At(start,center-start,calibration.up);
        At(center,exit.position-center,calibration.up);
        At(exit.position-exit.forward*2,exit.forward,calibration.up);
        var bridge=parts["BridgeQuotaLoot1"].Find("Markers");
        var samples=bridge.Cast<Transform>().Where(t=>t.name.Contains("__Sample_") || t.name.EndsWith("__Start") || t.name.EndsWith("__End"))
            .OrderBy(t=>t.name.EndsWith("__Start") ? 0 : t.name.EndsWith("__End") ? 100 : int.Parse(t.name.Substring(t.name.LastIndexOf('_')+1))).Reverse();
        foreach(var sample in samples) At(sample.position,-sample.forward,sample.up);
        Vector3 entry=parts["Quota"].Find("Markers/Quota__East_Loot1").position;
        Vector3 qcenter=quota.position;
        At(Vector3.Lerp(entry,qcenter,.3f),qcenter-entry,quota.up);
        At(qcenter,truck.position-qcenter,quota.up);
        At(truck.position-truck.forward*1.5f,truck.forward,quota.up);
        int count=0;
        for(int i=1;i<path.Count;i++)
        {
            var a=path[i-1];var b=path[i];
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a.position,b.position)/.12f),Mathf.CeilToInt(Quaternion.Angle(a.rotation,b.rotation)/4));
            for(int n=0;n<=steps;n++)
            {
                float t=n/(float)steps;Vector3 feet=Vector3.Lerp(a.position,b.position,t);Quaternion rotation=Quaternion.Slerp(a.rotation,b.rotation,t);
                Vector3 up=rotation*Vector3.up;
                // Chassis/cargo above wheel clearance; a flat wheelbase rides above the curved deck chord.
                var hits=Physics.OverlapBox(feet+up*1.075f,new Vector3(.825f,.825f,1.25f),rotation,~0,QueryTriggerInteraction.Ignore);
                if(hits.Length>0)throw new InvalidOperationException("Ring loaded lens route sample "+count+" at "+feet+" intersects "+string.Join(",",hits.Select(c=>c.name)));
                foreach(float x in new[]{-.65f,.65f})foreach(float z in new[]{-.85f,.85f})
                    if(!Physics.Raycast(feet+rotation*new Vector3(x,.55f,z),-up,out var hit,1.1f,~0,QueryTriggerInteraction.Ignore) || Vector3.Dot(hit.normal,up)<.6f)
                        throw new InvalidOperationException("Ring loaded lens cart lacks wheel support at "+feet+" sample "+count);
                count++;
            }
        }
        Record("Ring loaded lens 1.65x1.90x2.50m chassis/cargo, four wheel rays, all orientations: stand approach→BridgeQuotaLoot1→Quota→native truck doorway PASS; samples="+count);
        File.WriteAllText("ValidationOutput/catalog-sites.txt",string.Join("\n",results));
    }

    private static void CheckSite(Transform root, string key, Vector3 size, string support)
    {
        var marker = root.Find("valuable__" + key);
        ClearBox(root, key + " initial envelope", marker.localPosition + Vector3.up * size.y * .5f, size);
        if (!Physics.Raycast(marker.position + marker.up * .025f, -marker.up, out RaycastHit hit, .10f) || hit.collider.name != support)
            throw new InvalidOperationException(key + " does not stand on " + support);
        Record(key + " bottom=" + marker.position.ToString("F4") + " size=" + size.ToString("F3") + " support=" + support + " gap=" + (hit.distance - .025f).ToString("F4"));
    }

    private static void ClearBox(Transform root, string name, Vector3 center, Vector3 size, Quaternion? rotation = null)
    {
        var hits = Physics.OverlapBox(root.TransformPoint(center), size * .5f - Vector3.one * .001f, root.rotation * (rotation ?? Quaternion.identity),
            ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length != 0) throw new InvalidOperationException(name + " intersects " + string.Join(",", hits.Select(hit => hit.name)));
        Record(name + " clear; center=" + center.ToString("F3") + " size=" + size.ToString("F3"));
    }

    private static void ClearSphere(Transform root, string name, Vector3 center, float radius)
    {
        var hits = Physics.OverlapSphere(root.TransformPoint(center), radius, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length != 0) throw new InvalidOperationException(name + " intersects " + string.Join(",", hits.Select(hit => hit.name)));
        Record(name + " clear; radius=" + radius);
    }

    private static void Record(string line) { results.Add(line); Debug.Log("[Contour4Sites] " + line); }
    private static void Site(Transform parent, string key, Vector3 position) => Child(parent, "valuable__" + key, position);
    private static void Table(Transform parent,string name,Vector3 center,Vector3 size)
    {
        Box(parent,name,center,size,wood);
        float height=center.y-size.y/2;
        foreach(float x in new[]{-.5f,.5f}) foreach(float z in new[]{-.5f,.5f})
            Box(parent,name+" leg",new Vector3(center.x+x*(size.x-.12f),height/2,center.z+z*(size.z-.12f)),new Vector3(.06f,height,.06f),metal);
    }
    private static Transform Child(Transform parent, string name, Vector3 position)
    {
        var item = new GameObject(name).transform;
        item.SetParent(parent, false); item.localPosition = position; return item;
    }
    private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name; item.transform.SetParent(parent, false);
        item.transform.localPosition = position; item.transform.localScale = size;
        item.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
    private static void Rail(Transform parent, Vector3 bottom, Vector3 size)
    {
        Box(parent, "Gallery top handrail", bottom + Vector3.up * .98f, new Vector3(size.x, .04f, size.z), metal);
        Box(parent, "Gallery lower handrail", bottom + Vector3.up * .5f, new Vector3(size.x, .04f, size.z), metal);
        Vector3 axis = size.x > size.z ? Vector3.right * size.x * .5f : Vector3.forward * size.z * .5f;
        foreach (float sign in new[] { -1f, 1f })
            Box(parent, "Gallery handrail post", bottom + sign * axis + Vector3.up * .5f, new Vector3(.05f, 1f, .05f), metal);
    }
    private static void Label(Transform parent, string name, string text, Vector3 position)
    {
        var label = Child(parent, name, position);
        // Original geometry digits 04; no extracted font, texture or external resource.
        foreach (float x in new[] { -.10f, -.02f })
            Box(label, "0 vertical", new Vector3(x, 0, 0), new Vector3(.012f, .08f, .005f), marking);
        foreach (float y in new[] { -.04f, .04f })
            Box(label, "0 horizontal", new Vector3(-.06f, y, 0), new Vector3(.09f, .012f, .005f), marking);
        Box(label, "4 right", new Vector3(.09f, 0, 0), new Vector3(.012f, .09f, .005f), marking);
        Box(label, "4 left", new Vector3(.02f, .025f, 0), new Vector3(.012f, .05f, .005f), marking);
        Box(label, "4 middle", new Vector3(.055f, 0, 0), new Vector3(.08f, .012f, .005f), marking);
    }
    private static Material Material(string name, Color color)
    {
        var material = new Material(Shader.Find("Standard")) { name = name, color = color };
        AssetDatabase.CreateAsset(material, "Assets/Contour4Ring/" + name + ".mat"); return material;
    }
}

