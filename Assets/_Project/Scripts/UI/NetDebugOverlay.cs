using System.Text;
using Snackdown.Gameplay.Player;
using Snackdown.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.UI
{
    /// <summary>
    /// On-screen readout of what the netcode is doing, plus the two switches that make the demo
    /// legible: prediction on/off and visual smoothing on/off.
    /// </summary>
    /// <remarks>
    /// Netcode that works is invisible by design — which makes it impossible to show. This overlay
    /// is how the invisible part gets demonstrated: the correction count ticking up while the
    /// character keeps moving smoothly is the entire thesis of the project on screen at once.
    /// </remarks>
    public class NetDebugOverlay : MonoBehaviour
    {
        bool _visible = true;
        readonly StringBuilder _text = new StringBuilder(512);
        GUIStyle _style;

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f1Key.wasPressedThisFrame) PredictedPlayer.PredictionEnabled = !PredictedPlayer.PredictionEnabled;
            if (keyboard.f2Key.wasPressedThisFrame) VisualSmoother.SmoothingEnabled = !VisualSmoother.SmoothingEnabled;
            if (keyboard.f3Key.wasPressedThisFrame) _visible = !_visible;
        }

        void OnGUI()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (!_visible || networkManager == null || !networkManager.IsListening) return;

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false };

            _text.Clear();

            string role = networkManager.IsHost ? "HOST" : networkManager.IsServer ? "SERVER" : "CLIENT";
            _text.AppendLine($"── {role} ──────────────────────");
            _text.AppendLine($"tick   local {networkManager.LocalTime.Tick}  server {networkManager.ServerTime.Tick}");
            _text.AppendLine($"rate   {networkManager.NetworkConfig.TickRate} Hz     rtt {Rtt(networkManager)} ms");
            _text.AppendLine($"peers  {networkManager.ConnectedClients.Count}");

            if (NetworkSimulationLoop.Instance != null && networkManager.IsServer)
                _text.AppendLine($"sent   {NetworkSimulationLoop.Instance.SnapshotsSent} snapshots");

            _text.AppendLine();

            foreach (PredictedPlayer player in NetworkSimulationLoop.ActivePlayers)
            {
                if (player == null) continue;

                string tag = player.IsOwner ? "you" : $"#{player.OwnerClientId}";

                if (player.IsOwner && !player.IsServer)
                {
                    _text.AppendLine($"[{tag}] predicted");
                    _text.AppendLine($"   corrections  {player.ReconciliationCount}");
                    _text.AppendLine($"   last error   {player.LastPredictionError:F3} u");
                    _text.AppendLine($"   replayed     {player.LastReplayedTicks} ticks");
                    _text.AppendLine($"   authority    {player.LastAuthoritativePosition:F2}");
                }
                else if (player.IsServer)
                {
                    _text.AppendLine($"[{tag}] authoritative");
                    _text.AppendLine($"   input queue  {player.ServerQueueDepth}");
                    _text.AppendLine($"   starved      {player.StarvedTicks} ticks");
                }
                else
                {
                    _text.AppendLine($"[{tag}] interpolated");
                }
            }

            _text.AppendLine();
            _text.AppendLine($"F1 prediction {(PredictedPlayer.PredictionEnabled ? "ON " : "OFF")}   (off = feel the latency)");
            _text.AppendLine($"F2 smoothing  {(VisualSmoother.SmoothingEnabled ? "ON " : "OFF")}   (off = see the corrections)");
            _text.AppendLine("F3 hide");

            GUI.Box(new Rect(Screen.width - 330f, 12f, 318f, 30f + _text.Length * 0.42f), GUIContent.none);
            GUI.Label(new Rect(Screen.width - 320f, 18f, 300f, 600f), _text.ToString(), _style);
        }

        static ulong Rtt(NetworkManager networkManager)
        {
            if (networkManager.IsServer && !networkManager.IsHost) return 0;
            return networkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }
    }
}
