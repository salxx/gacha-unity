using System;
using System.Collections.Generic;

[Serializable]
public class PlayerStats
{
    public string name;
    public int tokens;
    public int packs_opened;
    public int sr_offset;
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

    public void AddRewards(IEnumerable<GachaReward> rewards)
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
