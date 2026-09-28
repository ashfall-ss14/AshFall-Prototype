using Robust.Shared.Configuration;

namespace Content.Shared.Ashfall;

/// <summary>
///     Configuration variables specific to Ashfall.
/// </summary>
[CVarDefs]
public sealed class AshfallCCVars
{
    /// <summary>
    ///     Lobby background prototype used by Ashfall. An empty value restores random selection.
    /// </summary>
    public static readonly CVarDef<string> LobbyBackground =
        CVarDef.Create("ashfall.lobby_background", "AshfallMain", CVar.SERVERONLY);

    /// <summary>
    ///     Whether the roundstart character pool system is enabled.
    /// </summary>
    public static readonly CVarDef<bool> CharacterPoolEnabled =
        CVarDef.Create("ashfall.character_pool_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Number of candidates generated per round pool.
    /// </summary>
    public static readonly CVarDef<int> CharacterPoolSize =
        CVarDef.Create("ashfall.character_pool_size", 5, CVar.SERVERONLY);

    /// <summary>
    ///     Cooldown in seconds between pool refreshes ("ЗАПРОСИТЬ ДРУГИЕ ЛИЧНЫЕ ДЕЛА").
    /// </summary>
    public static readonly CVarDef<float> CharacterPoolRefreshCooldown =
        CVarDef.Create("ashfall.character_pool_refresh_cooldown", 3.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum allowed pool refreshes per player per round (-1 for unlimited).
    /// </summary>
    public static readonly CVarDef<int> CharacterPoolMaxRefreshes =
        CVarDef.Create("ashfall.character_pool_max_refreshes", -1, CVar.SERVERONLY);

    /// <summary>
    ///     Whether examines and action popups should also be logged into the chat box.
    /// </summary>
    public static readonly CVarDef<bool> ChatLogInChat =
        CVarDef.Create("chat.log_in_chat", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Whether identical consecutive messages in the chat box should be coalesced into a single line with counter.
    /// </summary>
    public static readonly CVarDef<bool> ChatCoalesceIdenticalMessages =
        CVarDef.Create("chat.coalesce_identical_messages", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether dynamic agony/damage post-processing effects (shock blur, desaturation near crit, tunnel vision) are enabled.
    /// </summary>
    public static readonly CVarDef<bool> AgonyOverlayEnabled =
        CVarDef.Create("ashfall.agony_overlay", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether tinnitus high-pitch ringing sound is played upon suffering ear damage or muzzle blast.
    /// </summary>
    public static readonly CVarDef<bool> TinnitusAudioEnabled =
        CVarDef.Create("ashfall.tinnitus_audio_enabled", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether inter-character memory links are generated and active.
    /// </summary>
    public static readonly CVarDef<bool> MemoryEnabled =
        CVarDef.Create("ashfall.memory_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Interval in seconds between proximity-based recognition checks.
    /// </summary>
    public static readonly CVarDef<float> MemoryProximityInterval =
        CVarDef.Create("ashfall.memory_proximity_interval", 2.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum memory links to generate per character.
    /// </summary>
    public static readonly CVarDef<int> MemoryMinPerCharacter =
        CVarDef.Create("ashfall.memory_min_per_character", 1, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum memory links to generate per character.
    /// </summary>
    public static readonly CVarDef<int> MemoryMaxPerCharacter =
        CVarDef.Create("ashfall.memory_max_per_character", 3, CVar.SERVERONLY);

    /// <summary>
    ///     Base probability of forming a memory link between eligible characters.
    /// </summary>
    public static readonly CVarDef<float> MemoryBaseProbability =
        CVarDef.Create("ashfall.memory_base_probability", 0.6f, CVar.SERVERONLY);

    /// <summary>
    ///     Distance range in meters for proximity-based recognition.
    /// </summary>
    public static readonly CVarDef<float> MemoryProximityRange =
        CVarDef.Create("ashfall.memory_proximity_range", 6.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum discovery delay in seconds for pending recognition.
    /// </summary>
    public static readonly CVarDef<float> MemoryMinDiscoveryDelay =
        CVarDef.Create("ashfall.memory_min_discovery_delay", 1.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum discovery delay in seconds for pending recognition.
    /// </summary>
    public static readonly CVarDef<float> MemoryMaxDiscoveryDelay =
        CVarDef.Create("ashfall.memory_max_discovery_delay", 4.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Voice range in meters for spoken recognition triggers.
    /// </summary>
    public static readonly CVarDef<float> MemoryVoiceRange =
        CVarDef.Create("ashfall.memory_voice_range", 7.0f, CVar.SERVERONLY);
}
