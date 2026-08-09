using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Simulation
{
    public static class LogicContentJsonLoader
    {
        public static CompiledContent Load(TextAsset jsonAsset)
        {
            // Base-only compatibility path for focused tests. Production
            // composition roots must pass the discovered module array to the
            // overload below so newly authored cards cannot be omitted.
            return Load(
                jsonAsset,
                Array.Empty<TextAsset>());
        }

        /// <summary>
        /// 기본 카탈로그와 카드 모듈을 하나의 권위 콘텐츠로 합성한 뒤
        /// 기존 효과 컴파일 경계를 그대로 통과시킨다. 모듈 JSON의
        /// localization 필드는 GameLogic DTO에 포함되지 않으므로 여기서는
        /// 자연스럽게 무시되고, 같은 TextAsset 배열을 UI 텍스트 로더가
        /// 별도로 해석한다.
        /// </summary>
        public static CompiledContent Load(
            TextAsset jsonAsset,
            IReadOnlyList<TextAsset> cardModuleAssets)
        {
            if (jsonAsset == null)
            {
                throw new ArgumentNullException(nameof(jsonAsset));
            }
            if (cardModuleAssets == null)
            {
                throw new ArgumentNullException(
                    nameof(cardModuleAssets));
            }

            ContentCatalogDto dto;
            try
            {
                dto = JsonUtility.FromJson<ContentCatalogDto>(
                    jsonAsset.text);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Base logic content JSON '" +
                    jsonAsset.name +
                    "' could not be parsed.",
                    exception);
            }
            if (dto == null)
            {
                throw new InvalidOperationException(
                    "Base logic content JSON could not be parsed from '" +
                    jsonAsset.name +
                    "'.");
            }

            var modules = new List<CardContentModuleDto>(
                cardModuleAssets.Count);
            for (int moduleIndex = 0;
                 moduleIndex < cardModuleAssets.Count;
                 moduleIndex++)
            {
                TextAsset moduleAsset =
                    cardModuleAssets[moduleIndex];
                if (moduleAsset == null)
                {
                    throw new InvalidOperationException(
                        "Card content module asset at index " +
                        moduleIndex +
                        " is null.");
                }

                CardContentModuleDto module;
                try
                {
                    module =
                        JsonUtility.FromJson<CardContentModuleDto>(
                            moduleAsset.text);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Card content module JSON '" +
                        moduleAsset.name +
                        "' could not be parsed.",
                        exception);
                }

                if (module == null)
                {
                    throw new InvalidOperationException(
                        "Card content module JSON '" +
                        moduleAsset.name +
                        "' produced no module data.");
                }

                modules.Add(module);
            }

            ContentCatalogDto composed =
                CardContentCatalogComposer.Compose(
                    dto,
                    modules);
            return EffectContentCompiler.Compile(
                composed,
                GameSimulation.IsEffectOperationSupported);
        }
    }
}
