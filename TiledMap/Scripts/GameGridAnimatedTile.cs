using System;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class GameGridAnimatedTile : MonoBehaviour
{
    [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();
    [SerializeField, Min(0.01f)] private float framesPerSecond = 8f;
    [SerializeField] private bool loop = true;

    private SpriteRenderer spriteRenderer;
    private float elapsed;
    private int currentFrame = -1;

    public void Setup(GameGridTileAnimation animation)
    {
        frames = animation.frames ?? Array.Empty<Sprite>();
        framesPerSecond = Mathf.Max(0.01f, animation.framesPerSecond);
        loop = animation.loop;
        elapsed = Mathf.Max(0f, animation.timeOffset);
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        UpdateSprite(0);
    }

    private void Update()
    {
        if (frames == null || frames.Length < 2 || spriteRenderer == null) return;
        elapsed += Time.deltaTime;
        var fps = Mathf.Max(0.01f, framesPerSecond);
        var cycle = frames.Length / fps;
        if (loop)
        {
            elapsed = Mathf.Repeat(elapsed, cycle);
        }
        else
        {
            elapsed = Mathf.Min(elapsed, cycle);
        }

        var normalized = elapsed / cycle;
        var frame = Mathf.FloorToInt(normalized * frames.Length);
        UpdateSprite(Mathf.Clamp(frame, 0, frames.Length - 1));
    }

    private void UpdateSprite(int frame)
    {
        if (frame == currentFrame) return;
        currentFrame = frame;
        if (spriteRenderer != null) spriteRenderer.sprite = frames[frame];
    }
}
