using Unity.MP_FPS;
using UnityEngine;

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

            m_Collected = true;
            GhostGameObject.DestroyEntity();
            return;
        }
    }
}
