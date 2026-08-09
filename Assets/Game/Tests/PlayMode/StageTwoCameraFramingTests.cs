using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.Maps;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageTwoCameraFramingTests
    {
        [UnityTest]
        public IEnumerator MaximumZoomOut_CoversViewportWithStageTerrain()
        {
            SceneManager.LoadScene("Stage02", LoadSceneMode.Single);
            yield return null;
            yield return null;

            StageOneBattleController battle =
                Object.FindObjectOfType<StageOneBattleController>();
            Camera stageCamera = Camera.main;
            Assert.That(battle, Is.Not.Null);
            Assert.That(stageCamera, Is.Not.Null);
            WavePreviewView wavePreview = battle.WavePreviewView;
            Assert.That(
                wavePreview,
                Is.Not.Null,
                "Stage 02 must use the shared wave-preview module.");
            Assert.That(wavePreview.IsVisible, Is.True);
            Assert.That(
                wavePreview.GroupButtonCount,
                Is.GreaterThan(0));
            Assert.That(
                wavePreview.TotalEnemyText.text,
                Is.Not.Empty);

            StageOneCameraController controller = battle.CameraController;
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(
                stageCamera.orthographicSize,
                Is.EqualTo(controller.MaximumSize).Within(0.05f));

            float halfHeight = stageCamera.orthographicSize;
            float halfWidth = halfHeight * stageCamera.aspect;
            Bounds bounds = controller.MapBounds;
            Assert.That(
                stageCamera.transform.position.x - halfWidth,
                Is.GreaterThanOrEqualTo(bounds.min.x - 0.05f));
            Assert.That(
                stageCamera.transform.position.x + halfWidth,
                Is.LessThanOrEqualTo(bounds.max.x + 0.05f));
            Assert.That(
                stageCamera.transform.position.y - halfHeight,
                Is.GreaterThanOrEqualTo(bounds.min.y - 0.05f));
            Assert.That(
                stageCamera.transform.position.y + halfHeight,
                Is.LessThanOrEqualTo(bounds.max.y + 0.05f));
        }
    }
}
