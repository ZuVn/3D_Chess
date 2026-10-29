using UnityEngine;

namespace ChessGame
{
    /// <summary>
    /// Orbit camera around the board centre with a smooth perspective → orthographic top-down blend.
    /// The blend narrows the field of view while pulling back (a dolly zoom), so the switch to a true
    /// orthographic projection at the end is visually seamless. Yaw is shared by both modes, so camera
    /// mode and board orientation stay independent.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public const float MinPitch = 18f, MaxPitch = 88f, MinDistance = 7.5f, MaxDistance = 26f;
        public const float MinOrtho = 3.2f, MaxOrtho = 9f;
        const float Fov = 40f, NarrowFov = 8f, TransitionTime = 0.65f;

        public Camera Cam { get; private set; }
        public float Sensitivity = 1f;
        /// <summary>Fraction of the screen width covered by side panels (keeps the board framed between them).</summary>
        public float SidePanelFraction;

        public bool Is2D => _modeTarget > 0.5f;
        public float Flatness => Smooth(_mode);
        /// <summary>True while an automatic movement (mode switch, rotate button, auto-rotate, reset) is under way.</summary>
        public bool IsTransitioning => Mathf.Abs(_mode - _modeTarget) > 0.001f || _autoYaw && Mathf.Abs(Mathf.DeltaAngle(_yaw, _yawTarget)) > 1f;
        public float Yaw => _yaw;

        float _yaw, _yawTarget, _yawVel;
        float _pitch = 52f, _pitchTarget = 52f, _pitchVel;
        float _dist = 13f, _distTarget = 13f, _distVel;
        float _ortho = 5.2f, _orthoTarget = 5.2f, _orthoVel;
        float _mode, _modeTarget; // 0 = 3D, 1 = 2D
        bool _autoYaw;

        void Awake()
        {
            Cam = GetComponent<Camera>();
            Cam.fieldOfView = Fov;
        }

        // ------------------------------------------------------------------ controls

        public void Orbit(Vector2 pixelDelta)
        {
            _autoYaw = false;
            float k = 0.22f * Sensitivity;
            _yawTarget += pixelDelta.x * k;
            if (!Is2D) _pitchTarget = Mathf.Clamp(_pitchTarget - pixelDelta.y * k, MinPitch, MaxPitch);
        }

        public void Zoom(float scrollSteps)
        {
            float f = Mathf.Pow(0.88f, scrollSteps);
            if (Is2D) _orthoTarget = Mathf.Clamp(_orthoTarget * f, MinOrtho, MaxOrtho);
            else _distTarget = Mathf.Clamp(_distTarget * f, MinDistance, MaxDistance);
        }

        public void Set2D(bool on) => _modeTarget = on ? 1 : 0;
        public void Toggle2D() => Set2D(!Is2D);

        /// <summary>Rotate around the board by a fixed step using the shortest path.</summary>
        public void RotateBy(float degrees)
        {
            _yawTarget = Mathf.Round((_yawTarget + degrees) / 90f) * 90f;
            _autoYaw = true;
        }

        /// <summary>Face the board from one side (0 = White, 180 = Black) via the shortest path.</summary>
        public void LookFrom(float sideYaw)
        {
            _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, sideYaw);
            _autoYaw = true;
        }

        public void ResetView(float sideYaw)
        {
            LookFrom(sideYaw);
            _pitchTarget = 52f;
            _distTarget = FitDistance();
            _orthoTarget = FitOrtho();
        }

        /// <summary>Re-frame the board after a window resize or layout change, keeping the viewing angle.</summary>
        public void Refit()
        {
            _distTarget = FitDistance();
            _orthoTarget = FitOrtho();
        }

        public void SnapTo(float sideYaw, bool twoD)
        {
            _yaw = _yawTarget = sideYaw;
            _pitch = _pitchTarget = 52f;
            _dist = _distTarget = FitDistance();
            _ortho = _orthoTarget = FitOrtho();
            _mode = _modeTarget = twoD ? 1 : 0;
            _autoYaw = false;
            Apply();
        }

        /// <summary>Slow idle orbit used behind the main menu.</summary>
        public void Idle(float degreesPerSecond)
        {
            _yawTarget += degreesPerSecond * Time.unscaledDeltaTime;
            _autoYaw = false;
        }

        float UsableAspect => Cam.aspect * Mathf.Clamp(1f - 2f * SidePanelFraction, 0.45f, 1f);

        /// <summary>Half-extent of the board including frame, in world units, with a margin.</summary>
        const float BoardHalf = 5.0f;

        // 2D keeps a little extra room so the board edge clears the top bar.
        float FitOrtho() => Mathf.Clamp(Mathf.Max(BoardHalf * 1.1f, BoardHalf * 1.02f / UsableAspect), MinOrtho, MaxOrtho);

        float FitDistance()
        {
            float vFit = BoardHalf * 1.15f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float hFov = 2f * Mathf.Atan(Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * UsableAspect);
            float hFit = BoardHalf * 1.1f / Mathf.Tan(hFov * 0.5f);
            return Mathf.Clamp(Mathf.Max(vFit, hFit), MinDistance, MaxDistance);
        }

        // ------------------------------------------------------------------ update

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _yaw = Mathf.SmoothDamp(_yaw, _yawTarget, ref _yawVel, _autoYaw ? 0.22f : 0.06f, Mathf.Infinity, dt);
            _pitch = Mathf.SmoothDamp(_pitch, _pitchTarget, ref _pitchVel, 0.08f, Mathf.Infinity, dt);
            _dist = Mathf.SmoothDamp(_dist, _distTarget, ref _distVel, 0.1f, Mathf.Infinity, dt);
            _ortho = Mathf.SmoothDamp(_ortho, _orthoTarget, ref _orthoVel, 0.1f, Mathf.Infinity, dt);
            _mode = Mathf.MoveTowards(_mode, _modeTarget, dt / TransitionTime);
            if (_autoYaw && Mathf.Abs(_yaw - _yawTarget) < 0.05f) { _yaw = _yawTarget; _autoYaw = false; }
            // Keep angles bounded without visible jumps.
            if (Mathf.Abs(_yaw) > 3600f) { float w = Mathf.Repeat(_yaw, 360f); _yawTarget += w - _yaw; _yaw = w; }
            Apply();
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        void Apply()
        {
            float e = Smooth(_mode);
            var target = new Vector3(0, -0.15f * (1 - e), 0);
            // Visible half-height at the board for the 3D view, blended toward the 2D framing.
            float fov3 = Fov * Mathf.Deg2Rad * 0.5f;
            float half3 = _dist * Mathf.Tan(fov3);
            float half = Mathf.Lerp(half3, _ortho, e);
            float fov = Mathf.Lerp(Fov, NarrowFov, e);
            float pitch = Mathf.Lerp(_pitch, 90f, e);

            if (e >= 0.999f)
            {
                Cam.orthographic = true;
                Cam.orthographicSize = _ortho;
                Cam.nearClipPlane = 0.3f;
                Cam.farClipPlane = 60f;
                transform.SetPositionAndRotation(target + Vector3.up * 30f, Quaternion.Euler(90f, _yaw, 0));
                return;
            }
            Cam.orthographic = false;
            Cam.fieldOfView = fov;
            float dist = half / Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
            var rot = Quaternion.Euler(pitch, _yaw, 0);
            transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
            Cam.nearClipPlane = Mathf.Max(0.1f, dist - 25f);
            Cam.farClipPlane = dist + 120f;
        }
    }
}
