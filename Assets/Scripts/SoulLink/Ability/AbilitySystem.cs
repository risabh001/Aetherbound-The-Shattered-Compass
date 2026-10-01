using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulLink
{
    /// <summary>
    /// Runtime ability subsystem attached to a CharacterBase.
    /// It manages per-ability cooldowns, ability state, and mobile-friendly input
    /// button presses routed from AbilityInputManager or direct calls.
    ///
    /// Design notes:
    /// - Cooldowns are time-based and frame-rate independent.
    /// - Abilities are decoupled from visuals/effects; this system raises events so
    ///   presentation and gameplay logic can subscribe elsewhere.
    /// - All public methods are safe to call from mobile UI buttons, input managers,
    ///   or AI logic.
    /// </summary>
    public class AbilitySystem : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Configuration
        // ----------------------------------------------------------------------------
        [Header("Ability Roster")]
        [Tooltip("Optional override roster. If left empty, the default registry is used for this character's ID.")]
        [SerializeField] private CharacterAbilityRoster overrideRoster;

        // ----------------------------------------------------------------------------
        // Runtime State
        // ----------------------------------------------------------------------------
        /// The character this ability system belongs to.
        public CharacterBase Character { get; private set; }

        /// The active roster after initialization.
        public IReadOnlyList<AbilityDefinition> Abilities => _abilities;
        private IReadOnlyList<AbilityDefinition> _abilities;

        /// Cooldown tracking indexed by ability ID.
        private Dictionary<AbilityId, AbilityCooldown> _cooldowns = new Dictionary<AbilityId, AbilityCooldown>();

        /// Timestamp of the last Update call, used for cooldown comparisons.
        private float _currentTime;

        // Event: raised when an ability is successfully used.
        public event Action<AbilityId> OnAbilityUsed;

        // Event: raised when an ability finishes cooldown and becomes ready.
        public event Action<AbilityId> OnAbilityReady;

        // Event: raised when an ability activation is attempted but blocked (e.g. cooldown, channeling, AI gate).
        public event Action<AbilityId, string> OnAbilityRejected;

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Awake()
        {
            Character = GetComponent<CharacterBase>();
            if (Character == null)
            {
                Debug.LogError("[AbilitySystem] AbilitySystem must be attached to a CharacterBase.");
            }
        }

        private void Start()
        {
            InitializeRoster();
        }

        private void Update()
        {
            // Advance the simulated clock by real delta time.
            _currentTime += Time.deltaTime;

            UpdateCooldowns();
        }

        // ----------------------------------------------------------------------------
        // Roster Initialization
        // ----------------------------------------------------------------------------
        private void InitializeRoster()
        {
            if (overrideRoster != null && overrideRoster.abilities.Count > 0)
            {
                _abilities = overrideRoster.abilities;
            }
            else if (Character != null)
            {
                var defaultRoster = FindDefaultRosterForCharacter(Character.characterID);
                if (defaultRoster != null)
                {
                    _abilities = defaultRoster.abilities;
                }
                else
                {
                    Debug.LogWarning($"[AbilitySystem] No default roster found for {Character.characterID}. Abilities disabled.");
                    _abilities = Array.Empty<AbilityDefinition>();
                }
            }
            else
            {
                _abilities = Array.Empty<AbilityDefinition>();
            }

            RebuildCooldownLookup();
        }

        private CharacterAbilityRoster FindDefaultRosterForCharacter(CharacterID id)
        {
            foreach (var roster in DefaultAbilityRegistries.All)
            {
                if (roster.characterID == id)
                {
                    return roster;
                }
            }

            return null;
        }

        private void RebuildCooldownLookup()
        {
            _cooldowns.Clear();
            foreach (var ability in _abilities)
            {
                if (!_cooldowns.ContainsKey(ability.abilityId))
                {
                    _cooldowns[ability.abilityId] = new AbilityCooldown
                    {
                        abilityId = ability.abilityId,
                        availableTime = 0f
                    };
                }
            }
        }

        // ----------------------------------------------------------------------------
        // Cooldown Updates
        // ----------------------------------------------------------------------------
        private void UpdateCooldowns()
        {
            foreach (var kvp in _cooldowns)
            {
                var abilityId = kvp.Key;
                var cooldown = kvp.Value;

                if (!cooldown.IsReady(_currentTime))
                {
                    continue;
                }

                // Cooldown completed.
                _cooldowns[abilityId] = new AbilityCooldown
                {
                    abilityId = abilityId,
                    availableTime = 0f
                };

                OnAbilityReady?.Invoke(abilityId);
            }
        }

        // ----------------------------------------------------------------------------
        // Core Ability API
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Attempts to activate an ability. Returns true if the ability started.
        /// Mobile UI buttons and input managers should call this method.
        /// </summary>
        public bool UseAbility(AbilityId abilityId)
        {
            if (Character == null) return false;

            if (!_cooldowns.TryGetValue(abilityId, out var cooldown))
            {
                OnAbilityRejected?.Invoke(abilityId, "Ability not found in roster.");
                return false;
            }

            var definition = FindAbilityDefinition(abilityId);
            if (definition == null)
            {
                OnAbilityRejected?.Invoke(abilityId, "Ability definition missing.");
                return false;
            }

            // AI gating: if the character is not player-controlled, only allow abilities
            // that are marked as usable in AI. This keeps mobile input routing clean while
            // still allowing AI characters to use abilities internally if you call this from AI.
            if (!Character.isPlayerControlled && !definition.usableInAI)
            {
                OnAbilityRejected?.Invoke(abilityId, "Ability locked while in AI mode.");
                return false;
            }

            // Cooldown check.
            if (!_cooldowns[abilityId].IsReady(_currentTime))
            {
                OnAbilityRejected?.Invoke(abilityId, "Ability on cooldown.");
                return false;
            }

            // Channeling gate: for simplicity, channeling abilities start but do not
            // auto-complete here. Real implementations should track active channels.
            if (definition.isChanneling)
            {
                if (IsChannelingActive(abilityId))
                {
                    OnAbilityRejected?.Invoke(abilityId, "Channeling ability already active.");
                    return false;
                }
            }

            ActivateAbility(abilityId, definition);
            return true;
        }

        /// <summary>
        /// Raw activation path used after validation. Override in subclasses or route
        /// through your gameplay event system for real effects.
        /// </summary>
        protected virtual void ActivateAbility(AbilityId abilityId, AbilityDefinition definition)
        {
            // Mark cooldown.
            SetCooldown(abilityId, definition.cooldownSeconds);

            // Notify listeners.
            OnAbilityUsed?.Invoke(abilityId);

            Debug.Log($"[AbilitySystem] {Character.name} used {definition.displayName} ({abilityId}).");
        }

        /// <summary>
        /// Ends a channeling ability early if it is currently active.
        /// </summary>
        public bool CancelChanneling(AbilityId abilityId)
        {
            if (!IsChannelingActive(abilityId))
            {
                return false;
            }

            ClearChanneling(abilityId);
            Debug.Log($"[AbilitySystem] {Character.name} cancelled channeling for {abilityId}.");
            return true;
        }

        // ----------------------------------------------------------------------------
        // Cooldown Manipulation
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Sets an explicit cooldown on an ability, overriding the current cooldown.
        /// Useful for effects that refresh or delay abilities.
        /// </summary>
        public void SetCooldown(AbilityId abilityId, float durationSeconds)
        {
            if (!_cooldowns.TryGetValue(abilityId, out var cooldown))
            {
                return;
            }

            _cooldowns[abilityId] = new AbilityCooldown
            {
                abilityId = abilityId,
                availableTime = _currentTime + Mathf.Max(0f, durationSeconds)
            };
        }

        /// <summary>
        /// Resets an ability's cooldown immediately.
        /// </summary>
        public void ResetCooldown(AbilityId abilityId)
        {
            if (!_cooldowns.TryGetValue(abilityId, out var _))
            {
                return;
            }

            _cooldowns[abilityId] = new AbilityCooldown
            {
                abilityId = abilityId,
                availableTime = 0f
            };
        }

        /// <summary>
        /// Returns the remaining cooldown duration for an ability, or zero if ready.
        /// </summary>
        public float GetRemainingCooldown(AbilityId abilityId)
        {
            if (!_cooldowns.TryGetValue(abilityId, out var cooldown))
            {
                return 0f;
            }

            float remaining = cooldown.availableTime - _currentTime;
            return Mathf.Max(0f, remaining);
        }

        /// <summary>
        /// Returns true if the ability is currently ready to use.
        /// </summary>
        public bool IsAbilityReady(AbilityId abilityId)
        {
            if (!_cooldowns.TryGetValue(abilityId, out var cooldown))
            {
                return false;
            }

            return cooldown.IsReady(_currentTime);
        }

        // ----------------------------------------------------------------------------
        // Channeling State (simple placeholder)
        // ----------------------------------------------------------------------------
        private HashSet<AbilityId> _activeChannels = new HashSet<AbilityId>();

        public bool IsChannelingActive(AbilityId abilityId)
        {
            return _activeChannels.Contains(abilityId);
        }

        private void SetChannelingActive(AbilityId abilityId, bool active)
        {
            if (active)
            {
                _activeChannels.Add(abilityId);
            }
            else
            {
                _activeChannels.Remove(abilityId);
            }
        }

        private void ClearChanneling(AbilityId abilityId)
        {
            _activeChannels.Remove(abilityId);
        }

        // ----------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------
        private AbilityDefinition FindAbilityDefinition(AbilityId abilityId)
        {
            foreach (var ability in _abilities)
            {
                if (ability.abilityId == abilityId)
                {
                    return ability;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns ability metadata for UI without triggering use.
        /// </summary>
        public bool TryGetAbilityData(AbilityId abilityId, out AbilityDefinition definition)
        {
            definition = FindAbilityDefinition(abilityId);
            return definition != null;
        }

        /// <summary>
        /// Returns the current cooldown progress fraction for an ability:
        /// 0 = ready, 1 = full cooldown remaining.
        /// </summary>
        public float GetCooldownProgress(AbilityId abilityId)
        {
            if (!_cooldowns.TryGetValue(abilityId, out var cooldown))
            {
                return 0f;
            }

            var definition = FindAbilityDefinition(abilityId);
            if (definition == null || definition.cooldownSeconds <= 0f)
            {
                return 0f;
            }

            float remaining = GetRemainingCooldown(abilityId);
            return Mathf.Clamp01(remaining / definition.cooldownSeconds);
        }

        // ----------------------------------------------------------------------------
        // Reset / safety
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Clears all cooldowns. Useful for testing or scene reloads.
        /// </summary>
        public void ResetAllCooldowns()
        {
            foreach (var key in new List<AbilityId>(_cooldowns.Keys))
            {
                _cooldowns[key] = new AbilityCooldown
                {
                    abilityId = key,
                    availableTime = 0f
                };
            }
        }

        /// <summary>
        /// Convenience wrapper to force an ability use for testing or AI fallback.
        /// Skips AI gating but still respects cooldowns.
        /// </summary>
        public bool ForceUseAbility(AbilityId abilityId)
        {
            if (!_cooldowns.TryGetValue(abilityId, out var cooldown))
            {
                return false;
            }

            var definition = FindAbilityDefinition(abilityId);
            if (definition == null)
            {
                return false;
            }

            if (!cooldown.IsReady(_currentTime))
            {
                return false;
            }

            ActivateAbility(abilityId, definition);
            return true;
        }
    }
}
