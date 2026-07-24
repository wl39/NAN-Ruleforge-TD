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
        private TowerBuildSiteVisualState state =
            TowerBuildSiteVisualState.Available;

        public int BuildPointIndex => buildPointIndex;
        public int UnlockCost => unlockCost;
        public TowerBuildSiteVisualState State => state;
        public bool CanBuild => state == TowerBuildSiteVisualState.Available;

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

        private void Reset()
        {
            targetRenderer = GetComponent<SpriteRenderer>();
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

        private void RefreshVisual()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
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
