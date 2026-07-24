using System;
using RuleforgeTD.Enemies;
using RuleforgeTD.Towers.Archer;
using UnityEngine;

namespace RuleforgeTD.Towers.Testing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ArcherProjectileView : MonoBehaviour
    {
        private const float TargetAimHeight = 0.35f;

        [SerializeField] private SpriteRenderer spriteRenderer;

        private Sprite[] directionSprites = Array.Empty<Sprite>();
        private EnemyHealth target;
        private Vector2 direction;
        private float speed;
        private float remainingLifetime;
        private float hitRadius;
        private int damageMilli;

        public event Action<ArcherProjectileView> Expired;
        public event Action<ArcherProjectileView, EnemyHealth> Hit;

        public bool IsActive => gameObject.activeSelf;
        public EnemyHealth Target => target;
        public int DamageMilli => damageMilli;

        private void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void Update()
        {
            if (target == null || target.IsDead)
            {
                Expire();
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector2 targetPosition = GetTargetAimPoint(target);
            Vector2 displacement = targetPosition - (Vector2)transform.position;
            float travelDistance = speed * deltaTime;
            if (displacement.sqrMagnitude <= hitRadius * hitRadius ||
                travelDistance >= displacement.magnitude)
            {
                transform.position = targetPosition;
                EnemyHealth hitEnemy = target;
                ArcherEnemyCardStatusView status =
                    hitEnemy.GetComponent<ArcherEnemyCardStatusView>();
                if (status != null)
                {
                    status.ApplyDirectDamageMilli(damageMilli);
                }
                else
                {
                    hitEnemy.TakeDamage(
                        Mathf.Max(1, Mathf.RoundToInt(damageMilli / 1000f)));
                }

                Hit?.Invoke(this, hitEnemy);
                Expire();
                return;
            }

            direction = displacement.normalized;
            ApplyDirectionVisual();
            transform.position += (Vector3)(direction * travelDistance);
            remainingLifetime -= deltaTime;
            if (remainingLifetime <= 0f)
            {
                Expire();
            }
        }

        public void Configure(SpriteRenderer renderer)
        {
            spriteRenderer = renderer;
        }

        public void Launch(
            Sprite[] directionalArrowSprites,
            Vector3 origin,
            EnemyHealth targetEnemy,
            float travelSpeed,
            float lifetime,
            float visualScale,
            float collisionRadius,
            int hitDamageMilli)
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            transform.position = origin;
            transform.localScale = Vector3.one * visualScale;
            directionSprites =
                directionalArrowSprites ?? Array.Empty<Sprite>();
            target = targetEnemy;
            speed = Mathf.Max(0.01f, travelSpeed);
            remainingLifetime = Mathf.Max(0.01f, lifetime);
            hitRadius = Mathf.Max(0.01f, collisionRadius);
            damageMilli = Mathf.Max(1, hitDamageMilli);

            Vector2 displacement = target == null
                ? Vector2.up
                : (Vector2)(GetTargetAimPoint(target) - origin);
            direction = displacement.sqrMagnitude <= 0.000001f
                ? Vector2.up
                : displacement.normalized;
            ApplyDirectionVisual();
            gameObject.SetActive(true);
        }

        public static Vector3 GetTargetAimPoint(EnemyHealth enemy)
        {
            return enemy == null
                ? Vector3.zero
                : enemy.transform.position + Vector3.up * TargetAimHeight;
        }

        private void ApplyDirectionVisual()
        {
            if (spriteRenderer == null ||
                directionSprites == null ||
                directionSprites.Length == 0)
            {
                return;
            }

            ArcherArrowVisual visual = ArcherArrowDirectionResolver.Resolve(
                direction,
                directionSprites.Length);
            spriteRenderer.sprite = directionSprites[visual.SpriteIndex];
            spriteRenderer.flipX = visual.FlipX;
            spriteRenderer.flipY = visual.FlipY;
        }

        private void Expire()
        {
            target = null;
            gameObject.SetActive(false);
            Expired?.Invoke(this);
        }
    }
}
