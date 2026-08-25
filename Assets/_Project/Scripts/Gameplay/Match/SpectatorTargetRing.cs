using System.Collections.Generic;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Which player a spectator is watching, and what happens to that choice when the cast changes.
    /// </summary>
    /// <remarks>
    /// <para>Split out of <see cref="SpectatorCamera"/> for the same reason
    /// <c>Reconciler</c> was split out of the character: the interesting part is a decision, the
    /// decision needs no camera, and a decision reachable only through a <c>MonoBehaviour</c> in a
    /// running match is a decision nothing tests. Everything about a transform, an input axis and a
    /// frame stayed with the component.</para>
    /// <para><b>It remembers a position, not just a player.</b> When the player being watched dies
    /// they leave the list, and the natural thing for a spectator is to end up on the next one along
    /// rather than snapped back to the top of the roster. Keeping the index and clamping it into the
    /// shorter list does exactly that, because removing an entry shifts the following one down into
    /// the index that was just vacated. Storing only the client id would have lost the position with
    /// the player.</para>
    /// <para>The candidate list is passed in on every call rather than held, because who is alive
    /// changes every frame and a ring holding its own copy would be answering yesterday's question.
    /// It also means the caller decides what "watchable" means — alive, on this peer, with a body in
    /// the world — and this type never has to know.</para>
    /// </remarks>
    public class SpectatorTargetRing
    {
        ulong _current;
        bool _hasTarget;
        int _index;

        /// <summary>Whether <see cref="Current"/> names anybody.</summary>
        public bool HasTarget => _hasTarget;

        /// <summary>The client being watched. Meaningless unless <see cref="HasTarget"/>.</summary>
        public ulong Current => _current;

        /// <summary>
        /// Points the ring at somebody who is still in the list, and says whether it managed to.
        /// </summary>
        /// <remarks>
        /// Called every frame, not only when the list changes. A ring that reacted to a death event
        /// would keep watching a player who had left the moment one notification went missing, which
        /// is the failure the camera itself was already written to avoid.
        /// </remarks>
        public bool Refresh(IReadOnlyList<ulong> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                _hasTarget = false;
                return false;
            }

            if (_hasTarget)
            {
                int at = IndexOf(candidates, _current);
                if (at >= 0)
                {
                    _index = at;
                    return true;
                }
            }

            if (_index >= candidates.Count) _index = candidates.Count - 1;
            if (_index < 0) _index = 0;

            _current = candidates[_index];
            _hasTarget = true;
            return true;
        }

        /// <summary>Moves one player along the list, wrapping at both ends.</summary>
        /// <param name="direction">Positive to go down the list, negative to go up.</param>
        public void Step(IReadOnlyList<ulong> candidates, int direction)
        {
            if (!Refresh(candidates) || direction == 0) return;

            int count = candidates.Count;

            // Two modulos: C# gives a negative remainder for a negative left operand, so a single
            // one would produce an index of -1 the first time somebody pressed left on the top entry.
            _index = ((_index + direction) % count + count) % count;
            _current = candidates[_index];
        }

        /// <summary>Forgets the choice, so the next spectator starts from the top of the list.</summary>
        public void Clear()
        {
            _hasTarget = false;
            _index = 0;
        }

        static int IndexOf(IReadOnlyList<ulong> candidates, ulong id)
        {
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] == id) return i;

            return -1;
        }
    }
}
