# 📄 **Technical Design Document: Core Character & Soul-Link System**
**Target AI:** Freebuff (Solar Pro)  
**Engine Assumption:** Unity (C#) / Adaptable to Unreal  
**Module Goal:** Create a scalable character controller that seamlessly transitions control between Human Players and AI based on the current session player count (1, 2, or 4).

---

### **1. Core Data Structures & Classes**

#### **A. `CharacterBase` (MonoBehaviour / Actor)**
The base script attached to all 4 characters (Kaelen, Lyra, Jax, Elara).
*   **Variables:**
    *   `characterID` (Enum: Vanguard, Weaver, Shadow, Artificer)
    *   `isPlayerControlled` (bool): True if a human has input authority, False if AI.
    *   `currentHealth`, `maxHealth` (float)
    *   `movementSpeed`, `stamina` (float)
*   **Methods:**
    *   `Initialize(bool isHuman)`: Sets up input maps and AI behavior trees based on control type.
    *   `TakeInput(Vector2 moveInput, bool jump, bool action1, bool action2)`: Receives input *only* if `isPlayerControlled` is true.
    *   `ExecuteAIBehavior()`: Called by the AI Manager if `isPlayerControlled` is false.

#### **B. `SoulLinkManager` (Singleton)**
The brain of the game session. It tracks who is playing and who is AI.
*   **Variables:**
    *   `activePlayerCount` (int): 1, 2, or 4.
    *   `characterRoster` (List<CharacterBase>): Holds references to all 4 spawned characters.
    *   `playerAssignments` (Dictionary<int, CharacterBase>): Maps Player ID (1-4) to a Character.
*   **Methods:**
    *   `AssignPlayer(int playerID, CharacterBase character)`: Gives human control to a character.
    *   `RevertToAI(CharacterBase character)`: Strips human input and activates the character's AI Behavior Tree.
    *   `HandlePlayerDisconnect(int playerID)`: Automatically finds the orphaned character and calls `RevertToAI`.

#### **C. `InputManager` (Singleton)**
Handles mobile touch inputs and maps them to the currently active character.
*   **Variables:**
    *   `localPlayerID` (int)
    *   `activeCharacter` (CharacterBase)
*   **Methods:**
    *   `OnMoveInput(Vector2 direction)`: Passes direction to `activeCharacter.TakeInput()`.
    *   `OnSwapRequested(CharacterBase targetCharacter)`: Triggers the swap sequence.

---

### **2. The "Soul-Link" Logic Flow (Step-by-Step)**

**Scenario: 1-Player Mode swapping to a different character.**
1. Player taps the "Character Swap" UI button, targeting `Lyra`.
2. `InputManager` sends `OnSwapRequested(Lyra)` to `SoulLinkManager`.
3. `SoulLinkManager` identifies the *currently* controlled character (e.g., `Kaelen`).
4. `SoulLinkManager` calls `Kaelen.RevertToAI()`. 
   * *Kaelen's script disables local input listeners and enables his NavMeshAgent / Behavior Tree.*
5. `SoulLinkManager` calls `Lyra.Initialize(true)`.
   * *Lyra's script enables local input listeners, disables AI, and snaps the Main Camera to her transform.*
6. A brief visual VFX (e.g., a glowing "Soul" particle effect) plays on both characters to signify the transfer of control.

---

### **3. Scaling Logic (1 vs 2 vs 4 Players)**

The `SoulLinkManager` enforces these rules on game start or when a player joins:

*   **If `activePlayerCount == 1`:**
    *   Player 1 controls `Kaelen` (default).
    *   `Lyra`, `Jax`, `Elara` are set to `isPlayerControlled = false` (AI).
    *   *AI Behavior:* Follow Player 1, assist in combat, auto-solve puzzle steps if Player 1 is near.
*   **If `activePlayerCount == 2`:**
    *   Player 1 chooses a character (e.g., `Kaelen`).
    *   Player 2 chooses a character (e.g., `Lyra`).
    *   `Jax` and `Elara` are set to AI.
    *   *AI Behavior:* Fill the gaps. If a puzzle requires Elara, AI Elara autonomously moves to the objective and performs the action.
*   **If `activePlayerCount == 4`:**
    *   All 4 characters are assigned to Players 1-4.
    *   `isPlayerControlled = true` for all.
    *   *AI Behavior:* Disabled entirely. Pure human coordination.

---

### **4. Edge Cases & Safety Nets (Crucial for AI Coding)**

1.  **Player Disconnects (4-player → 3-player):** 
    *   `SoulLinkManager` detects the disconnect.
    *   Immediately calls `RevertToAI()` on the disconnected player's character.
    *   Spawns a "Reconnecting..." UI indicator over that character's head.
    *   If they rejoin within 60 seconds, control is seamlessly handed back.
2.  **Character Death:**
    *   If a player-controlled character reaches 0 HP, they enter a "Downed" state.
    *   Control is *forced* to swap to the nearest alive teammate (or AI takes over if solo).
    *   Prevents the player from being stuck on a "Game Over" screen while teammates are still alive.
3.  **Camera Snapping:**
    *   When swapping control, the camera should not instantly teleport (causes motion sickness). Use a smooth `Vector3.Lerp` or Unity's Cinemachine `Virtual Camera` priority system to smoothly blend to the new character over 0.5 seconds.

---

