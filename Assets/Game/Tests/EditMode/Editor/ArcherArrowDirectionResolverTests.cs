using NUnit.Framework;
using RuleforgeTD.Towers.Archer;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class ArcherArrowDirectionResolverTests
    {
        [TestCase(0f, 1f, 13, 0, false, false)]
        [TestCase(1f, 0f, 13, 12, false, false)]
        [TestCase(1f, 1f, 13, 6, false, false)]
        [TestCase(-1f, 1f, 13, 6, true, false)]
        [TestCase(1f, -1f, 13, 6, false, true)]
        [TestCase(-1f, -1f, 13, 6, true, true)]
        [TestCase(1f, 1f, 9, 4, false, false)]
        [TestCase(1f, 1f, 5, 2, false, false)]
        public void Resolve_SelectsNearestFirstQuadrantSpriteAndMirrors(
            float x,
            float y,
            int bankSize,
            int expectedIndex,
            bool expectedFlipX,
            bool expectedFlipY)
        {
            ArcherArrowVisual visual = ArcherArrowDirectionResolver.Resolve(
                new Vector2(x, y),
                bankSize);

            Assert.That(visual.SpriteIndex, Is.EqualTo(expectedIndex));
            Assert.That(visual.FlipX, Is.EqualTo(expectedFlipX));
            Assert.That(visual.FlipY, Is.EqualTo(expectedFlipY));
        }

        [TestCase(1, true, 1, 1)]
        [TestCase(2, true, 1, 2)]
        [TestCase(3, true, 2, 2)]
        [TestCase(4, false, 2, 2)]
        [TestCase(5, true, 2, 2)]
        [TestCase(6, true, 3, 3)]
        [TestCase(7, false, 3, 3)]
        public void TowerPresentationDefaults_MapRoofAndUnitTier(
            int level,
            bool expectedOpenRoof,
            int expectedUnitTier,
            int expectedArcherCount)
        {
            Assert.That(
                ArcherTowerView.LevelHasOpenRoof(level),
                Is.EqualTo(expectedOpenRoof));
            Assert.That(
                ArcherTowerView.GetDefaultUnitTier(level),
                Is.EqualTo(expectedUnitTier));
            Assert.That(
                ArcherTowerView.GetDefaultArcherCount(level),
                Is.EqualTo(expectedArcherCount));
        }
    }
}
