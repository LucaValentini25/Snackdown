using NUnit.Framework;
using Snackdown.Connection;

namespace Snackdown.Tests
{
    /// <summary>
    /// That a join carries how its target should be read, and that the older call still means what
    /// it always meant.
    /// </summary>
    /// <remarks>
    /// A join code and a listing id are both strings pointing at the same session, and the service
    /// has a separate call for each. Sending one to the other's call fails as "no such session",
    /// which reaches the player as the game having ended — a wrong answer they cannot act on. These
    /// are cheap tests for a mistake that would be expensive to recognise.
    /// </remarks>
    public class JoinTargetTests
    {
        [Test]
        public void ATypedJoin_SaysItWasTyped()
        {
            ConnectionRequest request = ConnectionRequest.Join("ABC123", "Luca");

            Assert.AreEqual(JoinTargetKind.Typed, request.TargetKind);
            Assert.AreEqual("ABC123", request.Target);
        }

        [Test]
        public void ADefaultRequest_IsTyped()
        {
            // The enum's zero. A provider reading TargetKind on a request built any other way must
            // fall into the path that existed before there was a browser.
            var bare = new ConnectionRequest();

            Assert.AreEqual(JoinTargetKind.Typed, bare.TargetKind);
        }

        [Test]
        public void HostingIsNotAJoin_AndCarriesNoTarget()
        {
            ConnectionRequest request = ConnectionRequest.Host("Luca");

            Assert.IsEmpty(request.Target);
        }

        [Test]
        public void AJoinFromTheBrowser_CarriesTheListingsIdAndSaysSo()
        {
            var listing = new SessionListing("abcd-1234", "Luca's game", 2, 4);
            ConnectionRequest request = ConnectionRequest.JoinListed(listing, "Someone");

            Assert.AreEqual(JoinTargetKind.Listing, request.TargetKind);
            Assert.AreEqual("abcd-1234", request.Target);
            Assert.AreEqual("Someone", request.Nickname);
        }

        [Test]
        public void AListingWithNoName_StillReadsAsSomething()
        {
            // The service's own default name is a GUID and a session can be created without one at
            // all. A blank row is a row nobody can tell apart from the next blank row.
            Assert.IsNotEmpty(new SessionListing("id", null, 0, 4).Name);
            Assert.IsNotEmpty(new SessionListing("id", "   ", 0, 4).Name);
        }

        [Test]
        public void AListingIsFull_OnlyWhenItHasNoRoomLeft()
        {
            Assert.IsFalse(new SessionListing("id", "game", 3, 4).IsFull);
            Assert.IsTrue(new SessionListing("id", "game", 4, 4).IsFull);

            // Capacity of zero means the service told us nothing useful, and greying out every row
            // in the list is a worse answer than letting the join fail on its own.
            Assert.IsFalse(new SessionListing("id", "game", 0, 0).IsFull);
        }

        [Test]
        public void AnEmptyBrowse_IsASuccessWithNothingInIt()
        {
            // The distinction the browser is rendered from: nobody hosting must not read as the
            // service being unreachable.
            BrowseResult empty = BrowseResult.Ok(new SessionListing[0]);

            Assert.IsTrue(empty.Success);
            Assert.IsEmpty(empty.Sessions);
            Assert.IsEmpty(empty.PlayerFacingMessage);
        }

        [Test]
        public void AFailedBrowse_HasNoSessionsAndSomethingToSay()
        {
            BrowseResult failed = BrowseResult.Failed(ConnectionFailure.Error, "no network");

            Assert.IsFalse(failed.Success);
            Assert.IsNotNull(failed.Sessions, "a caller iterating the list should not have to null-check it");
            Assert.IsEmpty(failed.Sessions);
            Assert.IsNotEmpty(failed.PlayerFacingMessage);
        }

        [Test]
        public void ACancelledBrowse_SaysNothing()
        {
            // The player backed out. Telling them their own click failed is noise.
            Assert.IsEmpty(BrowseResult.Failed(ConnectionFailure.Cancelled).PlayerFacingMessage);
        }
    }
}
