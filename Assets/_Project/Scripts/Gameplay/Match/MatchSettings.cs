using System;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// The numbers a match is actually being played with, as every peer sees them.
    /// </summary>
    /// <remarks>
    /// <para>A struct on the wire rather than a <see cref="MatchConfig"/> asset read locally. An
    /// asset is per-installation: the host lowering the starting life would change nothing on
    /// anybody else's machine, and the two sides would disagree about a number that decides who
    /// wins. See ADR D-005.</para>
    /// <para><b>Two of these are read by clients, not only by the server</b>, which is the part
    /// D-005 originally got wrong and the reason this has to replicate at all rather than merely
    /// being applied server-side. <see cref="DrainPerSecond"/> is what a client counts down with
    /// between the server's once-a-second updates, and <see cref="MaxLife"/> is the denominator of
    /// every life bar drawn. A host halving the drain on a session where clients still held the old
    /// number would have every bar on every other screen emptying at the wrong rate — visibly, and
    /// with nothing to point at.</para>
    /// <para>Nothing <c>Simulate()</c> reads is in here, and that is deliberate rather than
    /// incidental. Movement is executed identically on both sides of the wire; a divergence there
    /// produces a trembling character whose symptom points at reconciliation, which is not where the
    /// bug would be. Tuning is for the rules, not for the physics.</para>
    /// </remarks>
    public struct MatchSettings : INetworkSerializable, IEquatable<MatchSettings>
    {
        /// <summary>Seconds a player starts a round with. Server-side.</summary>
        public float StartingLife;

        /// <summary>Ceiling on banked life. Read by clients — it is what a bar is a fraction of.</summary>
        public float MaxLife;

        /// <summary>Seconds of life lost per second. Read by clients, who count down with it.</summary>
        public float DrainPerSecond;

        /// <summary>Hard stop for a round, or zero for no clock at all. Server-side.</summary>
        public float RoundSeconds;

        /// <summary>How often life is published, in times per second. Server-side.</summary>
        public float LifeReplicationHz;

        /// <summary>Seconds between replicated life updates.</summary>
        public float ReplicationInterval => 1f / Mathf.Max(0.5f, LifeReplicationHz);

        /// <summary>Reads the numbers off an authored asset.</summary>
        /// <remarks>
        /// The asset stays as the thing designers edit and as what a preset is made of. This is the
        /// one place it turns into a value that can travel, so an asset reference never has to.
        /// </remarks>
        public static MatchSettings From(MatchConfig config)
        {
            if (config == null) return Fallback;

            return new MatchSettings
            {
                StartingLife = config.StartingLife,
                MaxLife = config.MaxLife,
                DrainPerSecond = config.DrainPerSecond,
                RoundSeconds = config.RoundSeconds,
                LifeReplicationHz = config.LifeReplicationHz
            };
        }

        /// <summary>
        /// What a match runs on when nothing has been configured at all.
        /// </summary>
        /// <remarks>
        /// Playable rather than zeroed. A default-constructed struct gives a starting life of zero,
        /// which is a match every player loses on the first frame — a failure that looks like a rules
        /// bug rather than like a missing asset.
        /// </remarks>
        public static MatchSettings Fallback => new MatchSettings
        {
            StartingLife = 60f,
            MaxLife = 90f,
            DrainPerSecond = 1f,
            RoundSeconds = 180f,
            LifeReplicationHz = 1f
        };

        /// <summary>
        /// Brings every number into a range a match can be played in.
        /// </summary>
        /// <remarks>
        /// Applied on the server to whatever arrives, because these come from a host typing into a
        /// field. A starting life above the ceiling drains from full and looks like the field was
        /// ignored; a drain of zero is a round that can only end on the clock; a negative one heals
        /// everybody forever. None of those are worth a rule of their own — they are worth not being
        /// reachable.
        /// </remarks>
        public MatchSettings Clamped()
        {
            var clamped = new MatchSettings
            {
                MaxLife = Mathf.Clamp(MaxLife, MinLife, MaxAllowedLife),
                DrainPerSecond = Mathf.Clamp(DrainPerSecond, MinDrain, MaxDrain),
                RoundSeconds = Mathf.Clamp(RoundSeconds, 0f, MaxRoundSeconds),
                LifeReplicationHz = Mathf.Clamp(LifeReplicationHz, 0.5f, 10f)
            };

            // Last, and against the ceiling that was just clamped rather than the one that arrived.
            clamped.StartingLife = Mathf.Clamp(StartingLife, MinLife, clamped.MaxLife);

            return clamped;
        }

        /// <summary>Floor for both life values. Below a few seconds a round is over before it renders.</summary>
        public const float MinLife = 5f;

        /// <summary>Ceiling for both life values.</summary>
        public const float MaxAllowedLife = 600f;

        public const float MinDrain = 0.1f;
        public const float MaxDrain = 10f;

        /// <summary>Longest round that can be asked for. Zero, meaning no clock, is also allowed.</summary>
        public const float MaxRoundSeconds = 3600f;

        /// <remarks>
        /// NGO calls this to work out whether a write actually changed anything, and without it the
        /// comparison falls back to something coarser — so a host dragging a field to the value it
        /// already had would republish the whole struct to everyone.
        /// </remarks>
        public bool Equals(MatchSettings other)
            => StartingLife.Equals(other.StartingLife)
               && MaxLife.Equals(other.MaxLife)
               && DrainPerSecond.Equals(other.DrainPerSecond)
               && RoundSeconds.Equals(other.RoundSeconds)
               && LifeReplicationHz.Equals(other.LifeReplicationHz);

        public override bool Equals(object other) => other is MatchSettings settings && Equals(settings);

        public override int GetHashCode()
            => HashCode.Combine(StartingLife, MaxLife, DrainPerSecond, RoundSeconds, LifeReplicationHz);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref StartingLife);
            serializer.SerializeValue(ref MaxLife);
            serializer.SerializeValue(ref DrainPerSecond);
            serializer.SerializeValue(ref RoundSeconds);
            serializer.SerializeValue(ref LifeReplicationHz);
        }
    }
}
