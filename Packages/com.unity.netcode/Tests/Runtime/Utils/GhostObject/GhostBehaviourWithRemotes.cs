namespace Unity.Netcode.Tests
{
    internal partial class GhostBehaviourWithRemotes : GhostBehaviour
    {
        public int testValue = 0;

        [RPC(SendDirection.ServerToClient)]
        public void ServerRPC(int arg)
        {
            testValue = arg;
        }

        [RPC(SendDirection.ClientToServer)]
        public void ClientRPC(int arg)
        {
            testValue = arg;
        }
    }
}
