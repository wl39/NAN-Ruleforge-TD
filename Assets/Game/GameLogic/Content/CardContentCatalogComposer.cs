using System;
using System.Collections.Generic;

namespace RuleforgeTD.GameLogic.Content
{
    /// <summary>
    /// 기본 콘텐츠 카탈로그에 덧붙일 카드 전용 콘텐츠 모듈이다.
    /// 모듈은 타워·적·웨이브 규칙을 소유하지 않으며, 카드 정의만 추가한다.
    /// </summary>
    [Serializable]
    public sealed class CardContentModuleDto
    {
        /// <summary>현재 지원하는 카드 모듈 스키마 버전이다.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// 파일이 명시해야 하는 스키마 버전이다. 기본값 0을 유지해 필드
        /// 누락이나 오탈자가 현재 버전으로 조용히 통과하지 않게 한다.
        /// </summary>
        public int schemaVersion;

        /// <summary>
        /// 모듈을 식별하는 안정 ID다. 비교와 정렬에는 Ordinal 규칙을 사용한다.
        /// </summary>
        public string moduleId;

        /// <summary>작은 값의 모듈부터 기본 카드 뒤에 병합한다.</summary>
        public int order;

        /// <summary>모듈 내부 순서를 그대로 유지할 카드 정의다.</summary>
        public CardDefinitionDto[] cards;
    }

    /// <summary>
    /// 기본 카탈로그와 카드 모듈을 결정적인 순서로 합성한다.
    /// 입력 DTO와 입력 배열은 수정하지 않으며, 카드 배열만 새로 할당한다.
    /// </summary>
    public static class CardContentCatalogComposer
    {
        /// <summary>
        /// 기본 카드를 먼저 유지하고, 모듈을 order와 moduleId 순으로 정렬해
        /// 뒤에 붙인 새로운 카탈로그 DTO를 반환한다.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// 기본 카탈로그 또는 모듈 목록 자체가 null일 때 발생한다.
        /// </exception>
        /// <exception cref="ContentValidationException">
        /// 모듈 메타데이터나 전체 카드 ID 집합이 유효하지 않을 때 수집된 오류와 함께 발생한다.
        /// </exception>
        public static ContentCatalogDto Compose(
            ContentCatalogDto baseCatalog,
            IReadOnlyList<CardContentModuleDto> modules)
        {
            if (baseCatalog == null)
            {
                throw new ArgumentNullException(nameof(baseCatalog));
            }
            if (modules == null)
            {
                throw new ArgumentNullException(nameof(modules));
            }

            var errors = new List<string>();
            var moduleIds = new HashSet<string>(StringComparer.Ordinal);
            var cardIds = new HashSet<string>(StringComparer.Ordinal);
            var nonNullModules =
                new List<CardContentModuleDto>(modules.Count);

            ValidateCardIds(
                baseCatalog.cards,
                "Base catalog",
                cardIds,
                errors);

            for (int moduleIndex = 0;
                 moduleIndex < modules.Count;
                 moduleIndex++)
            {
                CardContentModuleDto module = modules[moduleIndex];
                if (module == null)
                {
                    errors.Add(
                        "Card content module at index " +
                        moduleIndex +
                        " is null.");
                    continue;
                }

                nonNullModules.Add(module);
                if (module.schemaVersion !=
                    CardContentModuleDto.CurrentSchemaVersion)
                {
                    errors.Add(
                        "Card content module at index " +
                        moduleIndex +
                        " must use schema version " +
                        CardContentModuleDto.CurrentSchemaVersion +
                        " (was " +
                        module.schemaVersion +
                        ").");
                }

                bool hasModuleId =
                    !string.IsNullOrWhiteSpace(module.moduleId);
                if (!hasModuleId)
                {
                    errors.Add(
                        "Card content module at index " +
                        moduleIndex +
                        " has no module id.");
                }
                else if (!string.Equals(
                             module.moduleId,
                             module.moduleId.Trim(),
                             StringComparison.Ordinal))
                {
                    errors.Add(
                        "Card content module id '" +
                        module.moduleId +
                        "' must not have surrounding whitespace.");
                }
                else if (!moduleIds.Add(module.moduleId))
                {
                    errors.Add(
                        "Duplicate card content module id '" +
                        module.moduleId +
                        "'.");
                }

                string owner = hasModuleId
                    ? "Card content module '" + module.moduleId + "'"
                    : "Card content module at index " + moduleIndex;
                ValidateCardIds(
                    module.cards,
                    owner,
                    cardIds,
                    errors);
                ValidateModulePresentation(
                    module.cards,
                    owner,
                    errors);
            }

            if (errors.Count > 0)
            {
                throw new ContentValidationException(
                    string.Join("\n", errors));
            }

            nonNullModules.Sort(CompareModules);
            CardDefinitionDto[] baseCards =
                baseCatalog.cards ?? Array.Empty<CardDefinitionDto>();
            int mergedCardCount = baseCards.Length;
            for (int moduleIndex = 0;
                 moduleIndex < nonNullModules.Count;
                 moduleIndex++)
            {
                CardDefinitionDto[] moduleCards =
                    nonNullModules[moduleIndex].cards;
                if (moduleCards != null)
                {
                    mergedCardCount += moduleCards.Length;
                }
            }

            var mergedCards = new CardDefinitionDto[mergedCardCount];
            Array.Copy(
                baseCards,
                0,
                mergedCards,
                0,
                baseCards.Length);
            int destinationIndex = baseCards.Length;
            for (int moduleIndex = 0;
                 moduleIndex < nonNullModules.Count;
                 moduleIndex++)
            {
                CardDefinitionDto[] moduleCards =
                    nonNullModules[moduleIndex].cards ??
                    Array.Empty<CardDefinitionDto>();
                Array.Copy(
                    moduleCards,
                    0,
                    mergedCards,
                    destinationIndex,
                    moduleCards.Length);
                destinationIndex += moduleCards.Length;
            }

            return new ContentCatalogDto
            {
                version = baseCatalog.version,
                cards = mergedCards,
                towers = baseCatalog.towers,
                enemies = baseCatalog.enemies,
                eliteTraits = baseCatalog.eliteTraits,
                waves = baseCatalog.waves,
                safety = baseCatalog.safety,
                run = baseCatalog.run
            };
        }

