using Unity.Entities;
using Unity.Netcode;

namespace Unity.MP_FPS
{
    [GhostComponent]
    public struct PlayerTeam : IComponentData
    {
        [GhostField]
        public int TeamId;
    }
}