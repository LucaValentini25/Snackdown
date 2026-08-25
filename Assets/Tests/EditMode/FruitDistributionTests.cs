using System;
using NUnit.Framework;
using Snackdown.Gameplay.Fruits;
using UnityEditor;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// A hundred thousand rolls against the table the game actually ships, so the rarity the docs
    /// claim is a fact about the repository rather than about a run somebody remembers doing.
    /// </summary>
    /// <remarks>
    /// <para>The distribution was checked once, outside the tree, and the number written down —
    /// "35% common down to 1% legendary". Nothing since could have failed if somebody had retuned a
    /// weight, reordered the table, or broken <see cref="FruitTable.Roll"/> outright: the claim and
    /// the code had no connection at all.</para>
    /// <para>Seeded, and the seed is written here. An unseeded run of this shape fails once a month
    /// for nobody's reason and teaches everyone to re-run it until it goes green, which is worse
    /// than not having it.</para>
    /// <para>Against the shipped asset rather than a table built in the test. What is worth
    /// protecting is that <i>this</i> table produces the spread the docs describe — a fabricated one
    /// would only prove the arithmetic, which the other test here already does.</para>
    /// </remarks>
    public class FruitDistributionTests
    {
        const int Rolls = 100_000;
        const int Seed = 20260825;

        /// <summary>
        /// How far a measured share may sit from the weight that asked for it.
        /// </summary>
        /// <remarks>
        /// A percentage point either way. At a hundred thousand rolls the sampling error on the
        /// rarest entry is a few hundredths of a point, so this is loose enough never to flap and
        /// tight enough that swapping two weights fails it.
        /// </remarks>
        const float ToleranceInPoints = 1f;

        FruitTable _table;

        [SetUp]
        public void LoadTheShippedTable()
        {
            _table = AssetDatabase.LoadAssetAtPath<FruitTable>("Assets/_Project/Settings/FruitTable.asset");
            Assert.IsNotNull(_table, "The fruit table is missing; there is no distribution to check.");
            Assert.Greater(_table.Count, 0, "The fruit table is empty.");
        }

        [Test]
        public void AHundredThousandRolls_LandInTheProportionsTheWeightsAskFor()
        {
            var random = new System.Random(Seed);
            var counts = new int[_table.Count];

            float totalWeight = 0f;
            for (int i = 0; i < _table.Count; i++) totalWeight += Mathf.Max(0f, _table.Get(i).Weight);

            for (int roll = 0; roll < Rolls; roll++)
            {
                int index = _table.Roll((float)random.NextDouble());

                Assert.GreaterOrEqual(index, 0, "the table refused to produce a fruit");
                Assert.Less(index, _table.Count, "the table produced a fruit it does not have");

                counts[index]++;
            }

            var report = new System.Text.StringBuilder();

            for (int i = 0; i < _table.Count; i++)
            {
                float asked = Mathf.Max(0f, _table.Get(i).Weight) / totalWeight * 100f;
                float got = counts[i] / (float)Rolls * 100f;

                report.AppendLine(
                    $"{_table.Get(i).DisplayName,-12} asked {asked,5:0.00}%   got {got,5:0.00}%");

                Assert.AreEqual(asked, got, ToleranceInPoints,
                    $"{_table.Get(i).DisplayName} came up {got:0.00}% of the time against a weight asking for {asked:0.00}%.");
            }

            // Printed on success as well as failure: the point of the test is the shape of the
            // table, and a reader who has to break it to see the numbers will not look.
            TestContext.WriteLine(report.ToString());
        }

        [Test]
        public void TheCommonestAndTheRarest_AreTheOnesTheDocsName()
        {
            // docs/00 and docs/03 both say "35% common down to 1% legendary". This is the assertion
            // that makes those sentences answerable rather than decorative.
            float total = 0f;
            for (int i = 0; i < _table.Count; i++) total += Mathf.Max(0f, _table.Get(i).Weight);

            float commonest = 0f;
            float rarest = float.MaxValue;

            for (int i = 0; i < _table.Count; i++)
            {
                float share = Mathf.Max(0f, _table.Get(i).Weight) / total * 100f;
                commonest = Mathf.Max(commonest, share);
                rarest = Mathf.Min(rarest, share);
            }

            Assert.AreEqual(35f, commonest, ToleranceInPoints, "the commonest fruit");
            Assert.AreEqual(1f, rarest, ToleranceInPoints, "the rarest fruit");
        }

        [Test]
        public void EveryFruit_ComesUpAtLeastOnce()
        {
            var random = new System.Random(Seed);
            var seen = new bool[_table.Count];

            for (int roll = 0; roll < Rolls; roll++) seen[_table.Roll((float)random.NextDouble())] = true;

            for (int i = 0; i < _table.Count; i++)
            {
                // An entry that never appears is a weight of zero written as something else, or an
                // off-by-one in the walk. Both are invisible in a play session — you simply never
                // see a pineapple and assume you were unlucky.
                Assert.IsTrue(seen[i],
                    $"{_table.Get(i).DisplayName} never came up in {Rolls:n0} rolls.");
            }
        }

        [Test]
        public void TheEdgesOfTheRoll_StayInsideTheTable()
        {
            // Roll takes a 0..1 from the caller, which is what makes it testable at all — the server
            // owns where randomness enters. Both ends have to land somewhere real.
            Assert.AreEqual(0, _table.Roll(0f), "a roll of zero should be the first entry");
            Assert.Less(_table.Roll(1f), _table.Count, "a roll of one fell off the end of the table");
            Assert.GreaterOrEqual(_table.Roll(1f), 0);

            // Out of range on purpose: Roll clamps rather than trusting, because a caller passing
            // something odd should get a fruit and not an exception in the middle of a match.
            Assert.GreaterOrEqual(_table.Roll(-5f), 0, "a negative roll");
            Assert.Less(_table.Roll(5f), _table.Count, "a roll past one");
        }
    }
}
