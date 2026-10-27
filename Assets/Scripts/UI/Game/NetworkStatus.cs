using System;
using System.Collections;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class NetworkStatus : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string ConnectionStatus = "ConnectionStatus";
            public const string NetworkingRole = "NetworkingRole";
            public const string SessionCode = "SessionCode";
        }

        VisualElement m_Root;
        Label m_ConnectionStatus;
        Label m_NetworkingRole;
        Label m_SessionCode;

        void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;

            m_ConnectionStatus = m_Root.Q<Label>(UIElementNames.ConnectionStatus);
            m_NetworkingRole = m_Root.Q<Label>(UIElementNames.NetworkingRole);
            m_SessionCode = m_Root.Q<Label>(UIElementNames.SessionCode);

            StartCoroutine(UpdateNetworkStatusInfo());
        }

        void OnDisable()
        {
            StopCoroutine(UpdateNetworkStatusInfo());
        }


        IEnumerator UpdateNetworkStatusInfo()
        {
            while (true)
            {
                m_ConnectionStatus.text = "Connection: ";
                switch(ConnectionSettings.Instance.GameConnectionState)
                {
                    case ConnectionState.State.Disconnected:
                        m_ConnectionStatus.text += "Offline";
                        m_NetworkingRole.text = m_SessionCode.text = string.Empty;
                        break;
                    case ConnectionState.State.Connecting:
                        m_ConnectionStatus.text += "Connecting ...";
                        m_NetworkingRole.text = m_SessionCode.text = string.Empty;
                        break;
                    case ConnectionState.State.Connected:
                        m_ConnectionStatus.text += "Connected";
                        m_NetworkingRole.text = "Role: ";
                        if (ClientServerBootstrap.HasServerWorld)
                        {
                            m_NetworkingRole.text += "Server";
                        }
                        else
                        {
                            m_NetworkingRole.text += "Client";
                        }

                        m_SessionCode.text = $"Join Code: {ConnectionSettings.Instance.SessionCode}";


                        break;
                }
                yield return (new WaitForSeconds(1.0f));
            }
        }
    }
}