        private static void ValidateCardIds(
            CardDefinitionDto[] cards,
            string owner,
            HashSet<string> cardIds,
            List<string> errors)
        {
            CardDefinitionDto[] source =
                cards ?? Array.Empty<CardDefinitionDto>();
            for (int cardIndex = 0;
                 cardIndex < source.Length;
                 cardIndex++)
            {
                CardDefinitionDto card = source[cardIndex];
                string cardId = card == null ? null : card.id;
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    errors.Add(
                        owner +
                        " card at index " +
                        cardIndex +
                        " has no id.");
                    continue;
                }

                if (!string.Equals(
                        cardId,
                        cardId.Trim(),
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        owner + " card id '" + cardId +
                        "' must not have surrounding whitespace.");
                }

                if (!cardIds.Add(cardId))
                {
                    errors.Add(
                        "Duplicate card id '" +
                        cardId +
                        "'.");
                }
            }
        }

        private static int CompareModules(
            CardContentModuleDto left,
            CardContentModuleDto right)
        {
            int orderComparison = left.order.CompareTo(right.order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            return StringComparer.Ordinal.Compare(
                left.moduleId,
                right.moduleId);
        }

        private static void ValidateModulePresentation(
            CardDefinitionDto[] cards,
            string owner,
            List<string> errors)
        {
            CardDefinitionDto[] source =
                cards ?? Array.Empty<CardDefinitionDto>();
            for (int cardIndex = 0;
                 cardIndex < source.Length;
                 cardIndex++)
            {
                CardDefinitionDto card = source[cardIndex];
                if (card != null && card.visualStyleIndex != -1)
                {
                    errors.Add(
                        owner + " card '" + card.id +
                        "' must use visualStyleIndex -1 so the " +
                        "module-safe generated presentation path is used.");
                }
            }
        }
    }
}
