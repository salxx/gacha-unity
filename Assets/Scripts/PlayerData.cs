using System;
using System.Collections.Generic;

[Serializable]
public class TraitShardBalances
{
    public int power;
    public int agility;
    public int impact;
    public int defense;
    public int finesse;
}

[Serializable]
public class PlayerStats
{
    public string name;
    public int tokens;
    public TraitShardBalances trait_shards = new TraitShardBalances();
    public int packs_opened;
    public int ssr_offset;
    public int sr_offset; // Backwards compatible alias
}

[Serializable]
public class ConnectResponse
{
    public bool success;
    public string player_id;
    public PlayerStats stats;
}

[Serializable]
public class PlayerStatsResponse
{
    public bool success;
    public string player_id;
    public PlayerStats stats;
}

[Serializable]
public class DailyLoginReward
{
    public string type;
    public int amount;
}

[Serializable]
public class DailyLoginStatus
{
    public bool success;
    public string player_id;
    public string date;
    public bool available;
    public DailyLoginReward[] rewards;
    public PlayerStats stats;
}

[Serializable]
public class DailyLoginClaim
{
    public bool success;
    public string player_id;
    public PlayerStats stats;
}

[Serializable]
public class ErrorResponse
{
    public bool success;
    public string message;
    public int required_tokens;
}

[Serializable]
public class PlayerData
{
    public string playerId;
    public PlayerStats stats = new PlayerStats();

    public List<CharacterData> characters = new List<CharacterData>();
    public List<SupportCardData> supportCards = new List<SupportCardData>();

    public int Tokens => stats.tokens;

    public string DisplayName => string.IsNullOrEmpty(stats.name) ? playerId : stats.name;

    public PlayerData(string playerId, PlayerStats stats)
    {
        this.playerId = playerId;
        this.stats = stats ?? new PlayerStats();
    }

    public void AddRewards(IEnumerable<GachaReward> rewards, bool isCharacter)
    {
        foreach (GachaReward reward in rewards)
        {
            if (reward.IsSupportCard)
            {
                supportCards.Add(reward.ToSupportCard());
            }
            else
            {
                characters.Add(reward.ToCharacter());
            }
        }
    }
}
