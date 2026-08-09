using System;
using RuleforgeTD.GameLogic.Content;
using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 웨이브 예고가 특정 스테이지의 문자열 카탈로그 구현에 의존하지 않게 하는 계약이다.
    /// </summary>
    public interface IWavePreviewLocalization
    {
        string Get(string key);
        string Format(string key, params object[] arguments);
        string ResolveDisplayName(CompiledCardDefinition definition);
    }

    /// <summary>
    /// 웨이브 예고가 특정 스테이지의 프리팹 카탈로그 구현에 의존하지 않게 하는 계약이다.
    /// </summary>
    public interface IEnemyPreviewSpriteProvider
    {
        bool TryGetEnemyPreviewSprite(
            string definitionId,
            out Sprite sprite);

        bool TryGetEnemyPreviewAnimatorController(
            string definitionId,
            out RuntimeAnimatorController controller);

        bool TryGetEnemyPreviewScaleMultiplier(
            string definitionId,
            out float scaleMultiplier);
    }

    internal sealed class WavePreviewFallbackLocalization :
        IWavePreviewLocalization
    {
        public static readonly WavePreviewFallbackLocalization Instance =
            new WavePreviewFallbackLocalization();

        private WavePreviewFallbackLocalization()
        {
        }

        public string Get(string key)
        {
            return string.IsNullOrWhiteSpace(key)
                ? string.Empty
                : key.Trim();
        }

        public string Format(string key, params object[] arguments)
        {
            string format = Get(key);
            try
            {
                return string.Format(
                    format,
                    arguments ?? Array.Empty<object>());
            }
            catch (FormatException)
            {
                return format;
            }
        }

        public string ResolveDisplayName(
            CompiledCardDefinition definition)
        {
            return definition == null
                ? string.Empty
                : Get(definition.DisplayNameKey);
        }
    }
}
