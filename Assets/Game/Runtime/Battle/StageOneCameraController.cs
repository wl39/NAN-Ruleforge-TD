using System;
using System.Collections.Generic;
using RuleforgeTD.Maps;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Keeps the battle camera aligned to the authored tile bounds while
    /// allowing cursor-centred wheel/pinch zoom and pointer panning. Maximum
    /// zoom-out covers the viewport so no mapless letterbox or pillarbox is
    /// exposed when the stage and browser use different aspect ratios.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class StageOneCameraController : MonoBehaviour
    {
        private const float DragThresholdPixels = 10f;
        private const int MousePointerId = -1;

        private static int suppressWorldClickUntilFrame = -1;
        private static readonly List<RaycastResult> UiRaycastResults =
            new List<RaycastResult>(16);

        [SerializeField, Min(2f)]
        private float preferredMinimumSize = 4.8f;

        [SerializeField, Min(0.1f)]
        private float wheelStep = 0.8f;

        private Camera targetCamera;
        private FieldStageMap stageMap;
        private Bounds mapBounds;
        private float maximumSize;
        private float minimumSize;
        private float lastAspect;
        private Vector3 previousPointerWorld;
        private Vector2 pointerStartScreen;
        private int activeMouseButton = -1;
        private int activeTouchFingerId = -1;
        private int ignoreMouseUntilFrame = -1;
        private bool pointerCandidate;
        private bool panning;
        private bool pinching;
        private bool touchStartedOverUi;
        private bool waitForTouchesToEnd;
        private bool initialized;
        private IStageOneCameraFocusTarget focusTarget;
        private Vector2 focusViewportAnchor =
            new Vector2(0.28f, 0.5f);

        public float MinimumSize => minimumSize;
        public float MaximumSize => maximumSize;
        public Bounds MapBounds => mapBounds;
        public bool IsPanning => panning;
        public bool IsPinching => pinching;
        public bool IsInitialized => initialized;
        public bool IsFocusLocked =>
            focusTarget != null &&
            focusTarget.IsCameraFocusValid;
        public IStageOneCameraFocusTarget FocusTarget =>
            IsFocusLocked ? focusTarget : null;
        public Vector2 FocusViewportAnchor =>
            focusViewportAnchor;
        public event Action<Vector2> BackgroundClicked;
        public static bool ShouldSuppressWorldClick =>
            Time.frameCount <= suppressWorldClickUntilFrame ||
            IsAnyPointerOverUi();

        public void Configure(FieldStageMap sourceStageMap)
        {
            stageMap = sourceStageMap;
            InitializeNow();
        }

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            DisablePixelPerfectOverride();
        }

        private void Update()
        {
            if (!initialized)
            {
                InitializeNow();
                if (!initialized)
                {
                    return;
                }
            }

            if (!Mathf.Approximately(lastAspect, targetCamera.aspect))
            {
                RecalculateLimits(false);
            }

            if (Input.touchCount > 0)
            {
                ignoreMouseUntilFrame = Time.frameCount + 1;
                HandleTouchNavigation();
                return;
            }

            if (activeTouchFingerId >= 0 ||
                pinching ||
                waitForTouchesToEnd)
            {
                ResetTouchState();
            }

            if (Time.frameCount <= ignoreMouseUntilFrame)
            {
                return;
            }

            HandleWheelZoom();
            HandleMousePan();
        }

        private void LateUpdate()
        {
            FollowFocusTarget();
        }

        /// <summary>
        /// Locks composition to a presentation target. The viewport anchor
        /// places the target in the unobscured part of the screen while map
        /// bounds remain authoritative.
        /// </summary>
        public bool SetFocusTarget(
            IStageOneCameraFocusTarget target,
            Vector2 viewportAnchor)
        {
            if (target == null ||
                !target.IsCameraFocusValid)
            {
                return false;
            }

            focusTarget = target;
            focusViewportAnchor = new Vector2(
                Mathf.Clamp(viewportAnchor.x, 0.05f, 0.95f),
                Mathf.Clamp(viewportAnchor.y, 0.05f, 0.95f));
            ResetMousePan();
            ResetTouchState();
            FollowFocusTarget();
            return true;
        }

        public void ReleaseFocus(
            IStageOneCameraFocusTarget expectedTarget = null)
        {
            if (expectedTarget != null &&
                !ReferenceEquals(expectedTarget, focusTarget))
            {
                return;
            }

            focusTarget = null;
            if (initialized && targetCamera != null)
            {
                ClampPosition();
            }
        }

        private void InitializeNow()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            DisablePixelPerfectOverride();
            if (stageMap == null)
            {
                stageMap = FindObjectOfType<FieldStageMap>();
            }

            Tilemap terrain =
                stageMap == null ? null : stageMap.Terrain;
            TilemapRenderer renderer = terrain == null
                ? null
                : terrain.GetComponent<TilemapRenderer>();
            if (targetCamera == null ||
                !targetCamera.orthographic ||
                renderer == null)
            {
                return;
            }

            mapBounds = renderer.bounds;
            if (mapBounds.size.x <= 0.01f ||
                mapBounds.size.y <= 0.01f)
            {
                return;
            }

            initialized = true;
            RecalculateLimits(true);
        }

        private void RecalculateLimits(bool resetView)
        {
            lastAspect = Mathf.Max(0.1f, targetCamera.aspect);
            float fitHeight = mapBounds.extents.y;
            float fitWidth =
                mapBounds.extents.x / lastAspect;
            maximumSize = Mathf.Max(
                0.1f,
                Mathf.Min(fitHeight, fitWidth) - 0.02f);
            minimumSize = Mathf.Min(
                preferredMinimumSize,
                maximumSize);

            if (resetView)
            {
                targetCamera.orthographicSize = maximumSize;
                Vector3 position = targetCamera.transform.position;
                if (stageMap != null &&
                    stageMap.Path != null &&
                    stageMap.Path.WaypointCount > 0)
                {
                    Vector2 spawn =
                        stageMap.Path.GetWorldWaypoint(0);
                    position.x = spawn.x;
                    position.y = spawn.y;
                }
                else
                {
                    position.x = mapBounds.center.x;
                    position.y = mapBounds.center.y;
                }

                targetCamera.transform.position = position;
            }
            else
            {
                targetCamera.orthographicSize = Mathf.Clamp(
                    targetCamera.orthographicSize,
                    minimumSize,
                    maximumSize);
            }

            ClampPosition();
        }

        private void HandleWheelZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) <= 0.001f)
            {
                return;
            }

            if (IsMouseOverUi())
            {
                return;
            }

            Vector3 pointerBefore =
                targetCamera.ScreenToWorldPoint(Input.mousePosition);
            float nextSize = Mathf.Clamp(
                targetCamera.orthographicSize -
                scroll * wheelStep,
                minimumSize,
                maximumSize);
            if (Mathf.Approximately(
                    nextSize,
                    targetCamera.orthographicSize))
            {
                return;
            }

            targetCamera.orthographicSize = nextSize;
            Vector3 pointerAfter =
                targetCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector3 position = targetCamera.transform.position;
            position += pointerBefore - pointerAfter;
            position.z = targetCamera.transform.position.z;
            targetCamera.transform.position = position;
            ClampPosition();
        }

        private void HandleMousePan()
        {
            if (activeMouseButton < 0)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    BeginMousePanCandidate(0);
                }
                else if (Input.GetMouseButtonDown(2))
                {
                    BeginMousePanCandidate(2);
                }
            }

            if (activeMouseButton < 0)
            {
                return;
            }

            bool held = Input.GetMouseButton(activeMouseButton);
            bool released = Input.GetMouseButtonUp(activeMouseButton);
            Vector2 pointerScreen = Input.mousePosition;
            if (pointerCandidate &&
                !panning &&
                CanPan() &&
                Vector2.Distance(pointerStartScreen, pointerScreen) >=
                DragThresholdPixels)
            {
                panning = true;
                MarkWorldClickSuppressed();
            }

            if (panning && (held || released))
            {
                PanToScreenPoint(pointerScreen);
                MarkWorldClickSuppressed();
            }

            if (released || !held)
            {
                if (panning)
                {
                    MarkWorldClickSuppressed();
                }
                else if (released &&
                         activeMouseButton == 0 &&
                         pointerCandidate &&
                         Vector2.Distance(
                             pointerStartScreen,
                             pointerScreen) <
                         DragThresholdPixels)
                {
                    RequestBackgroundClick(
                        pointerScreen,
                        MousePointerId);
                }

                ResetMousePan();
            }
        }

        private void BeginMousePanCandidate(int mouseButton)
        {
            if (IsMouseOverUi())
            {
                return;
            }

            activeMouseButton = mouseButton;
            pointerCandidate = true;
            panning = false;
            pointerStartScreen = Input.mousePosition;
            previousPointerWorld =
                targetCamera.ScreenToWorldPoint(Input.mousePosition);
        }

        private void HandleTouchNavigation()
        {
            if (Input.touchCount >= 2)
            {
                HandlePinch();
                return;
            }

            Touch touch = Input.GetTouch(0);
            if (pinching || waitForTouchesToEnd)
            {
                waitForTouchesToEnd = true;
                MarkWorldClickSuppressed();
                return;
            }

            if (touch.phase == TouchPhase.Began ||
                activeTouchFingerId != touch.fingerId)
            {
                activeTouchFingerId = touch.fingerId;
                touchStartedOverUi = IsTouchOverUi(touch.fingerId);
                pointerCandidate = !touchStartedOverUi;
                panning = false;
                pointerStartScreen = touch.position;
                previousPointerWorld =
                    targetCamera.ScreenToWorldPoint(touch.position);
            }

            if (touch.fingerId != activeTouchFingerId ||
                touchStartedOverUi)
            {
                return;
            }

            if (pointerCandidate &&
                !panning &&
                CanPan() &&
                Vector2.Distance(
                    pointerStartScreen,
                    touch.position) >= DragThresholdPixels)
            {
                panning = true;
                MarkWorldClickSuppressed();
            }

            if (panning &&
                (touch.phase == TouchPhase.Moved ||
                 touch.phase == TouchPhase.Stationary ||
                 touch.phase == TouchPhase.Ended))
            {
                PanToScreenPoint(touch.position);
                MarkWorldClickSuppressed();
            }

            if (touch.phase == TouchPhase.Ended ||
                touch.phase == TouchPhase.Canceled)
            {
                if (panning)
                {
                    MarkWorldClickSuppressed();
                }
                else if (touch.phase == TouchPhase.Ended &&
                         pointerCandidate &&
                         Vector2.Distance(
                             pointerStartScreen,
                             touch.position) <
                         DragThresholdPixels)
                {
                    RequestBackgroundClick(
                        touch.position,
                        touch.fingerId);
                }

                activeTouchFingerId = -1;
                pointerCandidate = false;
                panning = false;
                touchStartedOverUi = false;
            }
        }

        private void HandlePinch()
        {
            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            if (!pinching)
            {
                touchStartedOverUi =
                    touchStartedOverUi ||
                    IsTouchOverUi(first.fingerId) ||
                    IsTouchOverUi(second.fingerId);
                pinching = true;
                panning = false;
                pointerCandidate = false;
            }

            waitForTouchesToEnd = true;
            MarkWorldClickSuppressed();
            if (touchStartedOverUi)
            {
                return;
            }

            Vector2 currentFirst = first.position;
            Vector2 currentSecond = second.position;
            Vector2 previousFirst =
                currentFirst - first.deltaPosition;
            Vector2 previousSecond =
                currentSecond - second.deltaPosition;
            float currentDistance =
                Vector2.Distance(currentFirst, currentSecond);
            float previousDistance =
                Vector2.Distance(previousFirst, previousSecond);
            if (currentDistance <= 0.01f ||
                previousDistance <= 0.01f)
            {
                return;
            }

            Vector2 currentMidpoint =
                (currentFirst + currentSecond) * 0.5f;
            Vector2 previousMidpoint =
                (previousFirst + previousSecond) * 0.5f;
            Vector3 worldBefore =
                targetCamera.ScreenToWorldPoint(previousMidpoint);
            targetCamera.orthographicSize = Mathf.Clamp(
                targetCamera.orthographicSize *
                previousDistance /
                currentDistance,
                minimumSize,
                maximumSize);
            Vector3 worldAfter =
                targetCamera.ScreenToWorldPoint(currentMidpoint);
            Vector3 position = targetCamera.transform.position;
            position += worldBefore - worldAfter;
            position.z = targetCamera.transform.position.z;
            targetCamera.transform.position = position;
            ClampPosition();
        }

        private void PanToScreenPoint(Vector2 screenPoint)
        {
            Vector3 currentPointerWorld =
                targetCamera.ScreenToWorldPoint(screenPoint);
            Vector3 position = targetCamera.transform.position;
            position += previousPointerWorld - currentPointerWorld;
            position.z = targetCamera.transform.position.z;
            targetCamera.transform.position = position;
            ClampPosition();
            previousPointerWorld =
                targetCamera.ScreenToWorldPoint(screenPoint);
        }

        private bool CanPan()
        {
            if (IsFocusLocked)
            {
                return false;
            }

            float halfHeight = targetCamera.orthographicSize;
            float halfWidth = halfHeight * lastAspect;
            return halfWidth < mapBounds.extents.x - 0.01f ||
                halfHeight < mapBounds.extents.y - 0.01f;
        }

        private void ResetMousePan()
        {
            activeMouseButton = -1;
            pointerCandidate = false;
            panning = false;
        }

        private void ResetTouchState()
        {
            activeTouchFingerId = -1;
            pointerCandidate = false;
            panning = false;
            pinching = false;
            touchStartedOverUi = false;
            waitForTouchesToEnd = false;
        }

        private static void MarkWorldClickSuppressed()
        {
            suppressWorldClickUntilFrame = Mathf.Max(
                suppressWorldClickUntilFrame,
                Time.frameCount + 1);
        }

        private static bool IsMouseOverUi()
        {
            return IsScreenPointOverUi(
                Input.mousePosition,
                MousePointerId);
        }

        private static bool IsAnyPointerOverUi()
        {
            if (IsMouseOverUi())
            {
                return true;
            }

            for (int touchIndex = 0;
                 touchIndex < Input.touchCount;
                 touchIndex++)
            {
                if (IsTouchOverUi(
                        Input.GetTouch(touchIndex).fingerId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTouchOverUi(int fingerId)
        {
            if (fingerId < 0)
            {
                return false;
            }

            for (int touchIndex = 0;
                 touchIndex < Input.touchCount;
                 touchIndex++)
            {
                Touch touch = Input.GetTouch(touchIndex);
                if (touch.fingerId == fingerId)
                {
                    return IsScreenPointOverUi(
                        touch.position,
                        fingerId);
                }
            }

            return false;
        }

        /// <summary>
        /// Emits a click-away request only for an unoccupied part of the
        /// battlefield. UI, towers, and build sites keep ownership of their
        /// clicks, while terrain and other non-interactive world visuals are
        /// treated as background.
        /// </summary>
        public bool RequestBackgroundClick(Vector2 screenPosition)
        {
            return RequestBackgroundClick(
                screenPosition,
                MousePointerId);
        }

        private bool RequestBackgroundClick(
            Vector2 screenPosition,
            int pointerId)
        {
            if (!Application.isPlaying ||
                !initialized ||
                targetCamera == null ||
                IsScreenPointOverUi(screenPosition, pointerId) ||
                IsInteractiveWorldTarget(screenPosition))
            {
                return false;
            }

            BackgroundClicked?.Invoke(screenPosition);
            return true;
        }

        private bool IsInteractiveWorldTarget(
            Vector2 screenPosition)
        {
            Vector3 worldPosition =
                targetCamera.ScreenToWorldPoint(screenPosition);
            Collider2D[] hits =
                Physics2D.OverlapPointAll(worldPosition);
            for (int hitIndex = 0;
                 hitIndex < hits.Length;
                 hitIndex++)
            {
                Collider2D hit = hits[hitIndex];
                if (hit == null)
                {
                    continue;
                }

                if (hit.GetComponentInParent<TowerSelectionView>() !=
                        null ||
                    hit.GetComponentInParent<TowerBuildSiteView>() !=
                        null)
                {
                    return true;
                }

                EnemySelectionView enemy =
                    hit.GetComponentInParent<
                        EnemySelectionView>();
                if (enemy != null &&
                    enemy.IsCameraFocusValid)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Uses a fresh uGUI raycast instead of EventSystem's cached
        /// pointerEnter value. A reward button can be deactivated from inside
        /// its click callback, leaving that cache pointed at the hidden
        /// overlay until another input-module update. The live raycast makes
        /// the very next tower click observe the UI that is actually visible.
        /// </summary>
        private static bool IsScreenPointOverUi(
            Vector2 screenPosition,
            int pointerId)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var pointer = new PointerEventData(eventSystem)
            {
                pointerId = pointerId,
                position = screenPosition
            };
            UiRaycastResults.Clear();
            eventSystem.RaycastAll(pointer, UiRaycastResults);
            for (int i = 0; i < UiRaycastResults.Count; i++)
            {
                GameObject hit = UiRaycastResults[i].gameObject;
                if (hit != null && hit.activeInHierarchy)
                {
                    UiRaycastResults.Clear();
                    return true;
                }
            }

            UiRaycastResults.Clear();
            return false;
        }

        private void ClampPosition()
        {
            float halfHeight = targetCamera.orthographicSize;
            float halfWidth = halfHeight * lastAspect;
            Vector3 position = targetCamera.transform.position;
            position.x = ClampAxis(
                position.x,
                mapBounds.min.x + halfWidth,
                mapBounds.max.x - halfWidth,
                mapBounds.center.x);
            position.y = ClampAxis(
                position.y,
                mapBounds.min.y + halfHeight,
                mapBounds.max.y - halfHeight,
                mapBounds.center.y);
            targetCamera.transform.position = position;
        }

        private void FollowFocusTarget()
        {
            if (focusTarget == null)
            {
                return;
            }

            if (!focusTarget.IsCameraFocusValid)
            {
                ReleaseFocus();
                return;
            }

            if (!initialized || targetCamera == null)
            {
                return;
            }

            float halfHeight = targetCamera.orthographicSize;
            float halfWidth = halfHeight *
                Mathf.Max(0.1f, targetCamera.aspect);
            Vector3 targetPosition =
                focusTarget.CameraFocusPosition;
            Vector3 cameraPosition =
                targetCamera.transform.position;
            cameraPosition.x =
                targetPosition.x -
                (focusViewportAnchor.x - 0.5f) *
                halfWidth * 2f;
            cameraPosition.y =
                targetPosition.y -
                (focusViewportAnchor.y - 0.5f) *
                halfHeight * 2f;
            targetCamera.transform.position =
                cameraPosition;
            ClampPosition();
        }

        private void DisablePixelPerfectOverride()
        {
            Component component =
                gameObject.GetComponent("PixelPerfectCamera");
            if (component is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }
        }

        private static float ClampAxis(
            float value,
            float minimum,
            float maximum,
            float fallback)
        {
            return minimum <= maximum
                ? Mathf.Clamp(value, minimum, maximum)
                : fallback;
        }
    }
}
