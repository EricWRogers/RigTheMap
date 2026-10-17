using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.Netcode.Tests
{
    /// <summary>
    /// The world frame a ghost is parented under.
    /// </summary>
    /// <remarks>
    /// <see cref="Identity"/> is the frame both worldPositionStays modes leave the local transform alone in. <br />
    /// <see cref="RotatedAndNonUniformScaled"/> is the frame that shears. <br />
    /// </remarks>
    internal enum ParentFrame
    {
        Identity,
        Translated,
        Rotated,
        NonUniformScaled,
        RotatedAndNonUniformScaled,
    }

    /// <summary>
    /// End-to-end tests for GhostObject (GameObject bridge) parenting. <br />
    /// Covers replicating a parent change, worldPositionStays and how it is inferred, a non-uniform parent scale
    /// replicated through PostTransformMatrix, a shearing hierarchy, unparenting, and opting out of parenting
    /// altogether. <br />
    /// Runs against a dedicated server, for both interpolated and predicted ghosts. NetCodeTestWorld.OverrideUseSingleWorldHost re-runs the same cases against a single-world host. <br />
    /// Transforms are sampled every frame across a reparent, not only once the parenting has settled. <br />
    /// </summary>
    internal class ParentingTests
    {
        // Interpolated ghosts lag the server by a couple of ticks. A parenting change takes visual effect only once
        // the interpolation window has advanced past the reparent tick, so tick well beyond it.
        const int k_SettleTicks = 32;

        static readonly Vector3 k_ParentPos = new Vector3(5f, 0f, 0f);
        static readonly Vector3 k_ChildPos = new Vector3(10f, 0f, 0f);

        // 0.002 is quantization headroom, not slack. At 0.0002 the non-uniform scale and shear cases fail on the
        // PostTransformMatrix 3D scale, which is the tightest bound here. Position holds far below that.
        static void AssertApprox(Vector3 expected, Vector3 actual, string what, float tolerance = 0.002f)
        {
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"{what}: expected ~{expected} but was {actual}");
        }

        static ParentingData ParentingOf(GhostObject ghost)
        {
            return ghost.World.EntityManager.GetComponentData<ParentingData>(ghost.Entity);
        }

        // Resolves the client-side GhostObject that mirrors a given server ghost, matched by the replicated GhostId.
        static GhostObject ClientInstanceOf(GhostObject serverGhost, World clientWorld)
        {
            var id = serverGhost.GhostId;
            var match = PredictionCallbackHelper.ClientInstances
                .FirstOrDefault(pch => pch.Ghost.World == clientWorld && pch.Ghost.GhostId == id);
            Assert.IsNotNull(match, $"No client instance found for server ghost id {id}");
            return match.Ghost;
        }

        /// <summary>
        /// Returns the client-side copy of the GhostObject instance.<br />
        /// This will assert if the client-side transform does not matche the server side instance.
        /// </summary>
        static GhostObject RemoteChildOf(NetCodeTestWorld testWorld, GhostObject child)
        {
            var clientChild = ClientInstanceOf(child, RemoteClientWorld(testWorld));
            AssertApprox(child.transform.position, clientChild.transform.position,
                "client child world position should mirror the authority before the reparent");
            return clientChild;
        }

        /// <summary>
        /// Creates and returns a GhostPrefab with parent replication enabled.
        /// </summary>
        static GameObject CreateGhostPrefab(string name, Vector3 localScale, GhostOptimizationMode optimizationMode = GhostOptimizationMode.Dynamic)
        {
            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(
                NetCodeTestWorld.k_GeneratedFolderBasePath, name, autoRegister: false, typeof(PredictionCallbackHelper));
            prefab.transform.localScale = localScale;
            var ghost = prefab.GetComponent<GhostObject>();
            ghost.ParentReplication = true;
            ghost.OptimizationMode = optimizationMode;
            Netcode.RegisterPrefab(prefab);
            return prefab;
        }

        /// <summary>
        /// Spawns a parent and child ghost on the server at k_ParentPos / k_ChildPos and ticks until the client has both.
        /// </summary>
        /// <returns>The four instances plus the client world</returns>
        static async Task<(GhostObject serverParent, GhostObject serverChild, GhostObject clientParent, GhostObject clientChild, World clientWorld)>
            SetupParentAndChild(NetCodeTestWorld testWorld, Vector3 parentScale, GhostOptimizationMode optimizationMode = GhostOptimizationMode.Dynamic)
        {
            var parentPrefab = CreateGhostPrefab("ParentGhost", parentScale, optimizationMode);
            var childPrefab = CreateGhostPrefab("ChildGhost", Vector3.one, optimizationMode);
            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(64, 1f / 240f);

            var serverParent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var serverChild = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            serverParent.transform.position = k_ParentPos;
            serverChild.transform.position = k_ChildPos;
            await testWorld.TickMultipleAsync(16);

            var clientWorld = testWorld.ClientWorlds[0];
            var clientParent = ClientInstanceOf(serverParent, clientWorld);
            var clientChild = ClientInstanceOf(serverChild, clientWorld);

            // Sanity: both are at root and correctly positioned before any parenting happens.
            Assert.AreEqual(Entity.Null, ParentingOf(clientChild).Parent, "child should start un-parented");
            Assert.IsNull(clientChild.transform.parent, "child GameObject should start at the root");
            AssertApprox(k_ChildPos, clientChild.transform.position, "child world position before parenting");
            AssertApprox(k_ParentPos, clientParent.transform.position, "parent world position before parenting");

            return (serverParent, serverChild, clientParent, clientChild, clientWorld);
        }

        [Test]
        public async Task Parent_WorldPositionStaysTrue_ReplicatesAndKeepsWorldPosition()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            var childWorldBefore = clientChild.transform.position;

            // Reparent on the authority with worldPositionStays == true: the child must not visually move.
            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            // Parenting state replicated.
            var clientParenting = ParentingOf(clientChild);
            Assert.AreEqual(clientParent.Entity, clientParenting.Parent, "client child ParentingData.Parent should point at the parent ghost");
            Assert.IsTrue(clientParenting.GetWorldPositionStays(), "WorldPositionStays flag should replicate as true");

            // GameObject hierarchy mirrored + world position preserved.
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "client child should be parented under the client parent GameObject");
            AssertApprox(childWorldBefore, clientChild.transform.position, "child world position should be preserved (WPS=true)");
        }

        [Test]
        public async Task Parent_WorldPositionStaysFalse_ReplicatesAndKeepsLocalPosition()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            // At root, localPosition == world position.
            var childLocalBefore = serverChild.transform.localPosition;

            // Reparent with worldPositionStays == false: local values are kept, so the world position changes.
            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: false);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            var clientParenting = ParentingOf(clientChild);
            Assert.AreEqual(clientParent.Entity, clientParenting.Parent);
            Assert.IsFalse(clientParenting.GetWorldPositionStays(), "WorldPositionStays flag should replicate as false");

            Assert.AreEqual(clientParent.transform, clientChild.transform.parent);
            // Local kept -> world = parent + local.
            AssertApprox(childLocalBefore, clientChild.transform.localPosition, "child local position should be preserved (WPS=false)");
            AssertApprox(k_ParentPos + childLocalBefore, clientChild.transform.position, "child world position should shift by the parent (WPS=false)");
        }

        [Test]
        public async Task Unparent_ReturnsChildToRoot_AndReplicates()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            // Parent first.
            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "precondition: child should be parented");

            // Unparent on the authority. This is the path that previously threw a NullReferenceException in
            // OnTransformParentChanged (parentAdapter is null at root) before the parenting state was updated,
            // which silently prevented the unparent from ever replicating.
            serverChild.transform.SetParent(null, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            // Server recorded the unparent...
            Assert.AreEqual(Entity.Null, ParentingOf(serverChild).Parent, "server child ParentingData.Parent should be Null after unparent");
            // ...and it replicated to the client, returning the child to the root.
            Assert.AreEqual(Entity.Null, ParentingOf(clientChild).Parent, "client child ParentingData.Parent should be Null after unparent");
            Assert.IsNull(clientChild.transform.parent, "client child GameObject should be back at the root");
            AssertApprox(k_ChildPos, clientChild.transform.position, "child world position should be preserved through a WPS=true unparent");
        }

        [Test]
        public async Task Reparent_ParentUnparentReparentCycle_StaysConsistent()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            for (int i = 0; i < 2; i++)
            {
                serverChild.transform.SetParent(serverParent.transform, worldPositionStays: true);
                await testWorld.TickMultipleAsync(k_SettleTicks);
                Assert.AreEqual(clientParent.transform, clientChild.transform.parent, $"iteration {i}: child should be parented");
                Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, $"iteration {i}: parent should be replicated");
                AssertApprox(k_ChildPos, clientChild.transform.position, $"iteration {i}: world position preserved while parented");

                serverChild.transform.SetParent(null, worldPositionStays: true);
                await testWorld.TickMultipleAsync(k_SettleTicks);
                Assert.IsNull(clientChild.transform.parent, $"iteration {i}: child should be back at the root");
                Assert.AreEqual(Entity.Null, ParentingOf(clientChild).Parent, $"iteration {i}: parent should be Null after unparent");
                AssertApprox(k_ChildPos, clientChild.transform.position, $"iteration {i}: world position preserved after unparent");
            }
        }

        [Test]
        public async Task NonUniformParentScale_ReplicatesAndChildWorldScalePreserved()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();

            var parentScale = new Vector3(1.5f, 1f, 1.5f);
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, parentScale);

            // The parent is never itself parented, but its non-uniform base scale must survive replication.
            // LocalTransform.Scale is a single float, so the non-uniform scale rides in the replicated PostTransformMatrix;
            // without it this would be flattened to a uniform (1.5, 1.5, 1.5) on the client.
            AssertApprox(parentScale, clientParent.transform.localScale, "non-uniform parent scale should replicate (not be flattened to uniform)");

            // Parent the child under the non-uniformly scaled parent with world-position-stays: the child's world
            // (lossy) scale and world position must both be preserved.
            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.AreEqual(clientParent.transform, clientChild.transform.parent);
            AssertApprox(Vector3.one, clientChild.transform.lossyScale, "child world (lossy) scale should be preserved under a non-uniform parent (WPS=true)");
            AssertApprox(k_ChildPos, clientChild.transform.position, "child world position should be preserved under a non-uniform parent (WPS=true)");
        }

        /// <summary>
        /// Similar to <see cref="CreateGhostPrefab"/>, but lets the caller select the single-world-host smoothing mode.
        /// </summary>
        static GameObject CreateHostGhostPrefab(string name, Vector3 localScale, SingleWorldHostInterpolationMode mode,
            GhostMode defaultGhostMode = GhostMode.Interpolated)
        {
            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(
                NetCodeTestWorld.k_GeneratedFolderBasePath, name, autoRegister: false, typeof(PredictionCallbackHelper));
            prefab.transform.localScale = localScale;
            var ghost = prefab.GetComponent<GhostObject>();
            ghost.ParentReplication = true;
            ghost.SingleWorldHostInterpolationSmoothing = mode;
            // SupportedGhostModes is already All. A predicted ghost runs in PredictedSimulationSystemGroup off the
            // predicted tick, rather than being blended off the interpolation tick.
            ghost.DefaultGhostMode = defaultGhostMode;
            Netcode.RegisterPrefab(prefab);
            return prefab;
        }

        [Test(Description = "On a single-world host, a worldPositionStays:true reparent must preserve the child's world transform for every SingleWorldHostInterpolationMode.")]
        public async Task SingleWorldHost_ParentWorldPositionStaysTrue_PreservesWorldTransform([Values] SingleWorldHostInterpolationMode mode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 0, clientCount: 0, singleWorldHostCount: 1);

            // Non-uniform parent scale, so a lost WPS:true correction shows up in scale as well as position.
            var parentPrefab = CreateHostGhostPrefab("HostParentGhost", new Vector3(1.5f, 1f, 1.5f), mode);
            var childPrefab = CreateHostGhostPrefab("HostChildGhost", Vector3.one, mode);

            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(16);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var child = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            // Under Interpolate on a host, a direct GO transform write is reverted to the entity value
            // (see MakeSureTransformChanges_OutsidePrediction_AreStillApplied). Set it on the entity and publish.
            parent.Position = k_ParentPos;
            parent.PublishTransform();
            child.Position = k_ChildPos;
            child.PublishTransform();
            await testWorld.TickMultipleAsync(16);

            var worldPosBefore = child.transform.position;
            var worldScaleBefore = child.transform.lossyScale;
            var localPosBefore = child.transform.localPosition;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.AreEqual(parent.transform, child.transform.parent, $"mode={mode}: child should be parented");
            Assert.IsTrue(ParentingOf(child).GetWorldPositionStays(), $"mode={mode}: authority should have recorded WorldPositionStays=true");
            AssertApprox(worldPosBefore, child.transform.position, $"mode={mode}: world position preserved (WPS=true)");
            AssertApprox(worldScaleBefore, child.transform.lossyScale, $"mode={mode}: world (lossy) scale preserved (WPS=true)");
            Assert.Greater(Vector3.Distance(localPosBefore, child.transform.localPosition), 0.01f,
                $"mode={mode}: local position should have been recomputed by the WPS=true reparent");
        }

        // Sub-tick dt, so that off-frames occur. At the default 1/60 against a 60Hz SimulationTickRate every frame is
        // exactly one tick and InterpolationTickFraction is always 0, which no reparent artifact can survive.
        const float k_SubTickDt = 1f / 240f;

        // Per-tick step for predicted motion. Also the largest legitimate single-frame world movement.
        static readonly Vector3 k_MovePerTick = new Vector3(0.05f, 0f, 0f);

        // The parent sits off the origin, so that a coordinate-space error displaces the child by 1.25.
        static readonly Vector3 k_FlickerParentPos = new Vector3(0f, 1.25f, 0f);
        static readonly Vector3 k_FlickerChildPos = new Vector3(0f, 1.25f, 3f);

        // Kept out of the position tests. A non-uniform parent scale also changes the child's world scale, which
        // would confound a position measurement.
        static readonly Vector3 k_NonUniformParentScale = new Vector3(0.75f, 1f, 0.75f);

        // Every case writes prefabs into the generated folder that the next SetupGameObjectTest wipes. Reusing one
        // name across cases races the AssetDatabase and fails as "Cannot open file ... for write".
        static int s_FlickerPrefabSerial;

        /// <summary>
        /// Common method used throughout the tests that creates a host parent and child prefab and instances of the prefab.
        /// </summary>
        /// <returns>The parent and child instances of the prefabs created.</returns>
        static async Task<(GhostObject parent, GhostObject child)> SetupHostParentAndChild(NetCodeTestWorld testWorld,
            SingleWorldHostInterpolationMode childMode, Vector3 parentScale,
            GhostMode defaultGhostMode = GhostMode.Interpolated,
            SingleWorldHostInterpolationMode? parentModeOverride = null,
            Quaternion? childRotation = null)
        {
            var serial = ++s_FlickerPrefabSerial;
            var parentPrefab = CreateHostGhostPrefab($"FlickerParent{serial}", parentScale, parentModeOverride ?? SingleWorldHostInterpolationMode.Disabled, defaultGhostMode);
            var childPrefab = CreateHostGhostPrefab($"FlickerChild{serial}", Vector3.one, childMode, defaultGhostMode);

            // The default 7 step budget is not enough for the handshake under a latency profile. It ticks only until
            // connected, so a larger cap costs the cases without latency nothing.
            await testWorld.ConnectAsync(enableGhostReplication: true, maxSteps: 32);
            await testWorld.TickMultipleAsync(64, k_SubTickDt);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var child = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            // Under host smoothing the entity to GameObject restore reverts a direct transform write. Place these
            // through the entity and publish.
            parent.Position = k_FlickerParentPos;
            parent.PublishTransform();
            child.Position = k_FlickerChildPos;
            // A rotated child under a non-uniformly scaled parent shears: the world matrix is no longer a TRS.
            child.Rotation = childRotation ?? Quaternion.identity;
            child.PublishTransform();
            await testWorld.TickMultipleAsync(64, k_SubTickDt);

            return (parent, child);
        }

        /// <summary>
        /// Default world creation for the parenting tests. Creates a single authority and client to connect.
        /// </summary>
        /// <remarks>
        /// The authority is a dedicated server, which NetCodeTestWorld.OverrideUseSingleWorldHost turns into a single world
        /// host for the CI run that covers that mode. That is why there is no per-test authority selection: the host shape
        /// comes from re-running the suite with the override on, not from a second [Values] axis on every case.
        /// </remarks>
        static async Task SetupWorldsAsync(NetCodeTestWorld testWorld)
        {
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);
        }

        /// <summary>
        /// World creation for the cases that measure the authority's own GameObject, which only a single world host has.
        /// </summary>
        /// <remarks>
        /// These cannot ride on the override, because a dedicated server has no GameObject to observe at all.
        /// </remarks>
        static async Task SetupSingleWorldHostAsync(NetCodeTestWorld testWorld)
        {
            await testWorld.SetupGameObjectTest(serverCount: 0, clientCount: 1, singleWorldHostCount: 1);
        }

        // Motion must come from the predicted timeline. PredictionUpdate runs once per simulation tick, after the
        // entity to GameObject restore and before the GameObject to entity capture, so the write reaches the
        // authoritative transform. A MonoBehaviour.Update write is reverted by that restore.
        static void DrivePredictedMotion(GhostObject ghost)
        {
            ghost.GetComponent<PredictionCallbackHelper>().OnPredictionEvent += go => go.transform.localPosition += k_MovePerTick;
        }

        // Returns the remote (non-host) client world. The host world lives in ServerWorlds, not ClientWorlds.
        static World RemoteClientWorld(NetCodeTestWorld testWorld)
        {
            foreach (var w in testWorld.ClientWorlds)
            {
                if (!w.IsHost())
                {
                    return w;
                }
            }
            Assert.Fail("expected a remote (non-host) client world");
            return null;
        }

        /// <summary>
        /// Used to detect the deviation (has it moved?) between a server/host and client parented instance.
        /// </summary>
        /// <returns>The worst deviation of position, scale, and the frame that the worst position value was measured.</returns>
        static async Task<(float pos, float scale, int frame)> WorstDeviationAsync(NetCodeTestWorld testWorld, Transform observed, int frames)
        {
            var posFrom = observed.position;
            var scaleFrom = observed.lossyScale;
            var worstPos = 0f;
            var worstScale = 0f;
            var worstFrame = -1;
            for (int frame = 0; frame < frames; ++frame)
            {
                await testWorld.TickMultipleAsync(1, k_SubTickDt);
                var dPos = Vector3.Distance(posFrom, observed.position);
                if (dPos > worstPos)
                {
                    worstPos = dPos;
                    worstFrame = frame;
                }
                worstScale = Mathf.Max(worstScale, Vector3.Distance(scaleFrom, observed.lossyScale));
            }
            return (worstPos, worstScale, worstFrame);
        }

        /// <summary>
        /// Similar to <see cref="WorstDeviationAsync"/>, this detects deviation while a Ghost is moving.
        /// </summary>
        /// <returns>Returns the largest single-frame step of the world position, because a visual pop is a discontinuity rather than smoothed motion.</returns>
        static async Task<(float jump, float scale, int frame)> WorstFrameJumpAsync(NetCodeTestWorld testWorld, Transform observed, int frames)
        {
            var previous = observed.position;
            var scaleFrom = observed.lossyScale;
            var worst = 0f;
            var worstScale = 0f;
            var worstFrame = -1;
            for (int frame = 0; frame < frames; ++frame)
            {
                await testWorld.TickMultipleAsync(1, k_SubTickDt);
                var step = Vector3.Distance(previous, observed.position);
                previous = observed.position;
                if (step > worst)
                {
                    worst = step;
                    worstFrame = frame;
                }
                worstScale = Mathf.Max(worstScale, Vector3.Distance(scaleFrom, observed.lossyScale));
            }
            return (worst, worstScale, worstFrame);
        }

        [Test(Description = "The authority's own GameObject must not move on any frame of a worldPositionStays:true reparent. A reparent changes the transform space LocalTransform is expressed in, which host smoothing then blends across.")]
        public async Task SingleWorldHost_Static_ParentWorldPositionStaysTrue_NoHostPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 0, clientCount: 0, singleWorldHostCount: 1);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one);

            var posBefore = child.transform.position;
            var scaleBefore = child.transform.lossyScale;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, worstScale, worstFrame) = await WorstDeviationAsync(testWorld, child.transform, k_SettleTicks * 4);

            // Settled first, so a permanent error is never reported as a transient one.
            AssertApprox(posBefore, child.transform.position, $"childMode={childMode}: host settled world position");
            AssertApprox(scaleBefore, child.transform.lossyScale, $"childMode={childMode}: host settled world scale");
            Assert.Less(worstPos, 0.02f, $"childMode={childMode}: host WORLD position moved during a WPS=true reparent (worst {worstPos:F4} at frame {worstFrame})");
            Assert.Less(worstScale, 0.02f, $"childMode={childMode}: host WORLD scale changed during a WPS=true reparent (worst {worstScale:F4})");
        }

        [Test(Description = "As above, with the child driven from GhostBehaviour.PredictionUpdate so the reparent lands mid-motion.")]
        public async Task SingleWorldHost_PredictedMotion_ParentWorldPositionStaysTrue_NoHostPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 0, clientCount: 0, singleWorldHostCount: 1);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one);

            DrivePredictedMotion(child);
            // Reach a steady rate before the reparent, so the sampled frames open on a ghost already moving at
            // the rate the threshold is derived from.
            await testWorld.TickMultipleAsync(32, k_SubTickDt);

            var scaleBefore = child.transform.lossyScale;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstJump, worstScale, worstFrame) = await WorstFrameJumpAsync(testWorld, child.transform, k_SettleTicks * 4);

            var maxStep = k_MovePerTick.magnitude + 0.02f;
            AssertApprox(scaleBefore, child.transform.lossyScale, $"childMode={childMode}: host settled world scale");
            Assert.Less(worstJump, maxStep, $"childMode={childMode}: host WORLD position jumped {worstJump:F4} in a single frame at frame {worstFrame} during a WPS=true reparent (one tick of motion is {k_MovePerTick.magnitude:F4})");
            Assert.Less(worstScale, 0.02f, $"childMode={childMode}: host WORLD scale changed during a WPS=true reparent (worst {worstScale:F4})");
        }

        [Test(Description = "A remote client must not see the child's world pose move on any frame of a worldPositionStays:true reparent.")]
        public async Task RemoteClient_Static_ParentWorldPositionStaysTrue_NoClientPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one);
            var clientChild = RemoteChildOf(testWorld, child);

            var posBefore = clientChild.transform.position;
            var scaleBefore = clientChild.transform.lossyScale;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, worstScale, worstFrame) = await WorstDeviationAsync(testWorld, clientChild.transform, k_SettleTicks * 4);

            // Settled first, so a permanent error is never reported as a transient one.
            AssertApprox(posBefore, clientChild.transform.position, $"childMode={childMode}: client settled world position");
            AssertApprox(scaleBefore, clientChild.transform.lossyScale, $"childMode={childMode}: client settled world scale");
            Assert.Less(worstPos, 0.02f, $"childMode={childMode}: client WORLD position moved during a WPS=true reparent (worst {worstPos:F4} at frame {worstFrame}) - this is the visible flicker");
            Assert.Less(worstScale, 0.02f, $"childMode={childMode}: client WORLD scale changed during a WPS=true reparent (worst {worstScale:F4})");
        }

        [Test(Description = "As above, while the ghost is moving. This is the case ParentingInterpolationFixSystem pins for, where the interpolation window straddles the reparent tick.")]
        public async Task RemoteClient_PredictedMotion_ParentWorldPositionStaysTrue_NoClientPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one);
            var clientChild = RemoteChildOf(testWorld, child);

            DrivePredictedMotion(child);
            await testWorld.TickMultipleAsync(32, k_SubTickDt);

            var scaleBefore = clientChild.transform.lossyScale;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstJump, worstScale, worstFrame) = await WorstFrameJumpAsync(testWorld, clientChild.transform, k_SettleTicks * 4);

            var maxStep = k_MovePerTick.magnitude + 0.02f;
            AssertApprox(scaleBefore, clientChild.transform.lossyScale, $"childMode={childMode}: client settled world scale");
            Assert.Less(worstJump, maxStep, $"childMode={childMode}: client WORLD position jumped {worstJump:F4} in a single frame at frame {worstFrame} during a WPS=true reparent (one tick of motion is {k_MovePerTick.magnitude:F4})");
            Assert.Less(worstScale, 0.02f, $"childMode={childMode}: client WORLD scale changed during a WPS=true reparent (worst {worstScale:F4})");
        }

        [Test(Description = "A worldPositionStays:true reparent under a non-uniformly scaled parent must not change the child's world scale on any frame. The 3D scale is replicated through PostTransformMatrix rather than LocalTransform.Scale, so it reaches the client by a different path than position.")]
        public async Task SingleWorldHost_NonUniformParentScale_ParentWorldPositionStaysTrue_NoHostScaleFlicker([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 0, clientCount: 0, singleWorldHostCount: 1);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, k_NonUniformParentScale);

            var scaleBefore = child.transform.lossyScale;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (_, worstScale, _) = await WorstDeviationAsync(testWorld, child.transform, k_SettleTicks * 4);

            AssertApprox(scaleBefore, child.transform.lossyScale, $"childMode={childMode}: host settled world scale");
            Assert.Less(worstScale, 0.02f, $"childMode={childMode}: host WORLD scale changed during a WPS=true reparent under a non-uniform parent (worst {worstScale:F4})");
        }

        [Test(Description = "As above, observed on the remote client, where PostTransformMatrix interpolates across the reparent tick.")]
        public async Task RemoteClient_NonUniformParentScale_ParentWorldPositionStaysTrue_NoClientScaleFlicker([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, k_NonUniformParentScale);
            var clientChild = RemoteChildOf(testWorld, child);

            var scaleBefore = clientChild.transform.lossyScale;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (_, worstScale, _) = await WorstDeviationAsync(testWorld, clientChild.transform, k_SettleTicks * 4);

            AssertApprox(scaleBefore, clientChild.transform.lossyScale, $"childMode={childMode}: client settled world scale");
            Assert.Less(worstScale, 0.02f, $"childMode={childMode}: client WORLD scale changed during a WPS=true reparent under a non-uniform parent (worst {worstScale:F4})");
        }

        // The shear cases measure position only. No TRS can hold a sheared matrix, so Unity cannot preserve the world
        // scale exactly either, while position it does preserve.
        static readonly Quaternion k_ShearingChildRotation = Quaternion.Euler(0f, 30f, 0f);

        [Test(Description = "A rotated child under a non-uniformly scaled parent. The composed world matrix is a shear, so lossyScale only approximates it: inferring worldPositionStays from world space would read this as false and skip the pin.")]
        public async Task SingleWorldHost_ShearedParent_ParentWorldPositionStaysTrue_NoHostPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 0, clientCount: 0, singleWorldHostCount: 1);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, k_NonUniformParentScale,
                childRotation: k_ShearingChildRotation);

            var posBefore = child.transform.position;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, _, worstFrame) = await WorstDeviationAsync(testWorld, child.transform, k_SettleTicks * 4);

            Assert.IsTrue(ParentingOf(child).GetWorldPositionStays(), $"childMode={childMode}: authority should have recorded WorldPositionStays=true through the shear");
            AssertApprox(posBefore, child.transform.position, $"childMode={childMode}: host settled world position");
            Assert.Less(worstPos, 0.02f, $"childMode={childMode}: host WORLD position moved during a WPS=true reparent under a shearing parent (worst {worstPos:F4} at frame {worstFrame})");
        }

        [Test(Description = "As above, observed on the remote client, where the skipped pin shows up as the straddle artifact rather than as a settled error.")]
        public async Task RemoteClient_ShearedParent_ParentWorldPositionStaysTrue_NoClientPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, k_NonUniformParentScale,
                childRotation: k_ShearingChildRotation);
            var clientChild = RemoteChildOf(testWorld, child);

            var posBefore = clientChild.transform.position;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, _, worstFrame) = await WorstDeviationAsync(testWorld, clientChild.transform, k_SettleTicks * 4);

            Assert.IsTrue(ParentingOf(child).GetWorldPositionStays(), $"childMode={childMode}: authority should have recorded WorldPositionStays=true through the shear");
            AssertApprox(posBefore, clientChild.transform.position, $"childMode={childMode}: client settled world position");
            Assert.Less(worstPos, 0.02f, $"childMode={childMode}: client WORLD position moved during a WPS=true reparent under a shearing parent (worst {worstPos:F4} at frame {worstFrame})");
        }

        /// <summary>
        /// Returns the position, rotation, and scale of a given parent's frame.
        /// </summary>
        static (Vector3 position, Quaternion rotation, Vector3 scale) FrameOf(ParentFrame frame)
        {
            switch (frame)
            {
                case ParentFrame.Translated:
                    return (k_ParentPos, Quaternion.identity, Vector3.one);
                case ParentFrame.Rotated:
                    return (k_ParentPos, Quaternion.Euler(0f, 37f, 0f), Vector3.one);
                case ParentFrame.NonUniformScaled:
                    return (k_ParentPos, Quaternion.identity, k_NonUniformParentScale);
                case ParentFrame.RotatedAndNonUniformScaled:
                    return (k_ParentPos, Quaternion.Euler(0f, 37f, 0f), k_NonUniformParentScale);
                default:
                    return (Vector3.zero, Quaternion.identity, Vector3.one);
            }
        }

        [Test(Description = "The inference contract, across every parent frame. worldPositionStays:false leaves the local transform alone and true recomputes it, so comparing it identifies which ran. Identity is the blind spot: both modes match there, and skipping the pin is correct because nothing moves.")]
        public async Task ParentFrames_WorldPositionStaysInferredFromLocalTransform([Values] ParentFrame frame)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);

            var (parentPos, parentRot, parentScale) = FrameOf(frame);
            var serial = ++s_FlickerPrefabSerial;
            var parentPrefab = CreateGhostPrefab($"FramedParent{serial}", parentScale);
            var childPrefab = CreateGhostPrefab($"FramedChild{serial}", Vector3.one);

            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(16);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            parent.Position = parentPos;
            parent.Rotation = parentRot;
            parent.PublishTransform();

            // One child per mode, so that neither reparent observes the other.
            var keepsLocal = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            var keepsWorld = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            foreach (var instance in new[] { keepsLocal, keepsWorld })
            {
                instance.Position = k_ChildPos;
                // Rotated, so that RotatedAndNonUniformScaled actually shears.
                instance.Rotation = k_ShearingChildRotation;
                instance.PublishTransform();
            }
            await testWorld.TickMultipleAsync(16);

            var localPosBefore = keepsLocal.transform.localPosition;
            var localRotBefore = keepsLocal.transform.localRotation;
            var localScaleBefore = keepsLocal.transform.localScale;

            keepsLocal.transform.SetParent(parent.transform, worldPositionStays: false);

            // Bit-exact, not approximate: this is the invariant the inference rests on.
            Assert.IsTrue(keepsLocal.transform.localPosition.Equals(localPosBefore)
                && keepsLocal.transform.localRotation.Equals(localRotBefore)
                && keepsLocal.transform.localScale.Equals(localScaleBefore),
                $"frame={frame}: worldPositionStays:false must leave the local transform untouched");
            Assert.IsFalse(ParentingOf(keepsLocal).GetWorldPositionStays(),
                $"frame={frame}: a worldPositionStays:false reparent must never be recorded as true");

            var worldPosBefore = keepsWorld.transform.position;

            keepsWorld.transform.SetParent(parent.transform, worldPositionStays: true);

            // Identity matches the frame the child was already in, so both modes leave the local transform alone there.
            Assert.AreEqual(frame != ParentFrame.Identity, ParentingOf(keepsWorld).GetWorldPositionStays(),
                $"frame={frame}: worldPositionStays:true inference");
            AssertApprox(worldPosBefore, keepsWorld.transform.position, $"frame={frame}: world position preserved (WPS=true)");
        }

        /// <summary>
        /// Determines if a pre-determined motion strays too far off the starting and ending poses. When WorldPositionStays is false,
        /// a reparent can shift the child's world space values by the child's current world position and the parent's world position.
        /// </summary>
        /// <param name="testWorld"></param>
        /// <param name="observed"></param>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="frames"></param>
        /// <returns>The worst distance off the path and the frame it occurred.</returns>
        static async Task<(float offPath, int frame)> WorstOffPathAsync(NetCodeTestWorld testWorld, Transform observed, Vector3 from, Vector3 to, int frames)
        {
            var worst = 0f;
            var worstFrame = -1;
            var segment = to - from;
            var lengthSq = segment.sqrMagnitude;
            for (int frame = 0; frame < frames; ++frame)
            {
                await testWorld.TickMultipleAsync(1, k_SubTickDt);
                var p = observed.position;
                // Distance from the segment, clamping the projection so straying past either endpoint counts.
                var t = lengthSq > 0f ? Mathf.Clamp01(Vector3.Dot(p - from, segment) / lengthSq) : 0f;
                var offPath = Vector3.Distance(p, from + segment * t);
                if (offPath > worst)
                {
                    worst = offPath;
                    worstFrame = frame;
                }
            }
            return (worst, worstFrame);
        }

        [Test(Description = "Predicted ghosts, observed on the host. A predicted ghost runs off the predicted tick, so the interpolation window ParentingInterpolationFixSystem samples belongs to a different timeline.")]
        public async Task Predicted_Static_ParentWorldPositionStaysTrue_NoHostPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            // Host-observed, so single-world host only. A dedicated server has no local view to pop.
            await SetupSingleWorldHostAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: childMode);

            var posBefore = child.transform.position;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, _, worstFrame) = await WorstDeviationAsync(testWorld, child.transform, k_SettleTicks * 4);

            AssertApprox(posBefore, child.transform.position, $"childMode={childMode}: host settled world position");
            Assert.Less(worstPos, 0.02f, $"childMode={childMode}: predicted host WORLD position moved during a WPS=true reparent (worst {worstPos:F4} at frame {worstFrame})");
        }

        [Test(Description = "Predicted ghosts, observed on the remote client. One authored prefab serves both deployments: prediction and single-world-host interpolation are both enabled. The interpolation setting is inert on a dedicated server, so both smoothing values behave the same there.")]
        public async Task Predicted_Static_ParentWorldPositionStaysTrue_NoClientPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: childMode);
            var clientChild = RemoteChildOf(testWorld, child);

            var posBefore = clientChild.transform.position;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, _, worstFrame) = await WorstDeviationAsync(testWorld, clientChild.transform, k_SettleTicks * 4);

            AssertApprox(posBefore, clientChild.transform.position, $"childMode={childMode}: client settled world position");
            Assert.Less(worstPos, 0.02f, $"childMode={childMode}: predicted client WORLD position moved during a WPS=true reparent (worst {worstPos:F4} at frame {worstFrame}) - this is the reported flicker");
        }

        [Test(Description = "The worldPositionStays:false control. ClientApplyParentingToGameObjectSystem always reparents the client as though it were false, so a clean result here places any worldPositionStays:true artifact on the authority rather than in the client's SetParent call.")]
        public async Task Predicted_Static_ParentWorldPositionStaysFalse_NoClientPop([Values] SingleWorldHostInterpolationMode childMode)
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, childMode, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: childMode);
            var clientChild = RemoteChildOf(testWorld, child);

            // The local values are kept, so the world position shifts by the parent offset. That shift is intended.
            // Straying off the path between the two poses is not.
            var from = clientChild.transform.position;
            var to = k_FlickerParentPos + k_FlickerChildPos;

            child.transform.SetParent(parent.transform, worldPositionStays: false);
            var (offPath, worstFrame) = await WorstOffPathAsync(testWorld, clientChild.transform, from, to, k_SettleTicks * 4);

            AssertApprox(to, clientChild.transform.position, $"childMode={childMode}: client settled world position (WPS=false shifts by the parent offset)");
            Assert.Less(offPath, 0.02f, $"childMode={childMode}: predicted client WORLD position strayed {offPath:F4} off the direct path at frame {worstFrame} during a WPS=false reparent");
        }

        /// <summary>
        /// Counts predictions per server tick. A tick predicted more than once was re-simulated.
        /// </summary>
        static Dictionary<uint, int> CountPredictedTicks(NetCodeTestWorld testWorld, GhostObject clientGhost)
        {
            var counts = new Dictionary<uint, int>();
            var clientWorld = clientGhost.World;
            clientGhost.GetComponent<PredictionCallbackHelper>().OnPredictionEvent += _ =>
            {
                var tick = testWorld.GetNetworkTime(clientWorld).ServerTick.SerializedData;
                counts.TryGetValue(tick, out var seen);
                counts[tick] = seen + 1;
            };
            return counts;
        }

        [Test(Description = "A reparent that lands while the client is predicting ahead of the authority. The other predicted cases run without latency, so the client is never corrected across the reparent tick; here it is re-simulated through it.")]
        public async Task RemoteClient_PredictedReparentAcrossRollback_NoClientPop()
        {
            await using var testWorld = new NetCodeTestWorld();
            // Set before the worlds exist: the profile is read when the client driver is built.
            testWorld.SetTestLatencyProfile(NetCodeTestLatencyProfile.RTT60ms);
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, SingleWorldHostInterpolationMode.Interpolate, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: SingleWorldHostInterpolationMode.Interpolate);
            var clientChild = RemoteChildOf(testWorld, child);

            var predictedTicks = CountPredictedTicks(testWorld, clientChild);
            var posBefore = clientChild.transform.position;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            var (worstPos, _, worstFrame) = await WorstDeviationAsync(testWorld, clientChild.transform, k_SettleTicks * 4);

            // Proves the scenario happened. Without it a client that never rolled back would report a clean pass.
            Assert.IsTrue(predictedTicks.Values.Any(seen => seen > 1),
                "no server tick was predicted more than once, so nothing was re-simulated and this case proved nothing");
            AssertApprox(posBefore, clientChild.transform.position, "client settled world position");
            Assert.Less(worstPos, 0.02f, $"client WORLD position moved during a WPS=true reparent re-simulated across a rollback (worst {worstPos:F4} at frame {worstFrame})");
        }

        [Test(Description = "A client may parent a predicted ghost. The prediction is applied locally straight away, and because the authority never made the same change the snapshot corrects it back, which is what prediction is for.")]
        public async Task PredictedGhost_ClientParents_IsPredictedThenCorrected()
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, SingleWorldHostInterpolationMode.Interpolate, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: SingleWorldHostInterpolationMode.Interpolate);
            var clientWorld = RemoteClientWorld(testWorld);
            var clientChild = RemoteChildOf(testWorld, child);
            var clientParent = ClientInstanceOf(parent, clientWorld);

            Assert.IsTrue(clientChild.CanWriteState, "this case needs a predicted client ghost");

            // No LogAssert.Expect: a predicted ghost accepts the change rather than reporting it.
            clientChild.transform.SetParent(clientParent.transform, worldPositionStays: false);

            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the client's own prediction should be applied immediately");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the predicted parent should be written to the client's ParentingData");

            await testWorld.TickMultipleAsync(k_SettleTicks);

            // The authority never parented anything, so its snapshots carry Entity.Null and the prediction loses.
            Assert.AreEqual(Entity.Null, ParentingOf(clientChild).Parent, "the authority should have corrected the predicted parent away");
            Assert.IsNull(clientChild.transform.parent, "the GameObject hierarchy should follow the correction back to the root");
        }

        [Test(Description = "The interpolated counterpart. An interpolated ghost has no prediction loop to re-derive the change and no way to reach the authority, so a client parenting one is still reported and reverted.")]
        public async Task InterpolatedGhost_ClientParents_IsReportedAndReverted()
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, SingleWorldHostInterpolationMode.Interpolate, Vector3.one);
            var clientWorld = RemoteClientWorld(testWorld);
            var clientChild = RemoteChildOf(testWorld, child);
            var clientParent = ClientInstanceOf(parent, clientWorld);

            Assert.IsFalse(clientChild.CanWriteState, "this case needs an interpolated client ghost");

            LogAssert.Expect(LogType.Error, new Regex("Client attempting to parent"));
            clientChild.transform.SetParent(clientParent.transform, worldPositionStays: false);

            Assert.IsNull(clientChild.transform.parent, "the rejected parent change should have been reverted");
            Assert.AreEqual(Entity.Null, ParentingOf(clientChild).Parent, "ParentingData should be untouched");
        }

        [Test(Description = "The re-entrancy case. OnParentChanged is raised from inside the SetParent that ClientApplyParentingToGameObjectSystem uses to apply a replicated parent, so a client that parents from inside the callback must still be rejected instead of being read as part of that apply.")]
        public async Task InterpolatedGhost_ClientParentsFromOnParentChanged_IsReportedAndReverted()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            Assert.IsFalse(clientChild.CanWriteState, "this case needs an interpolated client ghost");

            // Parent from the callback the replicated apply raises, once. Without the guard this nested change is
            // accepted, leaving the GameObject at the root while ParentingData still points at the parent.
            var callbackCount = 0;
            clientChild.OnParentChanged += (before, after) =>
            {
                if (callbackCount++ == 0)
                {
                    clientChild.transform.SetParent(null, worldPositionStays: false);
                }
            };

            LogAssert.Expect(LogType.Error, new Regex("Client attempting to parent"));

            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.AreEqual(1, callbackCount, $"{nameof(GhostObject.OnParentChanged)} should fire once, for the replicated apply only");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the nested client parent change should have been reverted");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "ParentingData should still hold the replicated parent");
        }

        [Test(Description = "The predicted counterpart of the re-entrancy case. A predicted ghost may be parented by its client, so a parent change made from inside OnParentChanged has to be recorded rather than swallowed by the replicated apply it was raised from.")]
        public async Task PredictedGhost_ClientParentsFromOnParentChanged_IsRecorded()
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, SingleWorldHostInterpolationMode.Interpolate, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: SingleWorldHostInterpolationMode.Interpolate);
            var clientWorld = RemoteClientWorld(testWorld);
            var clientChild = RemoteChildOf(testWorld, child);
            var clientParent = ClientInstanceOf(parent, clientWorld);

            Assert.IsTrue(clientChild.CanWriteState, "this case needs a predicted client ghost");

            // Unparent from the callback the replicated apply raises, once, and sample the state it left behind.
            // Reading after the ticks would prove nothing: the authority's parent wins either way by then.
            Entity? recordedParent = null;
            Transform recordedHierarchy = clientChild.transform;
            var callbackCount = 0;
            clientChild.OnParentChanged += (before, after) =>
            {
                if (callbackCount++ != 0)
                {
                    return;
                }
                clientChild.transform.SetParent(null, worldPositionStays: false);
                recordedParent = ParentingOf(clientChild).Parent;
                recordedHierarchy = clientChild.transform.parent;
            };

            child.transform.SetParent(parent.transform, worldPositionStays: false);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.IsTrue(recordedParent.HasValue, "the authority's parent never reached the client, so this case proved nothing");
            Assert.IsNull(recordedHierarchy, "the nested client parent change should have been applied to the hierarchy");
            Assert.AreEqual(Entity.Null, recordedParent.Value, "the client's own parent change should have been written to ParentingData, not swallowed by the replicated apply");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the authority's parent should win once its snapshots arrive");
        }

        [Test(Description = "A deactivated client ghost still receives replicated parenting, and once it is reactivated a client-authored parent change on it is still rejected.")]
        public async Task InactiveClientGhost_ReplicatedParent_AppliesAndIsEnforcedAfterReactivation()
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, SingleWorldHostInterpolationMode.Interpolate, Vector3.one);
            var clientWorld = RemoteClientWorld(testWorld);
            var clientChild = RemoteChildOf(testWorld, child);
            var clientParent = ClientInstanceOf(parent, clientWorld);

            Assert.IsFalse(clientChild.CanWriteState, "this case needs an interpolated client ghost");

            // Land the replicated apply while the GameObject is deactivated.
            clientChild.gameObject.SetActive(false);
            child.transform.SetParent(parent.transform, worldPositionStays: false);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            // Proves the apply ran at all. Without it a client that never reparented would report a clean pass.
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the replicated parent should reach the deactivated GameObject");
            clientChild.gameObject.SetActive(true);

            // An ordinary client-authored change, after the GameObject is back.
            LogAssert.Expect(LogType.Error, new Regex("Client attempting to parent"));
            clientChild.transform.SetParent(null, worldPositionStays: false);

            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the rejected parent change should have been reverted");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "ParentingData should still hold the replicated parent");
        }

        /// <summary>
        /// Adds or removes a server ghost from every connection's irrelevant set.
        /// </summary>
        static void SetGhostIrrelevant(NetCodeTestWorld testWorld, GhostObject serverGhost, bool irrelevant)
        {
            var entityManager = testWorld.ServerWorld.EntityManager;
            using var relevancyQuery = entityManager.CreateEntityQuery(ComponentType.ReadWrite<GhostRelevancy>());
            var relevancy = relevancyQuery.GetSingletonRW<GhostRelevancy>();
            relevancy.ValueRW.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;

            using var connectionQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
            using var connections = connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            Assert.Greater(connections.Length, 0, "no connections to apply relevancy to");
            foreach (var connection in connections)
            {
                var key = new RelevantGhostForConnection(connection.Value, serverGhost.GhostId);
                if (irrelevant)
                {
                    relevancy.ValueRO.GhostRelevancySet.TryAdd(key, 1);
                }
                else
                {
                    relevancy.ValueRO.GhostRelevancySet.Remove(key);
                }
            }
        }

        [Test(Description = "A parent ghost that is not replicated to this client yet arrives as Entity.Null, which reads the same as the authority having unparented. On a statically optimized child that is terminal: GhostUpdateSystem stops applying its snapshot once it has settled, so the reference is never resolved again and the ghost stays at the root for the rest of the session.")]
        public async Task StaticOptimizedGhost_ParentReplicatedLate_IsStillParentedOnceItArrives()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            // Static: the ghost stops sending once it settles, which is what stops GhostUpdateSystem applying its snapshot again.
            var (serverParent, serverChild, _, clientChild, clientWorld) =
                await SetupParentAndChild(testWorld, Vector3.one, GhostOptimizationMode.Static);

            // Take the parent away from this client, so the reparent below replicates a ghost id it cannot resolve.
            // A ghost id is only assigned on the first send, so this has to come after SetupParentAndChild has ticked.
            SetGhostIrrelevant(testWorld, serverParent, irrelevant: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: false);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.IsNull(clientChild.transform.parent, "the parent ghost is not replicated to this client yet, so there is nothing to parent under");
            Assert.AreEqual(serverParent.Entity, ParentingOf(serverChild).Parent, "the authority should have recorded the parent");

            // Hand the parent back. The child is static and has already sent its reparent, so nothing further arrives for
            // it: the only thing that can still parent it is ClientApplyParentingToGameObjectSystem putting it back in line to be resolved.
            SetGhostIrrelevant(testWorld, serverParent, irrelevant: false);
            await testWorld.TickMultipleAsync(k_SettleTicks * 2);

            var clientParent = ClientInstanceOf(serverParent, clientWorld);
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent,
                "the child should have been parented once the parent ghost reached this client");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent,
                "the parent reference should have resolved on the client");
        }

        [Test(Description = "Unity catches an exception thrown by an OnParentChanged handler at the Transform.SetParent boundary, so it never reaches ClientApplyParentingToGameObjectSystem.OnUpdate and cannot abort its loop part way through. The replicated parent still lands and the guard still holds afterwards.")]
        public async Task OnParentChangedThrows_DoesNotBreakTheReplicatedApply()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            // Unity logs the handler's exception. This case is about control flow, not about logs.
            LogAssert.ignoreFailingMessages = true;

            var callbackCount = 0;
            Action<Transform, Transform> boom = (before, after) =>
            {
                callbackCount++;
                throw new InvalidOperationException("OnParentChanged handler boom");
            };
            clientChild.OnParentChanged += boom;

            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: false);
            await testWorld.TickMultipleAsync(k_SettleTicks);
            clientChild.OnParentChanged -= boom;

            Assert.Greater(callbackCount, 0, "the handler never ran, so this case proved nothing");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the replicated parent should have been applied despite the throwing handler");

            // A flag left set by the throw would take this client-authored change for part of the apply.
            clientChild.transform.SetParent(null, worldPositionStays: false);
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "a client-authored change after the throwing handler should still have been rejected and reverted");
        }

        // Prediction switching moves a ghost between the interpolated and predicted timelines at runtime. Parenting reads
        // PredictedGhost in two places: the client-may-parent gate in GhostObject.OnTransformParentChanged, and the
        // WithNone<PredictedGhost> filter on ParentingInterpolationFixSystem. PredictionSwitchingTests covers switching itself.
        // These ghosts are switchable as authored: SupportedGhostModes defaults to All and nothing here uses OwnerPredicted.

        // Transition passed to ConvertToPredicted/ConvertToInterpolated. Non-zero puts SwitchPredictionSmoothing on the
        // entity, so the reparent lands while prediction switching is also writing the transform.
        const float k_SwitchSmoothingDuration = 0.16f;

        [Test(Description = "A parented ghost that switches prediction mode keeps its parent. ParentingData carries no GhostComponentAttribute, so it is in neither the add nor the remove list the switch applies. Were it stripped, a parented ghost would lose its parent the first time it switched.")]
        public async Task PredictionSwitch_WhileParented_KeepsTheReplicatedParent([Values(0f, k_SwitchSmoothingDuration)] float transitionDuration)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: false);
            await testWorld.TickMultipleAsync(k_SettleTicks);
            Assert.IsFalse(clientChild.CanWriteState, "this case needs the child to start interpolated");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the replicated parent should have been applied before the switch");

            Assert.IsTrue(clientChild.ConvertToPredicted(transitionDuration), "a parented ghost should be allowed to switch to predicted");
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.IsTrue(clientChild.CanWriteState, "the switch to predicted did not take effect");
            Assert.IsTrue(clientChild.World.EntityManager.HasComponent<ParentingData>(clientChild.Entity), "ParentingData should have survived the switch to predicted");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the replicated parent should have survived the switch to predicted");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the hierarchy should have survived the switch to predicted");

            Assert.IsTrue(clientChild.ConvertToInterpolated(transitionDuration), "a parented ghost should be allowed to switch back to interpolated");
            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.IsFalse(clientChild.CanWriteState, "the switch back to interpolated did not take effect");
            Assert.IsTrue(clientChild.World.EntityManager.HasComponent<ParentingData>(clientChild.Entity), "ParentingData should have survived the switch back to interpolated");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the replicated parent should have survived the switch back to interpolated");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the hierarchy should have survived the switch back to interpolated");
        }

        [Test(Description = "The client-may-parent gate follows the switch. The same client-authored change is rejected while the ghost is interpolating and accepted once it is predicting.")]
        public async Task PredictionSwitch_ToPredicted_ThenClientParents_IsRecorded()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (_, _, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            Assert.IsFalse(clientChild.CanWriteState, "this case needs the child to start interpolated");

            LogAssert.Expect(LogType.Error, new Regex("Client attempting to parent"));
            clientChild.transform.SetParent(clientParent.transform, worldPositionStays: false);
            Assert.IsNull(clientChild.transform.parent, "an interpolated ghost should have rejected the client-authored parent");

            Assert.IsTrue(clientChild.ConvertToPredicted(), "the ghost should be allowed to switch to predicted");
            await testWorld.TickMultipleAsync(k_SettleTicks);
            Assert.IsTrue(clientChild.CanWriteState, "the switch to predicted did not take effect");

            // Read before ticking. The authority never parented anything, so its snapshots correct this away shortly.
            clientChild.transform.SetParent(clientParent.transform, worldPositionStays: false);
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the same change should be accepted once the ghost is predicting");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the predicted parent should have been recorded");
        }

        [Test(Description = "The reverse gate. A ghost authored predicted accepts a client-authored parent, and the same change is rejected and reverted once it has switched to interpolated.")]
        public async Task PredictionSwitch_ToInterpolated_ThenClientParents_IsReportedAndReverted()
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);
            var (parent, child) = await SetupHostParentAndChild(testWorld, SingleWorldHostInterpolationMode.Interpolate, Vector3.one,
                defaultGhostMode: GhostMode.Predicted, parentModeOverride: SingleWorldHostInterpolationMode.Interpolate);
            var clientWorld = RemoteClientWorld(testWorld);
            var clientParent = ClientInstanceOf(parent, clientWorld);
            var clientChild = ClientInstanceOf(child, clientWorld);

            Assert.IsTrue(clientChild.CanWriteState, "this case needs the child to start predicted");

            clientChild.transform.SetParent(clientParent.transform, worldPositionStays: false);
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "a predicted ghost should accept a client-authored parent");

            // Let the authority's snapshots correct the unconfirmed prediction away, so the switch starts from the root.
            await testWorld.TickMultipleAsync(k_SettleTicks);
            Assert.IsNull(clientChild.transform.parent, "the authority never parented, so the prediction should have been corrected away");

            Assert.IsTrue(clientChild.ConvertToInterpolated(), "the ghost should be allowed to switch to interpolated");
            await testWorld.TickMultipleAsync(k_SettleTicks);
            Assert.IsFalse(clientChild.CanWriteState, "the switch to interpolated did not take effect");

            LogAssert.Expect(LogType.Error, new Regex("Client attempting to parent"));
            clientChild.transform.SetParent(clientParent.transform, worldPositionStays: false);
            Assert.IsNull(clientChild.transform.parent, "the same change should be rejected and reverted once the ghost is interpolating");
        }

        // A character standing on a boat crosses the edge of the prediction bubble at the moment it is handed to the next
        // boat: the reparent and the mode change land on the same frame.
        [Test(Description = "The authority reparents on the frame the client switches to predicted. The switch lands while the interpolation window still straddles the reparent tick, so the ghost leaves the WithNone<PredictedGhost> query ParentingInterpolationFixSystem pins it with part way through.")]
        public async Task PredictionSwitch_ToPredicted_DuringReplicatedReparent_AppliesTheAuthorityParent([Values(0f, k_SwitchSmoothingDuration)] float transitionDuration)
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            Assert.IsFalse(clientChild.CanWriteState, "this case needs the child to start interpolated");

            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: false);
            Assert.IsTrue(clientChild.ConvertToPredicted(transitionDuration), "the ghost should be allowed to switch while a reparent is in flight");
            await testWorld.TickMultipleAsync(k_SettleTicks * 2);

            Assert.IsTrue(clientChild.CanWriteState, "the switch to predicted did not take effect");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the authority's parent should have been applied across the switch");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the hierarchy should match the authority after the switch");
        }

        [Test(Description = "The other direction of the same straddle: the ghost enters the WithNone<PredictedGhost> pin query part way through a replicated reparent rather than leaving it.")]
        public async Task PredictionSwitch_ToInterpolated_DuringReplicatedReparent_AppliesTheAuthorityParent()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            var (serverParent, serverChild, clientParent, clientChild, _) = await SetupParentAndChild(testWorld, Vector3.one);

            Assert.IsTrue(clientChild.ConvertToPredicted(), "the ghost should be allowed to switch to predicted");
            await testWorld.TickMultipleAsync(k_SettleTicks);
            Assert.IsTrue(clientChild.CanWriteState, "the switch to predicted did not take effect");

            serverChild.transform.SetParent(serverParent.transform, worldPositionStays: false);
            Assert.IsTrue(clientChild.ConvertToInterpolated(), "the ghost should be allowed to switch while a reparent is in flight");
            await testWorld.TickMultipleAsync(k_SettleTicks * 2);

            Assert.IsFalse(clientChild.CanWriteState, "the switch to interpolated did not take effect");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the authority's parent should have been applied across the switch");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the hierarchy should match the authority after the switch");
        }

        /// <summary>
        /// Creates an owner predicted prefab.
        /// </summary>
        static GameObject CreateOwnerPredictedPrefab(string name)
        {
            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(
                NetCodeTestWorld.k_GeneratedFolderBasePath, name, autoRegister: false, typeof(PredictionCallbackHelper));
            var ghost = prefab.GetComponent<GhostObject>();
            ghost.ParentReplication = true;
            ghost.HasOwner = true;
            ghost.DefaultGhostMode = GhostMode.Predicted;
            ghost.SupportAutoCommandTarget = true;
            Netcode.RegisterPrefab(prefab);
            return prefab;
        }

        /// <summary>
        /// Re-parents from PredictionUpdate on the input.Registered on both sides, so each derives the change itself and
        /// the client re-derives it on every re-simulated tick.
        /// </summary>
        static void ParentOnInput(GhostObject ghost, GhostObject under)
        {
            var helper = ghost.GetComponent<PredictionCallbackHelper>();
            helper.OnPredictionEvent += _ =>
            {
                if (helper.Input.Value.value == 1 && ghost.transform.parent != under.transform)
                {
                    ghost.transform.SetParent(under.transform, worldPositionStays: false);
                }
            };
        }

        [Test(Description = "The other half of predicted parenting: the client predicts the change AND the authority derives the same change from the same replicated input, so the prediction is confirmed instead of corrected away. This is the path a game would actually use.")]
        public async Task PredictedGhost_InputDrivenParent_IsConfirmedByTheAuthority()
        {
            await using var testWorld = new NetCodeTestWorld();
            await SetupWorldsAsync(testWorld);

            var serial = ++s_FlickerPrefabSerial;
            var parentPrefab = CreateOwnerPredictedPrefab($"InputParent{serial}");
            var childPrefab = CreateOwnerPredictedPrefab($"InputChild{serial}");

            await testWorld.ConnectAsync(enableGhostReplication: true, maxSteps: 32);
            await testWorld.TickMultipleAsync(16);

            var clientWorld = RemoteClientWorld(testWorld);
            var ownerId = testWorld.GetSingleton<NetworkId>(clientWorld);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var child = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            parent.OwnerNetworkId = ownerId;
            child.OwnerNetworkId = ownerId;
            parent.Position = k_FlickerParentPos;
            parent.PublishTransform();
            child.Position = k_FlickerChildPos;
            child.PublishTransform();
            await testWorld.TickMultipleAsync(32);

            var clientChild = ClientInstanceOf(child, clientWorld);
            var clientParent = ClientInstanceOf(parent, clientWorld);
            Assert.IsTrue(clientChild.CanWriteState, "this case needs a predicted client ghost");

            ParentOnInput(clientChild, clientParent);
            ParentOnInput(child, parent);

            // Raised by the owning client and replicated to the authority.
            var clientHelper = clientChild.GetComponent<PredictionCallbackHelper>();
            clientHelper.OnInputEvent += _ => clientHelper.Input.Value = new DummyInput { value = 1 };

            await testWorld.TickMultipleAsync(k_SettleTicks * 2);

            Assert.AreEqual(parent.Entity, ParentingOf(child).Parent, "the authority should have derived the same parent from the replicated input");
            Assert.AreEqual(clientParent.Entity, ParentingOf(clientChild).Parent, "the client's prediction should be confirmed, not corrected away");
            Assert.AreEqual(clientParent.transform, clientChild.transform.parent, "the client hierarchy should still be parented once the authority's snapshots arrive");
        }

        /// <summary>
        /// Creates a prefab that has a delayed spawn.
        /// </summary>
        static GameObject CreateDelayedSpawnPrefab(string name)
        {
            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(
                NetCodeTestWorld.k_GeneratedFolderBasePath, name, autoRegister: false, typeof(PredictionCallbackHelper));
            var ghost = prefab.GetComponent<GhostObject>();
            ghost.ParentReplication = true;
            ghost.DelaySpawning = true;
            Netcode.RegisterPrefab(prefab);
            return prefab;
        }

        /// <summary>
        /// Creates a parent-child pair of prefab instances that both have a delayed spawn.
        /// </summary>
        /// <returns>The parent and child instance that is yet to be spawned.</returns>
        static async Task<(GhostObject parent, GhostObject child)> SetupDelayedSpawnPairAsync(NetCodeTestWorld testWorld)
        {
            var serial = ++s_FlickerPrefabSerial;
            var parentPrefab = CreateDelayedSpawnPrefab($"DelayedParent{serial}");
            var childPrefab = CreateDelayedSpawnPrefab($"DelayedChild{serial}");

            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(16);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var child = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            Assert.IsFalse(parent.IsSpawned, "DelaySpawning parent should not be spawned by Instantiate");
            Assert.IsFalse(child.IsSpawned, "DelaySpawning child should not be spawned by Instantiate");

            return (parent, child);
        }

        [Test(Description = "Delayed spawning: parenting from the parent's OnGhostSpawn replicates, because that callback runs after the parent's entity link is established and the child was spawned first.")]
        public async Task DelayedSpawn_ParentFromOnGhostSpawn_Replicates()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);
            var (parent, child) = await SetupDelayedSpawnPairAsync(testWorld);

            child.Spawn();
            parent.GetComponent<PredictionCallbackHelper>().OnGhostSpawnEvent += _ =>
                child.transform.SetParent(parent.transform, worldPositionStays: true);
            parent.Spawn();

            await testWorld.TickMultipleAsync(k_SettleTicks);

            Assert.AreEqual(parent.transform, child.transform.parent, "child should be parented");
            Assert.AreEqual(parent.Entity, ParentingOf(child).Parent, "the parent ghost should be recorded in ParentingData");
        }

        [Test(Description = "Delayed spawning: parenting an unspawned child cannot replicate, so it is reported and reverted.")]
        public async Task DelayedSpawn_ParentUnspawnedChild_IsReverted()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);
            var (parent, child) = await SetupDelayedSpawnPairAsync(testWorld);

            parent.Spawn();

            LogAssert.Expect(LogType.Warning, new Regex("must be spawned before parenting"));
            child.transform.SetParent(parent.transform, worldPositionStays: true);

            Assert.IsNull(child.transform.parent, "the rejected parent change should have been reverted");
        }

        [Test(Description = "Delayed spawning: parenting under an unspawned parent cannot replicate either. The parent has no Entity to record yet, so reading one would throw.")]
        public async Task DelayedSpawn_ParentUnderUnspawnedParent_IsReverted()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);
            var (parent, child) = await SetupDelayedSpawnPairAsync(testWorld);

            child.Spawn();

            LogAssert.Expect(LogType.Warning, new Regex("must be spawned before parenting"));
            child.transform.SetParent(parent.transform, worldPositionStays: true);

            Assert.IsNull(child.transform.parent, "the rejected parent change should have been reverted");
            Assert.AreEqual(Entity.Null, ParentingOf(child).Parent, "ParentingData should be untouched");
        }

        /// <summary>
        /// Creates a prefab that has parent replication disabled.
        /// </summary>
        static GameObject CreateNonReplicatingPrefab(string name)
        {
            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(
                NetCodeTestWorld.k_GeneratedFolderBasePath, name, autoRegister: false, typeof(PredictionCallbackHelper));
            Assert.IsFalse(prefab.GetComponent<GhostObject>().ParentReplication,
                $"{nameof(GhostObject.ParentReplication)} must default to false, so that parenting stays opt in");
            Netcode.RegisterPrefab(prefab);
            return prefab;
        }

        [Test(Description = "ParentReplication is opt in. A prefab that leaves it off gets no ParentingData on its ghost, so a parent change stays a local hierarchy change and nothing about it reaches a client.")]
        public async Task ParentReplicationDisabled_NoParentingDataAndNoReplication()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);

            var serial = ++s_FlickerPrefabSerial;
            var parentPrefab = CreateNonReplicatingPrefab($"OptOutParent{serial}");
            var childPrefab = CreateNonReplicatingPrefab($"OptOutChild{serial}");

            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(16);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var child = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            parent.Position = k_ParentPos;
            parent.PublishTransform();
            child.Position = k_ChildPos;
            child.PublishTransform();
            await testWorld.TickMultipleAsync(16);

            Assert.IsFalse(child.World.EntityManager.HasComponent<ParentingData>(child.Entity),
                "an opted-out ghost should carry no ParentingData");

            // Resolved before the reparent. Afterwards the client no longer mirrors the authority's world position:
            // the authority's LocalTransform still replicates, and the client applies it without the parent that
            // now gives it meaning, so the child moves by the parent offset. That is what opting out costs.
            var clientChild = RemoteChildOf(testWorld, child);

            var parentChangedFired = false;
            child.OnParentChanged += (before, after) => parentChangedFired = true;

            child.transform.SetParent(parent.transform, worldPositionStays: true);
            await testWorld.TickMultipleAsync(k_SettleTicks);

            // The authority's own hierarchy still changes, because that is just Unity. None of it is replicated.
            Assert.AreEqual(parent.transform, child.transform.parent, "the local hierarchy change should stand");
            Assert.IsFalse(parentChangedFired, $"{nameof(GhostObject.OnParentChanged)} should not fire while ParentReplication is off");
            Assert.IsNull(clientChild.transform.parent, "the client should not have been parented");
        }

        [Test(Description = "The registered prefab decides whether a ghost carries ParentingData, while the instance's ParentReplication gates the parent change. No public API can make the two disagree, so this covers a team-side change reaching the internal setter after registration.")]
        public async Task ParentReplicationEnabledAfterRegistration_IsReportedAndReverted()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest(serverCount: 1, clientCount: 1);

            var serial = ++s_FlickerPrefabSerial;
            var parentPrefab = CreateNonReplicatingPrefab($"MismatchParent{serial}");
            var childPrefab = CreateNonReplicatingPrefab($"MismatchChild{serial}");

            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(16);

            var parent = Object.Instantiate(parentPrefab).GetComponent<GhostObject>();
            var child = Object.Instantiate(childPrefab).GetComponent<GhostObject>();
            await testWorld.TickMultipleAsync(16);

            // The mismatch: the prefab said no, so the ghost has no ParentingData, but the instance now says yes.
            child.ParentReplication = true;
            Assert.IsFalse(child.World.EntityManager.HasComponent<ParentingData>(child.Entity),
                "the registered prefab decides this, so flipping the instance must not have added the component");

            LogAssert.Expect(LogType.Error, new Regex("was changed after the prefab was registered"));
            child.transform.SetParent(parent.transform, worldPositionStays: true);

            Assert.IsNull(child.transform.parent, "the rejected parent change should have been reverted");
        }
    }
}
