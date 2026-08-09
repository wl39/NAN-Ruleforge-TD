using System;
using UnityEngine;

namespace RuleforgeTD.UI
{
    public readonly struct WavePreviewDetailSectionModel
    {
        public WavePreviewDetailSectionModel(
            string title,
            string body)
        {
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
        }

        public string Title { get; }
        public string Body { get; }
    }

    public readonly struct WavePreviewGroupModel
    {
        public WavePreviewGroupModel(
            string displayName,
            string rankLabel,
            int count,
            Sprite sprite,
            bool isElite,
            bool isBoss,
            string detailText,
            bool hasOwnedRecommendation,
            bool hasEquippedRecommendation,
            RuntimeAnimatorController previewAnimatorController = null,
            WavePreviewDetailSectionModel[] detailSections = null)
            : this(
                displayName,
                rankLabel,
                count,
                sprite,
                isElite,
                isBoss,
                detailText,
                hasOwnedRecommendation,
                hasEquippedRecommendation,
                previewAnimatorController,
                detailSections,
                Color.white,
                Color.clear,
                1f)
        {
        }

        public WavePreviewGroupModel(
            string displayName,
            string rankLabel,
            int count,
            Sprite sprite,
            bool isElite,
            bool isBoss,
            string detailText,
            bool hasOwnedRecommendation,
            bool hasEquippedRecommendation,
            RuntimeAnimatorController previewAnimatorController,
            WavePreviewDetailSectionModel[] detailSections,
            Color previewTint,
            Color previewOutlineColor,
            float previewVisualScale)
        {
            DisplayName = displayName ?? string.Empty;
            RankLabel = rankLabel ?? string.Empty;
            Count = Math.Max(0, count);
            Sprite = sprite;
            IsElite = isElite;
            IsBoss = isBoss;
            DetailText = detailText ?? string.Empty;
            HasOwnedRecommendation = hasOwnedRecommendation;
            HasEquippedRecommendation = hasEquippedRecommendation;
            PreviewAnimatorController = previewAnimatorController;
            DetailSections = detailSections == null
                ? Array.Empty<WavePreviewDetailSectionModel>()
                : (WavePreviewDetailSectionModel[])detailSections.Clone();
            PreviewTint = previewTint;
            PreviewOutlineColor = previewOutlineColor;
            PreviewVisualScale = Mathf.Clamp(
                previewVisualScale,
                0.8f,
                1.45f);
        }

        public string DisplayName { get; }
        public string RankLabel { get; }
        public int Count { get; }
        public Sprite Sprite { get; }
        public bool IsElite { get; }
        public bool IsBoss { get; }
        public string DetailText { get; }
        public bool HasOwnedRecommendation { get; }
        public bool HasEquippedRecommendation { get; }
        public RuntimeAnimatorController PreviewAnimatorController { get; }
        public WavePreviewDetailSectionModel[] DetailSections { get; }
        public Color PreviewTint { get; }
        public Color PreviewOutlineColor { get; }
        public float PreviewVisualScale { get; }
        public bool HasPreviewOutline => PreviewOutlineColor.a > 0f;
    }

    public sealed class WavePreviewModel
    {
        public WavePreviewModel(
            int waveNumber,
            string title,
            string totalText,
            string compositionText,
            string coverageText,
            bool loadoutLocked,
            WavePreviewGroupModel[] groups)
        {
            WaveNumber = Math.Max(1, waveNumber);
            Title = title ?? string.Empty;
            TotalText = totalText ?? string.Empty;
            CompositionText = compositionText ?? string.Empty;
            CoverageText = coverageText ?? string.Empty;
            LoadoutLocked = loadoutLocked;
            Groups = groups == null
                ? Array.Empty<WavePreviewGroupModel>()
                : (WavePreviewGroupModel[])groups.Clone();
        }

        public int WaveNumber { get; }
        public string Title { get; }
        public string TotalText { get; }
        public string CompositionText { get; }
        public string CoverageText { get; }
        public bool LoadoutLocked { get; }
        public WavePreviewGroupModel[] Groups { get; }
        public bool IsValid => Groups.Length > 0;
    }
}
