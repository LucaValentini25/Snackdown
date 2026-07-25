using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Snackdown.Core
{
    /// <summary>
    /// A deliberately ugly Host / Client / Server launcher for the Phase 1 test scene.
    /// </summary>
    /// <remarks>
    /// Phase 2 replaces this entirely with a real menu on top of <c>IConnectionProvider</c>
    /// (direct or Relay, one flow). Until then, every minute spent on connection UI is a minute
    /// stolen from the thing this phase exists to prove, so: three IMGUI buttons.
    /// </remarks>
    public class NetTestBootstrap : MonoBehaviour
    {
        [SerializeField] string _address = "127.0.0.1";
        [SerializeField] ushort _port = 7777;

        void OnGUI()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            GUILayout.BeginArea(new Rect(12f, 12f, 240f, 200f));

            if (!networkManager.IsClient && !networkManager.IsServer)
            {
                GUILayout.Label("Snackdown — Phase 1 test");

                GUILayout.BeginHorizontal();
                GUILayout.Label("IP", GUILayout.Width(20f));
                _address = GUILayout.TextField(_address);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Host")) Launch(networkManager, host: true);
                if (GUILayout.Button("Client")) Launch(networkManager, host: false);
                if (GUILayout.Button("Server only")) { ApplyTransport(networkManager); networkManager.StartServer(); }
            }
            else
            {
                string role = networkManager.IsHost ? "Host" : networkManager.IsServer ? "Server" : "Client";
                GUILayout.Label($"{role} — {networkManager.ConnectedClients.Count} connected");
                if (GUILayout.Button("Shutdown")) networkManager.Shutdown();
            }

            GUILayout.EndArea();
        }

        void Launch(NetworkManager networkManager, bool host)
        {
            ApplyTransport(networkManager);
            if (host) networkManager.StartHost();
            else networkManager.StartClient();
        }

        void ApplyTransport(NetworkManager networkManager)
        {
            if (networkManager.NetworkConfig.NetworkTransport is UnityTransport transport)
                transport.SetConnectionData(_address, _port);
        }
    }
}
