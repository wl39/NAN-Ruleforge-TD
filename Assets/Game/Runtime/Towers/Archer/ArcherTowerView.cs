using System;
using UnityEngine;

namespace RuleforgeTD.Towers.Archer
{
    [DisallowMultipleComponent]
    public sealed class ArcherTowerView : MonoBehaviour
    {
        // CraftPix tower frames are imported at 48 PPU. Their visible base
        // and the build-site image both occupy a 62 x 61 px footprint. Since
        // the site is centered on its root, its exact lower half is 30.5 px.
        private const float AuthoredGroundAnchorPixels = 30.5f;
        // The 62 px tower footprint occupies x=5...66 inside a 70 px frame.
        // Its visual bounds are therefore one source pixel right of the
        // centered pivot; move the complete tower presentation back left.
        private const float AuthoredHorizontalAlignmentPixels = 1f;
        private const float AuthoredPixelsPerUnit = 48f;

        public enum TowerAnimationMode
        {
            Idle,
            Upgrade
        }

        [SerializeField, Range(1, 7)] private int level = 1;
        [SerializeField, Range(1, 3)] private int unitTier = 1;
        [SerializeField] private bool hasOpenRoof = true;
        [SerializeField] private SpriteRenderer towerRenderer;
        [SerializeField] private GameObject archerRoot;
        [SerializeField] private DirectionalArcherAnimator[] archers =
            Array.Empty<DirectionalArcherAnimator>();
        [SerializeField] private Sprite[] idleFrames = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] upgradeFrames = Array.Empty<Sprite>();
        [SerializeField, Min(0.04f)] private float idleFrameDuration = 0.12f;
        [SerializeField, Min(0.04f)] private float upgradeFrameDuration = 0.16f;
        [SerializeField, Min(0.1f)] private float archerDropHeight = 1.65f;
        [SerializeField, Min(0.1f)] private float archerDropDuration = 0.34f;
        [SerializeField, Min(0f)] private float archerDropStagger = 0.055f;
        [SerializeField, Min(0.04f)] private float archerLandingBounceDuration = 0.14f;
        [SerializeField, Min(0f)] private float archerLandingBounceHeight = 0.08f;
        private TowerAnimationMode mode = TowerAnimationMode.Idle;
        private int currentFrameIndex;
        private int idleCycle;
        private float frameElapsed;
        private float currentFrameDuration;
        private bool archerEventsHooked;
        private Vector3[] archerSeatPositions = Array.Empty<Vector3>();
        private float archerLandingElapsed;
        private bool isArcherLanding;
        private bool archersVisible;
        private int nextProjectileArcherIndex;
        private int[] archerTargetSlots = Array.Empty<int>();
        private int aimedTargetCount;
        private bool blueprintPreviewAnimationEnabled;
        private bool visualAuthoringPositionsCaptured;
        private bool visibleBaseAlignmentEnabled;
        private Vector3 towerRendererAuthoredLocalPosition;
        private Vector3 archerRootAuthoredLocalPosition;
        private float visibleBaseOffsetX;
        private float visibleBaseOffsetY;

        public event Action<Vector3, Vector2, int> ArrowRequested;
        public event Action<int, Vector3, Vector2, int>
            ArrowRequestedForTargetSlot;

        public int Level => level;
        public int UnitTier => unitTier;
        public bool HasOpenRoof => hasOpenRoof;
        public int ArcherCount => archers == null ? 0 : archers.Length;
        public int IdleFrameCount => idleFrames == null ? 0 : idleFrames.Length;
        public float IdleFrameDuration => idleFrameDuration;
        public int AimedTargetCount => aimedTargetCount;
        public int UpgradeFrameCount => upgradeFrames == null ? 0 : upgradeFrames.Length;
        public int CurrentFrameIndex => currentFrameIndex;
        public int IdleCycle => idleCycle;
        public TowerAnimationMode Mode => mode;
        public bool IsUpgrading => mode == TowerAnimationMode.Upgrade;
        public bool IsArcherLanding => isArcherLanding;
        public bool AreArchersVisible =>
            archersVisible &&
            archerRoot != null &&
            archerRoot.activeInHierarchy;
        public SpriteRenderer TowerRenderer => towerRenderer;
        public bool IsBlueprintPreviewAnimationEnabled =>
            blueprintPreviewAnimationEnabled;
        public bool IsVisibleBaseAlignmentEnabled =>
            visibleBaseAlignmentEnabled;
        public float VisibleBaseOffsetX => visibleBaseOffsetX;
        public float VisibleBaseOffsetY => visibleBaseOffsetY;
        public Vector3 TowerVisualCenter => towerRenderer != null &&
                                            towerRenderer.sprite != null
            ? towerRenderer.bounds.center
            : transform.position;
        public float VisibleBaseWorldY =>
            GetVisibleBaseWorldY();

