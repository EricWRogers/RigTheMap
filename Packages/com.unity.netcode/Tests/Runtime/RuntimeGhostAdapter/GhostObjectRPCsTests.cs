#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Netcode.Tests
{
    internal class GhostObjectRPCsTests
    {
        [Test(Description = "Test sending of a RPC from a GhostBehaviour works in both directions.")]
        public async Task TestSendingOfRPCs()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            await testWorld.ConnectAsync(enableGhostReplication: true); // this does a lot of the boilerplate of connecting, ticking, enabling replication

            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(NetCodeTestWorld.k_GeneratedFolderBasePath, "RemotesTest", typeof(GhostBehaviourWithRemotes)); // interpolated ghost
            await testWorld.TickMultipleAsync(1);

            var serverOBJ = GameObject.Instantiate(prefab);
            await testWorld.TickMultipleAsync(6);

            // By this time we should have both objects on the client and server so lets find all of the components of type and sort them out for the server and client object
            GhostBehaviourWithRemotes serverObject = null;
            var clientObjects = new List<GhostBehaviourWithRemotes>();

            foreach (var gb in GameObject.FindObjectsByType<GhostBehaviourWithRemotes>())
            {
                if (gb.Ghost.IsPrefab())
                {
                    continue;
                }
                if (gb.IsServer)
                {
                    Assert.IsNull(serverObject, "There should only be one server object.");
                    serverObject = gb;
                }

                if (gb.IsClient)
                {
                    clientObjects.Add(gb);
                }
            }

            Assert.AreEqual(clientObjects.Count, ClientServerBootstrap.ClientWorlds.Count, "We should have the same number of Client Objects As Client Worlds");

            Assert.IsNotNull(serverObject, "Should have found a server object");
            Assert.AreNotEqual(0, clientObjects.Count, "Should have found a client object");

            Assert.AreEqual(serverObject.testValue, 0, "Server object should have it's initial value");
            foreach ( var clientObject in clientObjects)
                Assert.AreEqual(clientObject.testValue, 0, "Client object should have it's initial value");

            serverObject.ServerRPC(123);
            await testWorld.TickMultipleAsync(4);

            if (!serverObject.IsClient)
                Assert.AreEqual(0, serverObject.testValue, "Server object should have it's initial value after a server RPC");

            foreach (var clientObject in clientObjects)
                Assert.AreEqual(123, clientObject.testValue, "Client object should have it's value set after a server RPC");

            foreach (var clientObject in clientObjects)
                clientObject.testValue = 0;

            foreach (var clientObject in clientObjects)
                clientObject.ClientRPC(123);
            await testWorld.TickMultipleAsync(4);

            Assert.AreEqual(123, serverObject.testValue, "Server object should have it's value set after a client RPC");
            foreach (var clientObject in clientObjects)
                if (!clientObject.IsServer)
                    Assert.AreEqual(0, clientObject.testValue, "Client object should have it's initial value after a client RPC");
        }

        [Test(Description = "Test sending of a ClientToServer on the Server and a ServerToClient RPC on a Client is handled correctly.")]
        public async Task TestSendingOfRPCsWithIncorrectDirectionality()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            await testWorld.ConnectAsync(enableGhostReplication: true); // this does a lot of the boilerplate of connecting, ticking, enabling replication

            var prefab = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(NetCodeTestWorld.k_GeneratedFolderBasePath, "RemotesTest", typeof(GhostBehaviourWithRemotes)); // interpolated ghost
            await testWorld.TickMultipleAsync(1);

            var serverOBJ = GameObject.Instantiate(prefab);
            await testWorld.TickMultipleAsync(6);

            // By this time we should have both objects on the client and server so lets find all of the components of type and sort them out for the server and client object
            GhostBehaviourWithRemotes serverObject = null;
            var clientObjects = new List<GhostBehaviourWithRemotes>();

            foreach (var gb in GameObject.FindObjectsByType<GhostBehaviourWithRemotes>())
            {
                if (gb.Ghost.IsPrefab())
                {
                    continue;
                }
                if (gb.IsServer)
                {
                    Assert.IsNull(serverObject, "There should only be one server object.");
                    serverObject = gb;
                }

                if (gb.IsClient)
                {
                    clientObjects.Add(gb);
                }
            }

            Assert.AreEqual(clientObjects.Count, ClientServerBootstrap.ClientWorlds.Count, "We should have the same number of Client Objects As Client Worlds");

            Assert.IsNotNull(serverObject, "Should have found a server object");
            Assert.AreNotEqual(0, clientObjects.Count, "Should have found a client object");

            Assert.AreEqual(serverObject.testValue, 0, "Server object should have it's initial value");
            foreach (var clientObject in clientObjects)
                Assert.AreEqual(clientObject.testValue, 0, "Client object should have it's initial value");

            // On a single world host we don't expect to see this error
            if (!serverObject.IsClient)
            {
                // so these should cause an error
                serverObject.ClientRPC(123);
                await testWorld.TickMultipleAsync(4);

                UnityEngine.TestTools.LogAssert.Expect("Can't Invoke a ClientToServer RPC on a Server GhostBehaviour.");
            }

            foreach (var clientObject in clientObjects)
                if (!clientObject.IsServer) // Don't test client objects which are also server objects (Single world host ghost behaviours)
                    clientObject.ServerRPC(123);
            await testWorld.TickMultipleAsync(4);

            UnityEngine.TestTools.LogAssert.Expect("Can't Invoke a ServerToClient RPC on a Client GhostBehaviour.");
        }
    }
}
#endif
