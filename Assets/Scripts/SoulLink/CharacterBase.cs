using UnityEngine;

namespace SoulLink
{
    /// <summary>
    /// Base class for characters that can be player-controlled or AI-driven.
    /// Designed to be extended by concrete character implementations.
    /// </summary>
    public abstract class CharacterBase : MonoBehaviour
    {
        [Header("Character Identity")]
        [Tooltip("Unique identifier for this character within the Soul-Link system.")]
        public CharacterID characterID;

        [Header("Control Mode")]
        [Tooltip("When true, this character accepts player input. Otherwise AI drives it.")]
        public bool isPlayerControlled;

        // Event fired when control mode changes, used by managers and input systems.
        public event System.Action<bool> OnControlModeChanged;

        /// <summary>
        /// Callback for the manager or input system to push input to this character.
        /// Override in derived classes to consume movement/Action input.
        /// </summary>
        /// <param name="inputVector">Normalized input direction from the virtual joystick.</param>
        public virtual void ReceiveInput(Vector2 inputVector)
        {
            // Base behavior intentionally left empty; subclasses implement their own response.
        }

        /// <summary>
        /// Called by the SoulLinkManager each frame when this character is under player control.
        /// Override in subclasses to poll/accumulate input if preferred over event-based input.
        /// </summary>
        public virtual void TakeInput()
        {
            // Default implementation does nothing. Subclasses may read the shared input source here.
        }

        /// <summary>
        /// Called by the SoulLinkManager each frame when this character is running in AI mode.
        /// Override in subclasses to implement AI behavior (navigation, state machines, etc.).
        /// </summary>
        public virtual void ExecuteAIBehavior()
        {
            // Default implementation does nothing. Subclasses implement AI decision making.
        }

        /// <summary>
        /// Swaps control mode and notifies listeners.
        /// Prefer using SoulLinkManager to change this, but this method exists for local convenience.
        /// </summary>
        public void SetPlayerControlled(bool playerControlled)
        {
            if (isPlayerControlled == playerControlled) return;

            isPlayerControlled = playerControlled;
            OnControlModeChanged?.Invoke(isPlayerControlled);

            Debug.Log($"[CharacterBase] {name} control mode -> {(isPlayerControlled ? "PLAYER" : "AI")} (ID: {characterID})");
        }

        protected virtual void OnDisable()
        {
            // Safety: if this character is destroyed/disabled while player-controlled,
            // notify the manager so it can revert to AI. Subclasses can rely on the manager
            // to handle the actual revert, but we expose the hook here.
        }
    }

    /// <summary>
    /// Enum representing the soul-linked characters in the party.
    /// Add new entries here and update managers/UI accordingly when expanding the roster.
    /// </summary>
    public enum CharacterID
    {
        None = 0,
        Warrior = 1,
        Mage = 2,
        Archer = 3,
        Healer = 4
    }
}
