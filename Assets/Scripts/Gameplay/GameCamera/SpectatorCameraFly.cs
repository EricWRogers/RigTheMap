using UnityEngine;
using Unity.MP_FPS;

public class SpectatorCameraFly : MonoBehaviour
{
    private PlayerGhost m_LocalGhostOwner;
    private float m_RotationX = 0f;
    private float m_RotationY = 0f;

    public void InitializeSpectator(PlayerGhost owner)
    {
        m_LocalGhostOwner = owner;
        Vector3 rot = transform.localRotation.eulerAngles;
        m_RotationY = rot.y;
        m_RotationX = rot.x;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LateUpdate()
    {
        MainCameraSingleton.Instance.transform.SetPositionAndRotation(
            m_LocalGhostOwner.transform.position,
            m_LocalGhostOwner.transform.rotation);
    }
}
