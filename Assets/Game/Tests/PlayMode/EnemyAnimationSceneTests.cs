using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.Enemies;
using RuleforgeTD.Enemies.Testing;
using RuleforgeTD.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class EnemyAnimationSceneTests
    {
        [UnityTest]
        public IEnumerator TestScene_MovesAnimatesAndConfiguresHealthForAllEnemyPrefabs()
        {
            SceneManager.LoadScene("EnemyAnimationTest", LoadSceneMode.Single);
            yield return null;

            EnemyTestActor[] actors = Object.FindObjectsOfType<EnemyTestActor>();
            Assert.That(actors, Has.Length.EqualTo(4));

            var expectedHealth = new Dictionary<string, int>
            {
                { "Bee", 5 },
                { "Dog", 8 },
                { "Goblin", 30 },
                { "Slime", 10 }
            };

            var initialPositions = new Vector3[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                initialPositions[i] = actors[i].transform.position;
                Assert.That(actors[i].GetComponent<Animator>().runtimeAnimatorController, Is.Not.Null);
                Assert.That(actors[i].GetComponent<SpriteRenderer>().sprite, Is.Not.Null);

                EnemyHealth health = actors[i].GetComponent<EnemyHealth>();
                Assert.That(health, Is.Not.Null);
                Assert.That(health.MaxHealth, Is.EqualTo(expectedHealth[actors[i].name]));
                Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));

                EnemyHealthBarView healthBar = actors[i].GetComponent<EnemyHealthBarView>();
                Assert.That(healthBar, Is.Not.Null);
                Assert.That(
                    healthBar.DisplayedValue,
                    Is.EqualTo(health.MaxHealth + " / " + health.MaxHealth));
            }

            yield return new WaitForSecondsRealtime(0.35f);

            for (int i = 0; i < actors.Length; i++)
            {
                float movedDistance = Vector3.Distance(initialPositions[i], actors[i].transform.position);
                Assert.That(movedDistance, Is.GreaterThan(0.1f));

                Animator animator = actors[i].GetComponent<Animator>();
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("WalkSide"), Is.True);

                SpriteRenderer renderer = actors[i].GetComponent<SpriteRenderer>();
                Assert.That(renderer.flipX, Is.True, "Rightward movement must mirror left-facing side art.");
            }

            GameObject bee = GameObject.Find("Bee");
            GameObject dog = GameObject.Find("Dog");
            GameObject slime = GameObject.Find("Slime");

            DirectionalEnemyAnimator beeAnimator = bee.GetComponent<DirectionalEnemyAnimator>();
            DirectionalEnemyAnimator dogAnimator = dog.GetComponent<DirectionalEnemyAnimator>();
            DirectionalEnemyAnimator slimeAnimator = slime.GetComponent<DirectionalEnemyAnimator>();
            Assert.That(beeAnimator.Supports(EnemyAnimationBehaviour.Death), Is.True);
            Assert.That(beeAnimator.Supports(EnemyAnimationBehaviour.Attack), Is.False);
            Assert.That(dogAnimator.Supports(EnemyAnimationBehaviour.Attack), Is.True);
            Assert.That(slimeAnimator.Supports(EnemyAnimationBehaviour.Special), Is.True);
            Assert.That(slimeAnimator.Supports(EnemyAnimationBehaviour.Walk2), Is.True);
            Assert.That(slimeAnimator.Supports(EnemyAnimationBehaviour.Death2), Is.True);

            dogAnimator.PlayBehaviour(EnemyAnimationBehaviour.Attack);
            slimeAnimator.PlayBehaviour(EnemyAnimationBehaviour.Special);
            yield return null;
            Assert.That(
                dog.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("AttackSide"),
                Is.True);
            Assert.That(
                slime.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("SpecialSide"),
                Is.True);

            EnemyHealth beeHealth = bee.GetComponent<EnemyHealth>();
            beeHealth.Kill();
            yield return null;
            Assert.That(beeHealth.CurrentHealth, Is.Zero);
            Assert.That(beeHealth.IsDead, Is.True);
            Assert.That(bee.GetComponent<EnemyHealthBarView>().DisplayedValue, Is.EqualTo("0 / 5"));
            Assert.That(
                bee.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("DeathSide"),
                Is.True);
        }
    }
}
