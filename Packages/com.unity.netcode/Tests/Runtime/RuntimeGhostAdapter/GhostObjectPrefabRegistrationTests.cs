// Editor only: this test relies on real prefab assets (and thus asset GUIDs) being created on disk. In player builds the
// test helper cannot create assets, so both prefabs would fall back to the name-based ghost type and legitimately clash.
#if UNITY_EDITOR
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.Netcode.Tests
{
    internal class GhostObjectPrefabRegistrationTests
    {
        [Test]
        public async Task PrefabsWithSameNameInDifferentFolders_CanBothBeRegistered()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();

            // Two prefab assets sharing the same name, saved in different folders. Their ghost type must come from the
            // asset GUID rather than from the name, otherwise the second registration throws a duplicate ghost prefab exception.
            var prefabA = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(NetCodeTestWorld.k_GeneratedFolderBasePath + "FolderA", "SameName", typeof(TestMoveCube));
            var prefabB = GhostObjectPrefabHelper.CreateGhostBehaviourPrefab(NetCodeTestWorld.k_GeneratedFolderBasePath + "FolderB", "SameName", typeof(TestMoveCube));
            Assert.AreEqual(prefabA.name, prefabB.name);

            var linkA = GhostEntityMapping.LookupEntityReferencePrefab(prefabA.GetEntityId(), testWorld.ServerWorld.Unmanaged);
            var linkB = GhostEntityMapping.LookupEntityReferencePrefab(prefabB.GetEntityId(), testWorld.ServerWorld.Unmanaged);
            Assert.IsTrue(linkA.WasInitialized, "Prefab A should have been registered on the server");
            Assert.IsTrue(linkB.WasInitialized, "Prefab B should have been registered on the server");

            var serverEntityManager = testWorld.ServerWorld.EntityManager;
            var ghostTypeA = serverEntityManager.GetComponentData<GhostType>(linkA.Entity);
            var ghostTypeB = serverEntityManager.GetComponentData<GhostType>(linkB.Entity);
            Assert.AreNotEqual(ghostTypeA, ghostTypeB, "Same-named prefabs in different folders must have distinct ghost types");

            // The ghost type must match the one derived from the prefab asset GUID.
            var guidA = prefabA.GetComponent<GhostObject>().prefabId;
            var guidB = prefabB.GetComponent<GhostObject>().prefabId;
            Assert.IsFalse(string.IsNullOrEmpty(guidA), "The asset GUID should have been stamped on prefab A at import time");
            Assert.IsFalse(string.IsNullOrEmpty(guidB), "The asset GUID should have been stamped on prefab B at import time");
            Assert.AreEqual(GhostType.FromHash128(GhostPrefabCreation.UUID5FromPrefabGuid(guidA)), ghostTypeA);
            Assert.AreEqual(GhostType.FromHash128(GhostPrefabCreation.UUID5FromPrefabGuid(guidB)), ghostTypeB);

            // Both should spawn and replicate fine, each with its own ghost type.
            await testWorld.ConnectAsync(enableGhostReplication: true);
            await testWorld.TickMultipleAsync(16); // warmup: sync the prefab list and time
            Object.Instantiate(prefabA);
            Object.Instantiate(prefabB);
            using var clientGhosts = testWorld.ClientWorlds[0].EntityManager.CreateEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GhostInstance, GhostType>()
                .WithNone<PendingSpawnPlaceholder>());
            for (int i = 0; i < 16 && clientGhosts.CalculateEntityCount() < 2; ++i)
                await testWorld.TickAsync();
            Assert.AreEqual(2, clientGhosts.CalculateEntityCount(), "Both same-named prefabs should have been spawned on the client");
            using var clientGhostTypes = clientGhosts.ToComponentDataArray<GhostType>(Allocator.Temp);
            Assert.That(clientGhostTypes.ToArray(), Is.EquivalentTo(new[] { ghostTypeA, ghostTypeB }), "Client ghosts should carry the two distinct ghost types");
        }
    }
}
#endif