        private void Awake()
        {
            CaptureVisualAuthoringPositions();
            CaptureArcherSeatPositions();
            HookArcherEvents();
            RestartIdle();
        }

        private void OnEnable()
        {
            HookArcherEvents();
            RestartIdle();
        }

        private void OnDisable()
        {
            SetBlueprintPreviewAnimation(false);
            ClearDistinctTargets();
            ResetArcherLanding(true);
            UnhookArcherEvents();
        }

        private void Update()
        {
            float animationDeltaTime =
                blueprintPreviewAnimationEnabled
                    ? Time.unscaledDeltaTime
                    : Time.deltaTime;
            UpdateArcherLanding(animationDeltaTime);

            if (towerRenderer == null)
            {
                return;
            }

            frameElapsed += animationDeltaTime;
            if (frameElapsed < currentFrameDuration)
            {
                return;
            }

            frameElapsed -= currentFrameDuration;
            if (mode == TowerAnimationMode.Upgrade)
            {
                AdvanceUpgrade();
            }
            else
            {
                AdvanceIdle();
            }
        }

        public void Configure(
            int towerLevel,
            int archerUnitTier,
            bool openRoof,
            SpriteRenderer bodyRenderer,
            GameObject unitsRoot,
            DirectionalArcherAnimator[] towerArchers,
            Sprite[] towerIdleFrames,
            Sprite[] towerUpgradeFrames)
        {
            UnhookArcherEvents();
            level = Mathf.Clamp(towerLevel, 1, 7);
            unitTier = Mathf.Clamp(archerUnitTier, 1, 3);
            hasOpenRoof = openRoof;
            towerRenderer = bodyRenderer;
            archerRoot = unitsRoot;
            archers = towerArchers ?? Array.Empty<DirectionalArcherAnimator>();
            idleFrames = towerIdleFrames ?? Array.Empty<Sprite>();
            upgradeFrames = towerUpgradeFrames ?? Array.Empty<Sprite>();

            CaptureVisualAuthoringPositions();
            CaptureArcherSeatPositions();
            HookArcherEvents();
            RestartIdle();
        }

        public bool PlayUpgrade()
        {
            if (upgradeFrames == null || upgradeFrames.Length == 0)
            {
                return false;
            }

            mode = TowerAnimationMode.Upgrade;
            ClearDistinctTargets();
            currentFrameIndex = 0;
            frameElapsed = 0f;
            currentFrameDuration = upgradeFrameDuration;
            ResetArcherLanding(true);
            SetArchersVisible(false);
            ApplyTowerSprite(upgradeFrames[0]);
            return true;
        }

