using NUnit.Framework;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode.UI
{
    public sealed class RuleforgePixelUiTests
    {
        [Test]
        public void Apply_UsesExactSizeResponsiveFilteredSimpleSprite()
        {
            var host = new GameObject(
                "Open Campaign Map",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            try
            {
                Button button = host.GetComponent<Button>();
                button.targetGraphic = host.GetComponent<Image>();

                RuleforgePixelUi.Apply(
                    button,
                    RuleforgePixelButtonRole.Primary);

                Image image = host.GetComponent<Image>();
                Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(
                    image.sprite.texture.filterMode,
                    Is.EqualTo(FilterMode.Bilinear));
                Assert.That(image.sprite.texture.width, Is.EqualTo(990));
                Assert.That(image.sprite.texture.height, Is.EqualTo(216));
                Assert.That(
                    image.sprite.pixelsPerUnit,
                    Is.EqualTo(300f));
                Assert.That(
                    button.transition,
                    Is.EqualTo(Selectable.Transition.ColorTint));
                Assert.That(
                    image.sprite.border.x,
                    Is.Zero);
                Assert.That(
                    host.GetComponent<RuleforgePixelButtonSkin>().Role,
                    Is.EqualTo(RuleforgePixelButtonRole.Primary));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ApplyExactPanel_UsesNativeBlueprintFrame()
        {
            var host = new GameObject(
                "Pixel Panel Test",
                typeof(RectTransform),
                typeof(Image));
            try
            {
                Image image = host.GetComponent<Image>();
                RuleforgePixelUi.ApplyExactPanel(
                    image,
                    RuleforgeExactPanelAsset.Effect876x120,
                    Color.white);

                Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(
                    image.sprite.texture.filterMode,
                    Is.EqualTo(FilterMode.Bilinear));
                Assert.That(
                    image.sprite.texture.width,
                    Is.EqualTo(2628));
                Assert.That(
                    image.sprite.texture.height,
                    Is.EqualTo(360));
                Assert.That(
                    image.sprite.pixelsPerUnit,
                    Is.EqualTo(300f));
                Assert.That(image.sprite.border.x, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ApplyPanel_UsesSharedSlicedParchmentFrame()
        {
            var host = new GameObject(
                "Parchment Panel Test",
                typeof(RectTransform),
                typeof(Image));
            try
            {
                Image image = host.GetComponent<Image>();
                RuleforgePixelUi.ApplyPanel(
                    image,
                    RuleforgePixelPanelRole.Parchment,
                    Color.white);

                Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(image.preserveAspect, Is.False);
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(image.sprite.texture.width, Is.EqualTo(717));
                Assert.That(image.sprite.texture.height, Is.EqualTo(174));
                Assert.That(
                    image.sprite.pixelsPerUnit,
                    Is.EqualTo(400f));
                Assert.That(
                    image.sprite.texture.filterMode,
                    Is.EqualTo(FilterMode.Bilinear));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Typography_RemovesSyntheticWeightAndContainsText()
        {
            var host = new GameObject(
                "Typography Test",
                typeof(RectTransform),
                typeof(Text),
                typeof(Outline));
            try
            {
                Text text = host.GetComponent<Text>();
                text.fontStyle = FontStyle.Bold;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;

                RuleforgeUiTypography.Configure(
                    text,
                    null,
                    18,
                    Color.white,
                    TextAnchor.MiddleCenter,
                    true);

                Assert.That(text.fontStyle, Is.EqualTo(FontStyle.Normal));
                Assert.That(
                    text.horizontalOverflow,
                    Is.EqualTo(HorizontalWrapMode.Wrap));
                Assert.That(
                    text.verticalOverflow,
                    Is.EqualTo(VerticalWrapMode.Truncate));
                Assert.That(host.GetComponent<Outline>().enabled, Is.False);
                Assert.That(
                    host.GetComponents<Shadow>().Length,
                    Is.EqualTo(2),
                    "A disabled heavy outline and one subtle shadow are expected.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpeedButtons_UseFourDistinctCompactMechanicalSprites()
        {
            string[] names =
            {
                "Speed 0.5x Button",
                "Speed 1x Button",
                "Speed 2x Button",
                "Speed 3x Button"
            };
            var sprites = new HashSet<Sprite>();
            var hosts = new List<GameObject>();
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var host = new GameObject(
                        names[i],
                        typeof(RectTransform),
                        typeof(Image),
                        typeof(Button));
                    hosts.Add(host);
                    Button button = host.GetComponent<Button>();
                    button.targetGraphic = host.GetComponent<Image>();

                    RuleforgePixelUi.Apply(
                        button,
                        RuleforgePixelButtonRole.Secondary);

                    Image image = host.GetComponent<Image>();
                    Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
                    Assert.That(image.preserveAspect, Is.True);
                    Assert.That(image.sprite, Is.Not.Null);
                    Assert.That(image.sprite.texture.width, Is.EqualTo(150));
                    Assert.That(image.sprite.texture.height, Is.EqualTo(108));
                    sprites.Add(image.sprite);
                }

                Assert.That(sprites.Count, Is.EqualTo(4));
            }
            finally
            {
                for (int i = 0; i < hosts.Count; i++)
                {
                    Object.DestroyImmediate(hosts[i]);
                }
            }
        }

        [Test]
        public void TowerPickerPanel_UsesExactTwoOptionWoodFrame()
        {
            var host = new GameObject(
                "Tower Picker Panel Test",
                typeof(RectTransform),
                typeof(Image));
            try
            {
                Image image = host.GetComponent<Image>();
                RuleforgePixelUi.ApplyExactPanel(
                    image,
                    RuleforgeExactPanelAsset.TowerPicker2_380x272,
                    Color.white);

                Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(image.sprite.texture.width, Is.EqualTo(1140));
                Assert.That(image.sprite.texture.height, Is.EqualTo(816));
                Assert.That(image.sprite.texture.filterMode,
                    Is.EqualTo(FilterMode.Bilinear));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Apply_SelectedRoleKeepsMagicAccentSemantic()
        {
            var host = new GameObject(
                "Selected Pixel Button Test",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            try
            {
                Button button = host.GetComponent<Button>();
                button.targetGraphic = host.GetComponent<Image>();

                RuleforgePixelUi.Apply(
                    button,
                    RuleforgePixelButtonRole.Selected);

                Assert.That(
                    host.GetComponent<RuleforgePixelButtonSkin>().Role,
                    Is.EqualTo(RuleforgePixelButtonRole.Selected));
                Assert.That(
                    host.GetComponent<Image>().color,
                    Is.EqualTo(Color.white));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CardView_UsesSharedPixelFrameAndInsetPanels()
        {
            var parent = new GameObject(
                "Pixel Card Test Parent",
                typeof(RectTransform));
            try
            {
                StageOneCardView card =
                    StageOneCardView.CreateRuntime(
                        "Pixel Card Test",
                        parent.transform);
                card.Configure(
                    new StageOneCardDisplay(
                        "split",
                        "분열",
                        "탄환이 분열합니다.",
                        "적이 분열합니다.",
                        true,
                        1),
                    CardTier.Rare,
                    SubjectType.Projectile,
                    null,
                    true,
                    "장착",
                    true);

                Assert.That(
                    card.Button.transition,
                    Is.EqualTo(Selectable.Transition.ColorTint));
                Assert.That(card.BorderImage.sprite, Is.Not.Null);
                Assert.That(
                    card.BorderImage.type,
                    Is.EqualTo(Image.Type.Simple));
                Assert.That(
                    card.BorderImage.sprite.texture.filterMode,
                    Is.EqualTo(FilterMode.Bilinear));
                Assert.That(
                    card.BorderImage.sprite.texture.height,
                    Is.GreaterThan(560));
                Assert.That(
                    card.BorderImage.sprite.pixelsPerUnit,
                    Is.EqualTo(300f));
                Assert.That(
                    card.BodyImage.type,
                    Is.EqualTo(Image.Type.Sliced));
                Assert.That(
                    card.BodyImage.sprite.pixelsPerUnit,
                    Is.EqualTo(100f));
                Assert.That(
                    card.NameBackplateImage.type,
                    Is.EqualTo(Image.Type.Sliced));
                Assert.That(
                    card.ArtworkFrameImage.type,
                    Is.EqualTo(Image.Type.Sliced));
                Assert.That(
                    card.DescriptionBackplateImage.type,
                    Is.EqualTo(Image.Type.Sliced));
                Assert.That(
                    card.DescriptionText.resizeTextMinSize,
                    Is.EqualTo(7));
                Assert.That(
                    card.DescriptionText.resizeTextMaxSize,
                    Is.EqualTo(11));
                Assert.That(
                    card.DescriptionText.alignment,
                    Is.EqualTo(TextAnchor.UpperLeft));
                Assert.That(
                    card.DescriptionText.lineSpacing,
                    Is.EqualTo(0.85f).Within(0.001f));
                Assert.That(
                    card.DescriptionText.rectTransform.offsetMin.y,
                    Is.EqualTo(12f).Within(0.001f));
                Assert.That(
                    card.DescriptionText.rectTransform.offsetMin.x,
                    Is.EqualTo(12f).Within(0.001f));
                Assert.That(
                    card.DescriptionText.rectTransform.offsetMax.x,
                    Is.EqualTo(-12f).Within(0.001f));
                Assert.That(
                    card.DescriptionText.rectTransform.offsetMax.y,
                    Is.EqualTo(-10f).Within(0.001f));
                Assert.That(
                    card.DescriptionBackplateImage.rectTransform
                        .anchorMin.y,
                    Is.EqualTo(0.095f).Within(0.001f));
                Assert.That(
                    card.DescriptionBackplateImage.rectTransform
                        .anchorMax.y,
                    Is.EqualTo(0.42f).Within(0.001f));
                Assert.That(
                    card.GetComponent<RectTransform>().sizeDelta,
                    Is.EqualTo(new Vector2(113f, 192f)));
                Assert.That(
                    card.TierAccentImage.color,
                    Is.EqualTo(
                        StageOneCardView.GetTierColor(CardTier.Rare)));
                Assert.That(card.IsEquipped, Is.True);
                Assert.That(card.GetComponent<RectMask2D>(), Is.Not.Null);
                card.SetTextScale(1.1f);
                Assert.That(
                    card.DescriptionText.resizeTextMinSize,
                    Is.EqualTo(8));
                Assert.That(
                    card.DescriptionText.resizeTextMaxSize,
                    Is.EqualTo(12));
                Assert.That(
                    card.NameText.fontStyle,
                    Is.EqualTo(FontStyle.Normal));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
