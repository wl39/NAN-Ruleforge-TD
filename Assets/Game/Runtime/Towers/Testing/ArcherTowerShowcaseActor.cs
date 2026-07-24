using System;
using System.Collections.Generic;
using RuleforgeTD.Enemies;
using RuleforgeTD.Towers.Archer;
using UnityEngine;

namespace RuleforgeTD.Towers.Testing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArcherTowerView))]
    public sealed class ArcherTowerShowcaseActor : MonoBehaviour
    {
        private const int MaxPooledProjectiles = 8;
        public const float DefaultProjectileSpeed = 8.5f;

        private static readonly float[] DefaultVolleyIntervals =
        {
            0f,
            3f,
            2.6f,
            2.25f,
            1.95f,
            1.65f,
            1.35f,
            1.05f
        };

        [SerializeField] private ArcherTowerView towerView;
        [SerializeField] private Sprite[] arrowDirectionBank = Array.Empty<Sprite>();
        [SerializeField, Min(0f)] private float initialUpgradeDelay;
        [SerializeField, Min(0f)] private float initialVolleyDelay = 1.2f;
        [SerializeField, Min(2f)] private float upgradeInterval = 11f;
        [SerializeField, Min(0.5f)] private float volleyInterval = 3.1f;
        [SerializeField, Min(0.1f)] private float projectileSpeed =
            DefaultProjectileSpeed;
        [SerializeField, Min(0.1f)] private float projectileLifetime = 2.4f;
        [SerializeField, Min(0.25f)] private float projectileScale = 1.45f;
        [SerializeField, Min(0.05f)] private float targetScanInterval = 0.12f;
        [SerializeField, Min(0.5f)] private float targetRange = 8f;
        [SerializeField, Min(0.02f)] private float projectileHitRadius = 0.2f;
        [SerializeField] private EnemyHealth[] targets =
            Array.Empty<EnemyHealth>();
        [SerializeField] private bool automaticPlayback = true;

        private readonly List<ArcherProjectileView> projectilePool =
            new List<ArcherProjectileView>(MaxPooledProjectiles);
        private EnemyHealth currentTarget;
        private float upgradeTimer;
        private float volleyTimer;
        private float targetScanTimer;
        private bool subscribed;
        private int successfulHitCount;

        public int ArrowDirectionCount =>
            arrowDirectionBank == null ? 0 : arrowDirectionBank.Length;
        public int PooledProjectileCount => projectilePool.Count;
        public int TargetCount => targets == null ? 0 : targets.Length;
        public EnemyHealth CurrentTarget => currentTarget;
        public int SuccessfulHitCount => successfulHitCount;
        public bool AutomaticPlayback => automaticPlayback;
        public float VolleyInterval => volleyInterval;
        public float ProjectileSpeed => projectileSpeed;

        private void Awake()
        {
            if (towerView == null)
            {
                towerView = GetComponent<ArcherTowerView>();
            }

            EnsureTargets();
            ResetTimers();
        }

        private void OnEnable()
        {
            Subscribe();
            EnsureTargets();
            ResetTimers();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (towerView == null || !automaticPlayback)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            UpdateTargeting(deltaTime);
            upgradeTimer -= deltaTime;
            volleyTimer -= deltaTime;

            if (upgradeTimer <= 0f)
            {
                towerView.PlayUpgrade();
                upgradeTimer += upgradeInterval;
            }

            if (volleyTimer <= 0f)
            {
                EnemyHealth target = AcquireNearestTarget();
                if (!towerView.IsUpgrading && target != null)
                {
                    towerView.AimAt(
                        ArcherProjectileView.GetTargetAimPoint(target));
                    towerView.PlayVolley();
                }

                volleyTimer += volleyInterval;
            }
        }

        public void Configure(
            ArcherTowerView view,
            Sprite[] directionalArrowSprites,
            float firstUpgradeDelay,
            float firstVolleyDelay,
            float repeatedUpgradeInterval,
            float repeatedVolleyInterval,
            float projectileTravelSpeed)
        {
            Unsubscribe();
            towerView = view;
            arrowDirectionBank = directionalArrowSprites ?? Array.Empty<Sprite>();
            initialUpgradeDelay = Mathf.Max(0f, firstUpgradeDelay);
            initialVolleyDelay = Mathf.Max(0f, firstVolleyDelay);
            upgradeInterval = Mathf.Max(2f, repeatedUpgradeInterval);
            volleyInterval = Mathf.Max(0.5f, repeatedVolleyInterval);
            projectileSpeed = Mathf.Max(0.1f, projectileTravelSpeed);
            Subscribe();
            ResetTimers();
        }

        public static float GetDefaultVolleyInterval(int towerLevel)
        {
            int clampedLevel = Mathf.Clamp(towerLevel, 1, 7);
            return DefaultVolleyIntervals[clampedLevel];
        }

        public void SetAutomaticPlayback(bool enabled)
        {
            automaticPlayback = enabled;
        }

        public void RefreshTargets()
        {
            targets = FindObjectsOfType<EnemyHealth>();
            Array.Sort(targets, CompareTargets);
            targetScanTimer = 0f;
            AcquireNearestTarget();
        }

        public void SetTargets(EnemyHealth[] combatTargets)
        {
            if (combatTargets == null || combatTargets.Length == 0)
            {
                targets = Array.Empty<EnemyHealth>();
            }
            else
            {
                targets = new EnemyHealth[combatTargets.Length];
                Array.Copy(combatTargets, targets, combatTargets.Length);
            }

            targetScanTimer = 0f;
            AcquireNearestTarget();
        }

        public EnemyHealth AimAtNearestTarget()
        {
            EnemyHealth target = AcquireNearestTarget();
            if (target != null && towerView != null)
            {
                towerView.AimAt(
                    ArcherProjectileView.GetTargetAimPoint(target));
            }

            return target;
        }

        private void Subscribe()
        {
            if (subscribed || towerView == null)
            {
                return;
            }

            towerView.ArrowRequested += HandleArrowRequested;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || towerView == null)
            {
                return;
            }

            towerView.ArrowRequested -= HandleArrowRequested;
            subscribed = false;
        }

        private void ResetTimers()
        {
            upgradeTimer = initialUpgradeDelay;
            volleyTimer = initialVolleyDelay;
            targetScanTimer = 0f;
        }

        private void HandleArrowRequested(
            Vector3 origin,
            Vector2 direction,
            int unitTier)
        {
            if (arrowDirectionBank == null || arrowDirectionBank.Length == 0)
            {
                return;
            }

            EnemyHealth target = IsValidTarget(currentTarget)
                ? currentTarget
                : AcquireNearestTarget();
            if (target == null)
            {
                return;
            }

            ArcherProjectileView projectile = GetAvailableProjectile();
            if (projectile == null)
            {
                return;
            }

            projectile.Launch(
                arrowDirectionBank,
                origin,
                target,
                projectileSpeed,
                projectileLifetime,
                projectileScale,
                projectileHitRadius,
                Mathf.Max(1, unitTier));
        }

        private ArcherProjectileView GetAvailableProjectile()
        {
            for (int i = 0; i < projectilePool.Count; i++)
            {
                if (!projectilePool[i].IsActive)
                {
                    return projectilePool[i];
                }
            }

            if (projectilePool.Count >= MaxPooledProjectiles)
            {
                return null;
            }

            var projectileObject = new GameObject(
                "Archer Arrow (Pooled) L" + towerView.Level);
            SpriteRenderer renderer = projectileObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 40;
            ArcherProjectileView projectile =
                projectileObject.AddComponent<ArcherProjectileView>();
            projectile.Configure(renderer);
            projectile.Hit += HandleProjectileHit;
            projectileObject.SetActive(false);
            projectilePool.Add(projectile);
            return projectile;
        }

        private void UpdateTargeting(float deltaTime)
        {
            targetScanTimer -= deltaTime;
            if (targetScanTimer <= 0f || !IsValidTarget(currentTarget))
            {
                AcquireNearestTarget();
                targetScanTimer = targetScanInterval;
            }

            if (currentTarget != null)
            {
                towerView.AimAt(
                    ArcherProjectileView.GetTargetAimPoint(currentTarget));
            }
        }

        private void EnsureTargets()
        {
            if (targets == null || targets.Length == 0)
            {
                RefreshTargets();
                return;
            }

            AcquireNearestTarget();
        }

        private EnemyHealth AcquireNearestTarget()
        {
            if (towerView == null)
            {
                currentTarget = null;
                return null;
            }

            float maximumDistanceSquared = targetRange * targetRange;
            float bestDistanceSquared = maximumDistanceSquared;
            EnemyHealth bestTarget = null;
            Vector3 towerPosition = towerView.transform.position;
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    EnemyHealth candidate = targets[i];
                    if (!IsValidTarget(candidate))
                    {
                        continue;
                    }

                    float distanceSquared =
                        (candidate.transform.position - towerPosition).sqrMagnitude;
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        bestTarget = candidate;
                    }
                }
            }

            currentTarget = bestTarget;
            return currentTarget;
        }

        private static bool IsValidTarget(EnemyHealth candidate)
        {
            return candidate != null &&
                   candidate.gameObject.activeInHierarchy &&
                   !candidate.IsDead;
        }

        private static int CompareTargets(EnemyHealth left, EnemyHealth right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return string.CompareOrdinal(left.name, right.name);
        }

        private void HandleProjectileHit(
            ArcherProjectileView projectile,
            EnemyHealth hitEnemy)
        {
            successfulHitCount++;
            if (hitEnemy == currentTarget && hitEnemy.IsDead)
            {
                currentTarget = null;
                targetScanTimer = 0f;
            }
        }
    }
}
