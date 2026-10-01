using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SoulLink
{
    /// <summary>
    /// AI behavior controller for soul-linked characters that are not currently
    /// player-controlled. Drives movement with NavMeshAgent and switches between
    /// Idle, Follow, Combat, and Puzzle states depending on context.
    ///
    /// Designed to work alongside SoulLinkManager so AI characters take over
    /// gracefully when a player disconnects or reassigns control.
    /// </summary>
    public class AICharacterController : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Dependencies
        // ----------------------------------------------------------------------------
        [Header("References")]
        [Tooltip("The NavMeshAgent used for AI movement.")]
        [SerializeField] private NavMeshAgent navMeshAgent;

        [Tooltip("The CharacterBase this AI controller belongs to.")]
        [SerializeField] private CharacterBase character;

        // ----------------------------------------------------------------------------
        // Configuration
        // ----------------------------------------------------------------------------
        [Header("Follow Behavior")]
        [Tooltip("How close the AI stays behind the player-controlled character.")]
        [SerializeField] private float followDistance = 6f;

        [Tooltip("How tightly the AI adheres to the follow path.")]
        [SerializeField] private float followStoppingDistance = 1.2f;

        [Tooltip("Obstacle separation from other agents/characters during follow.")]
        [SerializeField] private float obstacleAvoidanceRadius = 2f;

        [Header("Combat Behavior")]
        [Tooltip("Maximum detection radius for auto-targeting enemies.")]
        [SerializeField] private float detectionRadius = 12f;

        [Tooltip("Minimum time between combat target re-evaluations.")]
        [SerializeField] private float combatRefreshInterval = 0.4f;

        [Tooltip("If true, the AI prefers enemies closer to the player to assist grouped combat.")]
        [SerializeField] private bool prioritizeEnemiesNearPlayer = true;

        [Header("Puzzle Behavior")]
        [Tooltip("Distance at which the AI considers the player to be near a puzzle.")]
        [SerializeField] private float puzzleAssistRadius = 4f;

        [Tooltip("Time the AI waits before resuming normal behavior after a puzzle task.")]
        [SerializeField] private float puzzleTaskCooldown = 1.5f;

        [Header("General")]
        [Tooltip("Optional layer mask for enemy detection.")]
        [SerializeField] private LayerMask enemyLayerMask;

        [Tooltip("Optional layer mask for puzzle detection.")]
        [SerializeField] private LayerMask puzzleLayerMask;

        // ----------------------------------------------------------------------------
        // Runtime State
        // ----------------------------------------------------------------------------
        public enum AIState
        {
            None,
            Idle,
            Follow,
            Combat,
            Puzzle
        }

        public AIState CurrentState { get; private set; } = AIState.Idle;

        /// <summary>
        /// Current combat target, if any.
        /// </summary>
        public Transform CurrentEnemyTarget { get; private set; }

        /// <summary>
        /// Current puzzle interactable the AI is assisting with.
        /// </summary>
        public Transform CurrentPuzzleTarget { get; private set; }

        /// <summary>
        /// Whether the AI is currently trying to reach the player.
        /// </summary>
        public bool IsFollowing => CurrentState == AIState.Follow;

        /// <summary>
        /// Whether the AI is currently engaged in combat behavior.
        /// </summary>
        public bool IsInCombat => CurrentState == AIState.Combat;

        // Events
        public event Action<AIState> OnStateChanged;
        public event Action<Transform> OnEnemyTargetChanged;
        public event Action<Transform> OnPuzzleTargetChanged;

        // ----------------------------------------------------------------------------
        // Internal Timing / Search
        // ----------------------------------------------------------------------------
        private float _combatRefreshTimer;
        private float _puzzleTaskCooldownTimer;
        private float _nextStateEvaluationTime;

        // Cached NavMesh query results to avoid per-frame allocations.
        private List<NavMeshHit> _nearbyHits = new List<NavMeshHit>(16);

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Awake()
        {
            if (character == null)
            {
                character = GetComponent<CharacterBase>();
            }

            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (navMeshAgent != null)
            {
                navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.DynamicObstacleAvoidance;
                navMeshAgent.radius = Mathf.Max(0.3f, navMeshAgent.radius);
            }
        }

        private void Start()
        {
            if (navMeshAgent != null)
            {
                navMeshAgent.stoppingDistance = followStoppingDistance;
                navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.DynamicObstacleAvoidance;
            }

            SetState(AIState.Idle);
        }

        private void Update()
        {
            if (character == null) return;
            if (!character.isPlayerControlled == false)
            {
                // Only AI characters should run this controller.
                // If the character becomes player-controlled, pause AI behavior below.
            }

            // If the character is player-controlled, AI should stand down.
            if (character.isPlayerControlled)
            {
                SetState(AIState.Idle);
                return;
            }

            _nextStateEvaluationTime -= Time.deltaTime;
            if (_nextStateEvaluationTime <= 0f)
            {
                EvaluateBestState();
                _nextStateEvaluationTime = 0.15f;
            }

            // State-specific update.
            switch (CurrentState)
            {
                case AIState.Idle:
                    UpdateIdle();
                    break;
                case AIState.Follow:
                    UpdateFollow();
                    break;
                case AIState.Combat:
                    UpdateCombat();
                    break;
                case AIState.Puzzle:
                    UpdatePuzzle();
                    break;
            }

            // Update cooldown timers.
            _combatRefreshTimer -= Time.deltaTime;
            _puzzleTaskCooldownTimer -= Time.deltaTime;
        }

        private void OnDisable()
        {
            if (navMeshAgent != null)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
            }

            ClearCombatTarget();
            ClearPuzzleTarget();
            SetState(AIState.Idle);
        }

        // ----------------------------------------------------------------------------
        // State Machine
        // ----------------------------------------------------------------------------
        private void EvaluateBestState()
        {
            var player = FindPlayerControlledCharacter();
            bool playerNearby = player != null && IsCharacterWithinRadius(player, detectionRadius * 1.4f);
            bool combatAvailable = FindNearestEnemy(out var enemyTarget);
            bool puzzleAvailable = FindNearestPuzzle(out var puzzleTarget);

            // Priority order: Combat > Puzzle > Follow > Idle
            if (combatAvailable)
            {
                if (CurrentState != AIState.Combat)
                {
                    SetCombatTarget(enemyTarget);
                    SetState(AIState.Combat);
                }
                return;
            }

            if (puzzleAvailable && playerNearby)
            {
                if (CurrentState != AIState.Puzzle)
                {
                    SetPuzzleTarget(puzzleTarget);
                    SetState(AIState.Puzzle);
                }
                return;
            }

            if (player != null)
            {
                if (CurrentState != AIState.Follow)
                {
                    SetState(AIState.Follow);
                }
                return;
            }

            if (CurrentState != AIState.Idle)
            {
                SetState(AIState.Idle);
            }
        }

        private void SetState(AIState newState)
        {
            if (CurrentState == newState) return;

            OnStateChanged?.Invoke(newState);

            // Stop agent when leaving movement-based states.
            if (navMeshAgent != null)
            {
                switch (newState)
                {
                    case AIState.Idle:
                    case AIState.Combat:
                    case AIState.Puzzle:
                        navMeshAgent.isStopped = true;
                        break;
                    case AIState.Follow:
                        navMeshAgent.isStopped = false;
                        break;
                }
            }

            CurrentState = newState;
        }

        // ----------------------------------------------------------------------------
        // Idle
        // ----------------------------------------------------------------------------
        private void UpdateIdle()
        {
            // Idle AI can optionally look around or prepare for re-engagement.
            // For now it remains stationary unless Follow becomes relevant.
        }

        // ----------------------------------------------------------------------------
        // Follow
        // ----------------------------------------------------------------------------
        private void UpdateFollow()
        {
            var player = FindPlayerControlledCharacter();
            if (player == null)
            {
                SetState(AIState.Idle);
                return;
            }

            var targetPosition = GetFollowTargetPosition(player);
            if (navMeshAgent == null) return;

            // Re-path if far enough off course or target moved.
            if (navMeshAgent.isStopped == false && navMeshAgent.pathPending == false)
            {
                float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
                if (distanceToTarget < followStoppingDistance)
                {
                    navMeshAgent.isStopped = true;
                    return;
                }

                // If the agent has reached a stale destination, request a new path.
                if (navMeshAgent.remainingDistance < 0.2f && !navMeshAgent.pathPending)
                {
                    RequestFollowPath(targetPosition);
                }
            }
            else
            {
                RequestFollowPath(targetPosition);
            }

            MaintainSeparationFromPlayer(player);
        }

        private Vector3 GetFollowTargetPosition(Transform player)
        {
            // Derive a follow point behind the player relative to their facing direction.
            Vector3 forward = player.forward;
            Vector3 desiredOffset = -forward * followDistance;

            // Add a small vertical offset to avoid clinging to flat ground exactly.
            Vector3 offsetPosition = player.position + desiredOffset;
            offsetPosition.y = transform.position.y;

            return offsetPosition;
        }

        private void RequestFollowPath(Vector3 destination)
        {
            if (navMeshAgent == null) return;

            // Sample the destination on the NavMesh to avoid off-mesh targets.
            if (SampleNavMeshDestination(destination, out var hit))
            {
                navMeshAgent.SetDestination(hit.position);
                navMeshAgent.isStopped = false;
            }
            else
            {
                // Fallback: try direct set and let the agent do best-effort navigation.
                navMeshAgent.SetDestination(destination);
                navMeshAgent.isStopped = false;
            }
        }

        private bool SampleNavMeshDestination(Vector3 destination, out NavMeshHit hit)
        {
            if (NavMesh.SamplePosition(destination, out hit, obstacleAvoidanceRadius, enemyLayerMask))
            {
                return true;
            }

            return NavMesh.SamplePosition(destination, out hit, obstacleAvoidanceRadius, NavMesh.AllAreas);
        }

        private void MaintainSeparationFromPlayer(Transform player)
        {
            if (navMeshAgent == null) return;

            float currentDistance = Vector3.Distance(transform.position, player.position);
            if (currentDistance < obstacleAvoidanceRadius && currentDistance > 0.01f)
            {
                Vector3 separationDir = (transform.position - player.position).normalized;
                Vector3 adjusted = transform.position + separationDir * (obstacleAvoidanceRadius - currentDistance);
                if (SampleNavMeshDestination(adjusted, out var hit))
                {
                    navMeshAgent.SetDestination(hit.position);
                }
            }
        }

        // ----------------------------------------------------------------------------
        // Combat
        // ----------------------------------------------------------------------------
        private void UpdateCombat()
        {
            // Keep the combat target valid.
            if (CurrentEnemyTarget == null || !CurrentEnemyTarget.gameObject.activeSelf)
            {
                if (FindNearestEnemy(out var newTarget))
                {
                    SetCombatTarget(newTarget);
                }
                else
                {
                    ClearCombatTarget();
                    SetState(AIState.Follow);
                    return;
                }
            }

            // Refresh target selection on interval.
            if (_combatRefreshTimer <= 0f)
            {
                _combatRefreshTimer = combatRefreshInterval;

                if (FindNearestEnemy(out var updatedTarget))
                {
                    if (updatedTarget != CurrentEnemyTarget)
                    {
                        SetCombatTarget(updatedTarget);
                    }
                }
                else
                {
                    ClearCombatTarget();
                    SetState(AIState.Follow);
                    return;
                }
            }

            // Face and approach the target if far away.
            if (navMeshAgent != null && CurrentEnemyTarget != null)
            {
                float distanceToEnemy = Vector3.Distance(transform.position, CurrentEnemyTarget.position);
                if (distanceToEnemy > followStoppingDistance)
                {
                    navMeshAgent.isStopped = false;
                    RequestFollowPath(CurrentEnemyTarget.position);
                }
                else
                {
                    navMeshAgent.isStopped = true;
                }

                // Look toward the target.
                LookAtTarget(CurrentEnemyTarget.position);
            }
        }

        private void SetCombatTarget(Transform target)
        {
            if (CurrentEnemyTarget == target) return;

            CurrentEnemyTarget = target;
            OnEnemyTargetChanged?.Invoke(target);
        }

        private void ClearCombatTarget()
        {
            if (CurrentEnemyTarget != null)
            {
                OnEnemyTargetChanged?.Invoke(null);
            }

            CurrentEnemyTarget = null;
        }

        // ----------------------------------------------------------------------------
        // Puzzle
        // ----------------------------------------------------------------------------
        private void UpdatePuzzle()
        {
            var player = FindPlayerControlledCharacter();
            bool playerStillNearby = player != null && IsCharacterWithinRadius(player, puzzleAssistRadius);

            if (!playerStillNearby)
            {
                ClearPuzzleTarget();
                SetState(AIState.Follow);
                return;
            }

            // Cooldown after completing a puzzle task.
            if (_puzzleTaskCooldownTimer > 0f)
            {
                _puzzleTaskCooldownTimer -= Time.deltaTime;
                return;
            }

            if (CurrentPuzzleTarget == null || !CurrentPuzzleTarget.gameObject.activeSelf)
            {
                if (FindNearestPuzzle(out var newTarget))
                {
                    SetPuzzleTarget(newTarget);
                }
                else
                {
                    ClearPuzzleTarget();
                    SetState(AIState.Follow);
                    return;
                }
            }

            // Move toward the puzzle interact point if needed.
            if (navMeshAgent != null && CurrentPuzzleTarget != null)
            {
                float distanceToPuzzle = Vector3.Distance(transform.position, CurrentPuzzleTarget.position);
                if (distanceToPuzzle > 1.5f)
                {
                    navMeshAgent.isStopped = false;
                    RequestFollowPath(CurrentPuzzleTarget.position);
                }
                else
                {
                    navMeshAgent.isStopped = true;
                    AttemptPuzzleAssist(CurrentPuzzleTarget);
                    _puzzleTaskCooldownTimer = puzzleTaskCooldown;
                }

                LookAtTarget(CurrentPuzzleTarget.position);
            }
        }

        private void SetPuzzleTarget(Transform target)
        {
            if (CurrentPuzzleTarget == target) return;

            CurrentPuzzleTarget = target;
            OnPuzzleTargetChanged?.Invoke(target);
        }

        private void ClearPuzzleTarget()
        {
            if (CurrentPuzzleTarget != null)
            {
                OnPuzzleTargetChanged?.Invoke(null);
            }

            CurrentPuzzleTarget = null;
        }

        /// <summary>
        /// Hook for puzzle assist behavior. Override or wire this into your puzzle
        /// system so AI characters can interact when near a puzzle with the player.
        /// </summary>
        protected virtual void AttemptPuzzleAssist(Transform puzzleTarget)
        {
            // Default placeholder: the AI is "assisting" by being present at the puzzle.
            // Real implementations can call into your puzzle controller here.
            Debug.Log($"[AICharacterController] {name} assisting puzzle at {puzzleTarget?.name ?? "null"}.");
        }

        // ----------------------------------------------------------------------------
        // Detection Helpers
        // ----------------------------------------------------------------------------
        private CharacterBase FindPlayerControlledCharacter()
        {
            var manager = SoulLink.SoulLinkManager.Instance;
            if (manager == null) return null;

            var playerChar = manager.ActivePlayerCharacter;
            if (playerChar != null) return playerChar;

            // Fallback: scan the roster for any player-controlled character.
            foreach (var c in manager.characters)
            {
                if (c != null && c.isPlayerControlled)
                {
                    return c;
                }
            }

            return null;
        }

        private bool IsCharacterWithinRadius(CharacterBase c, float radius)
        {
            if (c == null) return false;
            return Vector3.Distance(transform.position, c.transform.position) <= radius;
        }

        private bool FindNearestEnemy(out Transform nearest)
        {
            nearest = null;
            float bestSqr = detectionRadius * detectionRadius;
            Vector3 position = transform.position;

            // Use overlap sphere for broad detection, then refine.
            Collider[] hits = Physics.OverlapSphereNonAlloc(
                position,
                detectionRadius,
                _nearbyColliders,
                enemyLayerMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col == null) continue;

                // Exclude self.
                if (col.GetComponent<CharacterBase>() == character) continue;

                float sqr = (col.bounds.center - position).sqrMagnitude;
                if (sqr > bestSqr) continue;

                // Prefer enemies near the player when assisting.
                if (prioritizeEnemiesNearPlayer)
                {
                    var player = FindPlayerControlledCharacter();
                    if (player != null)
                    {
                        float playerSqr = (col.bounds.center - player.position).sqrMagnitude;
                        // Weight closer-to-player enemies slightly higher.
                        float combined = sqr + playerSqr * 0.6f;
                        if (combined < bestSqr)
                        {
                            // Only accept if still within detection radius from AI.
                            if (sqr <= bestSqr)
                            {
                                bestSqr = combined;
                                nearest = col.transform;
                            }
                        }
                    }
                    else
                    {
                        bestSqr = sqr;
                        nearest = col.transform;
                    }
                }
                else
                {
                    bestSqr = sqr;
                    nearest = col.transform;
                }
            }

            return nearest != null;
        }

        private bool FindNearestPuzzle(out Transform nearest)
        {
            nearest = null;
            float bestSqr = puzzleAssistRadius * puzzleAssistRadius;
            Vector3 position = transform.position;

            Collider[] hits = Physics.OverlapSphereNonAlloc(
                position,
                puzzleAssistRadius,
                _puzzleColliders,
                puzzleLayerMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col == null) continue;

                float sqr = (col.bounds.center - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = col.transform;
                }
            }

            return nearest != null;
        }

        private void LookAtTarget(Vector3 targetPosition)
        {
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion targetRotation = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 8f * Time.deltaTime);
        }

        // ----------------------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Forces the AI into follow mode toward the player-controlled character.
        /// </summary>
        public void ForceFollow()
        {
            if (character != null && character.isPlayerControlled) return;

            SetState(AIState.Follow);
        }

        /// <summary>
        /// Forces the AI into idle mode and clears current targets.
        /// </summary>
        public void ForceIdle()
        {
            ClearCombatTarget();
            ClearPuzzleTarget();
            SetState(AIState.Idle);
        }

        /// <summary>
        /// Returns the current state as a readable string for debugging.
        /// </summary>
        public string GetStateDebugString()
        {
            return CurrentState.ToString();
        }

        /// <summary>
        /// Notifies the AI that the player-controlled character has moved significantly.
        /// Can be used to trigger earlier re-evaluation if needed.
        /// </summary>
        public void OnPlayerMoved()
        {
            _nextStateEvaluationTime = 0f;
        }

        // ----------------------------------------------------------------------------
        // Internal buffers (reused to avoid allocations)
        // ----------------------------------------------------------------------------
        private Collider[] _nearbyColliders = new Collider[64];
        private Collider[] _puzzleColliders = new Collider[16];

        // ----------------------------------------------------------------------------
        // Gizmos
        // ----------------------------------------------------------------------------
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            Gizmos.color = new Color(0f, 0.9f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, puzzleAssistRadius);

            Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
            var player = FindPlayerControlledCharacter();
            if (player != null)
            {
                Gizmos.DrawLine(transform.position, player.position);
            }
        }
    }
}
