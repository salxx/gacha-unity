using System;
using System.Collections.Generic;

[Serializable]
public class UserData
{
    public int tokens;
    public int packs_opened;
}

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
    public UserData userdata;
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
    public string playerName;
    public UserData userData = new UserData();
    public PlayerStats stats;

    public List<GachaReward> rewards = new List<GachaReward>();
    public List<CharacterData> characters = new List<CharacterData>();
    public List<SupportCardData> supportCards = new List<SupportCardData>();

    public int Tokens
    {
        get
        {
            if (stats != null)
            {
                return stats.tokens;
            }
            return userData != null ? userData.tokens : 0;
        }
        set
        {
            if (userData == null)
            {
                userData = new UserData();
            }
            userData.tokens = value;
            if (stats != null)
            {
                stats.tokens = value;
            }
        }
    }

    public int PacksOpened
    {
        get
        {
            if (stats != null)
            {
                return stats.packs_opened;
            }
            return userData != null ? userData.packs_opened : 0;
        }
        set
        {
            if (userData == null)
            {
                userData = new UserData();
            }
            userData.packs_opened = value;
            if (stats != null)
            {
                stats.packs_opened = value;
            }
        }
    }

    public string DisplayName
    {
        get
        {
            if (stats != null && !string.IsNullOrEmpty(stats.name))
            {
                return stats.name;
            }
            if (!string.IsNullOrEmpty(playerName))
            {
                return playerName;
            }
            return playerId;
        }
    }

    public PlayerData() { }

    public PlayerData(string playerId, UserData userData)
    {
        this.playerId = playerId;
        this.userData = userData ?? new UserData();
    }

    public PlayerData(string playerId, PlayerStats stats)
    {
        this.playerId = playerId;
        this.stats = stats;
        if (stats != null)
        {
            this.playerName = stats.name;
            this.userData = new UserData
            {
                tokens = stats.tokens,
                packs_opened = stats.packs_opened
            };
        }
    }

    public void AddReward(GachaReward reward)
    {
        if (reward == null) return;
        rewards.Add(reward);

        if (reward.IsSupportCard())
        {
            supportCards.Add(reward.GetSupportCard());
        }
        else
        {
            characters.Add(reward.GetCharacter());
        }
    }

    public void AddRewards(IEnumerable<GachaReward> newRewards)
    {
        if (newRewards == null) return;
        foreach (var reward in newRewards)
        {
            AddReward(reward);
        }
    }
}
