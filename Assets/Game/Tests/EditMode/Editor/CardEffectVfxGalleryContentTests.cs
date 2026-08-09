using System;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.Editor.AssetImport;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class CardEffectVfxGalleryContentTests
    {
        private const string BaseContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        [TearDown]
        public void RestoreProjectPaletteRegistration()
        {
            TextAsset baseContent =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    BaseContentPath);
            if (baseContent != null)
            {
                StageOneCardEffectPalette.RegisterContent(
                    LogicContentJsonLoader.Load(
                        baseContent,
                        CardContentModuleCatalogDiscovery
                            .DiscoverTextAssets()));
            }
        }

        [Test]
        public void GalleryStyles_IncludeNewModuleCardAutomatically()
        {
            TextAsset baseContent =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    BaseContentPath);
            Assert.That(baseContent, Is.Not.Null);
            ContentCatalogDto baseCatalog =
                JsonUtility.FromJson<ContentCatalogDto>(
                    baseContent.text);
            Assert.That(baseCatalog.cards, Is.Not.Empty);

            CardDefinitionDto addedCard =
                JsonUtility.FromJson<CardDefinitionDto>(
                    JsonUtility.ToJson(baseCatalog.cards[0]));
            addedCard.id = "gallery_auto_module_card";
            addedCard.displayNameKey =
                "card.gallery_auto_module_card.name";
            addedCard.symbolKey =
                "card_symbol.gallery_auto_module_card";
            addedCard.visualStyleIndex = -1;
            var module = new CardContentModuleDto
            {
                schemaVersion =
                    CardContentModuleDto.CurrentSchemaVersion,
                moduleId = "test.gallery.auto_module",
                order = 999,
                cards = new[] { addedCard }
            };
            var moduleAsset = new TextAsset(
                JsonUtility.ToJson(module));
            try
            {
                CompiledContent content =
                    LogicContentJsonLoader.Load(
                        baseContent,
                        new[] { moduleAsset });
                StageOneCardEffectStyle[] styles =
                    StageOneCardEffectPalette
                        .CreateCardGalleryStyles(content);

                Assert.That(
                    styles.Length,
                    Is.EqualTo(content.CardCount));
                Assert.That(
                    styles[styles.Length - 1].Id,
                    Is.EqualTo(addedCard.id));
                Assert.That(
                    content.Cards[content.CardCount - 1].StableId,
                    Is.EqualTo(addedCard.id));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(moduleAsset);
            }
        }

        [Test]
        public void GalleryLayout_AddsScrollableRowsWithoutOverlap()
        {
            Assert.That(
                CardEffectVfxGallery.GetRowCount(58),
                Is.EqualTo(12));
            Assert.That(
                CardEffectVfxGallery.GetRowCount(61),
                Is.EqualTo(13));
            Assert.That(
                CardEffectVfxGallery.GetRowCount(58, 2),
                Is.EqualTo(29));

            Vector3 precedingRow =
                CardEffectVfxGallery.GetSlotPosition(55, 61, 5);
            Vector3 followingRow =
                CardEffectVfxGallery.GetSlotPosition(60, 61, 5);
            Assert.That(
                followingRow.y,
                Is.EqualTo(
                    precedingRow.y -
                    CardEffectVfxGallery.VerticalSpacing)
                    .Within(0.001f));
        }

        [Test]
        public void GalleryLayout_UsesMobileFriendlyColumnCounts()
        {
            Assert.That(
                CardEffectVfxGallery.GetColumnCountForAspect(16f / 9f),
                Is.EqualTo(5));
            Assert.That(
                CardEffectVfxGallery.GetColumnCountForAspect(1f),
                Is.EqualTo(3));
            Assert.That(
                CardEffectVfxGallery.GetColumnCountForAspect(390f / 844f),
                Is.EqualTo(2));
            Assert.That(
                CardEffectVfxGallery.GetColumnCountForAspect(320f / 900f),
                Is.EqualTo(1));
        }
    }
}
