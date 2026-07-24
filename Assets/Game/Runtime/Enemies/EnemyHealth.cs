using System;
using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DirectionalEnemyAnimator))]
    public sealed class EnemyHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 1;
        [SerializeField] private DirectionalEnemyAnimator directionalAnimator;

        private int currentHealth;
        private bool initialized;

        public event Action<int, int> HealthChanged;
        public event Action Died;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => initialized ? currentHealth : maxHealth;
        public bool IsDead => initialized && currentHealth <= 0;

        private void Awake()
        {
            if (directionalAnimator == null)
            {
                directionalAnimator = GetComponent<DirectionalEnemyAnimator>();
            }

            currentHealth = maxHealth;
            initialized = true;
        }

        public void Configure(int health, DirectionalEnemyAnimator targetAnimator)
        {
            maxHealth = Mathf.Max(1, health);
            currentHealth = maxHealth;
            initialized = true;
            directionalAnimator = targetAnimator;
        }

        public int TakeDamage(int amount)
        {
            if (amount <= 0 || IsDead)
            {
                return 0;
            }

            int previousHealth = currentHealth;
            currentHealth = Mathf.Max(0, currentHealth - amount);
            HealthChanged?.Invoke(currentHealth, maxHealth);

            if (currentHealth == 0)
            {
                PlayDeath(EnemyAnimationBehaviour.Death);
                Died?.Invoke();
            }

            return previousHealth - currentHealth;
        }

        public void Kill(EnemyAnimationBehaviour deathBehaviour = EnemyAnimationBehaviour.Death)
        {
            if (IsDead)
            {
                return;
            }

            currentHealth = 0;
            initialized = true;
            HealthChanged?.Invoke(currentHealth, maxHealth);
            PlayDeath(deathBehaviour);
            Died?.Invoke();
        }

        public void ResetHealth()
        {
            currentHealth = maxHealth;
            initialized = true;
            HealthChanged?.Invoke(currentHealth, maxHealth);

            if (directionalAnimator != null)
            {
                directionalAnimator.PlayBehaviour(EnemyAnimationBehaviour.Walk);
            }
        }

        private void PlayDeath(EnemyAnimationBehaviour requestedBehaviour)
        {
            if (directionalAnimator == null)
            {
                return;
            }

            if (!directionalAnimator.PlayBehaviour(requestedBehaviour))
            {
                directionalAnimator.PlayBehaviour(EnemyAnimationBehaviour.Death);
            }
        }
    }
}
