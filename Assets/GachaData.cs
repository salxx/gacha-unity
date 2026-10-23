using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CharacterData
{
    public string id;
    public string name;
    public int tier; // 1 to 5 stars (pulled at 1 to 3, upgradeable to 5)
    public int health;

    public int Tier => tier > 0 ? tier : 1;
    public int Stars => Tier;
    public int Health => health;

    public string GetTierDisplayName()
    {
        return $"{Tier} star";
    }

    public string GetRarityName()
    {
        switch (Tier)
        {
            case 1: return "Common";
            case 2: return "Rare";
            case 3: return "Super Rare";
            default: return "Super Rare";
        }
    }
}

[Serializable]
public class SupportCardData
{
    public string id;
    public string name;
    public int tier; // Base rarity tier: 1 (C), 2 (R), 3 (SR)
    public int level = 1;
    public string[] skills;

    public int Tier => tier > 0 ? tier : 1;
    public int Level => level > 0 ? level : 1;
    public string[] Skills => skills;

    public int MaxLevel
    {
        get
        {
            switch (Tier)
            {
                case 1: return 30; // Common max lvl 30
                case 2: return 40; // Rare max lvl 40
                case 3: return 50; // Super Rare max lvl 50
                default: return 30 + (Tier - 1) * 10;
            }
        }
    }

    public string GetTierDisplayName()
    {
        switch (Tier)
        {
            case 1: return "C";
            case 2: return "R";
            case 3: return "SR";
            default: return $"T{Tier}";
        }
    }

    public string GetRarityName()
    {
        switch (Tier)
        {
            case 1: return "Common";
            case 2: return "Rare";
            case 3: return "Super Rare";
            default: return $"Tier {Tier}";
        }
    }
}

[Serializable]
public class GachaRewardData
{
    public int health;
    public string[] skills;

    public CharacterData character;
    public SupportCardData support_card;

    public CharacterData AsCharacter(string fallbackId = null, string fallbackName = null, int fallbackTier = 1)
    {
        if (character != null)
        {
            if (string.IsNullOrEmpty(character.id)) character.id = fallbackId;
            if (string.IsNullOrEmpty(character.name)) character.name = fallbackName;
            if (character.tier <= 0) character.tier = fallbackTier;
            return character;
        }

        return new CharacterData
        {
            id = fallbackId,
            name = fallbackName,
            tier = fallbackTier,
            health = health
        };
    }

    public SupportCardData AsSupportCard(string fallbackId = null, string fallbackName = null, int fallbackTier = 1)
    {
        if (support_card != null)
        {
            if (string.IsNullOrEmpty(support_card.id)) support_card.id = fallbackId;
            if (string.IsNullOrEmpty(support_card.name)) support_card.name = fallbackName;
            if (support_card.tier <= 0) support_card.tier = fallbackTier;
            return support_card;
        }

        return new SupportCardData
        {
            id = fallbackId,
            name = fallbackName,
            tier = fallbackTier,
            level = 1,
            skills = skills
        };
    }
}

[Serializable]
public class GachaReward
{
    public string awarded_at;
    public string id;
    public string name;
    public int tier; // 1 (Common / 1-star), 2 (Rare / 2-star), 3 (Super Rare / 3-star)
    public string type;
    public GachaRewardData data;

    public CharacterData character;
    public SupportCardData support_card;

    public int TierInt => tier > 0 ? tier : 1;

    public bool IsSupportCard()
    {
        if (!string.IsNullOrEmpty(type) && (type.Equals("support_card", StringComparison.OrdinalIgnoreCase) || type.Equals("sc", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (id != null && id.StartsWith("sc_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (data != null && data.skills != null && data.skills.Length > 0)
            return true;
        return false;
    }

    public CharacterData GetCharacter()
    {
        if (character != null)
        {
            if (string.IsNullOrEmpty(character.id)) character.id = id;
            if (string.IsNullOrEmpty(character.name)) character.name = name;
            if (character.tier <= 0) character.tier = TierInt;
            return character;
        }
        return data != null ? data.AsCharacter(id, name, TierInt) : new CharacterData { id = id, name = name, tier = TierInt, health = 0 };
    }

    public SupportCardData GetSupportCard()
    {
        if (support_card != null)
        {
            if (string.IsNullOrEmpty(support_card.id)) support_card.id = id;
            if (string.IsNullOrEmpty(support_card.name)) support_card.name = name;
            if (support_card.tier <= 0) support_card.tier = TierInt;
            return support_card;
        }
        return data != null ? data.AsSupportCard(id, name, TierInt) : new SupportCardData { id = id, name = name, tier = TierInt, level = 1, skills = Array.Empty<string>() };
    }

    public string GetTierDisplay(bool asStars = false)
    {
        if (asStars || !IsSupportCard())
        {
            return $"{TierInt} star";
        }

        switch (TierInt)
        {
            case 1: return "Common";
            case 2: return "Rare";
            case 3: return "Super Rare";
            default: return $"Tier {TierInt}";
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
