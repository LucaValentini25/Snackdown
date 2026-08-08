using NUnit.Framework;
using Snackdown.Gameplay.Fruits;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the weighted fruit roll.
    /// </summary>
    /// <remarks>
    /// <see cref="FruitTable.Roll"/> takes the random number as an argument rather than calling
    /// <c>Random</c> itself, which is what makes this testable without seeding anything. The
    /// boundaries are the interesting part: a roll of exactly 0 or exactly 1 must still land on a
    /// real entry, and a table nobody can roll must say so rather than silently returning the first.
    /// </remarks>
    public class FruitTableTests
    {
        FruitTable _table;

        static FruitTable Build(params float[] weights)
        {
            var table = ScriptableObject.CreateInstance<FruitTable>();
            var entries = new FruitTable.Entry[weights.Length];

            for (int i = 0; i < weights.Length; i++)
                entries[i] = new FruitTable.Entry { DisplayName = $"F{i}", Weight = weights[i], LifeSeconds = i + 1 };

            typeof(FruitTable)
                .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(table, entries);

            return table;
        }

        [TearDown]
        public void TearDown()
        {
            if (_table != null) Object.DestroyImmediate(_table);
        }

        [Test]
        public void RollAtZero_LandsOnTheFirstEntry()
        {
            _table = Build(1f, 1f, 1f);
            Assert.AreEqual(0, _table.Roll(0f));
        }

        [Test]
        public void RollAtOne_LandsOnTheLastEntry()
        {
            // The float boundary: accumulating weights can fall a hair short of the total, and
            // returning -1 there would mean fruit occasionally failing to spawn for no reason.
            _table = Build(1f, 1f, 1f);
            Assert.AreEqual(2, _table.Roll(1f));
        }

        [Test]
        public void RollIsClampedRatherThanOutOfRange()
        {
            _table = Build(1f, 1f);

            Assert.AreEqual(0, _table.Roll(-5f));
            Assert.AreEqual(1, _table.Roll(5f));
        }

        [Test]
        public void WeightsDecideTheBoundaries()
        {
            // 70 / 30: everything below 0.7 is the first entry, everything above is the second.
            _table = Build(70f, 30f);

            Assert.AreEqual(0, _table.Roll(0.69f));
            Assert.AreEqual(1, _table.Roll(0.71f));
        }

        [Test]
        public void AZeroWeightEntry_IsNeverRolled()
        {
            _table = Build(1f, 0f, 1f);

            for (int i = 0; i <= 100; i++)
                Assert.AreNotEqual(1, _table.Roll(i / 100f), $"roll {i / 100f} landed on a zero-weight entry");
        }

        [Test]
        public void AnEmptyTable_ReportsMinusOneRatherThanGuessing()
        {
            _table = Build();
            Assert.AreEqual(-1, _table.Roll(0.5f));
        }

        [Test]
        public void ATableOfAllZeroWeights_ReportsMinusOne()
        {
            _table = Build(0f, 0f);
            Assert.AreEqual(-1, _table.Roll(0.5f));
        }

        [Test]
        public void ChanceOf_MatchesTheWeights()
        {
            _table = Build(35f, 15f);

            Assert.AreEqual(70f, _table.ChanceOf(0), 0.001f);
            Assert.AreEqual(30f, _table.ChanceOf(1), 0.001f);
        }

        [Test]
        public void ChanceOf_OutOfRangeIsZeroRatherThanAnError()
        {
            _table = Build(1f);

            Assert.AreEqual(0f, _table.ChanceOf(-1));
            Assert.AreEqual(0f, _table.ChanceOf(99));
        }

        [Test]
        public void Validate_CatchesTheTableThatCanNeverSpawnAnything()
        {
            _table = Build();
            Assert.IsNotNull(_table.Validate(), "an empty table must not pass validation");

            var allZero = Build(0f, 0f);
            Assert.IsNotNull(allZero.Validate(), "a table of zero weights must not pass validation");
            Object.DestroyImmediate(allZero);
        }
    }
}
