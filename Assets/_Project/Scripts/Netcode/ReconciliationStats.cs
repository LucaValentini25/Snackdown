namespace Snackdown.Netcode
{
    /// <summary>
    /// Rolling statistics about how often prediction was wrong and by how much, over the last
    /// <see cref="WindowSeconds"/> of play.
    /// </summary>
    /// <remarks>
    /// <para>A running total answers the wrong question. "412 corrections" says nothing without
    /// knowing over how long, and the last error says nothing about the one before it — yet those
    /// are exactly the numbers that describe whether the netcode is working: a <i>rate</i> of
    /// corrections and a <i>typical</i> magnitude. A counter that only grows looks alarming when
    /// the connection is fine and looks identical whether the last minute was calm or terrible.</para>
    /// <para>The window is a plain ring of timestamped samples, re-scanned on read. At a handful of
    /// corrections per second there is nothing to optimize, and keeping the raw samples means the
    /// worst case is a real observed value rather than a decayed average that never quite reaches
    /// the peak it is supposed to report.</para>
    /// </remarks>
    public class ReconciliationStats
    {
        /// <summary>How far back the rate and the average look. Long enough to be stable, short
        /// enough that flipping a network condition shows up while you are still watching.</summary>
        public const float WindowSeconds = 5f;

        /// <summary>Room for 30 corrections per second across the window — far past the point
        /// where the connection is the problem rather than the measurement.</summary>
        const int Capacity = 256;

        struct Sample
        {
            public float Time;
            public float Error;
            public uint ReplayedTicks;
        }

        readonly Sample[] _samples = new Sample[Capacity];
        int _next;
        int _count;

        /// <summary>Corrections recorded since the session started. Never reset.</summary>
        public int TotalCorrections { get; private set; }

        /// <summary>
        /// Records one reconciliation. <paramref name="time"/> is passed in rather than read from
        /// <c>Time</c> so this type stays testable without an engine loop.
        /// </summary>
        public void Record(float time, float error, uint replayedTicks)
        {
            _samples[_next] = new Sample { Time = time, Error = error, ReplayedTicks = replayedTicks };
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            TotalCorrections++;
        }

        /// <summary>
        /// Summarizes the window ending at <paramref name="now"/>. Returns false when nothing
        /// happened in it — which is the good case, and reads better as "no data" than as zeros.
        /// </summary>
        public bool TryGetWindow(float now, out ReconciliationWindow window)
        {
            float cutoff = now - WindowSeconds;
            int hits = 0;
            float sum = 0f;
            float worst = 0f;
            uint replaySum = 0;
            uint replayWorst = 0;

            for (int i = 0; i < _count; i++)
            {
                Sample s = _samples[i];
                if (s.Time < cutoff) continue;

                hits++;
                sum += s.Error;
                if (s.Error > worst) worst = s.Error;
                replaySum += s.ReplayedTicks;
                if (s.ReplayedTicks > replayWorst) replayWorst = s.ReplayedTicks;
            }

            if (hits == 0)
            {
                window = default;
                return false;
            }

            window = new ReconciliationWindow
            {
                CorrectionsPerSecond = hits / WindowSeconds,
                MeanError = sum / hits,
                WorstError = worst,
                MeanReplayedTicks = replaySum / (float)hits,
                WorstReplayedTicks = replayWorst,
                SampleCount = hits
            };
            return true;
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
        }

        /// <summary>Clears the window <i>and</i> the running total, as if the session had just begun.</summary>
        /// <remarks>
        /// Separate from <see cref="Clear"/>, which forgets the last few seconds and keeps the count
        /// since spawn. This one is for the case where the conditions changed underneath: a total
        /// spanning two different settings answers a question nobody asked.
        /// </remarks>
        public void Reset()
        {
            Clear();
            TotalCorrections = 0;
        }
    }

    /// <summary>One window's worth of reconciliation statistics, ready to display or record.</summary>
    public struct ReconciliationWindow
    {
        public float CorrectionsPerSecond;
        public float MeanError;
        public float WorstError;
        public float MeanReplayedTicks;
        public uint WorstReplayedTicks;
        public int SampleCount;
    }
}
