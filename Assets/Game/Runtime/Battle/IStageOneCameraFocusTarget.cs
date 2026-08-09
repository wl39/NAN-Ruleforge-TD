using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Presentation-only target contract for camera tracking. Implementations
    /// expose an authoritative display position and invalidate themselves
    /// before a pooled object is reused.
    /// </summary>
    public interface IStageOneCameraFocusTarget
    {
        bool IsCameraFocusValid { get; }
        Vector3 CameraFocusPosition { get; }
    }
}
