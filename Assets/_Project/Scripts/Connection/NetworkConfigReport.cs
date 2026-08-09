using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Connection
{
    /// <summary>
    /// Prints the parts of <c>NetworkConfig</c> that both peers must agree on, and the hash they
    /// produce.
    /// </summary>
    /// <remarks>
    /// <para>NGO checks these by comparing one 64-bit number, and when it disagrees it says exactly
    /// "NetworkConfig mismatch. The configuration between the server and client does not match" —
    /// which names none of the fields it hashed. With Sessions on top, that surfaces to the player
    /// as a metadata error with no relation to the cause. Diagnosing it otherwise means guessing
    /// which of seven values differs across two processes.</para>
    /// <para>So each peer logs its own inputs on connect. Two logs side by side turn a mismatch
    /// into a diff. This is cheap — a handful of values, once per connection attempt, not per
    /// frame — and it earns that cost the first time a join fails.</para>
    /// <para><b>The hash is computed uncached on purpose.</b> <c>GetConfig()</c> memoizes on first
    /// call and never recomputes, so a value changed afterwards — <c>ConnectionApproval</c>, which
    /// this project sets at runtime — would still report the old hash. Logging the cached one could
    /// print a number matching the other peer while the wire carried a different one.</para>
    /// </remarks>
    public static class NetworkConfigReport
    {
        /// <summary>Logs what this peer will be judged on, tagged with the role it is taking.</summary>
        public static void Log(NetworkManager networkManager, string role)
        {
            if (networkManager == null || networkManager.NetworkConfig == null) return;

            NetworkConfig config = networkManager.NetworkConfig;
            var line = new StringBuilder();

            line.Append("[Snackdown] NetworkConfig as ").Append(role)
                .Append(" — protocol=").Append(config.ProtocolVersion)
                .Append(" tick=").Append(config.TickRate)
                .Append(" approval=").Append(config.ConnectionApproval)
                .Append(" forceSamePrefabs=").Append(config.ForceSamePrefabs)
                .Append(" sceneManagement=").Append(config.EnableSceneManagement)
                .Append(" varLengthSafety=").Append(config.EnsureNetworkVariableLengthSafety)
                .Append(" rpcHashSize=").Append(config.RpcHashSize);

            AppendPrefabs(line, config);

            // Uncached: see the type remarks. This is the number the other peer will compare against.
            line.Append(" | hash=").Append(config.GetConfig(false));

            Debug.Log(line.ToString());
        }

        /// <remarks>
        /// The prefab list is hashed only when <c>ForceSamePrefabs</c> is on, and it is the input
        /// most likely to differ between two processes reading the same project — one of them can
        /// simply have imported a prefab the other has not.
        /// </remarks>
        static void AppendPrefabs(StringBuilder line, NetworkConfig config)
        {
            if (!config.ForceSamePrefabs || config.Prefabs == null) return;

            int count = config.Prefabs.NetworkPrefabOverrideLinks.Count;
            line.Append(" | prefabs=").Append(count).Append(" [");

            foreach (var entry in config.Prefabs.NetworkPrefabOverrideLinks)
                line.Append(entry.Key).Append(' ');

            line.Append(']');
        }
    }
}
