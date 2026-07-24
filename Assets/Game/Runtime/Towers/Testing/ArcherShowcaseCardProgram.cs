using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.Simulation;
using RuleforgeTD.Towers.Archer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Towers.Testing
{
    public readonly struct ArcherShowcaseStatusDefinition
    {
        public ArcherShowcaseStatusDefinition(
            int intensityMilli,
            int durationTicks,
            int intervalTicks,
            int maxStacks,
            int armorIgnoreBps,
            int tickRate)
        {
            IntensityMilli = Mathf.Max(0, intensityMilli);
            DurationTicks = Mathf.Max(1, durationTicks);
            IntervalTicks = Mathf.Max(1, intervalTicks);
            MaxStacks = Mathf.Max(1, maxStacks);
            ArmorIgnoreBps = Mathf.Clamp(armorIgnoreBps, 0, 10000);
            TickRate = Mathf.Max(1, tickRate);
        }

        public int IntensityMilli { get; }
        public int DurationTicks { get; }
        public int IntervalTicks { get; }
        public int MaxStacks { get; }
        public int ArmorIgnoreBps { get; }
        public int TickRate { get; }
    }

    [DisallowMultipleComponent]
    public sealed class ArcherShowcaseCardProgram : MonoBehaviour
    {
        private const int MaximumSplitBursts = 4;
        private const string SplitCardId = "split";
        private const string BurnCardId = "burn";
        private const string PoisonCardId = "poison";

        private static TextAsset cachedContentAsset;
        private static CompiledContent cachedContent;

        [SerializeField] private TextAsset contentJson;
        [SerializeField] private Sprite effectPixel;
        [SerializeField] private string[] equippedCardIds =
        {
            SplitCardId,
            BurnCardId,
            PoisonCardId
        };

        private readonly List<ArcherSplitBurstView> splitBursts =
            new List<ArcherSplitBurstView>(MaximumSplitBursts);
        private bool initialized;
        private int splitProjectileCount = 1;
        private int splitDamageMultiplierBps = 10000;
        private ArcherShowcaseStatusDefinition burnDefinition;
        private ArcherShowcaseStatusDefinition poisonDefinition;

        public bool IsReady => initialized;
        public int SplitProjectileCount => splitProjectileCount;
        public int SplitDamageMultiplierBps => splitDamageMultiplierBps;
        public int SplitDamageBasisPoints => splitDamageMultiplierBps;
        public float SplitDamageMultiplier =>
            splitDamageMultiplierBps / 10000f;
        public ArcherShowcaseStatusDefinition BurnDefinition =>
            burnDefinition;
        public ArcherShowcaseStatusDefinition PoisonDefinition =>
            poisonDefinition;
        public string[] EquippedCardIds =>
            equippedCardIds == null
                ? Array.Empty<string>()
                : (string[])equippedCardIds.Clone();

        private void Awake()
        {
            Initialize();
        }

        public void Configure(TextAsset logicContent, Sprite pixelSprite)
        {
            contentJson = logicContent;
            effectPixel = pixelSprite;
            equippedCardIds = new[]
            {
                SplitCardId,
                BurnCardId,
                PoisonCardId
            };
            initialized = false;
            Initialize();
        }

        public void PlaySplitBurst(Vector3 origin, Vector2 direction)
        {
            if (!initialized || effectPixel == null)
            {
                return;
            }

            ArcherSplitBurstView burst = null;
            for (int i = 0; i < splitBursts.Count; i++)
            {
                if (!splitBursts[i].IsActive)
                {
                    burst = splitBursts[i];
                    break;
                }
            }

            if (burst == null && splitBursts.Count < MaximumSplitBursts)
            {
                var burstObject = new GameObject(
                    "Split Card Burst L" +
                    GetComponent<ArcherTowerView>().Level);
                SceneManager.MoveGameObjectToScene(
                    burstObject,
                    gameObject.scene);
                burst = burstObject.AddComponent<ArcherSplitBurstView>();
                burst.Configure(effectPixel);
                burstObject.SetActive(false);
                splitBursts.Add(burst);
            }

            burst?.Play(origin, direction);
        }

        private void Initialize()
        {
            if (initialized || contentJson == null)
            {
                return;
            }

            CompiledContent content = LoadContent(contentJson);
            CompiledEffectNode split = FindProjectileNode(
                content,
                SplitCardId,
                EffectOperation.Split);
            CompiledEffectNode burn = FindProjectileNode(
                content,
                BurnCardId,
                EffectOperation.BindBurn);
            CompiledEffectNode poison = FindProjectileNode(
                content,
                PoisonCardId,
                EffectOperation.BindPoison);

            splitProjectileCount = Mathf.Max(1, split.Amount);
            splitDamageMultiplierBps = Mathf.Clamp(
                split.Amount2,
                1,
                10000);
            int tickRate = content.Run.TickRate;
            burnDefinition = new ArcherShowcaseStatusDefinition(
                burn.Amount,
                burn.DurationTicks,
                burn.IntervalTicks,
                burn.MaxStacks,
                0,
                tickRate);
            poisonDefinition = new ArcherShowcaseStatusDefinition(
                poison.Amount,
                poison.DurationTicks,
                poison.IntervalTicks,
                poison.MaxStacks,
                poison.ChanceBps,
                tickRate);
            initialized = true;
        }

        private static CompiledContent LoadContent(TextAsset contentAsset)
        {
            if (cachedContent == null || cachedContentAsset != contentAsset)
            {
                cachedContentAsset = contentAsset;
                cachedContent = LogicContentJsonLoader.Load(contentAsset);
            }

            return cachedContent;
        }

        private static CompiledEffectNode FindProjectileNode(
            CompiledContent content,
            string stableCardId,
            EffectOperation operation)
        {
            if (!content.TryGetCardId(stableCardId, out CardId cardId))
            {
                throw new InvalidOperationException(
                    "Missing showcase card definition: " + stableCardId);
            }

            CompiledEffectNode[] nodes =
                content.GetCard(cardId).ProjectileEffects;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Operation == operation)
                {
                    return nodes[i];
                }
            }

            throw new InvalidOperationException(
                "Card '" + stableCardId +
                "' has no projectile operation " + operation + ".");
        }
    }

    internal sealed class ArcherSplitBurstView : MonoBehaviour
    {
        private const float Duration = 0.18f;
        private static readonly Color SplitColor =
            new Color(0.47f, 0.84f, 1f, 1f);

        private readonly Transform[] sparks = new Transform[3];
        private readonly SpriteRenderer[] renderers =
            new SpriteRenderer[3];
        private Vector2 direction;
        private Vector2 perpendicular;
        private float elapsed;

        public bool IsActive => gameObject.activeSelf;

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Duration);
            float alpha = 1f - progress;
            for (int i = 0; i < sparks.Length; i++)
            {
                float lane = i - 1f;
                sparks[i].localPosition =
                    (Vector3)(perpendicular * lane * 0.12f * progress +
                              direction * 0.08f * progress);
                float size = Mathf.Lerp(0.08f, 0.025f, progress);
                sparks[i].localScale = new Vector3(
                    size,
                    size * (i == 1 ? 1.4f : 1f),
                    1f);
                Color color = SplitColor;
                color.a = alpha;
                renderers[i].color = color;
            }

            if (elapsed >= Duration)
            {
                gameObject.SetActive(false);
            }
        }

        public void Configure(Sprite pixelSprite)
        {
            for (int i = 0; i < sparks.Length; i++)
            {
                var sparkObject = new GameObject("Split Spark " + (i + 1));
                sparkObject.transform.SetParent(transform, false);
                SpriteRenderer renderer =
                    sparkObject.AddComponent<SpriteRenderer>();
                renderer.sprite = pixelSprite;
                renderer.color = SplitColor;
                renderer.sortingOrder = 45;
                sparks[i] = sparkObject.transform;
                renderers[i] = renderer;
            }
        }

        public void Play(Vector3 origin, Vector2 launchDirection)
        {
            direction = launchDirection.sqrMagnitude <= 0.000001f
                ? Vector2.up
                : launchDirection.normalized;
            perpendicular = new Vector2(-direction.y, direction.x);
            transform.position = origin;
            elapsed = 0f;
            gameObject.SetActive(true);
        }
    }
}
