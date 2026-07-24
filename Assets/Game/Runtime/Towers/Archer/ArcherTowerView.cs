using System;
using UnityEngine;

namespace RuleforgeTD.Towers.Archer
{
    [DisallowMultipleComponent]
    public sealed class ArcherTowerView : MonoBehaviour
    {
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

        public event Action<Vector3, Vector2, int> ArrowRequested;

        public int Level => level;
        public int UnitTier => unitTier;
        public bool HasOpenRoof => hasOpenRoof;
        public int ArcherCount => archers == null ? 0 : archers.Length;
        public int IdleFrameCount => idleFrames == null ? 0 : idleFrames.Length;
        public float IdleFrameDuration => idleFrameDuration;
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

        private void Awake()
        {
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
            ResetArcherLanding(true);
            UnhookArcherEvents();
        }

        private void Update()
        {
            UpdateArcherLanding();

            if (towerRenderer == null)
            {
                return;
            }

            frameElapsed += Time.deltaTime;
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
            currentFrameIndex = 0;
            frameElapsed = 0f;
            currentFrameDuration = upgradeFrameDuration;
            ResetArcherLanding(true);
            SetArchersVisible(false);
            ApplyTowerSprite(upgradeFrames[0]);
            return true;
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

            int started = 0;
            for (int i = 0; i < archers.Length; i++)
            {
                if (archers[i] != null && archers[i].PlayAttack())
                {
                    started++;
                }
            }

            return started;
        }

        public int AimAt(Vector3 worldPosition)
        {
            if (archers == null || archers.Length == 0)
            {
                return 0;
            }

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
                aimed++;
            }

            return aimed;
        }

        public void RestartIdle()
        {
            mode = TowerAnimationMode.Idle;
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

        private void UpdateArcherLanding()
        {
            if (!isArcherLanding || archers == null)
            {
                return;
            }

            archerLandingElapsed += Time.deltaTime;
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
        }

    }
}
