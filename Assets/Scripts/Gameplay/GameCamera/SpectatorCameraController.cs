using UnityEngine;
using Unity.MP_FPS;
public class SpectatorCameraController : MonoBehaviour
{
    [SerializeField] private float m_MoveSpeed = 12f;
    [SerializeField] private float m_FastMoveMultiplier = 2.5f;
    [SerializeField] private float m_MouseSensitivity = 2f;
    [SerializeField] private float m_FollowDistance = 6f;
    [SerializeField] private float m_FollowHeight = 2.5f;
    [SerializeField] private float m_FollowSmoothTime = 0.15f;

    private Transform m_Target;
    private Vector3 m_FollowVelocity;
    private float m_Yaw;
    private float m_Pitch;

    public void SetTarget(Transform target)
    {
        m_Target = target;

        if (target == null)
        {
            return;
        }

        Vector3 offset = target.forward * -m_FollowDistance;
        transform.position = target.position + offset + Vector3.up * m_FollowHeight;
        transform.LookAt(target.position + Vector3.up * 1.2f);
    }

    private void Update()
    {
        if (m_Target != null)
        {
            UpdateFollowCamera();
        }
        else
        {
            UpdateFreeCamera();
        }
    }

    private void UpdateFollowCamera()
    {
        Vector3 desiredPosition =
        m_Target.position -
        m_Target.forward * m_FollowDistance +
        Vector3.up * m_FollowHeight;

        transform.position = Vector3.SmoothDamp(
        transform.position,
        desiredPosition,
        ref m_FollowVelocity,
        m_FollowSmoothTime);

        Vector3 lookPoint = m_Target.position + Vector3.up * 1.2f;
        Quaternion desiredRotation = Quaternion.LookRotation( lookPoint - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp( transform.rotation, desiredRotation, 12f * Time.deltaTime);
    }

    private void UpdateFreeCamera()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        float speed = m_MoveSpeed;

        if (Input.GetKey(KeyCode.LeftShift))
        {
            speed *= m_FastMoveMultiplier;
        }

        Vector3 movement =
            transform.right * horizontal +
            transform.forward * vertical;

        if (Input.GetKey(KeyCode.E))
        {
            movement += Vector3.up;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            movement -= Vector3.up;
        }

        transform.position += movement.normalized * speed * Time.deltaTime;

        if (Input.GetMouseButton(1))
        {
            m_Yaw += Input.GetAxis("Mouse X") * m_MouseSensitivity;
            m_Pitch -= Input.GetAxis("Mouse Y") * m_MouseSensitivity;
            m_Pitch = Mathf.Clamp(m_Pitch, -85f, 85f);

            transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }
    }
}
