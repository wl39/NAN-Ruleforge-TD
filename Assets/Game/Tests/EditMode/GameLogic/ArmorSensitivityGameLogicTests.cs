using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class ArmorSensitivityGameLogicTests
    {
        private const string ContentAssetPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        private CompiledRunDefinition run;

        [SetUp]
        public void SetUp()
        {
            TextAsset asset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ContentAssetPath);
            Assert.That(asset, Is.Not.Null);
            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(asset.text);
            CompiledContent content = EffectContentCompiler.Compile(
                source,
                GameSimulation.IsEffectOperationSupported);
            run = content.Run;
        }

        [Test]
        public void Armor42_SeparatesDirectBurnAndAreaDamageRoles()
        {
            const long baseDamage = 100000;
            long direct = DamageArmorMitigation.Apply(
                baseDamage,
                42,
                0,
                DamageKind.Physical,
                EventTags.SingleTarget,
                run);
            long burn = DamageArmorMitigation.Apply(
                baseDamage,
                42,
                0,
                DamageKind.Fire,
                EventTags.DamageOverTime,
                run);
            long area = DamageArmorMitigation.Apply(
                baseDamage,
                42,
                0,
                DamageKind.Explosion,
                EventTags.Area,
                run);

            Assert.That(direct, Is.EqualTo(70422));
            Assert.That(burn, Is.EqualTo(37313));
            Assert.That(area, Is.EqualTo(32258));
            Assert.That(area * 2, Is.LessThan(direct));
            Assert.That(area, Is.LessThan(burn));
            Assert.That(burn, Is.LessThan(direct));
        }

        [Test]
        public void OverlappingAreaAndBurn_UsesStrongestSensitivityOnce()
        {
            long areaBurn = DamageArmorMitigation.Apply(
                100000,
                42,
                0,
                DamageKind.Fire,
                EventTags.Area | EventTags.DamageOverTime,
                run);
            long areaOnly = DamageArmorMitigation.Apply(
                100000,
                42,
                0,
                DamageKind.Physical,
                EventTags.Area,
                run);

            Assert.That(areaBurn, Is.EqualTo(areaOnly));
            Assert.That(areaBurn, Is.EqualTo(32258));
        }

        [Test]
        public void PoisonArmorIgnore_RemainsAHighArmorCounter()
        {
            long poison = DamageArmorMitigation.Apply(
                100000,
                42,
                5000,
                DamageKind.Poison,
                EventTags.SingleTarget | EventTags.DamageOverTime,
                run);
            long burn = DamageArmorMitigation.Apply(
                100000,
                42,
                0,
                DamageKind.Fire,
                EventTags.DamageOverTime,
                run);

            Assert.That(poison, Is.EqualTo(82644));
            Assert.That(poison, Is.GreaterThan(burn * 2));
        }

        [Test]
        public void BurnStacks_AreCombinedBeforeArmorMitigation()
        {
            long oneStack = DamageArmorMitigation.Apply(
                500,
                42,
                0,
                DamageKind.Fire,
                EventTags.DamageOverTime,
                run);
            long tenStacks = DamageArmorMitigation.Apply(
                500 * 10,
                42,
                0,
                DamageKind.Fire,
                EventTags.DamageOverTime,
                run);

            Assert.That(oneStack, Is.EqualTo(186));
            Assert.That(tenStacks, Is.EqualTo(1865));
            Assert.That(tenStacks, Is.GreaterThanOrEqualTo(oneStack * 10));
            Assert.That(tenStacks, Is.LessThan(oneStack * 10 + 10));
        }

        [Test]
        public void ArmorSensitivityValues_AreAuthoredInRunData()
        {
            Assert.That(run.ArmorMitigationScale, Is.EqualTo(100));
            Assert.That(run.AreaArmorSensitivityBps, Is.EqualTo(50000));
            Assert.That(run.BurnArmorSensitivityBps, Is.EqualTo(40000));
        }
    }
}
