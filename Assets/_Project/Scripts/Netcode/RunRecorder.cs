using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Snackdown.Netcode
{
    /// <summary>
    /// Records every reconciliation of a play session and writes the series out as CSV.
    /// </summary>
    /// <remarks>
    /// <para><see cref="ReconciliationStats"/> answers "how is it going right now" for the overlay;
    /// this answers "how did that run go" after the fact. The distinction matters because the
    /// numbers worth reporting come from a whole run under stated conditions, and a rolling window
    /// by definition throws away everything older than a few seconds.</para>
    /// <para>It writes to a file rather than to the screen because the only peer that reconciles is
    /// a client — on a host session there is nothing to measure — and reading a client's overlay
    /// means transcribing values that change several times a second. A file survives the run,
    /// carries the conditions it was produced under, and can be compared against the next one.</para>
    /// <para>Duration is tracked separately from the samples: a run with zero corrections is not an
    /// empty measurement, it is the best possible result, and a rate of "0 per second over 90
    /// seconds" only means something if the 90 seconds were recorded.</para>
    /// </remarks>
    public class RunRecorder
    {
        struct Sample
        {
            public float Time;
            public float Error;
            public uint ReplayedTicks;
            public ulong RttMs;
        }

        readonly List<Sample> _samples = new List<Sample>(4096);
        float _startTime;
        bool _running;

        public bool IsRunning => _running;
        public int SampleCount => _samples.Count;
        public float Duration(float now) => _running ? now - _startTime : 0f;

        public void Begin(float time)
        {
            _samples.Clear();
            _startTime = time;
            _running = true;
        }

        public void Record(float time, float error, uint replayedTicks, ulong rttMs)
        {
            if (!_running) return;

            _samples.Add(new Sample
            {
                Time = time - _startTime,
                Error = error,
                ReplayedTicks = replayedTicks,
                RttMs = rttMs
            });
        }

        /// <summary>
        /// Writes the run to <paramref name="directory"/> and returns the full path.
        /// <paramref name="conditions"/> is recorded verbatim in the header — a measurement without
        /// the conditions that produced it is not a result, it is a number.
        /// </summary>
        public string Write(string directory, string fileName, float now, string conditions)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);

            float duration = Duration(now);
            var text = new StringBuilder(_samples.Count * 48 + 512);

            text.AppendLine("# snackdown reconciliation run");
            text.AppendLine("# conditions," + conditions);
            text.AppendLine("# duration_s," + F(duration));
            text.AppendLine("# corrections," + _samples.Count);

            if (duration > 0f)
                text.AppendLine("# corrections_per_s," + F(_samples.Count / duration));

            if (_samples.Count > 0)
            {
                float errorSum = 0f, errorWorst = 0f;
                uint replaySum = 0, replayWorst = 0;
                ulong rttSum = 0;

                for (int i = 0; i < _samples.Count; i++)
                {
                    Sample s = _samples[i];
                    errorSum += s.Error;
                    if (s.Error > errorWorst) errorWorst = s.Error;
                    replaySum += s.ReplayedTicks;
                    if (s.ReplayedTicks > replayWorst) replayWorst = s.ReplayedTicks;
                    rttSum += s.RttMs;
                }

                text.AppendLine("# mean_error_units," + F(errorSum / _samples.Count));
                text.AppendLine("# max_error_units," + F(errorWorst));
                text.AppendLine("# mean_replayed_ticks," + F(replaySum / (float)_samples.Count));
                text.AppendLine("# max_replayed_ticks," + replayWorst);
                text.AppendLine("# mean_rtt_ms," + F(rttSum / (float)_samples.Count));
            }

            text.AppendLine("time_s,error_units,replayed_ticks,rtt_ms");
            for (int i = 0; i < _samples.Count; i++)
            {
                Sample s = _samples[i];
                text.Append(F(s.Time)).Append(',')
                    .Append(F(s.Error)).Append(',')
                    .Append(s.ReplayedTicks).Append(',')
                    .Append(s.RttMs).AppendLine();
            }

            File.WriteAllText(path, text.ToString());
            return path;
        }

        /// <summary>Invariant culture throughout: a machine here writes decimal commas, and a CSV
        /// where the separator and the decimal mark are the same character is not a CSV.</summary>
        static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
    }
}
