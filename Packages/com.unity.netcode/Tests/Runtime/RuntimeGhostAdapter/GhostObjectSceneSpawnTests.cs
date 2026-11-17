using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Netcode.Tests
{
    internal class GhostObjectSceneSpawnTests
    {
        [Test(Description = "A GhostObject authored straight into a scene has no source prefab, so no ghost type; it must say so instead of silently never initializing.")]
        public async Task SceneGhost_RawGameObjectWithoutPrefab_LogsExplicitError()
        {
            await using var testWorld = new NetCodeTestWorld();
            await testWorld.SetupGameObjectTest();
            await testWorld.ConnectAsync(enableGhostReplication: true);

            LogAssert.Expect(LogType.Error, new Regex(".*authored directly into a scene.*"));

            var sceneGhost = new GameObject("RawSceneGhost");
            sceneGhost.AddComponent<GhostObject>();

            await testWorld.TickMultipleAsync(1);
        }
    }
}
