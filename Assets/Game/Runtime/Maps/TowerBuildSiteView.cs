using System;
using RuleforgeTD.Battle;
using UnityEngine;

namespace RuleforgeTD.Maps
{
    public enum TowerBuildSiteVisualState
    {
        Locked = 0,
        Available = 1,
        Occupied = 2
    }

    /// <summary>
    /// Presentation for a deterministic build point. Gold spending and unlock
    /// authority belong to GameSimulation; this view only reflects snapshots.
    /// </summary>
    [ExecuteAlways]
    public sealed class TowerBuildSiteView : MonoBehaviour
    {
        public const float AuthoredVisualScale = 1.1f;

        [SerializeField]
        private int buildPointIndex = -1;

        [SerializeField]
        [Min(0)]
        private int unlockCost;

        [SerializeField]
        private Sprite availableSprite;

        [SerializeField]
        private Sprite lockedSprite;

        [SerializeField]
        private SpriteRenderer targetRenderer;

        [SerializeField]
        private Collider2D inputCollider;

        [SerializeField]
        private TowerBuildSiteVisualState state =
            TowerBuildSiteVisualState.Available;

        public int BuildPointIndex => buildPointIndex;
        public int UnlockCost => unlockCost;
        public TowerBuildSiteVisualState State => state;
        public bool CanBuild => state == TowerBuildSiteVisualState.Available;

        public event Action<TowerBuildSiteView> Clicked;

        public void ConfigureAuthoring(
            int pointIndex,
            int requiredResource,
            Sprite available,
            Sprite locked,
            bool initiallyUnlocked)
        {
            buildPointIndex = pointIndex;
            unlockCost = Mathf.Max(0, requiredResource);
            availableSprite = available;
            lockedSprite = locked;
            state = initiallyUnlocked
                ? TowerBuildSiteVisualState.Available
                : TowerBuildSiteVisualState.Locked;
            RefreshVisual();
        }

        public void ApplySimulationState(bool unlocked, bool occupied)
        {
            state = occupied
                ? TowerBuildSiteVisualState.Occupied
                : unlocked
                    ? TowerBuildSiteVisualState.Available
                    : TowerBuildSiteVisualState.Locked;
            RefreshVisual();
        }

        public bool RequestBuild()
        {
            if (!Application.isPlaying || !CanBuild)
            {
                return false;
            }

            Clicked?.Invoke(this);
            return true;
        }

        private void Reset()
        {
            targetRenderer = GetComponent<SpriteRenderer>();
            inputCollider = GetComponent<Collider2D>();
            RefreshVisual();
        }

        private void OnEnable()
        {
            RefreshVisual();
        }

        private void OnValidate()
        {
            unlockCost = Mathf.Max(0, unlockCost);
            RefreshVisual();
        }

        private void OnMouseUpAsButton()
        {
            if (StageOneCameraController.ShouldSuppressWorldClick)
            {
                return;
            }

            RequestBuild();
        }

        private void RefreshVisual()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            if (inputCollider == null)
            {
                inputCollider = GetComponent<Collider2D>();
            }

            // 점유된 건설 지점은 타워 아래에 그대로 남아 있으므로 렌더러만
            // 숨기면 이 콜라이더가 타워 하단의 마우스 이벤트를 먼저 받는다.
            // 보이는 타워 전체가 TowerSelectionView로 입력되도록 물리 입력도
            // 함께 끄고, 다시 빈 지점이 되면 복구한다.
            if (inputCollider != null)
            {
                inputCollider.enabled =
                    state != TowerBuildSiteVisualState.Occupied;
            }

            if (targetRenderer == null)
            {
                return;
            }

            targetRenderer.sprite = state ==
                TowerBuildSiteVisualState.Locked
                ? lockedSprite
                : availableSprite;
            targetRenderer.enabled =
                state != TowerBuildSiteVisualState.Occupied;
        }
    }
}
