using System;
using Unity.Entities;
using Unity.MP_FPS;
using UnityEngine;
using static GhostSpawner;

using Hash128 = Unity.Entities.Hash128;


public class HealthPack : GhostMonoBehaviour, IUpdateServer
{
    [SerializeField] private float m_HealAmount = 60f;
    [SerializeField] private float m_PickupRadius = 0.8f;
    [SerializeField] private LayerMask m_PlayerLayerMask;

    private bool m_Collected;

    private void Reset()
    {
        m_PlayerLayerMask = LayerMask.GetMask("ServerPlayer");
    }

    public void UpdateServer(float deltaTime)
    {
        if (m_Collected || GhostGameObject == null || !GhostGameObject.IsGhostLinked())
        {
            return;
        }

        if (GhostGameObject.Role != MultiplayerRole.Server)
        {
            return;
        }

        var overlaps = Physics.OverlapSphere(transform.position, m_PickupRadius, m_PlayerLayerMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < overlaps.Length; ++i)
        {
            var col = overlaps[i];
            if (col == null)
            {
                continue;
            }

            var playerGhost = col.GetComponentInParent<PlayerGhost>();
            if (playerGhost == null || playerGhost.GhostGameObject == null)
            {
                continue;
            }

            var predicted = playerGhost.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>();
            predicted.CurrentHealth = Mathf.Min(predicted.MaxHealth, predicted.CurrentHealth + m_HealAmount);
            playerGhost.GhostGameObject.WriteGhostComponentData(predicted);

            if (predicted.CurrentHealth < predicted.MaxHealth)
            {
                m_Collected = true;
            }
            if (m_Collected == true)
            {
                GhostGameObject.DestroyEntity();
            }
        }
    }

    public static bool SpawnGhostPrefab(GhostReference ghostPrefab, Vector3 spawnPos, Quaternion spawnRot, Hash128 netGuid, float uniformScale = 1.0f, Action<Entity, EntityCommandBuffer> postSpawnSpecialisation = null)
    {
        return GhostSpawner.SpawnGhostPrefab(ghostPrefab, spawnPos, spawnRot, netGuid, uniformScale, postSpawnSpecialisation);
    }
}
