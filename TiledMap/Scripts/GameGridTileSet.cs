using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameGridTileSetMode
{
    DualGrid16,
    Blob47,
    CustomMaskRules
}

public enum MaskTileVariantMode
{
    Random,
    Animation
}

/// <summary>Playable sprite-frame animation for one animated grid cell.</summary>
public readonly struct GameGridTileAnimation
{
    public readonly Sprite[] frames;
    public readonly float framesPerSecond;
    public readonly float timeOffset;
    public readonly bool loop;

    public GameGridTileAnimation(
        Sprite[] frames,
        float framesPerSecond,
        float timeOffset,
        bool loop)
    {
        this.frames = frames;
        this.framesPerSecond = framesPerSecond;
        this.timeOffset = timeOffset;
        this.loop = loop;
    }

    public bool IsAnimated => frames != null && frames.Length > 1;

    public Sprite GetFrame(int index)
    {
        if (frames == null || frames.Length == 0) return null;
        return frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }
}

public readonly struct GameGridTileSelection
{
    public readonly Sprite sprite;
    public readonly GameGridTileAnimation animation;

    public GameGridTileSelection(Sprite sprite, GameGridTileAnimation animation)
    {
        this.sprite = sprite;
        this.animation = animation;
    }

    public bool IsAnimated => animation.IsAnimated;
}

/// <summary>
/// Base class for one terrain layer rendered by <see cref="GameGridRenderer"/>.
/// Concrete subclasses own the sprite data for their layout, and the mode is
/// derived from the subclass type instead of being stored on the asset.
/// </summary>
public abstract class GameGridTileSet : ScriptableObject
{
    public string displayName = "Terrain";
    public Color tint = Color.white;
    public Material materialTemplate;
    [SerializeField] private GameGridTileType tileType = GameGridTileType.Rectangle;
    [SerializeField] private List<MaskTileVariant> maskVariants = new List<MaskTileVariant>();

    [Serializable]
    public sealed class MaskTileVariant
    {
        public int mask;
        public MaskTileVariantMode mode = MaskTileVariantMode.Random;
        public int randomSeed;
        public Sprite[] sprites = Array.Empty<Sprite>();

        [Header("Animation")]
        [Min(0.01f)] public float framesPerSecond = 8f;
        public bool loop = true;
        [Range(0f, 1f)] public float phaseOffset;

        public Sprite GetSprite(Vector2Int position)
        {
            if (sprites == null || sprites.Length == 0) return null;
            if (mode == MaskTileVariantMode.Animation) return sprites[0];

            // Keep the editor-configured seed as the root, then salt it by cell
            // position so identical masks still vary across the map.
            var seed = (int)((long)randomSeed ^ ((long)mask * 2654435761L));
            seed ^= position.x * 73856093;
            seed ^= position.y * 19349663;
            var prng = new System.Random(seed);
            return sprites[prng.Next(0, sprites.Length)];
        }

        public bool TryResolve(Vector2Int position, out GameGridTileSelection selection)
        {
            selection = default;
            if (sprites == null || sprites.Length == 0) return false;

            if (mode == MaskTileVariantMode.Animation)
            {
                for (var i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] == null) return false;
                }
                if (!TryGetAnimation(out var animation)) return false;
                selection = new GameGridTileSelection(sprites[0], animation);
                return true;
            }

            var sprite = GetSprite(position);
            if (sprite == null) return false;

            selection = new GameGridTileSelection(sprite, default);
            return true;
        }

        /// <summary>
        /// Creates a direct sprite-frame animation; each animated cell owns its
        /// own SpriteRenderer, so frames can live in any sprite sheet.
        /// </summary>
        public bool TryGetAnimation(out GameGridTileAnimation animation)
        {
            animation = default;
            if (sprites == null || sprites.Length < 2) return false;

            for (var i = 1; i < sprites.Length; i++)
            {
                if (sprites[i] == null) return false;
            }

            var fps = Mathf.Max(0.01f, framesPerSecond);
            var cycleSeconds = sprites.Length / fps;
            animation = new GameGridTileAnimation(
                sprites,
                fps,
                phaseOffset * cycleSeconds,
                loop);
            return true;
        }
    }

    /// <summary>How this asset maps masks to sprites; defined by the concrete type.</summary>
    public abstract GameGridTileSetMode Mode { get; }

    /// <summary>Number of sprite slots exposed through <see cref="GetSpriteAt"/>.</summary>
    public abstract int SpriteCount { get; }

    /// <summary>Resolve a raw terrain mask to its sprite.</summary>
    public abstract Sprite GetTileForMask(int mask);

    /// <summary>Resolve a slot index; used by the inspector's grid view.</summary>
    public abstract Sprite GetSpriteAt(int index);

    /// <summary>
    /// True when a sprite is placed on every cell corner from the four surround
    /// cells; false when one sprite is placed per filled cell from a cell mask.
    /// </summary>
    public abstract bool UsesCornerLayout { get; }

    public GameGridTileType TileType => tileType;

    public void SetGridTileType(GameGridTileType value)
    {
        tileType = value;
    }

    public string SafeName
    {
        get { return string.IsNullOrWhiteSpace(displayName) ? name : displayName; }
    }

    public bool IsBlob47 => Mode == GameGridTileSetMode.Blob47;
    public bool IsCustomMaskRules => Mode == GameGridTileSetMode.CustomMaskRules;

    public Sprite GetTileForMask(int mask, Vector2Int position)
    {
        return Resolve(mask, position).sprite;
    }

    /// <summary>
    /// Resolve the mask to both a sprite and optional per-cell animation data.
    /// </summary>
    public GameGridTileSelection Resolve(int mask, Vector2Int position)
    {
        if (maskVariants != null)
        {
            for (var i = 0; i < maskVariants.Count; i++)
            {
                var variant = maskVariants[i];
                if (variant == null || variant.mask != mask) continue;
                if (variant.TryResolve(position, out var selection))
                {
                    return selection;
                }
            }
        }

        return new GameGridTileSelection(GetTileForMask(mask), default);
    }

    public bool AddMaskVariant(int mask)
    {
        if (mask < 0) return false;
        if (maskVariants == null) maskVariants = new List<MaskTileVariant>();

        for (var i = 0; i < maskVariants.Count; i++)
        {
            var variant = maskVariants[i];
            if (variant != null && variant.mask == mask) return false;
        }

        maskVariants.Add(new MaskTileVariant
        {
            mask = mask,
            mode = MaskTileVariantMode.Random,
            randomSeed = 0,
            sprites = Array.Empty<Sprite>()
        });
        return true;
    }

    public bool HasTile(int mask)
    {
        return GetTileForMask(mask) != null;
    }

    public int CountAssignedTiles()
    {
        var count = 0;
        for (var i = 0; i < SpriteCount; i++)
            if (GetSpriteAt(i) != null)
                count++;
        return count;
    }

    /// <summary>
    /// Guarantees shared collections exist. Subclasses override to resize their
    /// own sprite arrays and must call the base implementation.
    /// </summary>
    public virtual void EnsureSlots()
    {
        if (maskVariants == null) maskVariants = new List<MaskTileVariant>();
    }

    private void OnEnable()
    {
        EnsureSlots();
    }

    private void OnValidate()
    {
        EnsureSlots();
    }
}
