using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 모든 전투 스테이지에서 동일하게 사용하는 다음 웨이브 예고 조정자다.
    /// 표시 가능 단계, 모델 캐시, 상세 UI 갱신을 전투 컨트롤러 밖에서 소유한다.
    /// </summary>
    public sealed class WavePreviewPresenter
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private readonly WavePreviewView view;
        private readonly CompiledContent content;
        private readonly IWavePreviewLocalization localization;
        private readonly IEnemyPreviewSpriteProvider spriteProvider;
        private ulong lastFingerprint;
        private bool hasFingerprint;

        public WavePreviewPresenter(
            WavePreviewView previewView,
            CompiledContent compiledContent,
            IWavePreviewLocalization textProvider,
            IEnemyPreviewSpriteProvider enemySpriteProvider)
        {
            view = previewView ??
                throw new ArgumentNullException(nameof(previewView));
            content = compiledContent ??
                throw new ArgumentNullException(nameof(compiledContent));
            localization = textProvider ??
                throw new ArgumentNullException(nameof(textProvider));
            spriteProvider = enemySpriteProvider;
        }

        public WavePreviewView View => view;

        public void Refresh(
            GameSimulation simulation,
            SimulationSnapshot snapshot,
            bool obscured)
        {
            if (simulation == null || snapshot == null || obscured)
            {
                Hide();
                return;
            }

            bool phaseAllowsPreview =
                snapshot.Phase == RunPhase.AwaitingStartingTower ||
                snapshot.Phase == RunPhase.Planning ||
                snapshot.Phase == RunPhase.Combat;
            WaveForecastSnapshot forecast = phaseAllowsPreview
                ? simulation.GetUpcomingWaveForecast()
                : null;
            if (forecast == null || !forecast.IsAvailable)
            {
                Hide();
                return;
            }

            bool loadoutLocked = snapshot.Phase == RunPhase.Combat;
            ulong fingerprint = BuildFingerprint(
                forecast,
                snapshot.Cards,
                loadoutLocked);
            if (!hasFingerprint || fingerprint != lastFingerprint)
            {
                view.ApplyModel(WavePreviewModelFactory.Create(
                    forecast,
                    content,
                    snapshot.Cards,
                    localization,
                    spriteProvider,
                    loadoutLocked));
                lastFingerprint = fingerprint;
                hasFingerprint = true;
            }

            view.SetVisible(true);
            view.SetInteractionBlocked(false);
        }

        public void Hide()
        {
            view.SetVisible(false);
        }

        private static ulong BuildFingerprint(
            WaveForecastSnapshot forecast,
            CardInstanceSnapshot[] cards,
            bool loadoutLocked)
        {
            ulong hash = FnvOffset;
            Add(ref hash, forecast.WaveIndex);
            Add(ref hash, forecast.WaveId);
            Add(ref hash, forecast.TotalCount);
            Add(ref hash, loadoutLocked ? 1 : 0);

            WaveForecastSpawn[] spawns = forecast.Spawns ??
                Array.Empty<WaveForecastSpawn>();
            for (int i = 0; i < spawns.Length; i++)
            {
                WaveForecastSpawn spawn = spawns[i];
                Add(ref hash, spawn.EnemyId);
                Add(ref hash, (int)spawn.Rank);
                Add(ref hash, spawn.Count);
                Add(ref hash, spawn.FirstSpawnTick);
                Add(ref hash, spawn.IntervalTicks);
                Add(ref hash, spawn.Stats.MaxHealthMilli);
                Add(ref hash, spawn.Stats.Armor);
                Add(ref hash, spawn.Stats.SpeedMilliPerTick);
                Add(ref hash, spawn.Stats.ShieldMilli);
                string[] traits = spawn.EliteTraitIds ??
                    Array.Empty<string>();
                for (int traitIndex = 0;
                     traitIndex < traits.Length;
                     traitIndex++)
                {
                    Add(ref hash, traits[traitIndex]);
                }
            }

            CardInstanceSnapshot[] source = cards ??
                Array.Empty<CardInstanceSnapshot>();
            for (int i = 0; i < source.Length; i++)
            {
                Add(ref hash, source[i].DefinitionId.Value);
                Add(ref hash, source[i].Equipped ? 1 : 0);
            }

            return hash;
        }

        private static void Add(ref ulong hash, int value)
        {
            Add(ref hash, (long)value);
        }

        private static void Add(ref ulong hash, long value)
        {
            unchecked
            {
                ulong raw = (ulong)value;
                for (int i = 0; i < sizeof(long); i++)
                {
                    hash = (hash ^ (byte)(raw >> (i * 8))) * FnvPrime;
                }
            }
        }

        private static void Add(ref ulong hash, string value)
        {
            string text = value ?? string.Empty;
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char character = text[i];
                    hash = (hash ^ (byte)character) * FnvPrime;
                    hash = (hash ^ (byte)(character >> 8)) * FnvPrime;
                }
                hash = (hash ^ 0xffUL) * FnvPrime;
            }
        }
    }
}
