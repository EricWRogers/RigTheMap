using System;
using Unity.Entities;
using Unity.MP_FPS;
using UnityEngine;
using static Gameplay.Leaderboard.LeaderboardManager;
using static GhostSpawner;

using Hash128 = Unity.Entities.Hash128;

public class HealthPackSpawner : GhostMonoBehaviour
{
    public bool HealthPackSpawned { get; set; }

    public bool isPlaced = false;

    [SerializeField] public GhostReference m_HealthPackPrefab;
    [SerializeField] public Transform[] m_HealthPackSpawnPoint;

    public void Start()
    {
        if (!isPlaced)
            return;

        SpawnHealthPack();
    }

    public static bool SpawnGhostPrefab(GhostReference ghostPrefab, Vector3 spawnPos, Quaternion spawnRot, Hash128 netGuid, float uniformScale = 1.0f,
        Action<Entity, EntityCommandBuffer> postSpawnSpecialisation = null)
    {
        return GhostSpawner.SpawnGhostPrefab(ghostPrefab, spawnPos, spawnRot, netGuid, uniformScale, postSpawnSpecialisation);
    }

    public void SpawnHealthPack()
    {
        Vector3 spawnPos = m_HealthPackSpawnPoint[UnityEngine.Random.Range(0, m_HealthPackSpawnPoint.Length)].position;

        var netGuid = GhostGameObject.GenerateRandomHash();
        var spawned = GhostSpawner.SpawnGhostPrefab(m_HealthPackPrefab, spawnPos, Quaternion.identity, netGuid);
        if (!spawned)
        {
            Debug.LogError("[HealthPackSpawner] Failed to spawn health pack.");
        }

        HealthPackSpawned = spawned;
    }

    private class HealthPackSpawnedRpc
    {
        public bool HealthPackSpawned { get; set; }
    }
}
