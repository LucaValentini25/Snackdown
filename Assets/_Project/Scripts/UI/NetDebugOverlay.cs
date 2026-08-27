using System.IO;
using System.Text;
using Snackdown.Gameplay.Player;
using Snackdown.Netcode;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.UI
{
    /// <summary>
    /// On-screen readout of what the netcode is doing, plus the two switches that make the demo
    /// legible: prediction on/off and visual smoothing on/off.
    /// </summary>
    /// <remarks>
    /// <para>Netcode that works is invisible by design — which makes it impossible to show. This
    /// overlay is how the invisible part gets demonstrated: the correction count ticking up while
    /// the character keeps moving smoothly is the entire thesis of the project on screen at
    /// once.</para>
    /// <para><b>It removes itself in a player build.</b> Its <c>OnGUI</c> pass is the most
    /// expensive thing in the project — the audit measured it at about 97% of all managed allocation
    /// on the host, 320–600 KB/s against roughly 12 KB/s from the entire simulation path.
    /// Development builds keep it, because a build handed to somebody to try is exactly where it
    /// earns that. See <see cref="DebugTools"/>.</para>
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

        string _lastExport;
        float _lastExportTime;

        /// <summary>How long the export confirmation stays on screen.</summary>
        const float ExportNoticeSeconds = 8f;

        /// <remarks>
        /// Destroyed rather than guarded, so Unity stops calling <c>OnGUI</c> at all. A component
        /// that returns early still costs a call per frame from the GUI pass; one that is not there
        /// costs nothing, which is the point of the exercise.
        /// </remarks>
        void Awake()
        {
            if (!DebugTools.Enabled) Destroy(this);
        }

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f1Key.wasPressedThisFrame) PredictedPlayer.PredictionEnabled = !PredictedPlayer.PredictionEnabled;
            if (keyboard.f2Key.wasPressedThisFrame) VisualSmoother.SmoothingEnabled = !VisualSmoother.SmoothingEnabled;
            if (keyboard.f3Key.wasPressedThisFrame) _visible = !_visible;
            if (keyboard.f4Key.wasPressedThisFrame) ExportRun();
            if (keyboard.f5Key.wasPressedThisFrame) TogglePeerContact();
        }

        /// <summary>
        /// Swaps where rival positions come from when predicting contact with them.
        /// </summary>
        /// <remarks>
        /// The recorder is restarted, not kept. A run whose first half was measured one way and
        /// whose second half was measured the other averages into a number describing neither, and
        /// the whole reason this key exists is to produce two files that can be compared.
        /// </remarks>
        void TogglePeerContact()
        {
            PredictedPlayer.PeerContact = PredictedPlayer.PeerContact == PeerContactSource.Interpolated
                ? PeerContactSource.Authoritative
                : PeerContactSource.Interpolated;

            foreach (IPredictedPeer peer in NetworkSimulationLoop.ActivePlayers)
                if (peer is PredictedPlayer player) player.RestartRunRecording();
        }

        /// <summary>
        /// Dumps the local client's recorded run to disk and reports where it landed.
        /// </summary>
        /// <remarks>
        /// Deliberately a keypress rather than an automatic write on shutdown: a run is worth
        /// keeping when the person watching decides it was, and writing on every exit would bury
        /// the interesting ones under dozens of accidental two-second files.
        /// </remarks>
        void ExportRun()
        {
            foreach (IPredictedPeer peer in NetworkSimulationLoop.ActivePlayers)
            {
                if (peer is not PredictedPlayer player) continue;

                string path = player.WriteRunMetrics(RunDirectory, BuildFileName(), DescribeConditions());
                if (path == null) continue;

                _lastExport = path;
                _lastExportTime = Time.realtimeSinceStartup;
                Debug.Log($"[Snackdown] Run metrics written to {path}");
                return;
            }

            _lastExport = "nothing to export — only a predicting client records a run";
            _lastExportTime = Time.realtimeSinceStartup;
        }

        static string RunDirectory => Path.Combine(Application.persistentDataPath, "metrics");

        static string BuildFileName()
        {
            System.DateTime now = System.DateTime.Now;
            return $"run-{now:yyyyMMdd-HHmmss}.csv";
        }

        /// <summary>
        /// The network conditions the run was produced under, read from the live simulator rather
        /// than typed in — a hand-written note about latency records what someone meant to set.
        /// </summary>
        /// <remarks>
        /// Read from <see cref="NetworkSimulator"/> and not from <c>UnityTransport.DebugSimulator</c>:
        /// the latter still exists, still compiles, and has no effect whatsoever since the
        /// Multiplayer Tools package took the job over. A run labelled with settings that were
        /// never applied is worse than an unlabelled one.
        /// </remarks>
        static string DescribeConditions()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return "unknown";

            // What the toggles were set to goes in first. A file that does not say which settings
            // produced it is not a measurement of anything - and prediction earns its place at the
            // front of that line, because a run recorded with it off is not a reconciliation run at
            // all. One was exported with zero corrections in sixty-seven seconds and read as the
            // netcode being broken; it was F1.
            string tick = $"prediction {(PredictedPlayer.PredictionEnabled ? "ON" : "OFF")}"
                          + $" | peer contact {PredictedPlayer.PeerContact}"
                          + $" | tick {networkManager.NetworkConfig.TickRate}Hz";
            NetworkSimulator simulator = FindFirstObjectByType<NetworkSimulator>();

            if (simulator == null || simulator.ConnectionPreset == null)
                return $"{tick} | no simulated impairment";

            INetworkSimulatorPreset preset = simulator.ConnectionPreset;

            // This reads THIS peer's simulator only. Impairment applied on the other end shapes the
            // connection just as much and is invisible from here — the first recorded run under
            // 150ms said "no impairment" and reported 466ms RTT in the same file. So the local
            // setting is labelled as local, and the measured round trip is what states the
            // conditions the run actually happened under.
            bool impaired = preset.PacketDelayMs > 0 || preset.PacketJitterMs > 0 || preset.PacketLossPercent > 0;

            if (!impaired)
                return $"{tick} | local simulator off — see mean_rtt_measured_ms for actual conditions";

            string name = string.IsNullOrWhiteSpace(preset.Name) ? "custom" : preset.Name;
            return $"{tick} | local simulator '{name}': delay {preset.PacketDelayMs}ms " +
                   $"jitter {preset.PacketJitterMs}ms loss {preset.PacketLossPercent}%";
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
            _text.AppendLine($"rate   {networkManager.NetworkConfig.TickRate} Hz     rtt {Rtt(networkManager)} ms (transport)");
            _text.AppendLine($"peers  {networkManager.ConnectedClients.Count}");

            if (NetworkSimulationLoop.Instance != null && networkManager.IsServer)
                _text.AppendLine($"sent   {NetworkSimulationLoop.Instance.SnapshotsSent} snapshots");

            _text.AppendLine();

            // The loop only knows peers as IPredictedPeer. This overlay is a debug view of the
            // concrete character, so it asks for the concrete type — UI sits above gameplay and is
            // allowed to. A peer that is not a PredictedPlayer has nothing to report here.
            foreach (IPredictedPeer peer in NetworkSimulationLoop.ActivePlayers)
            {
                if (peer is not PredictedPlayer player) continue;

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
                    _text.AppendLine($"   rtt (ours)   {player.LastMeasuredRttMs:F0} ms  measured on our own traffic");
                    _text.AppendLine($"   authority    {player.LastAuthoritativePosition:F2}");

                    if (player.IsRecording)
                        _text.AppendLine($"   recording    {player.RecordedDuration:F0}s, {player.RecordedCorrections} samples");
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
            _text.AppendLine($"F5 peer contact {PredictedPlayer.PeerContact}   (restarts the recording)");
            _text.AppendLine("F3 hide      F4 export run");

            if (_lastExport != null && Time.realtimeSinceStartup - _lastExportTime < ExportNoticeSeconds)
            {
                _text.AppendLine();
                _text.AppendLine($"saved: {_lastExport}");
            }

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
