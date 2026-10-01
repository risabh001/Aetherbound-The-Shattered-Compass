using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulLink
{
    /// <summary>
    /// Enumerates the abilities available in the Soul-Link system.
    /// Each character can expose a subset of these abilities.
    /// </summary>
    public enum AbilityId
    {
        None = 0,

        // Vanguard (Kaelen)
        ShieldBash,
        Taunt,
        HeavyArmor,

        // Weaver (Lyra)
        Heal,
        GravityLift,
        ShieldAura,

        // Shadow (Jax)
        Stealth,
        WallRun,
        Backstab,

        // Artificer (Elara)
        FrostArrow,
        ZiplineAnchor,
        DroneScout
    }

    /// <summary>
    /// Lightweight definition of an ability, used by characters and the ability system.
    /// This is data-driven so abilities can be configured in the Inspector or by code.
    /// </summary>
    [Serializable]
    public class AbilityDefinition
    {
        public AbilityId abilityId;
        public string displayName;
        public string description;

        /// <summary>
        /// Base cooldown in seconds before this ability can be used again.
        /// </summary>
        public float cooldownSeconds;

        /// <summary>
        /// Whether the ability can be used while the character is in AI mode.
        /// AI can still use abilities internally; this flag is mainly for UI/input gating.
        /// </summary>
        public bool usableInAI;

        /// <summary>
        /// If true, the ability is considered active/holding rather than instant.
        /// </summary>
        public bool isChanneling;
    }

    /// <summary>
    /// Helper struct for runtime cooldown tracking.
    /// </summary>
    public struct AbilityCooldown
    {
        public AbilityId abilityId;
        public float availableTime; // timestamp when the cooldown expires

        public bool IsReady(float currentTime)
        {
            return currentTime >= availableTime;
        }
    }

    /// <summary>
    /// Configuration container for a full character ability roster.
    /// Characters reference one of these so visuals/UI and logic stay decoupled.
    /// </summary>
    [Serializable]
    public class CharacterAbilityRoster
    {
        public CharacterID characterID;
        public List<AbilityDefinition> abilities = new List<AbilityDefinition>();
    }

    /// <summary>
    /// Static registry of the default ability rosters for the four soul-linked characters.
    /// Replace or extend these defaults with data assets or scriptable objects if you prefer.
    /// </summary>
    public static class DefaultAbilityRegistries
    {
        public static IReadOnlyList<CharacterAbilityRoster> All => _all;
        private static readonly IReadOnlyList<CharacterAbilityRoster> _all = new List<CharacterAbilityRoster>
        {
            KaelenVanguard,
            LyraWeaver,
            JaxShadow,
            ElaraArtificer
        };

        public static CharacterAbilityRoster KaelenVanguard { get; } = new CharacterAbilityRoster
        {
            characterID = CharacterID.Warrior,
            abilities = new List<AbilityDefinition>
            {
                new AbilityDefinition
                {
                    abilityId = AbilityId.ShieldBash,
                    displayName = "Shield Bash",
                    description = "Stun a nearby target with a shield strike.",
                    cooldownSeconds = 6f,
                    usableInAI = true,
                    isChanneling = false
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.Taunt,
                    displayName = "Taunt",
                    description = "Force enemies to attack you for a short duration.",
                    cooldownSeconds = 12f,
                    usableInAI = true,
                    isChanneling = false
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.HeavyArmor,
                    displayName = "Heavy Armor",
                    description = "Temporarily increase damage reduction.",
                    cooldownSeconds = 15f,
                    usableInAI = true,
                    isChanneling = true
                }
            }
        };

        public static CharacterAbilityRoster LyraWeaver { get; } = new CharacterAbilityRoster
        {
            characterID = CharacterID.Healer,
            abilities = new List<AbilityDefinition>
            {
                new AbilityDefinition
                {
                    abilityId = AbilityId.Heal,
                    displayName = "Heal",
                    description = "Restore health to a targeted ally.",
                    cooldownSeconds = 8f,
                    usableInAI = true,
                    isChanneling = false
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.GravityLift,
                    displayName = "Gravity Lift",
                    description = "Lift an ally or projectile with gravity control.",
                    cooldownSeconds = 10f,
                    usableInAI = true,
                    isChanneling = true
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.ShieldAura,
                    displayName = "Shield Aura",
                    description = "Surround the party with a protective barrier.",
                    cooldownSeconds = 18f,
                    usableInAI = true,
                    isChanneling = true
                }
            }
        };

        public static CharacterAbilityRoster JaxShadow { get; } = new CharacterAbilityRoster
        {
            characterID = CharacterID.Archer,
            abilities = new List<AbilityDefinition>
            {
                new AbilityDefinition
                {
                    abilityId = AbilityId.Stealth,
                    displayName = "Stealth",
                    description = "Become harder to detect for a short time.",
                    cooldownSeconds = 14f,
                    usableInAI = true,
                    isChanneling = false
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.WallRun,
                    displayName = "Wall Run",
                    description = "Run along walls to reposition quickly.",
                    cooldownSeconds = 9f,
                    usableInAI = true,
                    isChanneling = true
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.Backstab,
                    displayName = "Backstab",
                    description = "Execute a high-damage attack from behind.",
                    cooldownSeconds = 7f,
                    usableInAI = true,
                    isChanneling = false
                }
            }
        };

        public static CharacterAbilityRoster ElaraArtificer { get; } = new CharacterAbilityRoster
        {
            characterID = CharacterID.Mage,
            abilities = new List<AbilityDefinition>
            {
                new AbilityDefinition
                {
                    abilityId = AbilityId.FrostArrow,
                    displayName = "Frost Arrow",
                    description = "Fire a slowing projectile at a target.",
                    cooldownSeconds = 5f,
                    usableInAI = true,
                    isChanneling = false
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.ZiplineAnchor,
                    displayName = "Zipline Anchor",
                    description = "Create a zipline between two anchor points.",
                    cooldownSeconds = 16f,
                    usableInAI = true,
                    isChanneling = false
                },
                new AbilityDefinition
                {
                    abilityId = AbilityId.DroneScout,
                    displayName = "Drone Scout",
                    description = "Send a drone to reveal nearby enemies.",
                    cooldownSeconds = 20f,
                    usableInAI = true,
                    isChanneling = false
                }
            }
        };
    }
}
