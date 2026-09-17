using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PilotBonuses
{
    public float damage;
    public float armor;
    public float health;
    public float range;
    public float fire_rate;

    public float Damage => damage;
    public float Armor => armor;
    public float Health => health;
    public float Range => range;
    public float FireRate => fire_rate;
}

[Serializable]
public class PilotData
{
    public string id;
    public string name;
    public int health;
    public string skill;
    public PilotBonuses bonuses;

    public int Health => health;
    public string Skill => skill;
    public PilotBonuses Bonuses => bonuses;
}

[Serializable]
public class MechaData
{
    public string id;
    public string name;
    public int health;
    public float armor;
    public float shield;
    public float range;
    public float damage;
    public float fire_rate;
    public string[] module_slots;

    // For future assembly: assigned pilot and modules
    public PilotData assigned_pilot;
    public ModuleData[] assigned_modules;

    public int Health => health;
    public float Armor => armor;
    public float Shield => shield;
    public float Range => range;
    public float Damage => damage;
    public float FireRate => fire_rate;
    public string[] ModuleSlots => module_slots;
    public int SlotCount => module_slots != null ? module_slots.Length : 0;

    public bool CanAcceptModule(int slotIndex, ModuleData module)
    {
        if (module == null || module_slots == null || slotIndex < 0 || slotIndex >= module_slots.Length)
        {
            return false;
        }
        return string.Equals(module_slots[slotIndex], module.slot_color, StringComparison.OrdinalIgnoreCase);
    }
}

[Serializable]
public class ModuleBonuses
{
    public float armor;
    public float shield;
    public float damage;
    public float penetration;
    public float range;
    public float fire_rate;

    public float Armor => armor;
    public float Shield => shield;
    public float Damage => damage;
    public float Penetration => penetration;
    public float Range => range;
    public float FireRate => fire_rate;
}

[Serializable]
public class ModuleData
{
    public string id;
    public string name;
    public string slot_color;
    public ModuleBonuses bonuses;

    public string SlotColor => slot_color;
    public ModuleBonuses Bonuses => bonuses;
}

[Serializable]
public class GachaRewardData
{
    // Legacy / Shared fields
    public int health;
    public string skill;
    public float armor;
    public float shield;
    public float range;
    public float damage;
    public float fire_rate;
    public string[] module_slots;
    public string slot_color;

    // Nested reward models
    public PilotData pilot;
    public MechaData mecha;
    public ModuleData module;

    // Direct bonuses if flattened under data
    public PilotBonuses pilot_bonuses;
    public ModuleBonuses module_bonuses;

    public PilotData AsPilot(string fallbackId = null, string fallbackName = null)
    {
        if (pilot != null)
        {
            if (string.IsNullOrEmpty(pilot.id)) pilot.id = fallbackId;
            if (string.IsNullOrEmpty(pilot.name)) pilot.name = fallbackName;
            return pilot;
        }

        if (!string.IsNullOrEmpty(skill) || pilot_bonuses != null || health > 0)
        {
            return new PilotData
            {
                id = fallbackId,
                name = fallbackName,
                health = health,
                skill = skill,
                bonuses = pilot_bonuses
            };
        }

        return null;
    }

    public MechaData AsMecha(string fallbackId = null, string fallbackName = null)
    {
        if (mecha != null)
        {
            if (string.IsNullOrEmpty(mecha.id)) mecha.id = fallbackId;
            if (string.IsNullOrEmpty(mecha.name)) mecha.name = fallbackName;
            return mecha;
        }

        if (module_slots != null || armor > 0 || shield > 0 || damage > 0 || fire_rate > 0)
        {
            return new MechaData
            {
                id = fallbackId,
                name = fallbackName,
                health = health,
                armor = armor,
                shield = shield,
                range = range,
                damage = damage,
                fire_rate = fire_rate,
                module_slots = module_slots
            };
        }

        return null;
    }

    public ModuleData AsModule(string fallbackId = null, string fallbackName = null)
    {
        if (module != null)
        {
            if (string.IsNullOrEmpty(module.id)) module.id = fallbackId;
            if (string.IsNullOrEmpty(module.name)) module.name = fallbackName;
            return module;
        }

        if (!string.IsNullOrEmpty(slot_color) || module_bonuses != null)
        {
            return new ModuleData
            {
                id = fallbackId,
                name = fallbackName,
                slot_color = slot_color,
                bonuses = module_bonuses
            };
        }

        return null;
    }
}

[Serializable]
public class GachaReward
{
    public string awarded_at;
    public string id;
    public string name;
    public string tier;
    public string type;
    public GachaRewardData data;

    // Root-level nested objects if sent directly
    public PilotData pilot;
    public MechaData mecha;
    public ModuleData module;

    public PilotData GetPilot()
    {
        if (pilot != null)
        {
            if (string.IsNullOrEmpty(pilot.id)) pilot.id = id;
            if (string.IsNullOrEmpty(pilot.name)) pilot.name = name;
            return pilot;
        }
        return data != null ? data.AsPilot(id, name) : null;
    }

    public MechaData GetMecha()
    {
        if (mecha != null)
        {
            if (string.IsNullOrEmpty(mecha.id)) mecha.id = id;
            if (string.IsNullOrEmpty(mecha.name)) mecha.name = name;
            return mecha;
        }
        return data != null ? data.AsMecha(id, name) : null;
    }

    public ModuleData GetModule()
    {
        if (module != null)
        {
            if (string.IsNullOrEmpty(module.id)) module.id = id;
            if (string.IsNullOrEmpty(module.name)) module.name = name;
            return module;
        }
        return data != null ? data.AsModule(id, name) : null;
    }
}

[Serializable]
public class GachaResponse
{
    public bool success;
    public string player_id;
    public GachaReward[] rewards;
    public PlayerStats stats;
}
