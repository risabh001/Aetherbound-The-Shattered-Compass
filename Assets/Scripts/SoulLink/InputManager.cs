using UnityEngine;

namespace SoulLink
{
    /// <summary>
    /// Captures mobile touch input via a virtual joystick and routes it to the
    /// currently player-controlled CharacterBase through SoulLinkManager.
    ///
    /// This implementation uses the standard Update loop for touch detection so it
    /// works without the New Input System package, but it is structured so you can
    /// swap the internal input source for InputActionAsset later if desired.
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Singleton
        // ----------------------------------------------------------------------------
        private static InputManager _instance;
        public static InputManager Instance => _instance;

        [SerializeField] private bool destroyOnLoad = false;

        // ----------------------------------------------------------------------------
        // Virtual Joystick Configuration
        // ----------------------------------------------------------------------------
        [Header("Virtual Joystick")]
        [Tooltip("World-space position of the virtual joystick center on screen.")]
        [SerializeField] private Rect joystickBounds = new Rect(0, 0, 100, 100);

        [Tooltip("How far from the joystick center a touch must move to register full input.")]
        [SerializeField] private float joystickRadius = 50f;

        [Tooltip("If true, joystick calculates input every frame even without a touch (returns zero).")]
        [SerializeField] private bool normalizeIdleToZero = true;

        // ----------------------------------------------------------------------------
        // Runtime State
        // ----------------------------------------------------------------------------
        private bool _touchActive;
        private Vector2 _joystickCenterScreen; // cached screen coords for the joystick center
        private Vector2 _currentInput; // normalized input vector [-1..1]

        // Public read-only access for other systems (e.g. direct polling in CharacterBase.TakeInput).
        public Vector2 CurrentInput => _currentInput;
        public bool HasActiveTouch => _touchActive;

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[InputManager] Duplicate instance detected; destroying this one.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (destroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Start()
        {
            UpdateJoystickCenterScreen();
        }

        private void Update()
        {
            // Cache screen position in case the canvas/screen size changes dynamically.
            UpdateJoystickCenterScreen();

            ProcessTouchInput();
            RouteInputToActivePlayer();
        }

        // ----------------------------------------------------------------------------
        // Input Processing
        // ----------------------------------------------------------------------------
        private void UpdateJoystickCenterScreen()
        {
            // Convert the serialized joystick bounds center into screen space.
            // Adjust this mapping if your UI/canvas setup differs (e.g. anchored UI rect).
            _joystickCenterScreen.x = joystickBounds.x + joystickBounds.width * 0.5f;
            _joystickCenterScreen.y = joystickBounds.y + joystickBounds.height * 0.5f;
        }

        private void ProcessTouchInput()
        {
            _currentInput = Vector2.zero;

            if (Input.touchCount == 0)
            {
                _touchActive = false;
                return;
            }

            // Find the first touch inside the joystick area.
            foreach (var touch in Input.touches)
            {
                if (touch.phase == TouchPhase.Stationary || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Began)
                {
                    if (IsTouchInsideJoystick(touch.position))
                    {
                        _touchActive = true;
                        _currentInput = ComputeJoystickInput(touch.position);
                        return;
                    }
                }

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    // If the touch that ended was our active joystick touch, release it.
                    if (_touchActive && IsTouchInsideJoystick(touch.position))
                    {
                        // Only reset if this is the active touch we were tracking.
                        // For simplicity we track one joystick touch at a time.
                        _touchActive = false;
                        _currentInput = Vector2.zero;
                        return;
                    }
                }
            }

            // If we had a touch but it moved outside the joystick, hold the last input
            // or zero it out depending on configuration. Here we zero it out when no
            // touch is inside the joystick.
            if (!_touchActive)
            {
                _currentInput = Vector2.zero;
            }
        }

        private bool IsTouchInsideJoystick(Vector2 touchScreenPos)
        {
            float distanceSquared = (touchScreenPos - _joystickCenterScreen).sqrMagnitude;
            return distanceSquared <= joystickRadius * joystickRadius;
        }

        private Vector2 ComputeJoystickInput(Vector2 touchScreenPos)
        {
            Vector2 direction = touchScreenPos - _joystickCenterScreen;
            float magnitude = direction.magnitude;

            if (magnitude < 0.01f)
            {
                return Vector2.zero;
            }

            Vector2 normalized = direction.normalized;

            // Clamp to joystick radius so the magnitude represents the pressure/offset
            // within the joystick disc, normalized to [-1, 1].
            float clampedMagnitude = Mathf.Min(magnitude / joystickRadius, 1f);

            return normalized * clampedMagnitude;
        }

        // ----------------------------------------------------------------------------
        // Routing
        // ----------------------------------------------------------------------------
        private void RouteInputToActivePlayer()
        {
            var manager = SoulLinkManager.Instance;
            if (manager == null) return;

            var active = manager.ActivePlayerCharacter;
            if (active == null || !active.isPlayerControlled)
            {
                // No active player-controlled character; input is consumed harmlessly.
                return;
            }

            // Option A: push via event-style ReceiveInput (preferred for decoupled design).
            active.ReceiveInput(_currentInput);

            // Option B: if the character polls in TakeInput(), it can read InputManager.Instance.CurrentInput.
            // That path is intentionally left to the character implementation.
        }

        // ----------------------------------------------------------------------------
        // Public API for manual override / testing
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Force-set the current input vector. Useful for testing or for integrating
        /// with other input sources (gamepad, motion, etc.).
        /// </summary>
        public void SetInput(Vector2 input)
        {
            _currentInput = input;
            _touchActive = input != Vector2.zero;
        }

        /// <summary>
        /// Clear the current input immediately.
        /// </summary>
        public void ResetInput()
        {
            _currentInput = Vector2.zero;
            _touchActive = false;
        }

        /// <summary>
        /// Returns the current normalized input vector without routing it.
        /// Useful for characters that poll InputManager directly.
        /// </summary>
        public Vector2 GetCurrentInput()
        {
            return _currentInput;
        }

        // ----------------------------------------------------------------------------
        // Debug visualization (editor/runtime)
        // ----------------------------------------------------------------------------
        private void OnDrawGizmos()
        {
            // Draw the joystick bounds in the Scene view for easier tunables.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            var center = new Vector3(joystickBounds.x + joystickBounds.width * 0.5f,
                                     joystickBounds.y + joystickBounds.height * 0.5f,
                                     0f);
            var size = new Vector3(joystickBounds.width, joystickBounds.height, 0f);
            Gizmos.DrawWireCube(center, size);

            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(center, joystickRadius);
        }
    }
}
