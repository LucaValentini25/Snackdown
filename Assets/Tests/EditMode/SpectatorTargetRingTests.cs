using System.Collections.Generic;
using NUnit.Framework;
using Snackdown.Gameplay.Match;

namespace Snackdown.Tests
{
    /// <summary>
    /// What a spectator's choice of player does when the cast changes underneath it.
    /// </summary>
    /// <remarks>
    /// The whole reason <see cref="SpectatorTargetRing"/> is not part of the camera: every case here
    /// is a player dying, leaving or being the only one left, and reproducing any of them through a
    /// camera would mean a running match, four peers and a way to kill one of them on cue.
    /// </remarks>
    public class SpectatorTargetRingTests
    {
        SpectatorTargetRing _ring;

        [SetUp]
        public void Fresh() => _ring = new SpectatorTargetRing();

        static List<ulong> Players(params ulong[] ids) => new List<ulong>(ids);

        [Test]
        public void WithNobodyToWatch_ThereIsNoTarget()
        {
            Assert.IsFalse(_ring.Refresh(Players()));
            Assert.IsFalse(_ring.HasTarget);
        }

        [Test]
        public void ANullList_IsTreatedAsAnEmptyOne()
        {
            // The camera builds this list every frame and can be asked before anybody has spawned.
            Assert.IsFalse(_ring.Refresh(null));
            Assert.IsFalse(_ring.HasTarget);
        }

        [Test]
        public void TheFirstRefresh_LandsOnTheFirstPlayer()
        {
            Assert.IsTrue(_ring.Refresh(Players(1, 4, 7)));
            Assert.AreEqual(1UL, _ring.Current);
        }

        [Test]
        public void AChoiceThatIsStillAlive_IsKeptAcrossRefreshes()
        {
            _ring.Refresh(Players(1, 4, 7));
            _ring.Step(Players(1, 4, 7), 1);

            for (int frame = 0; frame < 10; frame++) _ring.Refresh(Players(1, 4, 7));

            Assert.AreEqual(4UL, _ring.Current, "a refresh with nothing changed moved the camera");
        }

        [Test]
        public void SteppingForward_MovesOnePlayerAlong()
        {
            var players = Players(1, 4, 7);

            _ring.Step(players, 1);
            Assert.AreEqual(4UL, _ring.Current);

            _ring.Step(players, 1);
            Assert.AreEqual(7UL, _ring.Current);
        }

        [Test]
        public void SteppingPastTheEnd_WrapsToTheStart()
        {
            var players = Players(1, 4, 7);

            _ring.Step(players, 1);
            _ring.Step(players, 1);
            _ring.Step(players, 1);

            Assert.AreEqual(1UL, _ring.Current);
        }

        [Test]
        public void SteppingBackFromTheFirst_WrapsToTheLast()
        {
            // C# gives a negative remainder for a negative left operand, so the naive single modulo
            // produces an index of -1 here — the first press of left in a real match.
            var players = Players(1, 4, 7);

            _ring.Refresh(players);
            _ring.Step(players, -1);

            Assert.AreEqual(7UL, _ring.Current);
        }

        [Test]
        public void SteppingWithOnePlayerLeft_StaysOnThem()
        {
            var players = Players(4);

            _ring.Step(players, 1);
            _ring.Step(players, -1);

            Assert.AreEqual(4UL, _ring.Current);
            Assert.IsTrue(_ring.HasTarget);
        }

        [Test]
        public void SteppingByZero_ChangesNothing()
        {
            var players = Players(1, 4, 7);

            _ring.Step(players, 1);
            _ring.Step(players, 0);

            Assert.AreEqual(4UL, _ring.Current);
        }

        [Test]
        public void WhenTheWatchedPlayerDies_TheCameraMovesToTheNextOneAlong()
        {
            // The case the type exists for. Watching 4 of [1,4,7]; 4 goes out; the spectator should
            // end up on 7, which took the index 4 just vacated — not thrown back to 1.
            _ring.Step(Players(1, 4, 7), 1);
            Assert.AreEqual(4UL, _ring.Current);

            Assert.IsTrue(_ring.Refresh(Players(1, 7)));
            Assert.AreEqual(7UL, _ring.Current);
        }

        [Test]
        public void WhenTheLastPlayerInTheListDies_TheCameraFallsBackOntoTheNewLast()
        {
            _ring.Step(Players(1, 4, 7), 1);
            _ring.Step(Players(1, 4, 7), 1);
            Assert.AreEqual(7UL, _ring.Current);

            Assert.IsTrue(_ring.Refresh(Players(1, 4)));
            Assert.AreEqual(4UL, _ring.Current, "the index was not clamped into the shorter list");
        }

        [Test]
        public void WhenEverybodyDies_TheTargetGoesAway()
        {
            _ring.Refresh(Players(1, 4));
            Assert.IsTrue(_ring.HasTarget);

            Assert.IsFalse(_ring.Refresh(Players()));
            Assert.IsFalse(_ring.HasTarget);
        }

        [Test]
        public void AfterEverybodyDies_TheNextRoundStartsFromTheTop()
        {
            _ring.Step(Players(1, 4, 7), 1);
            _ring.Refresh(Players());

            // Not Clear() — this is the round ending on its own and a new one filling the list.
            Assert.IsTrue(_ring.Refresh(Players(1, 4, 7)));
            Assert.AreEqual(4UL, _ring.Current,
                "the remembered index should survive an empty frame; only leaving spectator clears it");
        }

        [Test]
        public void Clearing_SendsTheNextSpectatorBackToTheTop()
        {
            _ring.Step(Players(1, 4, 7), 1);
            _ring.Clear();

            Assert.IsFalse(_ring.HasTarget);
            Assert.IsTrue(_ring.Refresh(Players(1, 4, 7)));
            Assert.AreEqual(1UL, _ring.Current);
        }

        [Test]
        public void APlayerJoiningTheList_DoesNotStealTheCamera()
        {
            // Not a late join — this is the next round, where everyone is alive again and the list
            // grows. Whoever was being watched is still being watched.
            _ring.Step(Players(1, 4), 1);
            Assert.AreEqual(4UL, _ring.Current);

            Assert.IsTrue(_ring.Refresh(Players(1, 2, 4, 7)));
            Assert.AreEqual(4UL, _ring.Current);
        }
    }
}
