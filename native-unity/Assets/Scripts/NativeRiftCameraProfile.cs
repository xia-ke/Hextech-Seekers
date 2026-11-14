using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Classic Summoner's Rift camera projection and position calculations.
    /// Values are in the native Map11 navigation coordinate system, not the
    /// imported glTF object's local coordinate system. Camera calibration must
    /// NEVER rotate the Map11 model independently of its navigation grid.
    /// </summary>
    public static class NativeRiftCameraProfile
    {
        // Published SR rendering reference: FOV 40, pitch 56 degrees below
        // horizontal, maximum zoom back/up offsets ~1289.558 / 1911.849.
        // Unlike an orthographic map preview, the Player uses perspective.
        public const float FieldOfView = 40f;
        public const float DefaultPitch = 56f;
        // Diagonal client-like view: camera-only, without rotating Map11.
        public const float DefaultYaw = 45f;
        public const float MaxZoomOutBack = 1289.558f;
        public const float MaxZoomOutUp = 1911.849f;
        public const float MinDistance = 1024f;
        public const float MinPitch = 42f;
        public const float MaxPitch = 74f;
        public const float PanUnitsPerSecond = 1250f;

        public static float DefaultDistance =>
            Mathf.Sqrt(MaxZoomOutBack * MaxZoomOutBack +
                       MaxZoomOutUp * MaxZoomOutUp);
        public static float ClampDistance(float value) =>
            Mathf.Clamp(value, MinDistance, DefaultDistance);
        public static float ClampPitch(float pitch) =>
            Mathf.Clamp(pitch, MinPitch, MaxPitch);
        public static float WrapYaw(float yaw) =>
            Mathf.Repeat(yaw + 180f, 360f) - 180f;

        public static Vector3 FocusOnMapPlane(Vector2 navigationPosition)
        {
            // LoL focus uses height=0, not the character's varying terrain
            // height. Thus the camera doesn't bob vertically on slopes.
            return new Vector3(navigationPosition.x, 0f, navigationPosition.y);
        }

        public static Quaternion Rotation(float pitch, float yaw)
        {
            return Quaternion.Euler(ClampPitch(pitch), WrapYaw(yaw), 0f);
        }

        public static Vector3 CameraPosition(Vector3 focus, float pitch,
                                             float yaw, float distance)
        {
            return focus - Rotation(pitch, yaw) * Vector3.forward *
                   ClampDistance(distance);
        }

        public static Vector2 PanPlane(float yaw, Vector2 screenDirection)
        {
            // Arrow keys translate in camera-visible screen axes regardless
            // of yaw; no hero/nav/mesh geometry is modified.
            Vector3 pan = Quaternion.Euler(0f, WrapYaw(yaw), 0f) *
                          new Vector3(screenDirection.x, 0f, screenDirection.y);
            return new Vector2(pan.x, pan.z);
        }
    }
}
