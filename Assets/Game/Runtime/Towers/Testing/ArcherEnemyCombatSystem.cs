using System;
using RuleforgeTD.Enemies;
using RuleforgeTD.Enemies.Testing;
using UnityEngine;

namespace RuleforgeTD.Towers.Testing
{
    [DisallowMultipleComponent]
    public sealed class ArcherEnemyCombatSystem : MonoBehaviour
    {
        [SerializeField] private EnemyHealth[] enemies =
            Array.Empty<EnemyHealth>();
        [SerializeField] private Vector3[] routeCenters =
            Array.Empty<Vector3>();
        [SerializeField, Min(0.25f)] private float respawnDelay = 1.8f;

        private float[] respawnTimers = Array.Empty<float>();
        private bool[] waitingForRespawn = Array.Empty<bool>();

        public int EnemyCount => enemies == null ? 0 : enemies.Length;

        public int LivingEnemyCount
        {
            get
            {
                int count = 0;
                if (enemies == null)
                {
                    return count;
                }

                for (int i = 0; i < enemies.Length; i++)
                {
                    if (enemies[i] != null && !enemies[i].IsDead)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private void Awake()
        {
            EnsureRuntimeState();
        }

        private void Update()
        {
            EnsureRuntimeState();
            float deltaTime = Time.deltaTime;
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyHealth enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                if (!waitingForRespawn[i])
                {
                    if (!enemy.IsDead)
                    {
                        continue;
                    }

                    waitingForRespawn[i] = true;
                    respawnTimers[i] = respawnDelay;
                    EnemyTestActor actor = enemy.GetComponent<EnemyTestActor>();
                    if (actor != null)
                    {
                        actor.SetMovementEnabled(false);
                    }
                }

                respawnTimers[i] -= deltaTime;
                if (respawnTimers[i] <= 0f)
                {
                    Respawn(i);
                }
            }
        }

        public void Configure(
            EnemyHealth[] combatEnemies,
            float enemyRespawnDelay)
        {
            enemies = combatEnemies ?? Array.Empty<EnemyHealth>();
            routeCenters = new Vector3[enemies.Length];
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null)
                {
                    routeCenters[i] = enemies[i].transform.position;
                }
            }

            respawnDelay = Mathf.Max(0.25f, enemyRespawnDelay);
            EnsureRuntimeState(true);
        }

        private void Respawn(int index)
        {
            EnemyHealth enemy = enemies[index];
            if (enemy == null)
            {
                waitingForRespawn[index] = false;
                respawnTimers[index] = 0f;
                return;
            }

            enemy.transform.position = routeCenters[index];
            ArcherEnemyCardStatusView cardStatus =
                enemy.GetComponent<ArcherEnemyCardStatusView>();
            if (cardStatus != null)
            {
                cardStatus.ClearAll();
            }

            enemy.ResetHealth();

            EnemyTestActor actor = enemy.GetComponent<EnemyTestActor>();
            if (actor != null)
            {
                actor.InitializeRoute();
                actor.SetMovementEnabled(true);
            }

            waitingForRespawn[index] = false;
            respawnTimers[index] = 0f;
        }

        private void EnsureRuntimeState(bool forceReset = false)
        {
            int count = enemies == null ? 0 : enemies.Length;
            if (!forceReset &&
                respawnTimers != null &&
                waitingForRespawn != null &&
                respawnTimers.Length == count &&
                waitingForRespawn.Length == count)
            {
                return;
            }

            respawnTimers = new float[count];
            waitingForRespawn = new bool[count];
        }
    }
}
