using Snackdown.Gameplay.Player;
using UnityEngine;

namespace Snackdown.Netcode
{
    /// <summary>
    /// Renders a remote character smoothly from a choppy stream of authoritative states, by
    /// drawing it slightly in the past and interpolating between the two snapshots that straddle
    /// "now minus the delay".
    /// </summary>
    /// <remarks>
    /// <para>Remote players are never predicted — we have no idea what buttons they are holding, and
    /// guessing would produce confident, wrong motion. Instead we trade a fixed sliver of latency
    /// for smoothness: hold the render clock ~100 ms behind the newest snapshot, so there are
    /// almost always two real samples to interpolate between, and jitter or a dropped packet is
    /// absorbed silently instead of becoming a stutter.</para>
    /// <para>100 ms is three ticks at 30 Hz — it survives two consecutive lost packets. Shorter and
    /// the buffer runs dry on any hiccup; longer and remote players visibly lag behind the world.</para>
    /// </remarks>
    public class SnapshotInterpolator
    {
        /// <summary>Enough history for ~half a second at 30 Hz.</summary>
        const int Capacity = 16;

        struct Sample
        {
            public double Time;
            public PlayerState State;
        }

        readonly Sample[] _samples = new Sample[Capacity];
        int _count;

        public double NewestTime => _count > 0 ? _samples[_count - 1].Time : 0d;
        public int SampleCount => _count;

        /// <summary>
        /// Adds an authoritative state stamped with the server time it belongs to.
        /// Out-of-order arrivals (unreliable delivery makes them normal) are dropped.
        /// </summary>
        public void Push(double time, in PlayerState state)
        {
            if (_count > 0 && time <= _samples[_count - 1].Time) return;

            if (_count == Capacity)
            {
                for (int i = 1; i < Capacity; i++) _samples[i - 1] = _samples[i];
                _count--;
            }

            _samples[_count++] = new Sample { Time = time, State = state };
        }

        /// <summary>
        /// Samples the buffer at <paramref name="renderTime"/>. Interpolates when two samples
        /// bracket it; otherwise holds the nearest one rather than extrapolating — a frozen remote
        /// for one frame reads as lag, an extrapolated one reads as a glitch and then snaps back.
        /// </summary>
        public bool TryEvaluate(double renderTime, out PlayerState state)
        {
            if (_count == 0)
            {
                state = default;
                return false;
            }

            if (_count == 1 || renderTime <= _samples[0].Time)
            {
                state = _samples[0].State;
                return true;
            }

            if (renderTime >= _samples[_count - 1].Time)
            {
                state = _samples[_count - 1].State;
                return true;
            }

            for (int i = 0; i < _count - 1; i++)
            {
                Sample a = _samples[i];
                Sample b = _samples[i + 1];
                if (renderTime < a.Time || renderTime > b.Time) continue;

                double span = b.Time - a.Time;
                float t = span > 0d ? (float)((renderTime - a.Time) / span) : 0f;

                state = a.State;
                state.Position = Vector2.Lerp(a.State.Position, b.State.Position, t);
                state.Velocity = Vector2.Lerp(a.State.Velocity, b.State.Velocity, t);
                state.Grounded = b.State.Grounded;
                return true;
            }

            state = _samples[_count - 1].State;
            return true;
        }

        public void Clear() => _count = 0;
    }
}
