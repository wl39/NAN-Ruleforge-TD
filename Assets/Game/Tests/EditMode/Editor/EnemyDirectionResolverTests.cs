using NUnit.Framework;
using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class EnemyDirectionResolverTests
    {
        [TestCase(0f, -1f, EnemyFacingDirection.Down)]
        [TestCase(0f, 1f, EnemyFacingDirection.Up)]
        [TestCase(-1f, 0f, EnemyFacingDirection.SideLeft)]
        [TestCase(1f, 0f, EnemyFacingDirection.SideRight)]
        [TestCase(2f, 1f, EnemyFacingDirection.SideRight)]
        [TestCase(1f, 2f, EnemyFacingDirection.Up)]
        public void Resolve_SelectsAssetDirectionFromMovement(
            float x,
            float y,
            EnemyFacingDirection expected)
        {
            EnemyFacingDirection actual = EnemyDirectionResolver.Resolve(
                new Vector2(x, y),
                EnemyFacingDirection.Down);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_PreservesFacingWhenMovementStops()
        {
            EnemyFacingDirection actual = EnemyDirectionResolver.Resolve(
                Vector2.zero,
                EnemyFacingDirection.SideLeft);

            Assert.That(actual, Is.EqualTo(EnemyFacingDirection.SideLeft));
        }
    }
}
