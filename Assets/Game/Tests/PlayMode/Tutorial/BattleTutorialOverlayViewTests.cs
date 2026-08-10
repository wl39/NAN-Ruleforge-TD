using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.Tutorial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode.Tutorial
{
    public sealed class BattleTutorialOverlayViewTests
    {
        [UnityTest]
        public IEnumerator UiAnchor_CreatesPassThroughHole_AndFallsBackSafely()
        {
            EventSystem existingEventSystem = EventSystem.current;
            var uiHost = new GameObject(
                "Underlying UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            Canvas underlyingCanvas = uiHost.GetComponent<Canvas>();
            underlyingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            Image target = new GameObject(
                "Tutorial Target",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
                .GetComponent<Image>();
            target.transform.SetParent(uiHost.transform, false);
            target.raycastTarget = true;
            target.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            target.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            target.rectTransform.sizeDelta = new Vector2(180f, 96f);

            TutorialAnchorRegistry registry =
                TutorialAnchorRegistry.CreateRuntime();
            Assert.That(
                registry.RegisterUi("target", target.rectTransform),
                Is.True);

            Font injectedFont = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            BattleTutorialOverlayView view =
                BattleTutorialOverlayView.CreateRuntime(injectedFont);
            view.AnchorRegistry = registry;
            view.Show(new TutorialOverlayContent(
                "target",
                "카드 장착",
                "카드를 슬롯으로 드래그하세요.",
                "5 / 12"));

            yield return null;
            Canvas.ForceUpdateCanvases();
            view.RefreshNow();

            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.HasResolvedAnchor, Is.True);
            Assert.That(view.DimPanelCount, Is.EqualTo(4));
            Assert.That(view.TitleText.font, Is.SameAs(injectedFont));
            Assert.That(view.TitleText.text, Is.EqualTo("카드 장착"));
            Assert.That(view.ProgressText.text, Is.EqualTo("5 / 12"));
            Assert.That(view.SkipButton.gameObject.activeSelf, Is.True);
            Assert.That(
                view.IsScreenPointInsideHole(
                    view.LastHoleScreenRect.center),
                Is.True);
            for (int i = 0; i < view.DimPanelCount; i++)
            {
                Assert.That(view.GetDimPanel(i).raycastTarget, Is.True);
            }

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = view.LastHoleScreenRect.center
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, results);
            Assert.That(
                results.Exists(result => result.gameObject == target.gameObject),
                Is.True,
                "The underlying target must remain clickable through the hole.");
            Assert.That(
                results.Exists(result => IsDimPanel(view, result.gameObject)),
                Is.False,
                "No dim graphic may cover the pass-through hole.");

            int nextRequests = 0;
            int skipRequests = 0;
            view.NextRequested += () => nextRequests++;
            view.SkipRequested += () => skipRequests++;
            view.NextButton.onClick.Invoke();
            view.SkipButton.onClick.Invoke();
            Assert.That(nextRequests, Is.EqualTo(1));
            Assert.That(skipRequests, Is.EqualTo(1));

            bool availabilityLost = false;
            view.AnchorAvailabilityChanged += available =>
                availabilityLost |= !available;
            Object.Destroy(target.gameObject);
            yield return null;
            view.RefreshNow();

            Assert.That(view.HasResolvedAnchor, Is.False);
            Assert.That(view.LastHoleScreenRect, Is.EqualTo(default(Rect)));
            Assert.That(availabilityLost, Is.True);
            Assert.That(view.GetDimPanel(0).gameObject.activeSelf, Is.True);
            Assert.That(view.GetDimPanel(1).gameObject.activeSelf, Is.False);
            for (int i = 0; i < view.DimPanelCount; i++)
            {
                Assert.That(view.GetDimPanel(i).raycastTarget, Is.False,
                    "An unresolved anchor must not lock unrelated input.");
            }
            Assert.That(view.SkipButton.gameObject.activeSelf, Is.True);

            Object.Destroy(view.gameObject);
            Object.Destroy(registry.gameObject);
            Object.Destroy(uiHost);
            if (existingEventSystem == null && EventSystem.current != null)
            {
                Object.Destroy(EventSystem.current.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldAnchor_TracksCameraProjection()
        {
            var cameraHost = new GameObject(
                "Tutorial Camera",
                typeof(Camera));
            Camera camera = cameraHost.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            var target = new GameObject("World Tutorial Target");
            target.transform.position = Vector3.zero;
            TutorialAnchorRegistry registry =
                TutorialAnchorRegistry.CreateRuntime();
            Assert.That(
                registry.RegisterWorld(
                    "world",
                    target.transform,
                    camera,
                    new Vector2(120f, 80f)),
                Is.True);

            yield return null;
            Assert.That(
                registry.TryGetScreenRect("world", out Rect screenRect),
                Is.True);
            Assert.That(screenRect.width, Is.EqualTo(120f).Within(0.1f));
            Assert.That(screenRect.height, Is.EqualTo(80f).Within(0.1f));
            Assert.That(
                screenRect.center.x,
                Is.EqualTo(Screen.width * 0.5f).Within(1f));
            Assert.That(
                screenRect.center.y,
                Is.EqualTo(Screen.height * 0.5f).Within(1f));

            Object.Destroy(registry.gameObject);
            Object.Destroy(target);
            Object.Destroy(cameraHost);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UiGroup_TracksUnionOfLiveTargets()
        {
            var canvasHost = new GameObject(
                "Choice Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            canvasHost.GetComponent<Canvas>().renderMode =
                RenderMode.ScreenSpaceOverlay;
            RectTransform left = CreateChoice(canvasHost.transform, -140f);
            RectTransform right = CreateChoice(canvasHost.transform, 140f);
            TutorialAnchorRegistry registry =
                TutorialAnchorRegistry.CreateRuntime();

            Assert.That(
                registry.RegisterUiGroup(
                    "choices",
                    new[] { left, right }),
                Is.True);
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.That(
                registry.TryGetScreenRect("choices", out Rect union),
                Is.True);
            Vector2 leftCenter = RectTransformUtility.WorldToScreenPoint(
                null,
                left.position);
            Vector2 rightCenter = RectTransformUtility.WorldToScreenPoint(
                null,
                right.position);
            Assert.That(union.Contains(leftCenter), Is.True);
            Assert.That(union.Contains(rightCenter), Is.True);

            Object.Destroy(right.gameObject);
            yield return null;
            Assert.That(
                registry.TryGetScreenRect("choices", out Rect remaining),
                Is.True);
            Assert.That(remaining.Contains(leftCenter), Is.True);
            Assert.That(remaining.width, Is.LessThan(union.width));

            Object.Destroy(registry.gameObject);
            Object.Destroy(canvasHost);
            yield return null;
        }

        private static RectTransform CreateChoice(
            Transform parent,
            float anchoredX)
        {
            Image image = new GameObject(
                "Choice",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition =
                new Vector2(anchoredX, 0f);
            image.rectTransform.sizeDelta = new Vector2(120f, 80f);
            return image.rectTransform;
        }

        private static bool IsDimPanel(
            BattleTutorialOverlayView view,
            GameObject candidate)
        {
            for (int i = 0; i < view.DimPanelCount; i++)
            {
                if (view.GetDimPanel(i).gameObject == candidate)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
