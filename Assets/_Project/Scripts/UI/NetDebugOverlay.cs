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
        const float PanelWidth = 330f;
        const float LabelWidth = PanelWidth - 20f;

        bool _visible = true;
        readonly StringBuilder _text = new StringBuilder(512);

        /// <summary>Reused so the per-frame OnGUI pass doesn't allocate a GUIContent every time.</summary>
        readonly GUIContent _content = new GUIContent();

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
                    _text.AppendLine($"   corrections  {player.ReconciliationCount} total");

                    // The running total says how much has happened; the window says what is
                    // happening. Only the second one tells you whether it is working right now.
                    if (player.TryGetReconciliationWindow(out ReconciliationWindow w))
                    {
                        _text.AppendLine($"   rate         {w.CorrectionsPerSecond:F1} /s   (last {ReconciliationStats.WindowSeconds:F0}s)");
                        _text.AppendLine($"   error        avg {w.MeanError:F3}  max {w.WorstError:F3} u");
                        _text.AppendLine($"   replayed     avg {w.MeanReplayedTicks:F1}  max {w.WorstReplayedTicks} ticks");
                    }
                    else
                    {
                        _text.AppendLine($"   rate         0 /s   (last {ReconciliationStats.WindowSeconds:F0}s — prediction holding)");
                    }

                    _text.AppendLine($"   visual lag   {player.VisualError:F3} u");
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

            // Ask the style how tall the text actually is. Estimating from character count happens
            // to look right for one particular block of text and silently stops fitting the moment
            // a line is added — which is exactly what just happened to this overlay.
            _content.text = _text.ToString();
            float textHeight = _style.CalcHeight(_content, LabelWidth);

            GUI.Box(new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, textHeight + 12f), GUIContent.none);
            GUI.Label(new Rect(Screen.width - PanelWidth - 2f, 18f, LabelWidth, textHeight), _content, _style);
        }

        static ulong Rtt(NetworkManager networkManager)
        {
            if (networkManager.IsServer && !networkManager.IsHost) return 0;
            return networkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }
    }
}
