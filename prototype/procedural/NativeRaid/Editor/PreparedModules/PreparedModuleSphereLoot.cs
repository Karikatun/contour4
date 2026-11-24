using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.Procedural.Catalog
{
    internal static partial class PreparedModuleLoot
    {
        private const string WestShellSpare = "west-shell-spares";
        private const string EastShellSpare = "east-shell-spares";
        private const string UpperBranchSpare = "upper-path-spares";
        private const string LowerBranchSpare = "lower-path-spares";

        private static IEnumerable<Bay> SphereBays(int mask)
        {
            yield return new Bay("west-bay-spares", "ground", -SphericalChamber.ServiceBayCenter, 3.34f, 1);
            if ((mask & SphericalChamber.EastMask) != 0)
                yield return new Bay("east-bay-spares", "ground", SphericalChamber.ServiceBayCenter, -3.34f, -1);
        }

        private static IEnumerable<Site> SphereSites(RoomQuery query, int mask)
        {
            if ((mask & SphericalChamber.UpperMask) != 0)
                yield return BranchSite(1, SphericalChamber.Left, UpperBranchSpare);
            if ((mask & SphericalChamber.LowerMask) != 0)
                yield return BranchSite(-1, SphericalChamber.Right, LowerBranchSpare);
            Vector3 outward = new Vector3(5, 3, 9).normalized;
            if (query.SphereShellPoint(outward, out Vector3 east))
                yield return ShellSite(EastShellSpare, east, outward, Vector3.down);
            if (query.SphereShellPoint(-outward, out Vector3 west))
                yield return ShellSite(WestShellSpare, west, -outward, Vector3.up);

            static Site BranchSite(int side, string branch, string id)
            {
                // Use the straight part of each branch inside the shell before the
                // dock approach, with open walking space to the inside of the item.
                var frame = SphericalChamber.BranchFrame(side, SphericalChamber.BranchPhaseCount - 1, .12f, side * 1.72f);
                return new Site(id, branch + "-landing", branch + "-sill-deck", LootSurfaceKind.Floor, Size.Small,
                    V(frame.Position), side * Vector3.right, V(frame.Up));
            }

            static Site ShellSite(string id, Vector3 bottom, Vector3 outward, Vector3 towardItem) =>
                new(id, "ground", SphericalChamber.Shell, LootSurfaceKind.Floor, Size.Small,
                    bottom, Vector3.ProjectOnPlane(towardItem, outward), -outward, lift: .018f);
        }

        private static void VerifySphereSites(IEnumerable<Site> sites, int mask)
        {
            var expected = new HashSet<string>(SphereBays(mask).Select(bay => bay.Id)) { WestShellSpare, EastShellSpare };
            if ((mask & SphericalChamber.UpperMask) != 0) expected.Add(UpperBranchSpare);
            if ((mask & SphericalChamber.LowerMask) != 0) expected.Add(LowerBranchSpare);
            var actual = sites.Select(site => site.Id).ToArray();
            if (!expected.SetEquals(actual))
                throw new InvalidOperationException($"{SphericalChamber.VariantId(mask)} loot differs from installed supports. " +
                    $"Expected: {string.Join(", ", expected)}. Found: {string.Join(", ", actual)}.");
        }

        private static bool SphereUp(Vector3 point, bool deck, int mask, out Vector3 up)
        {
            bool found = SphericalGravityField.TryUp(new System.Numerics.Vector3(point.x, point.y, point.z),
                System.Numerics.Vector3.Zero, out var direction, out bool onDeck, mask);
            up = V(direction);
            return found && onDeck == deck;
        }

        private static void VerifySphereSpawn(Transform room, IEnumerable<Site> sites,
            IReadOnlyDictionary<string, Transform> markers, int mask)
        {
            Vector3 position = room.position;
            Quaternion rotation = room.rotation;
            SimulationMode simulation = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                // Change all three world axes. Deck and radial gravity must both
                // cancel the engine's stock world-down acceleration.
                room.SetPositionAndRotation(position + new Vector3(17, 9, -31), Quaternion.Euler(31, 53, -67) * rotation);
                Physics.SyncTransforms();
                foreach (Site site in sites)
                {
                    Transform marker = markers[MarkerPrefix + site.Id];
                    CheckSphereSettling(site, marker, room, mask, compound: false);
                    CheckSphereSettling(site, marker, room, mask, compound: true);
                }
            }
            finally
            {
                room.SetPositionAndRotation(position, rotation);
                Physics.SyncTransforms();
                Physics.simulationMode = simulation;
            }
        }

        private static void CheckSphereSettling(Site site, Transform marker, Transform room, int mask, bool compound)
        {
            var witness = new GameObject("Sphere loot physics witness");
            bool deck = site.Support != SphericalChamber.Shell;
            const float tick = .02f;
            try
            {
                witness.transform.SetPositionAndRotation(marker.position + marker.up * .065f, marker.rotation);
                var body = witness.AddComponent<Rigidbody>();
                body.isKinematic = true;
                if (compound)
                {
                    AddBox(new Vector3(0, .08f, .24f), new Vector3(.74f, .16f, .27f));
                    AddBox(new Vector3(0, .08f, .72f), new Vector3(.68f, .16f, .31f));
                    AddBox(new Vector3(.07f, .43f, .51f), new Vector3(.66f, .54f, .78f));
                }
                else AddBox(new Vector3(0, .5f, .5f), Vector3.one);
                Physics.SyncTransforms();

                body.isKinematic = false;
                Vector3 firstUp = SiteUp();
                ApplyGravity(body, firstUp);
                Physics.Simulate(tick);
                float firstError = (body.linearVelocity + firstUp * (Physics.gravity.magnitude * tick)).magnitude;
                if (firstError > .001f)
                    throw new InvalidOperationException($"{site.Id}: first free tick gravity error {firstError:F5} m/s.");

                body.position = marker.position;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
                Physics.SyncTransforms();
                float maximumDrift = 0;
                for (int step = 0; step < 150; step++)
                {
                    Vector3 up = SiteUp();
                    if (!body.IsSleeping()) ApplyGravity(body, up);
                    Physics.Simulate(tick);
                    maximumDrift = Mathf.Max(maximumDrift, Vector3.Distance(body.position, marker.position));
                }
                float tilt = Quaternion.Angle(marker.rotation, body.rotation);
                if (maximumDrift > .04f || body.linearVelocity.magnitude > .01f || tilt > 5f)
                    throw new InvalidOperationException($"{site.Id}: loose loot shifted {maximumDrift:F4} m, " +
                        $"speed {body.linearVelocity.magnitude:F4} m/s, rotation {tilt:F2}° ({(compound ? "compound" : "box")}).");
                Debug.Log($"CONTOUR4 SPHERE LOOT SETTLED {site.Id} {(deck ? "deck" : "radial")} {(compound ? "compound" : "box")}: " +
                    $"first-tick error={firstError:F5} m/s, max shift={maximumDrift * 1000:F2} mm, rotation={tilt:F2}°, 150 ticks");

                Vector3 SiteUp()
                {
                    if (!SphereUp(room.InverseTransformPoint(body.worldCenterOfMass), deck, mask, out Vector3 localUp))
                        throw new InvalidOperationException($"{site.Id}: the physics witness left its authored gravity field.");
                    Vector3 up = room.TransformDirection(localUp);
                    if (Vector3.Dot(up, marker.up) < .995f)
                        throw new InvalidOperationException($"{site.Id}: loot marker is misaligned with its gravity field.");
                    return up;
                }

                void AddBox(Vector3 center, Vector3 size)
                {
                    var shape = witness.AddComponent<BoxCollider>();
                    shape.center = Vector3.Scale(center, site.Dimensions);
                    shape.size = Vector3.Scale(size, site.Dimensions);
                }
            }
            finally { Object.DestroyImmediate(witness); }
        }

        private sealed partial class RoomQuery
        {
            internal bool SphereShellPoint(Vector3 direction, out Vector3 point)
            {
                Vector3 outward = direction.normalized;
                var hit = Hit(outward * (SphericalChamber.Radius - .12f), outward, .24f);
                point = hit.collider ? frame.InverseTransformPoint(hit.point) : default;
                return hit.collider && hit.collider.name == SphericalChamber.Shell;
            }

            private bool CanRetrieveFromShell(Site site, IReadOnlyList<Volume> accepted, out Vector3 stance)
            {
                int mask = SphericalChamber.GetMask(module.Id);
                stance = default;
                foreach (float ahead in new[] { .7f, .86f })
                {
                    if (!ShellStance(site.Bottom - site.Forward * ahead, out Vector3 feet)) continue;
                    bool approachesClear = true;
                    // Check both approaches along the shell with every other site
                    // filled, including support for the player's full footprint.
                    foreach (int side in new[] { -1, 1 })
                    for (int step = 1; step <= 6; step++)
                        approachesClear &= ShellStance(feet + side * step * .20f * site.Right, out _);
                    if (approachesClear && FindReach(site, feet, new[] { feet }, accepted, out stance)) return true;
                }
                return false;

                bool ShellStance(Vector3 near, out Vector3 feet)
                {
                    if (!SphereShellPoint(near, out feet)) return false;
                    Vector3 up = -feet.normalized;
                    var player = Stance(feet, up);
                    if (site.Item.Intersects(player) || accepted.Any(item => item.Intersects(player)) || Overlaps(player).Any())
                        return false;
                    foreach (float height in new[] { .03f, .9f, 1.75f })
                        if (!SphereUp(feet + up * height, false, mask, out _)) return false;
                    foreach (Vector3 offset in new[] { player.Right * .20f, -player.Right * .20f,
                        player.Forward * .20f, -player.Forward * .20f })
                    {
                        if (!SphereShellPoint(feet + offset, out Vector3 contact) ||
                            Mathf.Abs(Vector3.Dot(contact - feet, up)) > .035f) return false;
                    }
                    return true;
                }
            }
        }
    }
}
