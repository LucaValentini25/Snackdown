using NUnit.Framework;
using Snackdown.Gameplay.Match;
using Snackdown.UI;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for when the escape menu is allowed on screen.
    /// </summary>
    /// <remarks>
    /// The rule is asked twice — once to open the menu and once every frame it stays open — so the
    /// cases that matter are the ones where the answer changes underneath a player who is already
    /// reading it: the host starting a match, or the connection ending while the menu is up.
    /// </remarks>
    public class EscapeMenuTests
    {
        [Test]
        public void WithNoSession_ThereIsNothingToOpen()
        {
            foreach (MatchPhase phase in System.Enum.GetValues(typeof(MatchPhase)))
                Assert.IsFalse(EscapeMenu.MayBeOpen(inSession: false, phase),
                    $"phase {phase} should not open a menu with no session");
        }

        [Test]
        public void InTheLobby_ItOpens()
            => Assert.IsTrue(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Lobby));

        [Test]
        public void DuringAMatch_ItOpens()
            => Assert.IsTrue(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Playing));

        [Test]
        public void OverTheEndScreen_ItOpens()
            => Assert.IsTrue(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Ended));

        [Test]
        public void WhileTheArenaLoads_ItDoesNot()
            => Assert.IsFalse(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Loading));

        [Test]
        public void DuringTheCountdown_ItDoesNot()
            => Assert.IsFalse(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Countdown));

        /// <remarks>
        /// The case the second call exists for: the menu is open in the lobby, the host presses
        /// start, and the answer has to turn false without anybody pressing Escape again.
        /// </remarks>
        [Test]
        public void AMatchStartingUnderAnOpenMenu_ClosesIt()
        {
            Assert.IsTrue(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Lobby));
            Assert.IsFalse(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Loading));
        }

        /// <remarks>
        /// The other one: the host walks out while a client has the menu up, so there is no session
        /// left to be in and the screen underneath is about to become the main menu.
        /// </remarks>
        [Test]
        public void LosingTheSessionUnderAnOpenMenu_ClosesIt()
        {
            Assert.IsTrue(EscapeMenu.MayBeOpen(inSession: true, MatchPhase.Playing));
            Assert.IsFalse(EscapeMenu.MayBeOpen(inSession: false, MatchPhase.Playing));
        }
    }
}
