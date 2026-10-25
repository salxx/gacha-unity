using System;
using UnityEngine;

/// <summary>
/// Pull tiers as returned by the backend: 1 = Common / 1 star, 2 = Rare / 2 stars, 3 = Super Rare / 3 stars.
/// </summary>
public static class Tiers
{
    public const int Common = 1;
    public const int Rare = 2;
    public const int SuperRare = 3;

    public static int Clamp(int tier) => Mathf.Clamp(tier, Common, SuperRare);


    public static string ShortName(int tier) => Clamp(tier) switch
    {
        Rare => "R",
        SuperRare => "SR",
        _ => "C"
    };
}

/// <summary>
/// The 5 trainable traits. Also used for growth bonuses (in percent, e.g. 20 = +20%, -20 = -20%).
/// </summary>
[Serializable]
public class Traits
{
    public int power;   // raw damage
    public int agility; // attack speed / fire rate
    public int impact;  // armor penetration
    public int defense; // health and armor
    public int finesse; // skill trigger and dodge chance

    public int Total => power + agility + impact + defense + finesse;

    public override string ToString() =>
        $"POW {power} / AGI {agility} / IMP {impact} / DEF {defense} / FIN {finesse}";
}

/// <summary>
/// Positional affinity grades (S, A, B, C, D, E, F) for the 4 ranges:
/// front (Front Line), close (Close Range), mid (Mid Range), back (Back Line).
/// At least one position is A by default; S is only reachable through play.
/// </summary>
[Serializable]
public class PositionAffinity
{
    public string front = "F";
    public string close = "F";
    public string mid = "F";
    public string back = "F";

    // Index = rank: F (0) .. S (6)
    private static readonly string[] Grades = { "F", "E", "D", "C", "B", "A", "S" };

    public const string DefaultMax = "A";

    public static int Rank(string grade)
    {
        int index = Array.IndexOf(Grades, grade?.Trim().ToUpperInvariant());
        return index < 0 ? 0 : index;
    }

    public static string Grade(int rank) => Grades[Mathf.Clamp(rank, 0, Grades.Length - 1)];

    /// <summary>Highest grade a position can ever reach from its starting grade: E/F cap at B, D needs deep commitment for A/S.</summary>
    public static string Ceiling(string startGrade) => Rank(startGrade) <= Rank("E") ? "B" : "S";

    /// <summary>Default affinity is valid if no grade is S and at least one is A.</summary>
    public bool IsValidDefault()
    {
        int max = Mathf.Max(Mathf.Max(Rank(front), Rank(close)), Mathf.Max(Rank(mid), Rank(back)));
        return max == Rank(DefaultMax);
    }

    public override string ToString() => $"Front {front} / Close {close} / Mid {mid} / Back {back}";
}

/// <summary>
/// Unique gear of a character: boosts an effect linked to one trait (value in percent).
/// </summary>
[Serializable]
public class GearData
{
    public string id;
    public string name;
    public string trait;  // "power" | "agility" | "impact" | "defense" | "finesse"
    public string effect; // e.g. "penetration_damage", "armor", "skill_trigger"
    public int value;
}

/// <summary>
/// Baseline PvE formulas derived from traits.
/// </summary>
public static class TraitMath
{
    /// <summary>Applies a growth bonus (percent) to a raw training gain.</summary>
    public static int ApplyGrowth(int gain, int growthPercent) =>
        Mathf.Max(0, Mathf.RoundToInt(gain * (1f + growthPercent / 100f)));

    public static float Damage(Traits t) => t.power;
    public static float AttacksPerSecond(Traits t) => 0.5f + t.agility / 200f;
    public static int MaxHealth(Traits t) => 100 + t.defense * 2;
    public static float Armor(Traits t) => t.defense / 2f;
    public static float Penetration(Traits t) => t.impact / 2f;
    public static float SkillTriggerChance(Traits t) => Mathf.Clamp01(t.finesse / 1000f);
    public static float DodgeChance(Traits t) => Mathf.Clamp(t.finesse / 2000f, 0f, 0.5f);

    /// <summary>Damage reduction from armor left after penetration (armor / (armor + 100)).</summary>
    public static float DamageReduction(float armor, float penetration)
    {
        float effective = Mathf.Max(0f, armor - penetration);
        return effective / (effective + 100f);
    }
}

[Serializable]
public class CharacterData
{
    public string id;
    public string name;
    public int tier; // 1 to 5 stars (pulled at 1 to 3, upgradeable to 5)
    public Traits traits = new Traits();
    public Traits growth = new Traits();
    public PositionAffinity affinity = new PositionAffinity();
    public GearData gear;

    public string StarsDisplay => $"{Mathf.Max(tier, 1)}★";
}

