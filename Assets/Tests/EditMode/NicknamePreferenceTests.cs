using NUnit.Framework;
using Snackdown.UI;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// The name the menu offers: the one this machine last played under, or a random one for
    /// somebody who has never played.
    /// </summary>
    /// <remarks>
    /// <para>These are the first tests in the project that touch <c>PlayerPrefs</c>, which is real
    /// storage shared with the editor and with whoever is using it. Every test here saves what was
    /// there and puts it back, so running the suite cannot take the name off the machine of the
    /// person running it — a test that quietly costs you a setting is a test you stop running.
    /// </para>
    /// <para>Edit mode rather than Play mode: none of this needs a scene, a peer or a frame. It is
    /// the half of <c>wr-4</c> that can be checked without looking at anything.</para>
    /// </remarks>
    public class NicknamePreferenceTests
    {
        const string Key = "snackdown.nickname";

        string _saved;
        bool _hadOne;

        [SetUp]
        public void RememberWhatWasThere()
        {
            _hadOne = PlayerPrefs.HasKey(Key);
            _saved = PlayerPrefs.GetString(Key, string.Empty);

            NicknamePreference.Forget();
        }

        [TearDown]
        public void PutItBack()
        {
            if (_hadOne) PlayerPrefs.SetString(Key, _saved);
            else PlayerPrefs.DeleteKey(Key);

            PlayerPrefs.Save();
        }

        [Test]
        public void WithNothingRemembered_AName_IsStillOffered()
        {
            string offered = NicknamePreference.Offered;

            // The old default was the computer's name, which reaches every other player through the
            // roster. Anything is better than that, but empty is not: an unnamed player is a gap in
            // every list that shows them.
            Assert.IsFalse(string.IsNullOrWhiteSpace(offered), "The menu opened with no name in it.");
        }

        [Test]
        public void WithNothingRemembered_TheOfferIsNotTheMachinesName()
        {
            // The whole reason this task exists. Named explicitly so that reintroducing
            // SystemInfo.deviceName as a default fails here rather than in a lobby.
            Assert.AreNotEqual(SystemInfo.deviceName, NicknamePreference.Offered);
        }

        [Test]
        public void ANameGoneIntoASessionWith_IsOfferedAgain()
        {
            NicknamePreference.Remember("Luca");

            Assert.AreEqual("Luca", NicknamePreference.Offered);
        }

        [Test]
        public void SurroundingWhitespace_IsNotRemembered()
        {
            NicknamePreference.Remember("  Luca  ");

            Assert.AreEqual("Luca", NicknamePreference.Offered);
        }

        [Test]
        public void AnEmptyName_IsNotWorthRemembering()
        {
            NicknamePreference.Remember("Luca");
            NicknamePreference.Remember("   ");

            // Storing it would greet the player with a blank field next launch, which reads as the
            // setting having been lost rather than as never having been set.
            Assert.AreEqual("Luca", NicknamePreference.Offered);
        }

        [Test]
        public void Forgetting_GoesBackToOfferingARandomOne()
        {
            NicknamePreference.Remember("Luca");
            NicknamePreference.Forget();

            Assert.AreNotEqual("Luca", NicknamePreference.Offered);
        }
    }
}
