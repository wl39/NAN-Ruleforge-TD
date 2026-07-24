using System;
using RuleforgeTD.Enemies;
using UnityEngine;

namespace RuleforgeTD.Towers.Testing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class ArcherEnemyCardStatusView : MonoBehaviour
    {
        private const int MilliPerHealth = 1000;
        private const float BadgeY = 0.9f;
        private const float BadgeX = 0.58f;

        private static readonly Color BurnColor =
            new Color(1f, 0.36f, 0.08f, 1f);
        private static readonly Color PoisonColor =
            new Color(0.58f, 0.2f, 0.75f, 1f);

        [SerializeField] private EnemyHealth health;
        [SerializeField] private SpriteRenderer enemyRenderer;
        [SerializeField] private Sprite pixelSprite;

        private readonly StatusRuntime burn = new StatusRuntime();
        private readonly StatusRuntime poison = new StatusRuntime();

        [SerializeField] private SpriteRenderer burnBadgeRenderer;
        [SerializeField] private SpriteRenderer poisonBadgeRenderer;
        [SerializeField] private TextMesh burnBadgeText;
        [SerializeField] private TextMesh poisonBadgeText;
        private float fixedTickAccumulator;
        private int tickRate = 30;
        private int directPendingMilli;
        private EnemyHealth subscribedHealth;

        public int BurnStacks => burn.Stacks;
        public int BurnRemainingTicks => burn.RemainingTicks;
        public int BurnIntervalTicks => burn.IntervalTicks;
        public int BurnPendingMilli => burn.PendingMilli;
        public int BurnPendingDamageMilli => burn.PendingMilli;
        public int BurnTicksUntilDamage => burn.TicksUntilDamage;
        public int BurnTickCount => burn.DamageTickCount;
        public int PoisonStacks => poison.Stacks;
        public int PoisonRemainingTicks => poison.RemainingTicks;
        public int PoisonIntervalTicks => poison.IntervalTicks;
        public int PoisonPendingMilli => poison.PendingMilli;
        public int PoisonPendingDamageMilli => poison.PendingMilli;
        public int PoisonTicksUntilDamage => poison.TicksUntilDamage;
        public int PoisonTickCount => poison.DamageTickCount;
        public int DirectPendingMilli => directPendingMilli;
        public int TickRate => tickRate;
        public bool HasBurn => burn.IsActive;
        public bool HasPoison => poison.IsActive;
        public bool IsBurning => burn.IsActive;
        public bool IsPoisoned => poison.IsActive;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<EnemyHealth>();
            }

            SubscribeToHealth();
            EnsureBadges();
            RefreshBadges(health == null || health.IsDead);
        }

        private void OnDestroy()
        {
            UnsubscribeFromHealth();
        }

        private void Update()
        {
            if (health == null || health.IsDead)
            {
                RefreshBadges(true);
                return;
            }

            fixedTickAccumulator = Mathf.Min(
                8f,
                fixedTickAccumulator + Time.deltaTime * tickRate);
            int elapsedTicks = Mathf.FloorToInt(fixedTickAccumulator);
            if (elapsedTicks > 0)
            {
                fixedTickAccumulator -= elapsedTicks;
                SimulateTicksForTesting(elapsedTicks);
            }

            RefreshBadges(false);
        }

        public void Configure(
            EnemyHealth targetHealth,
            SpriteRenderer targetEnemyRenderer,
            Sprite effectPixel)
        {
            UnsubscribeFromHealth();
            health = targetHealth != null
                ? targetHealth
                : GetComponent<EnemyHealth>();
            enemyRenderer = targetEnemyRenderer;
            pixelSprite = effectPixel;
            SubscribeToHealth();
            EnsureBadges();
            RefreshBadges(health == null || health.IsDead);
        }

        public void ApplyBurn(ArcherShowcaseStatusDefinition definition)
        {
            if (!CanApplyStatus())
            {
                return;
            }

            tickRate = Mathf.Max(1, definition.TickRate);
            burn.Apply(definition);
            RefreshBadges(false);
        }

        public void ApplyPoison(ArcherShowcaseStatusDefinition definition)
        {
            if (!CanApplyStatus())
            {
                return;
            }

            tickRate = Mathf.Max(1, definition.TickRate);
            poison.Apply(definition);
            RefreshBadges(false);
        }

        public int ApplyDirectDamageMilli(int damageMilli)
        {
            if (health == null || health.IsDead || damageMilli <= 0)
            {
                return 0;
            }

            directPendingMilli = SaturatingAdd(
                directPendingMilli,
                damageMilli);
            return FlushPendingDamage(ref directPendingMilli);
        }

        public void ClearAll()
        {
            burn.Clear();
            poison.Clear();
            directPendingMilli = 0;
            fixedTickAccumulator = 0f;
            RefreshBadges(true);
        }

        public void SimulateTicksForTesting(int tickCount)
        {
            if (tickCount <= 0 || health == null || health.IsDead)
            {
                return;
            }

            for (int i = 0; i < tickCount; i++)
            {
                if (health.IsDead)
                {
                    break;
                }

                ProcessStatusTick(burn);
                if (health.IsDead)
                {
                    break;
                }

                ProcessStatusTick(poison);
            }

            RefreshBadges(health.IsDead);
        }

        private bool CanApplyStatus()
        {
            return health != null && !health.IsDead;
        }

        private void ProcessStatusTick(StatusRuntime status)
        {
            if (!status.IsActive)
            {
                return;
            }

            status.RemainingTicks--;
            status.TicksUntilDamage--;
            if (status.TicksUntilDamage <= 0)
            {
                int tickDamageMilli = SaturatingMultiply(
                    status.IntensityMilli,
                    status.Stacks);
                status.PendingMilli = SaturatingAdd(
                    status.PendingMilli,
                    tickDamageMilli);
                status.DamageTickCount++;
                FlushPendingDamage(ref status.PendingMilli);
                status.TicksUntilDamage += status.IntervalTicks;
            }

            if (status.RemainingTicks <= 0)
            {
                status.Expire();
            }
        }

        private int FlushPendingDamage(ref int pendingMilli)
        {
            if (health == null || health.IsDead ||
                pendingMilli < MilliPerHealth)
            {
                return 0;
            }

            int wholeDamage = pendingMilli / MilliPerHealth;
            pendingMilli -= wholeDamage * MilliPerHealth;
            return health.TakeDamage(wholeDamage);
        }

        private void EnsureBadges()
        {
            if (pixelSprite == null)
            {
                return;
            }

            int sortingLayerId =
                enemyRenderer == null ? 0 : enemyRenderer.sortingLayerID;
            const int backgroundOrder = 32;
            const int textOrder = 33;

            if (burnBadgeRenderer == null)
            {
                CreateBadge(
                    "Burn Card Badge",
                    -BadgeX,
                    "B",
                    sortingLayerId,
                    backgroundOrder,
                    textOrder,
                    out burnBadgeRenderer,
                    out burnBadgeText);
            }
            else
            {
                burnBadgeRenderer.sprite = pixelSprite;
            }

            if (poisonBadgeRenderer == null)
            {
                CreateBadge(
                    "Poison Card Badge",
                    BadgeX,
                    "P",
                    sortingLayerId,
                    backgroundOrder,
                    textOrder,
                    out poisonBadgeRenderer,
                    out poisonBadgeText);
            }
            else
            {
                poisonBadgeRenderer.sprite = pixelSprite;
            }
        }

        private void CreateBadge(
            string objectName,
            float localX,
            string label,
            int sortingLayerId,
            int backgroundOrder,
            int textOrder,
            out SpriteRenderer badgeRenderer,
            out TextMesh badgeText)
        {
            var badgeObject = new GameObject(objectName);
            badgeObject.transform.SetParent(transform, false);
            badgeObject.transform.localPosition =
                new Vector3(localX, BadgeY, -0.03f);
            badgeObject.transform.localScale =
                new Vector3(0.32f, 0.23f, 1f);

            badgeRenderer = badgeObject.AddComponent<SpriteRenderer>();
            badgeRenderer.sprite = pixelSprite;
            badgeRenderer.sortingLayerID = sortingLayerId;
            badgeRenderer.sortingOrder = backgroundOrder;

            var textObject = new GameObject("Label");
            textObject.transform.SetParent(badgeObject.transform, false);
            textObject.transform.localPosition =
                new Vector3(0f, -0.015f, -0.01f);
            textObject.transform.localScale =
                new Vector3(3.125f, 4.348f, 1f);

            badgeText = textObject.AddComponent<TextMesh>();
            badgeText.text = label;
            badgeText.anchor = TextAnchor.MiddleCenter;
            badgeText.alignment = TextAlignment.Center;
            badgeText.characterSize = 0.085f;
            badgeText.fontSize = 44;
            badgeText.color = Color.white;

            Font font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font != null)
            {
                badgeText.font = font;
                MeshRenderer textRenderer =
                    badgeText.GetComponent<MeshRenderer>();
                textRenderer.sharedMaterial = font.material;
                textRenderer.sortingLayerID = sortingLayerId;
                textRenderer.sortingOrder = textOrder;
            }
        }

        private void RefreshBadges(bool forceInactive)
        {
            float pulse = 0.62f +
                          Mathf.PingPong(Time.unscaledTime * 2.8f, 0.38f);
            RefreshBadge(
                burnBadgeRenderer,
                burnBadgeText,
                "B",
                burn,
                BurnColor,
                pulse,
                forceInactive);
            RefreshBadge(
                poisonBadgeRenderer,
                poisonBadgeText,
                "P",
                poison,
                PoisonColor,
                pulse,
                forceInactive);
        }

        private static void RefreshBadge(
            SpriteRenderer badgeRenderer,
            TextMesh badgeText,
            string label,
            StatusRuntime status,
            Color activeColor,
            float pulse,
            bool forceInactive)
        {
            bool active = !forceInactive && status.IsActive;
            if (badgeRenderer != null)
            {
                badgeRenderer.enabled = active;
                badgeRenderer.color = active
                    ? Color.Lerp(
                        activeColor * 0.68f,
                        activeColor,
                        pulse)
                    : Color.clear;
            }

            if (badgeText != null)
            {
                badgeText.text = active && status.Stacks > 1
                    ? label + "×" + status.Stacks
                    : label;
                badgeText.color = active
                    ? label == "P"
                        ? new Color(0.7f, 1f, 0.32f, 1f)
                        : Color.white
                    : Color.clear;
                badgeText.gameObject.SetActive(active);
            }
        }

        private void SubscribeToHealth()
        {
            if (health == null || subscribedHealth == health)
            {
                return;
            }

            subscribedHealth = health;
            subscribedHealth.Died += HandleDied;
        }

        private void UnsubscribeFromHealth()
        {
            if (subscribedHealth == null)
            {
                return;
            }

            subscribedHealth.Died -= HandleDied;
            subscribedHealth = null;
        }

        private void HandleDied()
        {
            RefreshBadges(true);
        }

        private static int SaturatingAdd(int left, int right)
        {
            long result = (long)left + right;
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private static int SaturatingMultiply(int left, int right)
        {
            long result = (long)left * right;
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private sealed class StatusRuntime
        {
            public int Stacks;
            public int RemainingTicks;
            public int IntervalTicks;
            public int TicksUntilDamage;
            public int IntensityMilli;
            public int MaxStacks;
            public int PendingMilli;
            public int DamageTickCount;

            public bool IsActive =>
                Stacks > 0 && RemainingTicks > 0;

            public void Apply(ArcherShowcaseStatusDefinition definition)
            {
                int durationTicks = Mathf.Max(1, definition.DurationTicks);
                int intervalTicks = Mathf.Max(1, definition.IntervalTicks);
                int requestedMaxStacks =
                    Mathf.Max(1, definition.MaxStacks);

                if (!IsActive)
                {
                    Stacks = 1;
                    RemainingTicks = durationTicks;
                    IntervalTicks = intervalTicks;
                    TicksUntilDamage = intervalTicks;
                    IntensityMilli =
                        Mathf.Max(0, definition.IntensityMilli);
                    MaxStacks = requestedMaxStacks;
                    DamageTickCount = 0;
                    return;
                }

                MaxStacks = Mathf.Max(MaxStacks, requestedMaxStacks);
                Stacks = Mathf.Min(MaxStacks, Stacks + 1);
                RemainingTicks = Mathf.Max(
                    RemainingTicks,
                    durationTicks);
                IntensityMilli = Mathf.Max(
                    IntensityMilli,
                    definition.IntensityMilli);
                if (intervalTicks < IntervalTicks)
                {
                    IntervalTicks = intervalTicks;
                    TicksUntilDamage = Mathf.Min(
                        TicksUntilDamage,
                        intervalTicks);
                }
            }

            public void Expire()
            {
                Stacks = 0;
                RemainingTicks = 0;
                IntervalTicks = 0;
                TicksUntilDamage = 0;
                IntensityMilli = 0;
                MaxStacks = 0;
            }

            public void Clear()
            {
                Expire();
                PendingMilli = 0;
                DamageTickCount = 0;
            }
        }
    }
}