[Serializable]
public class SupportCardData
{
    public string id;
    public string name;
    public int tier; // 1 (C), 2 (R), 3 (SR)
    public string facility; // "power" | "agility" | "impact" | "defense" | "finesse"
    public int level = 1;
    public int bond = 20; // 0 to 100%
    public string[] skills = Array.Empty<string>();

    public int MaxLevel => 20 + Tiers.Clamp(tier) * 10; // 30 / 40 / 50
    public bool IsRainbowEligible => bond >= 80;
}

[Serializable]
public class FacilityPreview
{
    public string facility;
    public int level;
    public float failure_rate;
    public int energy_cost;
    public int energy_restore;
    public string main_stat;
    public int main_gain;
    public string secondary_stat;
    public int secondary_gain;
    public bool rainbow_active;
    public int rainbow_bonus_percent;
    public string[] present_card_ids = Array.Empty<string>();
}

[Serializable]
public class FacilityLevelMap
{
    public int power = 1;
    public int agility = 1;
    public int impact = 1;
    public int defense = 1;
    public int finesse = 1;

    public int GetLevel(string facility) => facility?.ToLowerInvariant() switch
    {
        "power" => power,
        "agility" => agility,
        "impact" => impact,
        "defense" => defense,
        "finesse" => finesse,
        _ => 1
    };
}

[Serializable]
public class FacilityPreviewMap
{
    public FacilityPreview power;
    public FacilityPreview agility;
    public FacilityPreview impact;
    public FacilityPreview defense;
    public FacilityPreview finesse;

    public FacilityPreview GetPreview(string facility) => facility?.ToLowerInvariant() switch
    {
        "power" => power,
        "agility" => agility,
        "impact" => impact,
        "defense" => defense,
        "finesse" => finesse,
        _ => null
    };
}

[Serializable]
public class TrainingActionResult
{
    public string action;
    public string facility;
    public bool success;
    public float failure_rate;
    public string main_stat;
    public int main_gain;
    public string secondary_stat;
    public int secondary_gain;
    public int energy_consumed;
    public int energy_recovered;
    public int energy_after;
    public bool rainbow_triggered;
}

[Serializable]
public class TrainingRunData
{
    public string run_id;
    public string player_id;
    public string status; // "active" | "completed"
    public int round = 1;
    public int max_rounds = 36;
    public int energy = 100;
    public int max_energy = 100;
    public CharacterData trainee;
    public FacilityLevelMap facility_levels = new FacilityLevelMap();
    public SupportCardData[] support_cards = Array.Empty<SupportCardData>();
    public FacilityPreviewMap facility_previews = new FacilityPreviewMap();
    public TrainingActionResult last_action_result;

    public bool IsCompleted => status == "completed" || round > max_rounds;
}

[Serializable]
public class TrainingResponse
{
    public bool success;
    public TrainingRunData run;
    public TrainingActionResult action_result;
}

[Serializable]
public class GachaRewardData
{
    public string facility;
    public Traits base_traits;
    public Traits growth;
    public PositionAffinity affinity;
    public GearData gear;
    public string[] skills;
}

[Serializable]
public class GachaReward
{
    public const string SupportCardType = "support_card";

    public string id;
    public string name;
    public int tier;
    public string facility;
    public string type; // "character" or "support_card"
    public string awarded_at;
    public GachaRewardData data;

    public int Tier => Tiers.Clamp(tier);

    public bool IsSupportCard => type == SupportCardType;

    public CharacterData ToCharacter() => new CharacterData
    {
        id = id,
        name = name,
        tier = Tier,
        traits = data?.base_traits ?? new Traits(),
        growth = data?.growth ?? new Traits(),
        affinity = data?.affinity ?? new PositionAffinity(),
        gear = data?.gear
    };

    public SupportCardData ToSupportCard() => new SupportCardData
    {
        id = id,
        name = name,
        tier = Tier,
        facility = !string.IsNullOrEmpty(facility) ? facility : (data?.facility ?? "power"),
        skills = data?.skills ?? Array.Empty<string>()
    };

    public string Label
    {
        get
        {
            string displayName = string.IsNullOrEmpty(name) ? "Unknown" : name;

            if (IsSupportCard)
            {
                SupportCardData card = ToSupportCard();
                string header = $"{displayName} [{Tiers.ShortName(Tier)} | {card.facility.ToUpperInvariant()}]\n<Support Card>";
                return card.skills.Length > 0 ? $"{header} ({string.Join(", ", card.skills)})" : header;
            }

            CharacterData character = ToCharacter();
            string text = $"{displayName} [{character.StarsDisplay}]\n<Character>";
            return character.traits.Total > 0 ? $"{text}\n{character.traits}\n{character.affinity}" : text;
        }
    }
}

[Serializable]
public class GachaResponse
{
    public bool success;
    public string player_id;
    public string target;
    public int pack_cost;
    public GachaReward[] rewards;
    public PlayerStats stats;
}
