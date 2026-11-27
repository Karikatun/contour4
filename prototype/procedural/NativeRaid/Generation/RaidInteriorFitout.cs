using System.Collections.Generic;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidInteriorFitout
    {
        private enum Equipment { Machine, Controls, Storage, Partition }

        private readonly struct Station
        {
            internal readonly string Name;
            internal readonly Vector3 Center, Size;
            internal readonly Equipment Kind;
            internal readonly int Face;

            internal Station(string name, float x, float z, float width, float depth,
                float height, Equipment kind, int face = 1)
            {
                Name = name;
                Center = new Vector3(x, height / 2, z);
                Size = new Vector3(width, height, depth);
                Kind = kind;
                Face = face;
            }
        }

        private static readonly Station[] Shaft =
        {
            new("Return manifold", 18.85f, -3.65f, 1.45f, 3.4f, 1.9f, Equipment.Machine),
            new("Pressure control", 14.85f, -3.65f, 1.45f, 3.4f, 1.8f, Equipment.Controls),
            new("Incoming filter bank", 2.85f, 3.85f, 3.6f, 1.65f, 1.9f, Equipment.Machine, -1),
            new("Replacement pump cradles", 8.35f, -.85f, 3.6f, 1.65f, 1.65f, Equipment.Storage),
            new("Isolator and bypass pumps", 19.85f, 3.85f, 3.6f, 1.65f, 1.9f, Equipment.Machine, -1),
            new("Field-core transport cases", 19.35f, -.15f, 3.1f, 1.3f, 1.6f, Equipment.Storage),
            new("Service shutoff station", 3.85f, -2.15f, 1.25f, 2.8f, 1.8f, Equipment.Controls),
            new("Ladder maintenance cabinet", 10.85f, -3.65f, 1.8f, 1.2f, 1.65f, Equipment.Storage),
            new("Pump cradle service partition", 6.55f, -2.225f, .18f, 1.10f, 2.3f, Equipment.Partition)
        };

        private static readonly Station[] Calibration =
        {
            new("Reference gauge exchange", 20.35f, 3.85f, 2.5f, 1.15f, 1.7f, Equipment.Storage, -1),
            new("Metrology isolation panel", 1.25f, 5.05f, 1.8f, 1.2f, 1.85f, Equipment.Controls, -1),
            new("Exciter power cabinet", 9.35f, 2.64f, 1.10f, .90f, 1.95f, Equipment.Machine),
            new("Field-stand operator console", 10.85f, 2.52f, 1.8f, .66f, 1.17f, Equipment.Controls),
            new("Coil coolant return", 14.35f, 2.85f, 1.8f, 1.2f, 1.87f, Equipment.Machine),
            new("Reference loading shield", 7.95f, -2.65f, 1.65f, .20f, 2.10f, Equipment.Partition),
            new("Coil service splash return", 14.85f, -2.02f, .20f, 2.80f, 2.10f, Equipment.Partition),
            new("Exciter disconnect access guard", 14.85f, 1.48f, .20f, 1.16f, 2.10f, Equipment.Partition),
            new("Field alignment terminal", 13.35f, 6.35f, 1.8f, 1.2f, 1.65f, Equipment.Controls, -1),
            new("Reference handling splash screen", 1.60f, -2.65f, 2.15f, .24f, 1.85f, Equipment.Partition),
            new("Metrology panel side screen", 3.60f, 5.85f, .24f, 2.15f, 1.85f, Equipment.Partition)
        };

        private static readonly Station[] Trench =
        {
            new("Deck inspection controller", 3.35f, -6.37f, 1.75f, .63f, 1.55f, Equipment.Controls),
            new("Bridge tools locker", 1.05f, -6.35f, 1.4f, .67f, 1.7f, Equipment.Storage),
        };

        private static readonly Station[] Observation =
        {
            new("Cold archive below gallery", 12.35f, -3.65f, 3.6f, 1.65f, 1.8f, Equipment.Storage),
            new("Film loading bench", 13.35f, 3.85f, 1.8f, 1.2f, 1.6f, Equipment.Storage, -1)
        };

        private static readonly Station[] IntactObservation =
        {
            new("Camera test receiver", 10.35f, .35f, 1.8f, 1.2f, 1.8f, Equipment.Machine),
            new("Image capture terminal", 8.85f, 6.35f, 1.8f, 1.2f, 1.7f, Equipment.Controls, -1),
            new("Optical stand control", 6.85f, 2.35f, 1.8f, 1.2f, 1.65f, Equipment.Controls)
        };

        private static readonly Station[] BrokenObservation =
        {
            new("Spare optical heads", 8.85f, 6.35f, 1.8f, 1.2f, 1.65f, Equipment.Storage, -1),
            new("Disabled optics console", 8.35f, 2.35f, 1.8f, 1.2f, 1.7f, Equipment.Controls),
            new("Locked lens carriage", 8.35f, -2.65f, 1.8f, 1.2f, 1.65f, Equipment.Machine)
        };

        private static readonly Station[] Storage =
        {
            new("Sealed archive transport", 13.35f, .85f, 1.8f, 1.2f, 1.7f, Equipment.Storage),
            new("Intake inspection splash screen", 5.10f, .85f, 2.15f, .24f, 2.13f, Equipment.Partition),
            new("Inspection screen return", 6.12f, 1.68f, .19f, 1.42f, 2.13f, Equipment.Partition),
            new("Cold return sorting bank", 11.45f, 1.92f, 1.65f, 1.15f, 1.88f, Equipment.Storage)
        };

        private static readonly Station[] NarrowStorage =
        {
            new("Cold sample return register", 7.85f, -3.65f, 2.5f, .94f, 1.77f, Equipment.Controls, -1)
        };

        private static readonly Station[] Dispatch =
        {
            new("Yard communications panel", 3.35f, -2.02f, .45f, 1.3f, 1.65f, Equipment.Controls),
            new("Sealed shipment supply", 9.93f, -3.55f, 1.55f, .6f, 1.65f, Equipment.Storage, 1)
        };

        // Horizontal top, the two right strokes, horizontal bottom, the two left strokes, middle.
        private static readonly byte[] Numerals = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };
        private static readonly Vector2[] Strokes =
        {
            new(0, .096f), new(.056f, .048f), new(.056f, -.048f), new(0, -.096f),
            new(-.056f, -.048f), new(-.056f, .048f), new(0, 0)
        };

        private readonly Material housing, controls, storage, trim, faceplate;
        private readonly Dictionary<PrimitiveType, Mesh> equipmentMeshes = new();
        private readonly RaidReferenceRoomFitout reference;
        private readonly BrokenEquipment brokenEquipment;

        internal RaidInteriorFitout(Material housing, Material controls, Material storage, Material trim, Material faceplate,
            ICollection<Material> materials, ICollection<Mesh> meshes, BrokenEquipment brokenEquipment)
        {
            this.housing = housing;
            this.controls = controls;
            this.storage = storage;
            this.trim = trim;
            this.faceplate = faceplate;
            reference = new RaidReferenceRoomFitout(housing.shader, materials, meshes, EquipmentMesh, brokenEquipment);
            this.brokenEquipment = brokenEquipment;
        }

        internal Collider Install(string module, Transform room, int layer, List<Collider> colliders)
        {
            Collider cableSupport = reference.Install(module, room, layer, colliders);
            if (cableSupport) colliders.Add(cableSupport);
            Station[] stations = module switch
            {
                "shaft-plant" => Shaft,
                "calibration-sector" => Calibration,
                "maintenance-trench" => Trench,
                "observation-lab" or "observation-lab-broken-floor" => Observation,
                "sample-receiving" => System.Array.Empty<Station>(),
                "sample-storage" or "sample-storage-narrow" => Storage,
                "freight-dispatch" => Dispatch,
                _ => null
            };
            if (stations == null) return cableSupport;
            Transform group = RaidAssembly.Child(room, "Work bays");
            Place(stations);
            if (module == "shaft-plant") brokenEquipment.DressCabinet(room, colliders);
            if (module == "maintenance-trench") DressTrench(group, layer);
            if (module == "calibration-sector") DressCalibration(group, layer, colliders);
            if (module == "observation-lab" || module == "observation-lab-broken-floor")
            {
                bool broken = module == "observation-lab-broken-floor";
                Place(broken ? BrokenObservation : IntactObservation);
                DressObservation(group, layer, colliders, broken);
            }
            if (module == "sample-receiving") DressReceiving(group, layer);
            if (module == "sample-storage-narrow") Place(NarrowStorage);

            return cableSupport;

            void Place(Station[] units)
            {
                foreach (var station in units)
                {
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.name = station.Name;
                    body.layer = layer;
                    body.transform.SetParent(group, false);
                    body.transform.localPosition = station.Center;
                    body.transform.localScale = station.Size;
                    body.GetComponent<MeshRenderer>().sharedMaterial = station.Kind == Equipment.Controls ? controls :
                        station.Kind == Equipment.Storage ? storage : housing;
                    colliders.Add(body.GetComponent<BoxCollider>());

                    Detail(station, body.transform, layer);
                }
            }
        }

        private void DressTrench(Transform group, int layer)
        {
            // The catalog stand bears the equipment and owns collision for its footprint.
            foreach (float x in new[] { 8.64f, 10.50f, 12.36f })
            {
                Piece(group, layer, "Fixed drain pump", new Vector3(x, 1.80f, .10f),
                    new Vector3(1.38f, .76f, 2.90f), housing);
                Piece(group, layer, "Pump nameplate", new Vector3(x, 1.83f, 1.56f),
                    new Vector3(.60f, .20f, .03f), controls);
            }
        }

        private void DressCalibration(Transform group, int layer, List<Collider> colliders)
        {
            var stand = RaidAssembly.Child(group, "Field calibration apparatus");
            Vector3 shaft = new Vector3(11.25f, 3.17f, 0);
            AddCylinder("Reference shaft", shaft, .49f, 5.9f, trim, solid: true);
            foreach (float x in new[] { 8.65f, 11.25f, 13.85f })
            {
                Vector3 coil = new Vector3(x, shaft.y, shaft.z);
                AddCylinder("Exciter coil jacket", coil, 1.27f, .38f, housing, solid: true);
                AddCylinder("Coil retaining band", coil, 1.31f, .11f, faceplate);
                AddCylinder("Bearing flange", coil - Vector3.right * .21f, .81f, .07f, controls);
                AddCylinder("Shaft bearing", coil - Vector3.right * .26f, .58f, .07f, trim);
                Box("Coil mount", new Vector3(x, 2.61f, 0), new Vector3(.48f, .22f, 1.12f), faceplate);
            }
            AddCylinder("Instrument reference face", new Vector3(8.29f, shaft.y, 0), .49f, .06f, controls);
            AddCylinder("Reference head", new Vector3(8.24f, shaft.y, 0), .25f, .09f, trim);
            Box("Reference face service panel", new Vector3(7.986f, 1.66f, 0),
                new Vector3(.035f, 1.00f, 2.25f), housing);
            Box("Reference face badge", new Vector3(7.965f, 1.92f, 0),
                new Vector3(.019f, .18f, 1.03f), trim);
            Box("Field response recorder", new Vector3(10.85f, 2.19f, 2.017f),
                new Vector3(1.54f, .39f, .079f), faceplate);
            Box("Field response scale", new Vector3(10.75f, 2.225f, 2.069f),
                new Vector3(1.12f, .15f, .025f), controls);
            for (int i = 0; i < 5; i++)
                Box("Scale graduation", new Vector3(10.29f + i * .23f, 2.225f, 2.083f),
                    new Vector3(.021f, .082f, .010f), trim);

            // An open guard on the stand's edge protects the raised apparatus and preserves the operator's view.
            foreach (float x in new[] { 9.30f, 12.30f })
                Box("Guard upright", new Vector3(x, 2.94f, 1.88f), new Vector3(.10f, .88f, .14f), faceplate);
            Box("Guard sill", new Vector3(10.80f, 2.56f, 1.88f), new Vector3(3.0f, .08f, .14f), faceplate);
            Box("Guard top rail", new Vector3(10.80f, 3.37f, 1.88f), new Vector3(3.0f, .08f, .14f), faceplate);

            void AddCylinder(string name, Vector3 center, float diameter, float length, Material material, bool solid = false)
            {
                var cylinder = Piece(stand, layer, name, center, new Vector3(diameter, length / 2, diameter),
                    material, PrimitiveType.Cylinder);
                cylinder.transform.localRotation = Quaternion.AngleAxis(90, Vector3.forward);
                if (!solid) return;
                var collider = cylinder.AddComponent<BoxCollider>();
                collider.size = cylinder.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                colliders.Add(collider);
            }

            void Box(string name, Vector3 center, Vector3 size, Material material) =>
                Piece(stand, layer, name, center, size, material);
        }

        private void DressReceiving(Transform group, int layer)
        {
            // The existing intake, sorting shelves and cold locker carry the workflow markers.
            InventoryNumber(group, layer, "Intake", 1, new Vector3(1.46f, 2.54f, -4.11f), 1);
            InventoryNumber(group, layer, "Sort", 2, new Vector3(3.48f, 1.82f, -1.247f), 1);
            InventoryNumber(group, layer, "Sorting shelves", 2, new Vector3(7.88f, 2.13f, 3.213f), -1);
            InventoryNumber(group, layer, "Short-term refrigeration", 3, new Vector3(5.63f, 2.37f, -3.18f), 1);
            Piece(group, layer, "Intake tag slots", new Vector3(1.46f, .80f, -3.02f),
                new Vector3(1.58f, .26f, .06f), controls);
            foreach (float x in new[] { .98f, 1.46f, 1.94f })
                Piece(group, layer, "Manifest slot", new Vector3(x, .83f, -2.984f),
                    new Vector3(.33f, .047f, .014f), faceplate);
            foreach (float x in new[] { 7.22f, 8.54f })
                Piece(group, layer, "Sorting tray field", new Vector3(x, 2.13f, 3.22f),
                    new Vector3(.28f, .053f, .031f), trim);
            Piece(group, layer, "Refrigeration unit fascia", new Vector3(5.63f, 2.365f, -3.47f),
                new Vector3(1.68f, .31f, .55f), controls);
            for (int i = 0; i < 3; i++)
            {
                foreach (int side in new[] { -1, 1 })
                    Piece(group, layer, "Refrigeration intake grille", new Vector3(5.63f + side * .52f, 2.30f + i * .065f, -3.185f),
                        new Vector3(.40f, .028f, .018f), faceplate);
            }
        }

        private void DressObservation(Transform group, int layer, List<Collider> colliders, bool broken)
        {
            var optics = RaidAssembly.Child(group, "Observation test optics");
            // The optical head points along the sightline from the test plinth to the upper gallery rail.
            Vector3 head = new Vector3(7.57f, 2.88f, 1.10f);
            Vector3 axis = (new Vector3(11.68f, 4.85f, -3.72f) - head).normalized;
            Piece(optics, layer, "Optical head pedestal", new Vector3(7.57f, 1.48f, 1.04f),
                new Vector3(.77f, .26f, .84f), faceplate);
            var mast = Piece(optics, layer, "Optical tracking mast", new Vector3(7.57f, 2.13f, 1.04f),
                new Vector3(.43f, 1.31f, .47f), housing);
            colliders.Add(mast.AddComponent<BoxCollider>());
            Piece(optics, layer, "Tracking yoke", new Vector3(7.57f, 2.69f, 1.10f),
                new Vector3(.52f, .26f, .57f), controls);
            Lens("Camera tube", head, .56f, .81f, housing);
            Lens("Retaining bezel", head + axis * .426f, .62f, .096f, faceplate);
            Lens(broken ? "Blind lens cap" : "Optical objective", head + axis * .479f,
                .45f, .024f, broken ? faceplate : trim);

            var gallery = RaidAssembly.Child(group, "Gallery observation station");
            Piece(gallery, layer, "Rail mount", new Vector3(11.92f, 4.60f, -3.327f),
                new Vector3(.63f, .14f, .18f), faceplate);
            Piece(gallery, layer, "Readout bezel", new Vector3(11.92f, 4.704f, -3.365f),
                new Vector3(.60f, .14f, .17f), controls).transform.localRotation = Quaternion.Euler(-27, 0, 0);
            Piece(gallery, layer, "Sightline readout", new Vector3(11.88f, 4.733f, -3.438f),
                new Vector3(.35f, .069f, .012f), faceplate).transform.localRotation = Quaternion.Euler(-27, 0, 0);
            Piece(gallery, layer, "Readout selector", new Vector3(12.15f, 4.74f, -3.43f),
                new Vector3(.032f, .033f, .025f), trim);

            void Lens(string name, Vector3 center, float diameter, float length, Material material) =>
                Piece(optics, layer, name, center, new Vector3(diameter, length / 2, diameter), material,
                    PrimitiveType.Cylinder).transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
        }

        private void InventoryNumber(Transform parent, int layer, string name, int number, Vector3 center, int face)
        {
            var badge = RaidAssembly.Child(parent, name + " " + number.ToString("00"));
            badge.localPosition = center;
            if (face < 0) badge.localRotation = Quaternion.Euler(0, 180, 0);
            Piece(badge, layer, "Number backing", Vector3.zero, new Vector3(.412f, .295f, .028f), faceplate);
            for (int digit = 0; digit < 2; digit++)
            {
                int bits = Numerals[digit == 0 ? number / 10 : number % 10];
                float x = digit == 0 ? -.093f : .093f;
                for (int stroke = 0; stroke < Strokes.Length; stroke++)
                {
                    if ((bits & (1 << stroke)) == 0) continue;
                    Vector2 offset = Strokes[stroke];
                    Vector3 size = stroke == 0 || stroke == 3 || stroke == 6
                        ? new Vector3(.09f, .022f, .008f) : new Vector3(.022f, .077f, .008f);
                    Piece(badge, layer, "Stencil stroke", new Vector3(x + offset.x, offset.y, .017f), size, trim);
                }
            }
        }

        private GameObject Piece(Transform parent, int layer, string name, Vector3 center, Vector3 size,
            Material material, PrimitiveType shape = PrimitiveType.Cube)
        {
            var part = RaidAssembly.Child(parent, name);
            part.gameObject.layer = layer;
            part.localPosition = center;
            part.localScale = size;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = EquipmentMesh(shape);
            part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part.gameObject;
        }

        private Mesh EquipmentMesh(PrimitiveType shape)
        {
            if (!equipmentMeshes.TryGetValue(shape, out var mesh))
            {
                var primitive = GameObject.CreatePrimitive(shape);
                primitive.SetActive(false);
                mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
                equipmentMeshes.Add(shape, mesh);
                if (Application.isPlaying) Object.Destroy(primitive);
                else Object.DestroyImmediate(primitive);
            }
            return mesh;
        }

        private void Detail(Station station, Transform body, int layer)
        {
            // Face details use the body mesh without adding collision or navigation shapes.
            Mesh mesh = body.GetComponent<MeshFilter>().sharedMesh;
            float front = station.Face * .502f;
            float raised = station.Face * .505f;
            switch (station.Kind)
            {
                case Equipment.Machine:
                    Part("Access cover", new Vector3(0, -.06f, front), new Vector3(.73f, .57f, .005f), faceplate);
                    for (int i = 0; i < 3; i++)
                        Part("Cooling vent", new Vector3(-.502f, .24f - i * .08f, 0), new Vector3(.005f, .025f, .60f), faceplate);
                    Part("Service latch", new Vector3(.24f, .10f, raised), new Vector3(.10f, .025f, .004f), trim);
                    break;
                case Equipment.Controls:
                    Part("Control face", new Vector3(0, .21f, front), new Vector3(.74f, .38f, .005f), faceplate);
                    Part("Readout", new Vector3(-.12f, .25f, raised), new Vector3(.32f, .12f, .004f), housing);
                    Part("Status indicator", new Vector3(.22f, .25f, raised), new Vector3(.08f, .07f, .004f), trim);
                    break;
                case Equipment.Storage:
                    for (int i = 0; i < 3; i++)
                    {
                        float height = .25f - i * .26f;
                        Part("Drawer face", new Vector3(0, height, front), new Vector3(.82f, .22f, .005f), faceplate);
                        Part("Drawer pull", new Vector3(0, height + .04f, raised), new Vector3(.25f, .03f, .004f), trim);
                    }
                    break;
                case Equipment.Partition:
                    if (station.Size.x > station.Size.z)
                        Part("Kick plate", new Vector3(0, -.39f, front), new Vector3(.91f, .14f, .005f), faceplate);
                    else
                        Part("Kick plate", new Vector3(-.502f, -.39f, 0), new Vector3(.005f, .14f, .91f), faceplate);
                    break;
            }

            void Part(string name, Vector3 position, Vector3 size, Material material)
            {
                Transform part = RaidAssembly.Child(body, name);
                part.gameObject.layer = layer;
                part.localPosition = position;
                part.localScale = size;
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
        }
    }
}
