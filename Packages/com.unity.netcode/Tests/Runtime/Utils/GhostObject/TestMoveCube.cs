using Unity.Entities;
using UnityEngine;

namespace Unity.Netcode.Tests
{
    internal class TestMoveCube : GhostBehaviour
    {
        public static int CubeInstances;

        public override void Awake()
        {
            base.Awake();
            if (Ghost.IsPrefab())
                return;
            transform.position = Vector3.zero;
            CubeInstances++;
        }

        public override void OnDestroy()
        {
            if (Ghost.IsPrefab())
                return;
            CubeInstances--;
            base.OnDestroy();
        }

        public bool BelongTo(World world)
        {
            return world == this.Ghost.World;
        }
    }
}
