using System;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    public sealed class CatalogPreview : MonoBehaviour
    {
        [Serializable]
        public sealed class Route
        {
            public string Label;
            public bool Cart;
            public Vector3[] Positions;
            public Quaternion[] Rotations;
        }

        public Route[] Routes;
        public Transform Load;
        public Camera RideCamera;
        public GameObject CartFixture;
        public GameObject HandFixture;

        private int routeIndex;
        private int poseIndex;
        private float time;

        private void Start() => SetPose();

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                routeIndex = (routeIndex + 1) % Routes.Length;
                poseIndex = 0;
                SetPose();
            }
            int direction = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            if (direction == 0) return;
            time += Time.deltaTime;
            while (time >= .025f)
            {
                time -= .025f;
                poseIndex = Mathf.Clamp(poseIndex + direction, 0, Routes[routeIndex].Positions.Length - 1);
                SetPose();
            }
        }

        private void SetPose()
        {
            Route route = Routes[routeIndex];
            CartFixture.SetActive(route.Cart);
            HandFixture.SetActive(!route.Cart);
            Load.SetPositionAndRotation(route.Positions[poseIndex], route.Rotations[poseIndex]);
            float height = route.Cart ? 2.65f : 1.65f;
            Vector3 cameraPosition = Load.position + Load.up * height - Load.forward * .15f;
            RideCamera.transform.SetPositionAndRotation(cameraPosition,
                Quaternion.LookRotation(Load.forward - Load.up * .18f, Load.up));
        }
    }
}
