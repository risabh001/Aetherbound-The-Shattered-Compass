using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulLink
{
    /// <summary>
    /// Central manager for the Soul-Link party. Maintains the roster of 4 characters,
    /// handles switching between player control and AI, and routes input to the active
    /// player-controlled character.
    ///
    /// Singleton pattern with graceful fallback: if the active player disconnects or the
    /// controlled character is destroyed, this manager automatically reverts that character
    /// to AI control.
    /// </summary>
    public class SoulLinkManager : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Singleton
        // ----------------------------------------------------------------------------
        private static SoulLinkManager _instance;
        public static SoulLinkManager Instance => _instance;

        [SerializeField] private bool destroyOnLoad = false;

        // ----------------------------------------------------------------------------
        // Roster
        // ----------------------------------------------------------------------------
        [Header("Party Roster")]
        [Tooltip("The 4 soul-linked characters in the party.")]
        [SerializeField] private List<CharacterBase> characters = new List<CharacterBase>(4);

        // Runtime lookup by CharacterID for fast reassignment.
        private Dictionary<CharacterID, CharacterBase> _byId = new Dictionary<CharacterID, CharacterBase>();

        // Runtime lookup by character GameObject reference.
        private Dictionary<CharacterBase, CharacterID> _byInstance = new Dictionary<CharacterBase, CharacterID>();

        // Which player ID is currently controlling which character.
        // playerID -> CharacterBase
        private Dictionary<int, CharacterBase> _playerAssignments = new Dictionary<int, CharacterBase>();

        // The current player-controlled character (there should only be one at a time
        // for a single client). Exposed for InputManager and other systems.
        public CharacterBase ActivePlayerCharacter => _activePlayerCharacter;
        private CharacterBase _activePlayerCharacter;

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[SoulLinkManager] Duplicate instance detected; destroying this one.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (destroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            RebuildLookups();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            // Update each character according to its current control mode.
            foreach (var character in characters)
            {
                if (character == null) continue;

                if (character.isPlayerControlled)
                {
                    character.TakeInput();
                }
                else
                {
                    character.ExecuteAIBehavior();
                }
            }
        }

        // ----------------------------------------------------------------------------
        // Initialization / Validation
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Validate and register the initial roster from the serialized list.
        /// Call this from a bootstrap scene or after populating the list in the Inspector.
        /// </summary>
        public void InitializeRoster()
        {
            RebuildLookups();

            if (characters.Count > 4)
            {
                Debug.LogWarning($"[SoulLinkManager] Roster has {characters.Count} characters; expected max 4.");
            }
        }

        /// <summary>
        /// Adds a character to the roster at runtime. Useful for spawning.
        /// </summary>
        public bool AddCharacter(CharacterBase character, CharacterID id)
        {
            if (character == null)
            {
                Debug.LogError("[SoulLinkManager] Cannot add a null character.");
                return false;
            }

            if (_byId.ContainsKey(id))
            {
                Debug.LogWarning($"[SoulLinkManager] A character with ID {id} already exists.");
                return false;
            }

            if (characters.Count >= 4)
            {
                Debug.LogWarning("[SoulLinkManager] Roster is full (max 4 characters).");
                return false;
            }

            character.characterID = id;
            characters.Add(character);
            _byId[id] = character;
            _byInstance[character] = id;

            return true;
        }

        /// <summary>
        /// Removes a character from the roster and cleans up assignments.
        /// </summary>
        public bool RemoveCharacter(CharacterBase character)
        {
            if (character == null || !characters.Contains(character))
            {
                return false;
            }

            var id = _byInstance[character];

            // If a player is assigned to this character, revert it to AI first.
            if (_playerAssignments.ContainsValue(character))
            {
                RevertToAI(character);
                _playerAssignments.Remove(_playerAssignments.FirstOrDefault(kvp => kvp.Value == character).Key);
            }

            // Clear active player character reference if it was this one.
            if (_activePlayerCharacter == character)
            {
                _activePlayerCharacter = null;
            }

            characters.Remove(character);
            _byId.Remove(id);
            _byInstance.Remove(character);

            return true;
        }

        // ----------------------------------------------------------------------------
        // Assignment API
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Assigns a character to a player. Only one player character is active at a time
        /// for this client. Previous active character is reverted to AI.
        /// </summary>
        /// <param name="playerID">Identifier for the local player (e.g. 1).</param>
        /// <param name="character">The character to put under player control.</param>
        public void AssignPlayer(int playerID, CharacterBase character)
        {
            if (character == null)
            {
                Debug.LogError("[SoulLinkManager] AssignPlayer called with a null character.");
                return;
            }

            if (!characters.Contains(character))
            {
                Debug.LogError($"[SoulLinkManager] Character {character.name} is not in the roster.");
                return;
            }

            // If this character is already assigned to this player, nothing to do.
            if (_playerAssignments.TryGetValue(playerID, out var current) && current == character)
            {
                return;
            }

            // If another character is currently player-controlled, revert it to AI.
            if (_activePlayerCharacter != null && _activePlayerCharacter != character)
            {
                RevertToAI(_activePlayerCharacter);
            }

            // Unassign any other player that may have been controlling this character.
            foreach (var kvp in new Dictionary<int, CharacterBase>(_playerAssignments))
            {
                if (kvp.Value == character)
                {
                    _playerAssignments.Remove(kvp.Key);
                }
            }

            // Assign and activate.
            _playerAssignments[playerID] = character;
            SetCharacterPlayerControlled(character, true);
            _activePlayerCharacter = character;

            Debug.Log($"[SoulLinkManager] Player {playerID} assigned to {character.name} (ID: {character.characterID}).");
        }

        /// <summary>
        /// Reverts a character to AI control. Clears player assignment and stops routing
        /// input to it. Safe to call even if the character was not player-controlled.
        /// </summary>
        public void RevertToAI(CharacterBase character)
        {
            if (character == null)
            {
                Debug.LogError("[SoulLinkManager] RevertToAI called with a null character.");
                return;
            }

            if (!characters.Contains(character))
            {
                Debug.LogWarning($"[SoulLinkManager] Character {character.name} is not in the roster.");
                return;
            }

            if (!character.isPlayerControlled)
            {
                // Already AI-driven. Still clean up any stale assignment.
                foreach (var kvp in new Dictionary<int, CharacterBase>(_playerAssignments))
                {
                    if (kvp.Value == character)
                    {
                        _playerAssignments.Remove(kvp.Key);
                        Debug.Log($"[SoulLinkManager] Cleared stale player assignment for {character.name} (player {kvp.Key}).");
                    }
                }
                return;
            }

            SetCharacterPlayerControlled(character, false);
            _playerAssignments.Remove(_playerAssignments.FirstOrDefault(kvp => kvp.Value == character).Key);

            if (_activePlayerCharacter == character)
            {
                _activePlayerCharacter = null;
            }

            Debug.Log($"[SoulLinkManager] {character.name} reverted to AI control.");
        }

        /// <summary>
        /// Convenience: revert every character currently under player control back to AI.
        /// Useful on game over, pause, or disconnect handling.
        /// </summary>
        public void RevertAllToAI()
        {
            var playerControlled = new List<CharacterBase>();
            foreach (var character in characters)
            {
                if (character != null && character.isPlayerControlled)
                {
                    playerControlled.Add(character);
                }
            }

            foreach (var character in playerControlled)
            {
                RevertToAI(character);
            }
        }

        // ----------------------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------------------
        private void SetCharacterPlayerControlled(CharacterBase character, bool playerControlled)
        {
            if (character == null) return;

            character.SetPlayerControlled(playerControlled);
        }

        private void RebuildLookups()
        {
            _byId.Clear();
            _byInstance.Clear();

            foreach (var character in characters)
            {
                if (character == null) continue;

                if (!_byId.ContainsKey(character.characterID))
                {
                    _byId[character.characterID] = character;
                    _byInstance[character] = character.characterID;
                }
                else
                {
                    Debug.LogWarning($"[SoulLinkManager] Duplicate CharacterID {character.characterID} ignored on {character.name}.");
                }
            }
        }

        // ----------------------------------------------------------------------------
        // Disconnect / fallback handling
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Call this when the local player disconnects or the session ends.
        /// Reverts the active player character to AI so the game continues gracefully.
        /// </summary>
        public void HandleDisconnect(int disconnectedPlayerID)
        {
            if (_playerAssignments.TryGetValue(disconnectedPlayerID, out var character))
            {
                Debug.Log($"[SoulLinkManager] Player {disconnectedPlayerID} disconnected. Reverting {character.name} to AI.");
                RevertToAI(character);
            }
            else
            {
                Debug.LogWarning($"[SoulLinkManager] Disconnect for player {disconnectedPlayerID}: no active assignment found.");
            }
        }

        /// <summary>
        /// If the active player character GameObject is destroyed/disabled, this helper
        /// can be called from a monitor to revert it to AI. Alternatively, other systems
        /// can observe OnControlModeChanged events on each character.
        /// </summary>
        public void CheckActivePlayerCharacterValidity()
        {
            if (_activePlayerCharacter != null && (_activePlayerCharacter.gameObject == null || !_activePlayerCharacter.gameObject.activeInHierarchy))
            {
                Debug.Log("[SoulLinkManager] Active player character no longer valid. Reverting to AI.");
                RevertToAI(_activePlayerCharacter);
            }
        }

        /// <summary>
        /// Returns the player-controlled character for a given player ID, if assigned.
        /// </summary>
        public CharacterBase GetCharacterForPlayer(int playerID)
        {
            if (_playerAssignments.TryGetValue(playerID, out var character))
            {
                return character;
            }

            return null;
        }

        /// <summary>
        /// Returns the character with the given CharacterID, if present in the roster.
        /// </summary>
        public CharacterBase GetCharacterByID(CharacterID id)
        {
            if (_byId.TryGetValue(id, out var character))
            {
                return character;
            }

            return null;
        }

        /// <summary>
        /// Returns the CharacterID for a given character instance, if known.
        /// </summary>
        public bool TryGetIDForCharacter(CharacterBase character, out CharacterID id)
        {
            id = CharacterID.None;
            return _byInstance.TryGetValue(character, out id);
        }
    }
}
