using System;
using UnityEngine;
using UnityEngine.UI;

namespace SoulLink
{
    /// <summary>
    /// Mobile-friendly ability input layer.
    ///
    /// This is intentionally separate from SoulLink's movement InputManager so ability
    /// buttons can be laid out on a touch panel without colliding with the virtual
    /// joystick. It resolves ability activations to the correct CharacterBase and
    /// supports two mobile input patterns:
    ///
    ///  - Direct ability buttons bound to a specific character.
    ///  - Request-based buttons that ask SoulLinkManager for the active player
    ///    character and then activate an ability.
    ///
    /// Button events are expected from Unity UI Button.OnClick or a custom touch
    /// handler. The system is also callable from code if you prefer non-UI input.
    /// </summary>
    public class AbilityInputManager : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Singleton
        // ----------------------------------------------------------------------------
        private static AbilityInputManager _instance;
        public static AbilityInputManager Instance => _instance;

        [SerializeField] private bool destroyOnLoad = false;

        // ----------------------------------------------------------------------------
        // Routing Mode
        // ----------------------------------------------------------------------------
        public enum RoutingMode
        {
            /// <summary>
            /// Use the character this component is placed on (or its child ability system).
            /// </summary>
            LocalCharacter,

            /// <summary>
            /// Use the currently active player-controlled character from SoulLinkManager.
            /// </summary>
            ActivePlayerCharacter
        }

        [Header("Routing")]
        [Tooltip("How ability buttons resolve the target character.")]
        [SerializeField] private RoutingMode routingMode = RoutingMode.ActivePlayerCharacter;

        // ----------------------------------------------------------------------------
        // Runtime State
        // ----------------------------------------------------------------------------
        /// The ability system that will receive activations. Set at runtime based on routing.
        public AbilitySystem ActiveAbilitySystem { get; private set; }

        // Ability button bindings for UI wiring.
        // Map each ability ID to a Unity Button (or leave null if not used).
        [Serializable]
        public class AbilityButtonBinding
        {
            public AbilityId abilityId;
            public Button button;
        }

        [Header("Ability Buttons")]
        [Tooltip("Bind each ability to a UI button. Useful for quick mobile layout.")]
        [SerializeField] private AbilityButtonBinding[] abilityButtons;

        // Event raised when an ability button is pressed, before routing.
        // Useful for sound/haptics independent of target selection.
        public event Action<AbilityId> OnAbilityButtonPressed;

        // Event raised when an ability activation is rejected by the target ability system.
        public event Action<AbilityId, string> OnAbilityActivationRejected;

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[AbilityInputManager] Duplicate instance detected; destroying this one.");
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
            RebuildActiveAbilitySystem();
            WireAbilityButtons();
        }

        // ----------------------------------------------------------------------------
        // Target Resolution
        // ----------------------------------------------------------------------------
        private void RebuildActiveAbilitySystem()
        {
            ActiveAbilitySystem = ResolveAbilitySystem();
        }

        private AbilitySystem ResolveAbilitySystem()
        {
            switch (routingMode)
            {
                case RoutingMode.LocalCharacter:
                    return GetComponent<AbilitySystem>();

                case RoutingMode.ActivePlayerCharacter:
                    var manager = SoulLink.SoulLinkManager.Instance;
                    if (manager == null)
                    {
                        Debug.LogWarning("[AbilityInputManager] SoulLinkManager not found; ability routing inactive.");
                        return null;
                    }

                    var active = manager.ActivePlayerCharacter;
                    if (active == null)
                    {
                        Debug.LogWarning("[AbilityInputManager] No active player character; ability routing inactive.");
                        return null;
                    }

                    return active.GetComponent<AbilitySystem>();

                default:
                    return null;
            }
        }

        // ----------------------------------------------------------------------------
        // Button Wiring
        // ----------------------------------------------------------------------------
        private void WireAbilityButtons()
        {
            if (abilityButtons == null) return;

            foreach (var binding in abilityButtons)
            {
                if (binding.button == null)
                {
                    continue;
                }

                // Capture the abilityId in a local variable for the listener closure.
                var abilityId = binding.abilityId;
                binding.button.onClick.RemoveListener(() => HandleAbilityButton(abilityId));
                binding.button.onClick.AddListener(() => HandleAbilityButton(abilityId));
            }
        }

        /// <summary>
        /// Call this when the roster or active character changes so UI buttons target the
        /// correct ability system.
        /// </summary>
        public void RefreshRouting()
        {
            RebuildActiveAbilitySystem();
            WireAbilityButtons();
        }

        // ----------------------------------------------------------------------------
        // Input Handling
        // ----------------------------------------------------------------------------
        private void HandleAbilityButton(AbilityId abilityId)
        {
            OnAbilityButtonPressed?.Invoke(abilityId);

            var system = ActiveAbilitySystem;
            if (system == null)
            {
                OnAbilityActivationRejected?.Invoke(abilityId, "No target ability system.");
                return;
            }

            if (system.UseAbility(abilityId))
            {
                return;
            }

            // If UseAbility returned false, it already raised OnAbilityRejected internally.
            // Mirror it here so UI listeners can show feedback in one place.
            var definition = system.TryGetAbilityData(abilityId, out var def) ? def : null;
            string reason = definition != null
                ? $"Ability on cooldown or unavailable."
                : "Unknown ability.";

            OnAbilityActivationRejected?.Invoke(abilityId, reason);
        }

        // ----------------------------------------------------------------------------
        // Public API for non-UI input sources
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Activate an ability through this manager's routing rules.
        /// Useful for gamepad, accessibility, or alternate touch controllers.
        /// </summary>
        public bool ActivateAbility(AbilityId abilityId)
        {
            if (ActiveAbilitySystem == null)
            {
                RefreshRouting();
            }

            if (ActiveAbilitySystem == null)
            {
                OnAbilityActivationRejected?.Invoke(abilityId, "No target ability system available.");
                return false;
            }

            OnAbilityButtonPressed?.Invoke(abilityId);
            return ActiveAbilitySystem.UseAbility(abilityId);
        }

        /// <summary>
        /// Returns the remaining cooldown for the currently routed ability system.
        /// </summary>
        public float GetAbilityCooldownRemaining(AbilityId abilityId)
        {
            if (ActiveAbilitySystem == null)
            {
                RefreshRouting();
            }

            if (ActiveAbilitySystem == null)
            {
                return -1f;
            }

            return ActiveAbilitySystem.GetRemainingCooldown(abilityId);
        }

        /// <summary>
        /// Returns cooldown progress fraction for UI fill bars.
        /// </summary>
        public float GetAbilityCooldownProgress(AbilityId abilityId)
        {
            if (ActiveAbilitySystem == null)
            {
                RefreshRouting();
            }

            if (ActiveAbilitySystem == null)
            {
                return 0f;
            }

            return ActiveAbilitySystem.GetCooldownProgress(abilityId);
        }

        /// <summary>
        /// Checks whether the currently routed ability system has the requested ability.
        /// </summary>
        public bool HasAbility(AbilityId abilityId)
        {
            if (ActiveAbilitySystem == null)
            {
                RefreshRouting();
            }

            if (ActiveAbilitySystem == null)
            {
                return false;
            }

            return ActiveAbilitySystem.TryGetAbilityData(abilityId, out _);
        }

        // ----------------------------------------------------------------------------
        // Debug helpers
        // ----------------------------------------------------------------------------
        private void OnDrawGizmos()
        {
            // Quick visual reminder in the Editor about routing mode.
            // No runtime logic here.
        }
    }
}
