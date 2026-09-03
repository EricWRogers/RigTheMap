using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using TMPro;

namespace Unity.MP_FPS
{
    public class TeamUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text teamText;

        private void Update()
        {
            if (teamText == null)
                return;

            var world = ClientServerBootstrap.ClientWorld;

            if (world == null)
            {
                teamText.text = "Team: --";
                return;
            }

            var entityManager = world.EntityManager;

            var networkIdQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkId>()
            );

            if (networkIdQuery.IsEmptyIgnoreFilter)
            {
                teamText.text = "Team: --";
                networkIdQuery.Dispose();
                return;
            }

            var localNetworkId =
                networkIdQuery.GetSingleton<NetworkId>().Value;

            networkIdQuery.Dispose();

            var playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTeam>(),
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadOnly<PredictedPlayerGhost>()
            );

            if (playerQuery.IsEmptyIgnoreFilter)
            {
                teamText.text = "Team: --";
                playerQuery.Dispose();
                return;
            }

            using (var entities = playerQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    if (!entityManager.Exists(entity))
                        continue;

                    var ghostOwner =
                        entityManager.GetComponentData<GhostOwner>(entity);

                    if (ghostOwner.NetworkId != localNetworkId)
                        continue;

                    var playerTeam =
                        entityManager.GetComponentData<PlayerTeam>(entity);

                    if (playerTeam.TeamId == 0)
                    {
                        teamText.text = "Team: RED";
                    }
                    else if (playerTeam.TeamId == 1)
                    {
                        teamText.text = "Team: BLUE";
                    }
                    else
                    {
                        teamText.text = "Team: UNKNOWN";
                    }

                    playerQuery.Dispose();
                    return;
                }
            }

            playerQuery.Dispose();
            teamText.text = "Team: --";
        }
    }
}