        /// <summary>
        /// Advances only this selected tower's visual animation on unscaled
        /// time. GameSimulation and every other world presentation remain
        /// paused; crew animators are forced to safe idle loops.
        /// </summary>
        public void SetBlueprintPreviewAnimation(bool enabled)
        {
            if (blueprintPreviewAnimationEnabled == enabled)
            {
                return;
            }

            blueprintPreviewAnimationEnabled = enabled;
            if (enabled)
            {
                ClearDistinctTargets();
            }

            if (archers == null)
            {
                return;
            }

            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null)
                {
                    archers[i].SetBlueprintPreviewAnimation(enabled);
                }
            }
        }

        public int PlayVolley()
        {
            if (IsUpgrading ||
                IsArcherLanding ||
                archers == null ||
                archers.Length == 0)
            {
                return 0;
            }

            EnsureArcherTargetSlotCapacity();
            int started = 0;
            for (int i = 0; i < archers.Length; i++)
            {
                bool hasDistinctTarget =
                    aimedTargetCount <= 0 ||
                    archerTargetSlots[i] >= 0;
                if (hasDistinctTarget &&
                    archers[i] != null &&
                    archers[i].PlayAttack())
                {
                    started++;
                }
            }

            return started;
        }

        /// <summary>
        /// Aligns the authored ground-footprint point of every tower frame
        /// with the prefab root. The root remains the deterministic build
        /// point, while the body, crew, and projectile origins move together.
        /// </summary>
        public void EnableVisibleBaseAlignment()
        {
            CaptureVisualAuthoringPositions();
            visibleBaseAlignmentEnabled = true;
            ApplyVisibleBaseAlignment();
        }

        public int AimAt(Vector3 worldPosition)
        {
            if (archers == null || archers.Length == 0)
            {
                return 0;
            }

            EnsureArcherTargetSlotCapacity();
            int aimed = 0;
            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] == null)
                {
                    continue;
                }

                Vector2 direction =
                    worldPosition - archers[i].transform.position;
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                archers[i].SetAim(direction);
                archerTargetSlots[i] = 0;
                aimed++;
            }

            aimedTargetCount = aimed > 0 ? 1 : 0;
            return aimed;
        }

        /// <summary>
        /// Assigns each available target slot to a different archer. The lead
        /// archer matches the next authored projectile origin, so simulation
        /// projectiles and bow release poses keep the same target ordering.
        /// </summary>
        public int AimAtDistinctTargets(
            Vector3[] worldPositions,
            int targetCount)
        {
            if (archers == null ||
                archers.Length == 0 ||
                worldPositions == null)
            {
                ClearDistinctTargets();
                return 0;
            }

            EnsureArcherTargetSlotCapacity();
            for (int i = 0; i < archerTargetSlots.Length; i++)
            {
                archerTargetSlots[i] = -1;
            }

            int count = Mathf.Clamp(
                targetCount,
                0,
                Mathf.Min(
                    archers.Length,
                    worldPositions.Length));
            int leadIndex = PositiveModulo(
                nextProjectileArcherIndex,
                archers.Length);
            int aimed = 0;
            for (int slot = 0; slot < count; slot++)
            {
                int archerIndex = PositiveModulo(
                    leadIndex + slot,
                    archers.Length);
                DirectionalArcherAnimator archer =
                    archers[archerIndex];
                if (archer == null)
                {
                    continue;
                }

                Vector2 direction =
                    worldPositions[slot] -
                    archer.transform.position;
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                archer.SetAim(direction);
                archerTargetSlots[archerIndex] = slot;
                aimed++;
            }

            aimedTargetCount = aimed;
            return aimed;
        }

        /// <summary>
        /// Returns an authored bow-tip origin for visible crews. Closed-roof,
        /// crewless, upgrading, and landing towers fall back to the rendered
        /// tower body's center.
        /// </summary>
        public Vector3 GetNextProjectileLaunchOrigin()
        {
            if (!AreArchersVisible ||
                IsUpgrading ||
                IsArcherLanding ||
                archers == null ||
                archers.Length == 0)
            {
                return TowerVisualCenter;
            }

            for (int attempt = 0;
                 attempt < archers.Length;
                 attempt++)
            {
                int index = PositiveModulo(
                    nextProjectileArcherIndex++,
                    archers.Length);
                DirectionalArcherAnimator archer = archers[index];
                bool hasDistinctTarget =
                    aimedTargetCount <= 0 ||
                    archerTargetSlots[index] >= 0;
                if (archer != null && hasDistinctTarget)
                {
                    return archer.ProjectileOrigin;
                }
            }

            return TowerVisualCenter;
        }

        public void RestartIdle()
        {
            mode = TowerAnimationMode.Idle;
            ClearDistinctTargets();
            currentFrameIndex = 0;
            frameElapsed = 0f;
            idleCycle = 0;
            currentFrameDuration = idleFrameDuration;
            ResetArcherLanding(true);
            SetArchersVisible(hasOpenRoof);
            if (idleFrames != null && idleFrames.Length > 0)
            {
                ApplyTowerSprite(idleFrames[0]);
            }
        }

        public static bool LevelHasOpenRoof(int towerLevel)
        {
            return towerLevel >= 1 &&
                   towerLevel <= 7 &&
                   towerLevel != 4 &&
                   towerLevel != 7;
        }

        public static int GetDefaultUnitTier(int towerLevel)
        {
            if (towerLevel <= 2)
            {
                return 1;
            }

            return towerLevel <= 5 ? 2 : 3;
        }

        public static int GetDefaultArcherCount(int towerLevel)
        {
            switch (towerLevel)
            {
                case 1:
                    return 1;
                case 2:
                case 3:
                case 4:
                case 5:
                    return 2;
                case 6:
                case 7:
                    return 3;
                default:
                    return 0;
            }
        }

        private void AdvanceIdle()
        {
            if (idleFrames == null || idleFrames.Length <= 1)
            {
                currentFrameIndex = 0;
                frameElapsed = 0f;
                currentFrameDuration = idleFrameDuration;
                return;
            }

            currentFrameIndex++;
            if (currentFrameIndex >= idleFrames.Length)
            {
                currentFrameIndex = 0;
                idleCycle++;
            }

            currentFrameDuration = idleFrameDuration;
            ApplyTowerSprite(idleFrames[currentFrameIndex]);
        }

        private void AdvanceUpgrade()
        {
            currentFrameIndex++;
            if (upgradeFrames == null || currentFrameIndex >= upgradeFrames.Length)
            {
                EnterIdleAfterUpgrade();
                return;
            }

            currentFrameDuration = upgradeFrameDuration;
            ApplyTowerSprite(upgradeFrames[currentFrameIndex]);
        }

        private void EnterIdleAfterUpgrade()
        {
            mode = TowerAnimationMode.Idle;
            currentFrameIndex = 0;
            frameElapsed = 0f;
            currentFrameDuration = idleFrameDuration;
            if (idleFrames != null && idleFrames.Length > 0)
            {
                ApplyTowerSprite(idleFrames[0]);
            }

            BeginArcherLanding();
        }

        private void ApplyTowerSprite(Sprite sprite)
        {
            if (towerRenderer != null && sprite != null)
            {
                towerRenderer.sprite = sprite;
                ApplyVisibleBaseAlignment();
            }
        }

        private void CaptureVisualAuthoringPositions()
        {
            if (visualAuthoringPositionsCaptured)
            {
                return;
            }

            if (towerRenderer != null)
            {
                towerRendererAuthoredLocalPosition =
                    towerRenderer.transform.localPosition;
            }

            if (archerRoot != null)
            {
                archerRootAuthoredLocalPosition =
                    archerRoot.transform.localPosition;
            }

            visualAuthoringPositionsCaptured = true;
        }

        private void ApplyVisibleBaseAlignment()
        {
            if (!visibleBaseAlignmentEnabled ||
                towerRenderer == null ||
                towerRenderer.sprite == null)
            {
                return;
            }

            CaptureVisualAuthoringPositions();
            visibleBaseOffsetX =
                -AuthoredHorizontalAlignmentPixels /
                AuthoredPixelsPerUnit;
            visibleBaseOffsetY =
                -GetSpriteMeshMinimumY(towerRenderer.sprite) -
                AuthoredGroundAnchorPixels /
                AuthoredPixelsPerUnit;
            Vector3 bodyPosition =
                towerRendererAuthoredLocalPosition;
            bodyPosition.x += visibleBaseOffsetX;
            bodyPosition.y += visibleBaseOffsetY;
            towerRenderer.transform.localPosition = bodyPosition;

            if (archerRoot != null)
            {
                Vector3 crewPosition =
                    archerRootAuthoredLocalPosition;
                crewPosition.x += visibleBaseOffsetX;
                crewPosition.y += visibleBaseOffsetY;
                archerRoot.transform.localPosition = crewPosition;
            }
        }

        private float GetVisibleBaseWorldY()
        {
            if (towerRenderer == null ||
                towerRenderer.sprite == null)
            {
                return transform.position.y;
            }

            Vector2[] vertices =
                towerRenderer.sprite.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return towerRenderer.bounds.min.y;
            }

            float minimum = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                float worldY = towerRenderer.transform
                    .TransformPoint(vertices[i]).y;
                minimum = Mathf.Min(minimum, worldY);
            }

            return float.IsPositiveInfinity(minimum)
                ? towerRenderer.bounds.min.y
                : minimum;
        }

        private static float GetSpriteMeshMinimumY(
            Sprite sprite)
        {
            Vector2[] vertices = sprite == null
                ? null
                : sprite.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return sprite == null
                    ? 0f
                    : sprite.bounds.min.y;
            }

            float minimum = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                minimum = Mathf.Min(
                    minimum,
                    vertices[i].y);
            }

            return float.IsPositiveInfinity(minimum)
                ? 0f
                : minimum;
        }

        private void EnsureArcherTargetSlotCapacity()
        {
            int required = archers == null
                ? 0
                : archers.Length;
            if (archerTargetSlots == null ||
                archerTargetSlots.Length != required)
            {
                archerTargetSlots =
                    new int[required];
                for (int i = 0; i < required; i++)
                {
                    archerTargetSlots[i] = -1;
                }
            }
        }

        private void ClearDistinctTargets()
        {
            aimedTargetCount = 0;
            if (archerTargetSlots == null)
            {
                return;
            }

            for (int i = 0; i < archerTargetSlots.Length; i++)
            {
                archerTargetSlots[i] = -1;
            }
        }

        private void SetArchersVisible(bool visible)
        {
            bool hasArcherCrew = archers != null && archers.Length > 0;
            if (archerRoot != null)
            {
                archerRoot.SetActive(hasArcherCrew);
            }

            archersVisible = hasArcherCrew && visible && hasOpenRoof;
            if (archers == null)
            {
                return;
            }

            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] == null)
                {
                    continue;
                }

                SpriteRenderer renderer =
                    archers[i].GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = archersVisible;
                }
            }
        }

        private void CaptureArcherSeatPositions()
        {
            if (archers == null || archers.Length == 0)
            {
                archerSeatPositions = Array.Empty<Vector3>();
                return;
            }

            archerSeatPositions = new Vector3[archers.Length];
            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null)
                {
                    archerSeatPositions[i] = archers[i].transform.localPosition;
                }
            }
        }

        private void BeginArcherLanding()
        {
            if (!hasOpenRoof || archers == null || archers.Length == 0)
            {
                ResetArcherLanding(true);
                SetArchersVisible(false);
                return;
            }

            if (archerSeatPositions == null ||
                archerSeatPositions.Length != archers.Length)
            {
                CaptureArcherSeatPositions();
            }

            archerLandingElapsed = 0f;
            isArcherLanding = true;
            SetArchersVisible(true);
            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null)
                {
                    archers[i].transform.localPosition =
                        archerSeatPositions[i] + Vector3.up * archerDropHeight;
                }
            }
        }

        private void UpdateArcherLanding(float animationDeltaTime)
        {
            if (!isArcherLanding || archers == null)
            {
                return;
            }

            archerLandingElapsed += animationDeltaTime;
            bool allLanded = true;
            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] == null)
                {
                    continue;
                }

                float localTime = archerLandingElapsed - i * archerDropStagger;
                float verticalOffset;
                if (localTime <= 0f)
                {
                    verticalOffset = archerDropHeight;
                    allLanded = false;
                }
                else if (localTime < archerDropDuration)
                {
                    float progress = Mathf.Clamp01(localTime / archerDropDuration);
                    verticalOffset = archerDropHeight * (1f - progress * progress);
                    allLanded = false;
                }
                else if (localTime <
                         archerDropDuration + archerLandingBounceDuration)
                {
                    float bounceProgress = Mathf.Clamp01(
                        (localTime - archerDropDuration) /
                        archerLandingBounceDuration);
                    verticalOffset =
                        Mathf.Sin(bounceProgress * Mathf.PI) *
                        archerLandingBounceHeight *
                        (1f - bounceProgress);
                    allLanded = false;
                }
                else
                {
                    verticalOffset = 0f;
                }

                archers[i].transform.localPosition =
                    archerSeatPositions[i] + Vector3.up * verticalOffset;
            }

            if (allLanded)
            {
                ResetArcherLanding(true);
            }
        }

        private void ResetArcherLanding(bool placeAtSeats)
        {
            isArcherLanding = false;
            archerLandingElapsed = 0f;
            if (!placeAtSeats ||
                archers == null ||
                archerSeatPositions == null ||
                archerSeatPositions.Length != archers.Length)
            {
                return;
            }

            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null)
                {
                    archers[i].transform.localPosition = archerSeatPositions[i];
                }
            }
        }

        private void HookArcherEvents()
        {
            if (archerEventsHooked || archers == null)
            {
                return;
            }

            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null)
                {
                    archers[i].ArrowReleased += HandleArrowReleased;
                }
            }

            archerEventsHooked = true;
        }

        private void UnhookArcherEvents()
        {
            if (!archerEventsHooked || archers == null)
            {
                return;
            }

            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null)
                {
                    archers[i].ArrowReleased -= HandleArrowReleased;
                }
            }

            archerEventsHooked = false;
        }

        private void HandleArrowReleased(DirectionalArcherAnimator archer)
        {
            if (IsUpgrading || IsArcherLanding)
            {
                return;
            }

            Vector2 direction = archer.AimDirection.sqrMagnitude <= 0.000001f
                ? Vector2.down
                : archer.AimDirection.normalized;
            Vector3 origin = archer.ProjectileOrigin +
                             (Vector3)(direction * 0.08f);
            ArrowRequested?.Invoke(origin, direction, unitTier);
            int archerIndex =
                Array.IndexOf(archers, archer);
            int targetSlot =
                archerIndex >= 0 &&
                archerIndex < archerTargetSlots.Length
                    ? archerTargetSlots[archerIndex]
                    : -1;
            ArrowRequestedForTargetSlot?.Invoke(
                targetSlot,
                origin,
                direction,
                unitTier);
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

    }
}
