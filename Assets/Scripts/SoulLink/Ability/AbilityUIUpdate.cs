using UnityEngine;
using UnityEngine.UI;

namespace SoulLink
{
    /// <summary>
    /// Simple MonoBehaviour to drive ability UI elements from an AbilitySystem.
    ///
    /// Mobile ability pages usually need:
    ///  - Cooldown fill images
    ///  - Interactable toggling on buttons
    ///  - Visual readiness/locked feedback
    ///
    /// This helper keeps the UI update logic out of the core systems while staying
    /// generic enough to reuse across character ability panels.
    /// </summary>
    public class AbilityUIUpdate : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The ability system to read cooldown state from. If null, the manager resolves from routing.")]
        [SerializeField] private AbilitySystem targetAbilitySystem;

        [Header("UI Elements")]
        [SerializeField] private AbilityButtonBinding[] abilityBindings;

        // Optional: a normalized Image for cooldown fills.
        // If provided, it is updated to reflect cooldown progress.
        [System.Serializable]
        public class AbilityUIState
        {
            public AbilityId abilityId;
            public Image cooldownFillImage;
            public GameObject readyIndicator;
            public GameObject lockedIndicator;
        }

        [SerializeField] private AbilityUIState[] uiStates;

        private AbilitySystem _cachedSystem;

        private void Update()
        {
            var system = ResolveAbilitySystem();
            if (system == null) return;

            foreach (var state in uiStates)
            {
                if (state == null) continue;

                UpdateUIForAbility(system, state);
            }
        }

        private AbilitySystem ResolveAbilitySystem()
        {
            if (targetAbilitySystem != null)
            {
                return targetAbilitySystem;
            }

            var manager = AbilityInputManager.Instance;
            if (manager == null) return null;

            return manager.ActiveAbilitySystem;
        }

        private void UpdateUIForAbility(AbilitySystem system, AbilityUIState state)
        {
            bool hasData = system.TryGetAbilityData(state.abilityId, out var definition);
            if (!hasData)
            {
                SetUIInteractable(state, false);
                return;
            }

            bool ready = system.IsAbilityReady(state.abilityId);
            float progress = system.GetCooldownProgress(state.abilityId);

            SetCooldownFill(state, progress);
            SetReadyLockedIndicators(state, ready);
            SetUIInteractable(state, ready);
        }

        private void SetCooldownFill(AbilityUIState state, float progress)
        {
            if (state.cooldownFillImage != null)
            {
                // Assumes the image fill represents unavailable time: 0 = ready, 1 = on cooldown.
                state.cooldownFillImage.fillAmount = progress;
            }
        }

        private void SetReadyLockedIndicators(AbilityUIState state, bool ready)
        {
            if (state.readyIndicator != null)
            {
                state.readyIndicator.SetActive(ready);
            }

            if (state.lockedIndicator != null)
            {
                state.lockedIndicator.SetActive(!ready);
            }
        }

        private void SetUIInteractable(AbilityUIState state, bool interactable)
        {
            foreach (var binding in abilityBindings)
            {
                if (binding.abilityId != state.abilityId) continue;
                if (binding.button != null)
                {
                    binding.button.interactable = interactable;
                }
            }
        }
    }
}
