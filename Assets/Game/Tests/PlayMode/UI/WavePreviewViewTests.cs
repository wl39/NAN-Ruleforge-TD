using System.Collections;
using NUnit.Framework;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode.UI
{
    public sealed class WavePreviewViewTests
    {
        [UnityTest]
        public IEnumerator SummaryOpensTouchFriendlyDetailAndKeepsRankText()
        {
            const string localization =
                "{\"locale\":\"ko-KR\",\"strings\":[" +
                "{\"key\":\"wave_preview.close\",\"value\":\"닫기\"}," +
                "{\"key\":\"wave_preview.loadout_locked\",\"value\":\"전투 중 잠김\"}" +
                "]}";
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(
                    new TextAsset(localization));
            var enemyTexture = new Texture2D(48, 48);
            Sprite enemySprite = Sprite.Create(
                enemyTexture,
                new Rect(0f, 0f, 48f, 48f),
                new Vector2(0.5f, 0.5f),
                48f);
            WavePreviewView view =
                WavePreviewView.CreateRuntime(
                    catalog,
                    null);
            var model = new WavePreviewModel(
                2,
                "웨이브 2 예고",
                "총 36마리",
                "일반 34 · 정예 2 · 보스 0",
                "보유 카드로 대응 보완 가능",
                true,
                new[]
                {
                    new WavePreviewGroupModel(
                        "약탈자",
                        "일반",
                        34,
                        enemySprite,
                        false,
                        false,
                        "약탈자 상세",
                        false,
                        false,
                        null,
                        new[]
                        {
                            new WavePreviewDetailSectionModel(
                                "능력치",
                                "체력 30 · 방어력 0"),
                            new WavePreviewDetailSectionModel(
                                "약점",
                                "- 범위 피해")
                        }),
                    new WavePreviewGroupModel(
                        "철갑 약탈자",
                        "정예",
                        2,
                        null,
                        true,
                        false,
                        "철갑 특성 상세",
                        true,
                        false)
                });

            view.ApplyModel(model);
            yield return null;

            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.GroupButtonCount, Is.EqualTo(2));
            Assert.That(view.TotalEnemyText.text, Is.EqualTo("총 36마리"));
            Canvas.ForceUpdateCanvases();
            Button firstGroup = FindNamedComponent<Button>(
                view.transform,
                "Enemy Group 0");
            Assert.That(firstGroup, Is.Not.Null);
            RectTransform firstGroupRect =
                firstGroup.GetComponent<RectTransform>();
            Assert.That(firstGroupRect.anchorMin.x, Is.EqualTo(0f));
            Assert.That(firstGroupRect.anchorMax.x, Is.EqualTo(0.5f));
            Assert.That(firstGroupRect.rect.width, Is.GreaterThanOrEqualTo(120f));
            Image firstGroupFrame = firstGroup.GetComponent<Image>();
            Assert.That(firstGroupFrame.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(firstGroupFrame.preserveAspect, Is.False);
            RectTransform monsterImage = firstGroup.transform
                .Find("Monster Image") as RectTransform;
            Assert.That(monsterImage, Is.Not.Null);
            Assert.That(monsterImage.anchoredPosition.x, Is.EqualTo(16f));
            Assert.That(monsterImage.sizeDelta, Is.EqualTo(new Vector2(60f, 60f)));
            Text firstGroupLabel = firstGroup.GetComponentInChildren<Text>();
            Assert.That(firstGroupLabel.text, Does.StartWith("약탈자\n"));
            Assert.That(firstGroupLabel.rectTransform.rect.width, Is.GreaterThan(0f));
            Assert.That(
                view.TotalEnemyText.fontStyle,
                Is.EqualTo(FontStyle.Normal));
            Assert.That(
                view.TotalEnemyText.horizontalOverflow,
                Is.EqualTo(HorizontalWrapMode.Wrap));
            Image[] images = view.GetComponentsInChildren<Image>(true);
            Image summaryPanel = null;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject.name == "Compact Preview")
                {
                    summaryPanel = images[i];
                    break;
                }
            }
            Assert.That(summaryPanel, Is.Not.Null);
            Assert.That(summaryPanel.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(summaryPanel.sprite, Is.Not.Null);
            Assert.That(
                summaryPanel.sprite.texture.width,
                Is.EqualTo(717));
            bool portrait = Screen.height > Screen.width;
            float occupiedTop =
                StageOneHudLayoutMetrics.GetTopOccupiedHeight(portrait) +
                StageOneHudLayoutMetrics.OverlaySeparation;
            Assert.That(
                summaryPanel.rectTransform.anchoredPosition.y,
                Is.LessThanOrEqualTo(-occupiedTop));
            Assert.That(view.IsDetailVisible, Is.False);

            view.SummaryButton.onClick.Invoke();
            yield return null;
            Assert.That(view.IsDetailVisible, Is.True);
            Button closeButton = FindNamedComponent<Button>(
                view.transform,
                "Close Detail");
            Assert.That(closeButton, Is.Not.Null);
            Assert.That(
                closeButton.GetComponentInChildren<Text>().text,
                Is.EqualTo("×"));
            RectTransform selectedEnemyImage = FindNamedComponent<Image>(
                view.transform,
                "Selected Enemy Image").rectTransform;
            Assert.That(
                selectedEnemyImage.sizeDelta,
                Is.EqualTo(new Vector2(112f, 112f)));
            Assert.That(view.DetailText.text, Does.Contain("약탈자 상세"));
            Assert.That(view.DetailText.text, Does.Contain("전투 중 잠김"));
            Assert.That(
                CountNamedChildren(view.transform, "Section Divider"),
                Is.EqualTo(3));

            view.OpenGroup(1);
            yield return null;
            Assert.That(view.DetailText.text, Does.Contain("철갑 특성 상세"));

            Object.Destroy(view.gameObject);
            Object.Destroy(enemySprite);
            Object.Destroy(enemyTexture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CrowdedCardsUseLargeMonstersAndCountOnlyLabels()
        {
            var enemyTexture = new Texture2D(48, 48);
            Sprite enemySprite = Sprite.Create(
                enemyTexture,
                new Rect(0f, 0f, 48f, 48f),
                new Vector2(0.5f, 0.5f),
                48f);
            WavePreviewView view =
                WavePreviewView.CreateRuntime(null, null);
            var groups = new WavePreviewGroupModel[4];
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new WavePreviewGroupModel(
                    i == 0 ? "고블린" : "슬라임",
                    i == 1 ? "정예" : "일반",
                    18 - i,
                    enemySprite,
                    i == 1,
                    false,
                    "상세 정보",
                    i == 1,
                    false,
                    null,
                    null,
                    i == 0
                        ? new Color32(113, 135, 166, 255)
                        : Color.white,
                    i == 0
                        ? new Color32(216, 228, 242, 255)
                        : Color.clear,
                    i == 0 ? 1.2f : 1f);
            }

            view.ApplyModel(new WavePreviewModel(
                2,
                "웨이브 2 예고",
                "총 34마리",
                "일반 28 · 정예 6 · 보스 0",
                "보유 카드로 대응 보완 가능",
                true,
                groups));
            yield return null;
            Canvas.ForceUpdateCanvases();

            Button firstGroup = FindNamedComponent<Button>(
                view.transform,
                "Enemy Group 0");
            Assert.That(firstGroup, Is.Not.Null);
            RectTransform firstRect =
                firstGroup.GetComponent<RectTransform>();
            Assert.That(firstRect.anchorMax.x, Is.EqualTo(0.25f));

            Text label = firstGroup.GetComponentInChildren<Text>();
            Assert.That(label.text, Is.EqualTo("×18"));
            Assert.That(label.fontSize, Is.EqualTo(14));
            Assert.That(label.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
            Assert.That(label.lineSpacing, Is.EqualTo(0.86f));
            Assert.That(label.rectTransform.rect.width, Is.GreaterThanOrEqualTo(50f));

            RectTransform icon = firstGroup.transform
                .Find("Monster Image") as RectTransform;
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(icon.anchoredPosition, Is.EqualTo(new Vector2(0f, -4f)));
            Assert.That(icon.sizeDelta, Is.EqualTo(new Vector2(48f, 48f)));
            Assert.That(icon.localScale, Is.EqualTo(Vector3.one * 1.2f));
            Image monsterImage = icon.GetComponent<Image>();
            Assert.That(
                monsterImage.color,
                Is.EqualTo((Color)new Color32(113, 135, 166, 255)));
            Outline outline = icon.GetComponent<Outline>();
            Assert.That(outline, Is.Not.Null);
            Assert.That(outline.enabled, Is.True);

            Image summaryPanel = FindNamedComponent<Image>(
                view.transform,
                "Compact Preview");
            Assert.That(summaryPanel, Is.Not.Null);
            Assert.That(
                summaryPanel.rectTransform.sizeDelta.y,
                Is.EqualTo(54f));
            Assert.That(
                FindNamedComponent<Text>(
                    view.transform,
                    "Composition").gameObject.activeSelf,
                Is.False);
            Assert.That(
                FindNamedComponent<RectTransform>(
                    view.transform,
                    "Enemy Groups").gameObject.activeSelf,
                Is.False);

            Object.Destroy(view.gameObject);
            Object.Destroy(enemySprite);
            Object.Destroy(enemyTexture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompactSummaryCanBeDraggedWithinTheSafeArea()
        {
            WavePreviewView view =
                WavePreviewView.CreateRuntime(null, null);
            view.ApplyModel(new WavePreviewModel(
                1,
                "웨이브 1 예고",
                "총 12마리",
                "일반 12",
                "대응 가능",
                false,
                new[]
                {
                    new WavePreviewGroupModel(
                        "약탈자",
                        "일반",
                        12,
                        null,
                        false,
                        false,
                        "상세 정보",
                        false,
                        false)
                }));
            yield return null;
            Canvas.ForceUpdateCanvases();

            RectTransform summary = FindNamedComponent<RectTransform>(
                view.transform,
                "Compact Preview");
            Assert.That(summary, Is.Not.Null);
            Vector2 initialPosition = summary.anchoredPosition;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = 7,
                pointerPress = view.SummaryButton.gameObject,
                delta = new Vector2(260f, -180f)
            };

            view.OnBeginDrag(pointer);
            view.OnDrag(pointer);
            view.OnEndDrag(pointer);

            Assert.That(
                summary.anchoredPosition,
                Is.Not.EqualTo(initialPosition));
            RectTransform parent = summary.parent as RectTransform;
            Assert.That(parent, Is.Not.Null);
            Assert.That(
                summary.anchoredPosition.x,
                Is.GreaterThanOrEqualTo(8f));
            Assert.That(
                summary.anchoredPosition.x + summary.rect.width,
                Is.LessThanOrEqualTo(parent.rect.width - 8f));
            Assert.That(
                -summary.anchoredPosition.y,
                Is.GreaterThanOrEqualTo(8f));
            Assert.That(
                -summary.anchoredPosition.y + summary.rect.height,
                Is.LessThanOrEqualTo(parent.rect.height - 8f));

            view.SummaryButton.onClick.Invoke();
            Assert.That(
                view.IsDetailVisible,
                Is.False,
                "Finishing a drag must not also open the detail panel.");
            yield return null;
            view.SummaryButton.onClick.Invoke();
            Assert.That(view.IsDetailVisible, Is.True);

            Object.Destroy(view.gameObject);
            yield return null;
        }

        private static T FindNamedComponent<T>(
            Transform root,
            string targetName)
            where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i].gameObject.name == targetName)
                {
                    return components[i];
                }
            }

            return null;
        }

        private static int CountNamedChildren(
            Transform root,
            string targetName)
        {
            int count = 0;
            Transform[] children =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == targetName)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
