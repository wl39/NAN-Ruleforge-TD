using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleforgeTD.Tutorial
{
    /// <summary>
    /// Presentation-only lookup for tutorial focus targets. UI targets are
    /// measured from their four world corners, while world targets are
    /// represented by a camera-projected point and a screen-space footprint.
    /// The registry does not own or mutate any registered object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialAnchorRegistry : MonoBehaviour
    {
        public static readonly Vector2 DefaultWorldScreenSize =
            new Vector2(96f, 96f);

        private enum TargetKind
        {
            Ui,
            UiGroup,
            World
        }

        private sealed class Registration
        {
            public TargetKind Kind;
            public RectTransform UiTarget;
            public RectTransform[] UiTargets;
            public Transform WorldTarget;
            public Camera WorldCamera;
            public Vector2 ScreenSize;
            public Vector3 WorldOffset;
            public Vector2 Padding;

            public UnityEngine.Object Target
            {
                get
                {
                    if (Kind == TargetKind.Ui)
                    {
                        return UiTarget;
                    }
                    if (Kind == TargetKind.UiGroup)
                    {
                        return UiTargets != null && UiTargets.Length > 0
                            ? UiTargets[0]
                            : null;
                    }
                    return WorldTarget;
                }
            }
        }

        private readonly Dictionary<string, Registration> registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly Vector3[] uiWorldCorners = new Vector3[4];

        public event Action<string> AnchorRegistered;
        public event Action<string> AnchorRemoved;

        public int Count => registrations.Count;

        public static TutorialAnchorRegistry CreateRuntime(
            Transform parent = null)
        {
            var host = new GameObject("Tutorial Anchor Registry");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            return host.AddComponent<TutorialAnchorRegistry>();
        }

        /// <summary>
        /// Registers or replaces a UI target. Padding is expressed in physical
        /// screen pixels because the resulting rectangle is also screen-space.
        /// </summary>
        public bool RegisterUi(
            string anchorId,
            RectTransform target,
            Vector2 padding = default(Vector2))
        {
            if (!IsValidAnchorId(anchorId) || target == null)
            {
                return false;
            }

            registrations[anchorId] = new Registration
            {
                Kind = TargetKind.Ui,
                UiTarget = target,
                Padding = Positive(padding)
            };
            AnchorRegistered?.Invoke(anchorId);
            return true;
        }

        /// <summary>
        /// Registers the live screen-space union of several UI targets. This
        /// keeps a single pass-through hole around choices that may rearrange
        /// between landscape and portrait layouts.
        /// </summary>
        public bool RegisterUiGroup(
            string anchorId,
            IReadOnlyList<RectTransform> targets,
            Vector2 padding = default(Vector2))
        {
            if (!IsValidAnchorId(anchorId) || targets == null)
            {
                return false;
            }

            var validTargets = new List<RectTransform>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                {
                    validTargets.Add(targets[i]);
                }
            }
            if (validTargets.Count == 0)
            {
                return false;
            }

            registrations[anchorId] = new Registration
            {
                Kind = TargetKind.UiGroup,
                UiTargets = validTargets.ToArray(),
                Padding = Positive(padding)
            };
            AnchorRegistered?.Invoke(anchorId);
            return true;
        }

        /// <summary>
        /// Registers or replaces a world target. A zero screenSize selects the
        /// 96x96 default footprint. The camera may be null; Camera.main is then
        /// resolved when the anchor is queried so camera replacement is safe.
        /// </summary>
        public bool RegisterWorld(
            string anchorId,
            Transform target,
            Camera worldCamera,
            Vector2 screenSize = default(Vector2),
            Vector3 worldOffset = default(Vector3),
            Vector2 padding = default(Vector2))
        {
            if (!IsValidAnchorId(anchorId) || target == null)
            {
                return false;
            }

            Vector2 resolvedSize = screenSize.x > 0f && screenSize.y > 0f
                ? screenSize
                : DefaultWorldScreenSize;
            registrations[anchorId] = new Registration
            {
                Kind = TargetKind.World,
                WorldTarget = target,
                WorldCamera = worldCamera,
                ScreenSize = Positive(resolvedSize),
                WorldOffset = worldOffset,
                Padding = Positive(padding)
            };
            AnchorRegistered?.Invoke(anchorId);
            return true;
        }

        public bool Contains(string anchorId)
        {
            return IsValidAnchorId(anchorId) &&
                registrations.ContainsKey(anchorId);
        }

        public bool Unregister(string anchorId)
        {
            if (!IsValidAnchorId(anchorId) ||
                !registrations.Remove(anchorId))
            {
                return false;
            }

            AnchorRemoved?.Invoke(anchorId);
            return true;
        }

        /// <summary>
        /// Removes an anchor only when it still points at the expected object.
        /// This keeps a late OnDisable from removing a newer replacement.
        /// </summary>
        public bool Unregister(
            string anchorId,
            UnityEngine.Object expectedTarget)
        {
            if (!IsValidAnchorId(anchorId) || expectedTarget == null ||
                !registrations.TryGetValue(anchorId, out Registration entry) ||
                entry.Target != expectedTarget)
            {
                return false;
            }

            registrations.Remove(anchorId);
            AnchorRemoved?.Invoke(anchorId);
            return true;
        }

        public void Clear()
        {
            if (registrations.Count == 0)
            {
                return;
            }

            string[] removedIds = new string[registrations.Count];
            registrations.Keys.CopyTo(removedIds, 0);
            registrations.Clear();
            for (int i = 0; i < removedIds.Length; i++)
            {
                AnchorRemoved?.Invoke(removedIds[i]);
            }
        }

        /// <summary>
        /// Resolves a visible, screen-clamped rectangle in physical pixels.
        /// False is returned for missing, inactive, destroyed, behind-camera,
        /// or fully off-screen targets.
        /// </summary>
        public bool TryGetScreenRect(
            string anchorId,
            out Rect screenRect)
        {
            screenRect = default(Rect);
            if (!IsValidAnchorId(anchorId) ||
                !registrations.TryGetValue(anchorId, out Registration entry))
            {
                return false;
            }

            bool resolved;
            if (entry.Kind == TargetKind.Ui)
            {
                resolved = TryResolveUi(entry, out screenRect);
            }
            else if (entry.Kind == TargetKind.UiGroup)
            {
                resolved = TryResolveUiGroup(entry, out screenRect);
            }
            else
            {
                resolved = TryResolveWorld(entry, out screenRect);
            }
            if (!resolved)
            {
                return false;
            }

            screenRect = Expand(screenRect, entry.Padding);
            return TryClampToScreen(screenRect, out screenRect);
        }

        private bool TryResolveUi(
            Registration entry,
            out Rect screenRect)
        {
            return TryResolveUiTarget(entry.UiTarget, out screenRect);
        }

        private bool TryResolveUiGroup(
            Registration entry,
            out Rect screenRect)
        {
            screenRect = default(Rect);
            bool found = false;
            RectTransform[] targets = entry.UiTargets;
            if (targets == null)
            {
                return false;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (!TryResolveUiTarget(targets[i], out Rect targetRect))
                {
                    continue;
                }

                screenRect = found
                    ? Rect.MinMaxRect(
                        Mathf.Min(screenRect.xMin, targetRect.xMin),
                        Mathf.Min(screenRect.yMin, targetRect.yMin),
                        Mathf.Max(screenRect.xMax, targetRect.xMax),
                        Mathf.Max(screenRect.yMax, targetRect.yMax))
                    : targetRect;
                found = true;
            }
            return found;
        }

        private bool TryResolveUiTarget(
            RectTransform target,
            out Rect screenRect)
        {
            screenRect = default(Rect);
            if (target == null || !target.gameObject.activeInHierarchy ||
                target.rect.width <= 0f || target.rect.height <= 0f)
            {
                return false;
            }

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera eventCamera = ResolveCanvasCamera(canvas);
            target.GetWorldCorners(uiWorldCorners);

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < uiWorldCorners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    uiWorldCorners[i]);
                if (!IsFinite(point))
                {
                    return false;
                }

                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }

            if (maxX <= minX || maxY <= minY)
            {
                return false;
            }

            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private static bool TryResolveWorld(
            Registration entry,
            out Rect screenRect)
        {
            screenRect = default(Rect);
            Transform target = entry.WorldTarget;
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                return false;
            }

            Camera camera = entry.WorldCamera != null
                ? entry.WorldCamera
                : Camera.main;
            if (camera == null)
            {
                return false;
            }

            Vector3 screenPoint = camera.WorldToScreenPoint(
                target.position + entry.WorldOffset);
            if (screenPoint.z <= 0f || !IsFinite(screenPoint))
            {
                return false;
            }

            Vector2 halfSize = entry.ScreenSize * 0.5f;
            screenRect = Rect.MinMaxRect(
                screenPoint.x - halfSize.x,
                screenPoint.y - halfSize.y,
                screenPoint.x + halfSize.x,
                screenPoint.y + halfSize.y);
            return true;
        }

        private static Camera ResolveCanvasCamera(Canvas canvas)
        {
            if (canvas == null ||
                canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            if (canvas.worldCamera != null)
            {
                return canvas.worldCamera;
            }

            return Camera.main;
        }

        private static bool TryClampToScreen(
            Rect source,
            out Rect clamped)
        {
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            float minX = Mathf.Clamp(source.xMin, 0f, width);
            float minY = Mathf.Clamp(source.yMin, 0f, height);
            float maxX = Mathf.Clamp(source.xMax, 0f, width);
            float maxY = Mathf.Clamp(source.yMax, 0f, height);
            if (maxX <= minX || maxY <= minY)
            {
                clamped = default(Rect);
                return false;
            }

            clamped = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private static Rect Expand(Rect source, Vector2 padding)
        {
            return Rect.MinMaxRect(
                source.xMin - padding.x,
                source.yMin - padding.y,
                source.xMax + padding.x,
                source.yMax + padding.y);
        }

        private static Vector2 Positive(Vector2 value)
        {
            return new Vector2(
                Mathf.Max(0f, value.x),
                Mathf.Max(0f, value.y));
        }

        private static bool IsValidAnchorId(string anchorId)
        {
            return !string.IsNullOrWhiteSpace(anchorId);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
