using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SoulLink.UI
{
    /// <summary>
    /// Root mobile HUD controller. It owns the virtual joystick, character swap wheel,
    /// ability bar, and player count indicator, and keeps them synchronized with
    /// SoulLinkManager and the ability system.
    ///
    /// Layout is expected to live on a Unity Canvas, with the joystick on the left,
    /// the character wheel on the right, ability buttons at the bottom, and a small
    /// player count badge in a corner. This script does not create those UIs for you
    /// at runtime; instead it binds to existing UI elements so you can style them
    /// freely in the Editor.
    /// </summary>
    public class MobileHudManager : MonoBehaviour
    {
        // ----------------------------------------------------------------------------
        // Singleton
        // ----------------------------------------------------------------------------
        private static MobileHudManager _instance;
        public static MobileHudManager Instance => _instance;

        [SerializeField] private bool destroyOnLoad = false;

        // ----------------------------------------------------------------------------
        // References
        // ----------------------------------------------------------------------------
        [Header("Core Systems")]
        [Tooltip("Optional explicit reference. If null, the manager finds SoulLinkManager at startup.")]
        [SerializeField] private SoulLink.SoulLinkManager soulLinkManager;

        [Header("Character Swap Wheel")]
        [Tooltip("UI panel that represents the wheel container.")]
        [SerializeField] private RectTransform characterWheelRoot;

        [Tooltip("UI element for the active/selected character portrait or icon.")]
        [SerializeField] private Image activeCharacterIcon;

        [Tooltip("UI text for the active character name.")]
        [SerializeField] private Text activeCharacterNameText;

        [Header("Character Wheel Items")]
        [Tooltip("List of wheel slot UI elements, one per character.")]
        [SerializeField] private CharacterWheelSlot[] wheelSlots;

        [Header("Ability Bar")]
        [Tooltip("Container for the ability button row.")]
        [SerializeField] private RectTransform abilityBarRoot;

        [Tooltip("Attack button.")]
        [SerializeField] private Button attackButton;

        [Tooltip("Button for ability slot 1.")]
        [SerializeField] private Button abilityButton1;

        [Tooltip("Button for ability slot 2.")]
        [SerializeField] private Button abilityButton2;

        [Tooltip("Button for ability slot 3.")]
        [SerializeField] private Button abilityButton3;

        [Header("Ability Cooldown Overlays")]
        [Tooltip("Image fill or overlay for ability slot 1 cooldown.")]
        [SerializeField] private Image abilityCooldown1;

        [Tooltip("Image fill or overlay for ability slot 2 cooldown.")]
        [SerializeField] private Image abilityCooldown2;

        [Tooltip("Image fill or overlay for ability slot 3 cooldown.")]
        [SerializeField] private Image abilityCooldown3;

        [Header("Player Count Indicator")]
        [Tooltip("Text showing active player count, e.g. '1 / 2 / 4'.")]
        [SerializeField] private Text playerCountText;

        [Header("Misc")]
        [Tooltip("Optional joystick UI root referenced for input visual sync.")]
        [SerializeField] private RectTransform joystickUiRoot;

        // ----------------------------------------------------------------------------
        // Runtime State
        // ----------------------------------------------------------------------------
        private SoulLink.Ability.AbilityInputManager _abilityInputManager;
        private SoulLink.Ability.AbilitySystem _activeAbilitySystem;
        private List<SoulLink.CharacterBase> _rosterCache = new List<SoulLink.CharacterBase>();
        private int _totalPlayerSlots = 2;

        // Current active character binding for the wheel/ability bar.
        private SoulLink.CharacterBase _activeCharacter;

        // Current active ability bindings for the 3 skill slots.
        private SoulLink.Ability.AbilityId[] _skillAbilityIds = new SoulLink.Ability.AbilityId[3];

        // ----------------------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------------------
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[MobileHudManager] Duplicate HUD instance; destroying this one.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (destroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Start()
        {
            ResolveSystemReferences();
            PopulateWheelFromRoster();
            RefreshActiveCharacterUI();
            RefreshAbilityBars();
            RefreshPlayerCount();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ----------------------------------------------------------------------------
        // Reference Resolution
        // ----------------------------------------------------------------------------
        private void ResolveSystemReferences()
        {
            if (soulLinkManager == null)
            {
                soulLinkManager = SoulLink.SoulLinkManager.Instance;
            }

            _abilityInputManager = SoulLink.Ability.AbilityInputManager.Instance;
        }

        // ----------------------------------------------------------------------------
        // Character Swap Wheel
        // ----------------------------------------------------------------------------
        public void RefreshWheelFromRoster()
        {
            PopulateWheelFromRoster();
            RefreshActiveCharacterUI();
        }

        private void PopulateWheelFromRoster()
        {
            _rosterCache.Clear();
            if (soulLinkManager != null)
            {
                _rosterCache.AddRange(soulLinkManager.characters);
            }

            if (wheelSlots == null) return;

            int index = 0;
            foreach (var slot in wheelSlots)
            {
                if (slot == null) continue;

                SoulLink.CharacterBase character = null;
                SoulLink.CharacterID assignedId = SoulLink.CharacterID.None;

                if (index < _rosterCache.Count)
                {
                    character = _rosterCache[index];
                    if (character != null)
                    {
                        SoulLink.SoulLinkManager.Instance.TryGetIDForCharacter(character, out assignedId);
                    }
                }

                slot.Bind(character, assignedId);
                index++;
            }
        }

        private void RefreshActiveCharacterUI()
        {
            var active = SoulLink.SoulLinkManager.Instance?.ActivePlayerCharacter;
            _activeCharacter = active;

            if (activeCharacterIcon != null)
            {
                // If you use sprite assets per character, assign them on the slot side.
                activeCharacterIcon.gameObject.SetActive(active != null);
            }

            if (activeCharacterNameText != null)
            {
                if (active != null)
                {
                    activeCharacterNameText.text = FormatCharacterDisplayName(active);
                }
                else
                {
                    activeCharacterNameText.text = "No Active Character";
                }
            }

            RefreshAbilitySystemBinding();
        }

        private string FormatCharacterDisplayName(SoulLink.CharacterBase character)
        {
            if (character == null) return "Unknown";

            var id = SoulLink.SoulLinkManager.Instance?.GetCharacterByID(character.characterID);
            // Fall back to enum name if no custom display name is available.
            return character.characterID.ToString();
        }

        // ----------------------------------------------------------------------------
        // Ability Bar
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Assigns the 3 skill slot abilities. The first slot can optionally represent
        /// the main attack. AbilityInputManager routes presses to the active character.
        /// </summary>
        public void SetSkillAbilities(
            SoulLink.Ability.AbilityId skill1,
            SoulLink.Ability.AbilityId skill2,
            SoulLink.Ability.AbilityId skill3)
        {
            _skillAbilityIds[0] = skill1;
            _skillAbilityIds[1] = skill2;
            _skillAbilityIds[2] = skill3;

            BindAbilityButtons();
            RefreshAbilityBars();
        }

        /// <summary>
        /// Convenience wrapper if you want the first slot treated as a plain attack
        /// with no cooldown behavior.
        /// </summary>
        public void SetAttackButtonBinding(Action onAttackPressed)
        {
            if (attackButton != null)
            {
                attackButton.onClick.RemoveAllListeners();
                attackButton.onClick.AddListener(() => onAttackPressed?.Invoke());
            }
        }

        private void BindAbilityButtons()
        {
            if (_abilityInputManager == null) return;

            if (abilityButton1 != null)
            {
                abilityButton1.onClick.RemoveAllListeners();
                abilityButton1.onClick.AddListener(() => PressSkillButton(0));
            }

            if (abilityButton2 != null)
            {
                abilityButton2.onClick.RemoveAllListeners();
                abilityButton2.onClick.AddListener(() => PressSkillButton(1));
            }

            if (abilityButton3 != null)
            {
                abilityButton3.onClick.RemoveAllListeners();
                abilityButton3.onClick.AddListener(() => PressSkillButton(2));
            }
        }

        private void PressSkillButton(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _skillAbilityIds.Length) return;

            var abilityId = _skillAbilityIds[slotIndex];
            if (abilityId == SoulLink.Ability.AbilityId.None) return;

            if (_abilityInputManager != null)
            {
                _abilityInputManager.ActivateAbility(abilityId);
            }
            else if (_activeAbilitySystem != null)
            {
                _activeAbilitySystem.UseAbility(abilityId);
            }
        }

        private void RefreshAbilityBars()
        {
            if (_abilityInputManager == null)
            {
                _abilityInputManager = SoulLink.Ability.AbilityInputManager.Instance;
            }

            RefreshAbilityCooldownOverlay(abilityCooldown1, _skillAbilityIds[0], _abilityInputManager);
            RefreshAbilityCooldownOverlay(abilityCooldown2, _skillAbilityIds[1], _abilityInputManager);
            RefreshAbilityCooldownOverlay(abilityCooldown3, _skillAbilityIds[2], _abilityInputManager);
        }

        private void RefreshAbilityCooldownOverlay(Image overlay, SoulLink.Ability.AbilityId abilityId, SoulLink.Ability.AbilityInputManager manager)
        {
            if (overlay == null || abilityId == SoulLink.Ability.AbilityId.None)
            {
                if (overlay != null) overlay.fillAmount = 0f;
                return;
            }

            if (manager == null)
            {
                if (overlay != null) overlay.fillAmount = 0f;
                return;
            }

            float progress = manager.GetAbilityCooldownProgress(abilityId);
            if (overlay != null)
            {
                overlay.fillAmount = progress;
            }
        }

        private void RefreshAbilitySystemBinding()
        {
            _activeAbilitySystem = null;

            if (_abilityInputManager != null)
            {
                _activeAbilitySystem = _abilityInputManager.ActiveAbilitySystem;
            }
            else if (soulLinkManager?.ActivePlayerCharacter != null)
            {
                _activeAbilitySystem = soulLinkManager.ActivePlayerCharacter.GetComponent<SoulLink.Ability.AbilitySystem>();
            }
        }

        // ----------------------------------------------------------------------------
        // Player Count Indicator
        // ----------------------------------------------------------------------------
        public void SetTotalPlayerSlots(int totalSlots)
        {
            _totalPlayerSlots = Mathf.Max(1, totalSlots);
            RefreshPlayerCount();
        }

        public void RefreshPlayerCount()
        {
            if (playerCountText == null) return;

            int activeCount = CountActivePlayers();
            playerCountText.text = $"{activeCount} / {_totalPlayerSlots}";
        }

        private int CountActivePlayers()
        {
            var manager = soulLinkManager;
            if (manager == null) return 0;

            int count = 0;
            foreach (var character in manager.characters)
            {
                if (character != null && character.isPlayerControlled)
                {
                    count++;
                }
            }

            return count;
        }

        // ----------------------------------------------------------------------------
        // Runtime Sync
        // ----------------------------------------------------------------------------
        private void Update()
        {
            SyncFromGameState();
        }

        private void SyncFromGameState()
        {
            var manager = soulLinkManager;
            if (manager == null) return;

            // Character wheel selection should reflect the active player character.
            RefreshActiveCharacterUI();
            RefreshAbilityBars();
            RefreshPlayerCount();

            // Cooldown overlays need per-frame updates because AbilityInputManager
            // exposes progress from its current routing target.
            if (_abilityInputManager != null)
            {
                RefreshAbilityCooldownOverlay(abilityCooldown1, _skillAbilityIds[0], _abilityInputManager);
                RefreshAbilityCooldownOverlay(abilityCooldown2, _skillAbilityIds[1], _abilityInputManager);
                RefreshAbilityCooldownOverlay(abilityCooldown3, _skillAbilityIds[2], _abilityInputManager);
            }
        }

        // ----------------------------------------------------------------------------
        // Wheel Slot Interaction
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Call this from a wheel slot UI button to attempt a character swap.
        /// </summary>
        public void OnWheelSlotSelected(int slotIndex)
        {
            if (wheelSlots == null) return;
            if (slotIndex < 0 || slotIndex >= wheelSlots.Length) return;

            var slot = wheelSlots[slotIndex];
            if (slot.character == null) return;

            var manager = soulLinkManager;
            if (manager == null) return;

            manager.AssignPlayer(1, slot.character);
            RefreshWheelFromRoster();
        }

        // ----------------------------------------------------------------------------
        // Layout Helper
        // ----------------------------------------------------------------------------
        /// <summary>
        /// Can be called if the canvas changes size at runtime or you support orientation
        /// changes and need to reposition HUD parts.
        /// </summary>
        public void RequestLayoutRefresh()
        {
            // Placeholder for any custom anchor/position logic.
            // Most projects can rely on CanvasScaler + anchors.
        }
    }

    // ----------------------------------------------------------------------------
    // Wheel slot data bound to UI
    // ----------------------------------------------------------------------------
    [Serializable]
    public class CharacterWheelSlot
    {
        public SoulLink.CharacterBase character;
        public SoulLink.CharacterID characterID;

        [Tooltip("UI image for the slot portrait/icon.")]
        public Image iconImage;

        [Tooltip("UI text for character name in the slot.")]
        public Text nameText;

        [Tooltip("Button that triggers OnWheelSlotSelected on MobileHudManager.")]
        public Button selectButton;

        // Visual feedback helpers.
        [Tooltip("Highlight overlay when this slot is selected.")]
        public Image selectionHighlight;

        private int _slotIndex;

        public void Bind(SoulLink.CharacterBase character, SoulLink.CharacterID characterID)
        {
            this.character = character;
            this.characterID = characterID;

            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(character != null);
            }

            if (nameText != null)
            {
                nameText.text = character != null ? character.characterID.ToString() : "Empty";
            }

            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                // Capture current slot index for selection.
                var capturedSlotIndex = _slotIndex;
                selectButton.onClick.AddListener(() =>
                {
                    var hud = SoulLink.UI.MobileHudManager.Instance;
                    if (hud != null)
                    {
                        hud.OnWheelSlotSelected(capturedSlotIndex);
                    }
                });
            }
        }

        public void SetSlotIndex(int index)
        {
            _slotIndex = index;
        }

        public void SetSelected(bool selected)
        {
            if (selectionHighlight != null)
            {
                selectionHighlight.gameObject.SetActive(selected);
            }
        }
    }
}
