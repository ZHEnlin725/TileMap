using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mask layout driven by explicit rules: an exact mask match wins, otherwise the
/// first rule whose bits are all present in the queried mask is used.
/// </summary>
public sealed class CustomMaskRulesTileSet : GameGridTileSet
{
    [Serializable]
    public sealed class CustomTileRule
    {
        public int mask;
        public Sprite sprite;
    }

    public List<CustomTileRule> customRules = new List<CustomTileRule>();

    public override GameGridTileSetMode Mode => GameGridTileSetMode.CustomMaskRules;
    public override int SpriteCount => customRules == null ? 0 : customRules.Count;
    public override bool UsesCornerLayout => false;

    public override Sprite GetTileForMask(int mask)
    {
        if (customRules == null) return null;

        for (var i = 0; i < customRules.Count; i++)
        {
            var rule = customRules[i];
            if (rule != null && rule.mask == mask) return rule.sprite;
        }

        for (var i = 0; i < customRules.Count; i++)
        {
            var rule = customRules[i];
            if (rule != null && (rule.mask & mask) > 0
                && (rule.mask & ~mask) == 0) return rule.sprite;
        }

        return null;
    }

    public override Sprite GetSpriteAt(int index)
    {
        if (customRules == null || index < 0 || index >= customRules.Count) return null;
        return customRules[index]?.sprite;
    }

    public override void EnsureSlots()
    {
        base.EnsureSlots();
        if (customRules == null) customRules = new List<CustomTileRule>();
    }
}
