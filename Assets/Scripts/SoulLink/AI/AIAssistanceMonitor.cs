using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulLink
{
    /// <summary>
    /// Light-weight monitor that helps AI-controlled party members react when the
    /// player-controlled character enters combat range or puzzle range.
    ///
    /// This is optional glue between SoulLinkManager, AICharacterController, and your
    /// puzzle/combat systems. It avoids tight coupling while giving AI a faster signal
    /// than polling alone.
    /// </summary>
    public class AIAssistanceMonitor : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Configuration
        // ----------------------------------------------------------------------------
        [Header("Detection")]
        [Tooltip("Radius around the player in which AI characters are prompted to assist.")]
        [SerializeField] private float assistTriggerRadius = 8f;

        [Header("Targets")]
        [Tooltip("Optional layer mask for combat-related objects.")]
        [SerializeField] private LayerMask combatLayerMask;

        [Tooltip("Optional layer mask for puzzle-related objects.")]
        [SerializeField] private LayerMask puzzleLayerMask;

        // ----------------------------------------------------------------------------
        // Runtime State
        // ----------------------------------------------------------------------------
        private List<AICharacterController> _trackedAI = new List<AICharacterController>();

        // Events for other systems to subscribe to.
        public event Action<AICharacterController, Transform> OnAIAssistingEnemy;
        public event Action<AICharacterController, Transform> OnAIAssistingPuzzle;

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Update()
        {
            var player = FindPlayerControlledCharacter();
            if (player == null) return;

            UpdateTrackedAICharacters(player);
        }

        // ----------------------------------------------------------------------------
        // Tracking
        // ----------------------------------------------------------------------------
        private void UpdateTrackedAICharacters(CharacterBase player)
        {
            // Refresh tracked AI list from party roster.
            var manager = SoulLink.SoulLinkManager.Instance;
            if (manager == null) return;

            _trackedAI.Clear();
            foreach (var c in manager.characters)
            {
                if (c == null || c.isPlayerControlled) continue;

                var ai = c.GetComponent<AICharacterController>();
                if (ai != null)
                {
                    _trackedAI.Add(ai);
                }
            }

            // Prompt AI when player is near combat or puzzle objects.
            TryTriggerCombatAssist(player);
            TryTriggerPuzzleAssist(player);
        }

        private void TryTriggerCombatAssist(CharacterBase player)
        {
            Collider[] hits = Physics.OverlapSphereNonAlloc(
                player.transform.position,
                assistTriggerRadius,
                _tempColliderBuffer,
                combatLayerMask,
                QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                if (hit == null) continue;

                foreach (var ai in _trackedAI)
                {
                    if (ai.IsInCombat) continue;

                    // Ask this AI to consider this enemy for assist.
                    ai.ForceFollow(); // ensure movement toward relevant context
                    ai.OnPlayerMoved();
                }
            }
        }

        private void TryTriggerPuzzleAssist(CharacterBase player)
        {
            Collider[] hits = Physics.OverlapSphereNonAlloc(
                player.transform.position,
                assistTriggerRadius,
                _tempColliderBuffer,
                puzzleLayerMask,
                QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                if (hit == null) continue;

                foreach (var ai in _trackedAI)
                {
                    if (ai.IsFollowing == false && ai.CurrentState != AICharacterController.AIState.Puzzle)
                    {
                        continue;
                    }

                    ai.OnPlayerMoved();
                }
            }
        }

        // ----------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------
        private CharacterBase FindPlayerControlledCharacter()
        {
            var manager = SoulLink.SoulLinkManager.Instance;
            if (manager == null) return null;

            var active = manager.ActivePlayerCharacter;
            if (active != null) return active;

            foreach (var c in manager.characters)
            {
                if (c != null && c.isPlayerControlled)
                {
                    return c;
                }
            }

            return null;
        }

        // ----------------------------------------------------------------------------
        // Internal Buffers
        // ----------------------------------------------------------------------------
        private Collider[] _tempColliderBuffer = new Collider[64];

        // ----------------------------------------------------------------------------
        // Editor Visualization
        // ----------------------------------------------------------------------------
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, assistTriggerRadius);
        }
    }
}
