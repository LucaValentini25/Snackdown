using UnityEngine;

namespace Snackdown.Core
{
    /// <summary>
    /// Caps the render loop so drawing frames nobody asked for cannot starve the network tick.
    /// </summary>
    /// <remarks>
    /// <para>Unity defaults to rendering as fast as the hardware allows. On one machine running a
    /// host and a client at once — which is every Multiplayer Play Mode session, and how this
    /// project is developed — two uncapped renderers saturate the CPU between them. The simulation
    /// is the first thing to suffer: a client that misses its sampling window sends no input, and a
    /// server with no input repeats the last one it had and drifts away from what the client
    /// predicted. A measured run showed 122 starved ticks and 1.66 corrections per second under 2%
    /// packet loss, where the redundancy in <c>InputPacket</c> should have made loss a non-event.
    /// None of those corrections were the network's doing.</para>
    /// <para>60 is deliberate rather than arbitrary: double the 30 Hz tick, so every simulated step
    /// is presented at least twice and interpolation still has frames to be smooth in, while
    /// everything above that is spent on nothing a player can perceive.</para>
    /// <para>VSync is cleared first because it takes precedence — with it on, the target frame rate
    /// is silently ignored and the cap that looks configured does nothing.</para>
    /// </remarks>
    public static class FrameRatePolicy
    {
        /// <summary>Twice the network tick. See the type remarks for why not more.</summary>
        public const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
