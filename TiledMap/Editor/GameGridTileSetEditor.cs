using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(GameGridTileSet), true)]
public sealed class GameGridTileSetEditor : Editor
{
    private const float SlotSize = 52f;
    private const float SlotGap = 4f;
    private const int Columns = 4;
    private const int BlobColumns = 8;

    private SerializedProperty displayNameProperty;
    private SerializedProperty tilesProperty;
    private SerializedProperty blobTilesProperty;
    private SerializedProperty tintProperty;
    private SerializedProperty materialProperty;
    private SerializedProperty tileTypeProperty;
    private SerializedProperty sourceTilesetProperty;
    private SerializedProperty maskVariantsProperty;
    private SerializedProperty customRulesProperty;
    private ReorderableList customRulesList;

    private void OnEnable()
    {
        displayNameProperty = serializedObject.FindProperty("displayName");
        tilesProperty = serializedObject.FindProperty("tiles");
        blobTilesProperty = serializedObject.FindProperty("blobTiles");
        tintProperty = serializedObject.FindProperty("tint");
        materialProperty = serializedObject.FindProperty("materialTemplate");
        tileTypeProperty = serializedObject.FindProperty("tileType");
        sourceTilesetProperty = serializedObject.FindProperty("sourceTileset");
        maskVariantsProperty = serializedObject.FindProperty("maskVariants");
        customRulesProperty = serializedObject.FindProperty("customRules");
        ((GameGridTileSet)target).EnsureSlots();

        if (customRulesProperty == null) return;

        customRulesList = new ReorderableList(
            serializedObject,
            customRulesProperty,
            true,
            true,
            true,
            true);
        customRulesList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Custom Mask Rules");
        customRulesList.drawElementCallback = (rect, index, isActive, isFocused) =>
            DrawCustomRuleElement(rect, index);
        customRulesList.elementHeight = 64f;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var tileSet = (GameGridTileSet)target;
        var isBlob47 = tileSet is Blob47TileSet;
        var isCustomMaskRules = tileSet is CustomMaskRulesTileSet;

        EditorGUILayout.LabelField("Terrain Layer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Tile Layout",
            ObjectNames.NicifyVariableName(tileSet.Mode.ToString()));
        EditorGUILayout.PropertyField(displayNameProperty, new GUIContent("Display Name"));
        EditorGUILayout.PropertyField(tileTypeProperty, new GUIContent("Tile Type"));
        EditorGUILayout.PropertyField(tintProperty, new GUIContent("Tint"));
        EditorGUILayout.PropertyField(materialProperty, new GUIContent("Material"));

        if (!isCustomMaskRules)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Tile Sheet", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(sourceTilesetProperty, new GUIContent("Source Tileset"));
                using (new EditorGUI.DisabledScope(sourceTilesetProperty.objectReferenceValue == null))
                {
                    if (GUILayout.Button("Auto Assign", GUILayout.Width(90f)))
                        AutoAssignTiles(sourceTilesetProperty.objectReferenceValue as Texture2D, isBlob47);
                }
            }
        }

        var spriteCount = isBlob47 ? Blob47TileSet.TileCount : DualGrid16TileSet.TileCount;
        var columns = isBlob47 ? BlobColumns : Columns;
        var spriteProperty = isBlob47 ? blobTilesProperty : tilesProperty;

        EditorGUILayout.Space(6f);
        if (isCustomMaskRules)
        {
            EditorGUILayout.LabelField("Custom Mask Rules", EditorStyles.boldLabel);
            customRulesList.DoLayoutList();
        }
        else
        {
            EditorGUILayout.LabelField(
                isBlob47
                    ? "Blob 47 (source slice order; label = runtime mask)"
                    : "Dual Grid 16 (source slice order; label = runtime mask)",
                EditorStyles.miniLabel);

            if (spriteProperty == null || spriteProperty.arraySize < spriteCount)
            {
                EditorGUILayout.HelpBox($"Tile slots need {spriteCount} entries.", MessageType.Warning);
            }
            else
            {
                var rows = Mathf.CeilToInt(spriteCount / (float)columns);
                var gridRect = GUILayoutUtility.GetRect(
                    1f,
                    rows * SlotSize + (rows - 1) * SlotGap,
                    GUILayout.ExpandWidth(true));
                var slotWidth = Mathf.Min(
                    SlotSize,
                    (gridRect.width - (columns - 1) * SlotGap) / columns);
                for (var spriteIndex = 0; spriteIndex < spriteCount; spriteIndex++)
                {
                    var column = spriteIndex % columns;
                    var row = spriteIndex / columns;
                    var slot = new Rect(
                        gridRect.x + column * (slotWidth + SlotGap),
                        gridRect.y + row * (SlotSize + SlotGap),
                        slotWidth,
                        SlotSize);
                    // Slots hold sprites in source-slice order, so the mask a
                    // slot serves at runtime comes from the slice map.
                    var mask = isBlob47
                        ? Blob47TileSet.GetBlob47Mask(Blob47TileSet.GetTileSlotForSlice(spriteIndex))
                        : DualGrid16TileSet.GetTileSlotForSlice(spriteIndex);
                    DrawTileSlot(
                        slot,
                        spriteProperty.GetArrayElementAtIndex(spriteIndex),
                        mask.ToString(),
                        mask);
                }
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Mask Variants", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Optional. Add one row per mask that needs multiple visual variants. " +
            "Random rows use their own seed. Animation rows render as separate " +
            "SpriteRenderer tiles and swap frames at runtime.",
            MessageType.Info);
        EditorGUILayout.PropertyField(maskVariantsProperty, new GUIContent("Mask Tile Variants"), true);

        if (GUI.changed)
        {
            serializedObject.ApplyModifiedProperties();
            ((GameGridTileSet)target).EnsureSlots();
            EditorUtility.SetDirty(target);
            serializedObject.Update();
        }
    }

    private void AutoAssignTiles(Texture2D sourceTileset, bool isBlob47)
    {
        if (sourceTileset == null) return;
        var tileSet = (GameGridTileSet)target;

        var spriteCount = isBlob47 ? Blob47TileSet.TileCount : DualGrid16TileSet.TileCount;
        var sprites = LoadSpritesFromTexture(sourceTileset);
        if (sprites.Length < spriteCount)
        {
            Debug.LogWarning(
                $"Grid TileSet: expected {spriteCount} sprites on '{sourceTileset.name}', but found {sprites.Length}.",
                sourceTileset);
            return;
        }

        Undo.RecordObject(target, "Auto Assign Grid Tiles");
        var targetProperty = isBlob47 ? blobTilesProperty : tilesProperty;
        var isometricToSourceSlice = (!isBlob47 && tileSet.TileType == GameGridTileType.Isometric)
            ? GetIsometricToSourceSliceOrder(sprites)
            : null;
        targetProperty.arraySize = spriteCount;
        for (var i = 0; i < spriteCount; i++)
            targetProperty.GetArrayElementAtIndex(i).objectReferenceValue = null;

        // Slots follow the source-sheet slice order: rectangle sheets go in
        // left-to-right, top-to-bottom; isometric sheets go in the rotated
        // scan order. Mask routing is applied at runtime by the TileSet.
        for (var slot = 0; slot < spriteCount; slot++)
        {
            var sourceSliceIndex = isometricToSourceSlice != null
                ? isometricToSourceSlice[slot]
                : slot;
            targetProperty.GetArrayElementAtIndex(slot).objectReferenceValue = sprites[sourceSliceIndex];
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        GUI.changed = true;
    }

    private static int[] GetIsometricToSourceSliceOrder(Sprite[] sprites)
    {
        if (sprites == null || sprites.Length == 0) return System.Array.Empty<int>();

        var result = new int[sprites.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = i;

        // Rotating the diamond layout by -45 degrees maps (x, y) to
        // (x + y, y - x) up to a positive scale, so the sprite centers can
        // be ordered directly on those axes without trigonometry.
        System.Array.Sort(result, (a, b) =>
        {
            var aCenter = sprites[a].rect.center;
            var bCenter = sprites[b].rect.center;
            var yComparison = (bCenter.y - bCenter.x).CompareTo(aCenter.y - aCenter.x);
            return yComparison != 0
                ? yComparison
                : (aCenter.x + aCenter.y).CompareTo(bCenter.x + bCenter.y);
        });
        return result;
    }

    private static Sprite[] LoadSpritesFromTexture(Texture2D texture)
    {
        var path = AssetDatabase.GetAssetPath(texture);
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        var sprites = new List<Sprite>();
        foreach (var asset in assets)
            if (asset is Sprite sprite)
                sprites.Add(sprite);

        sprites.Sort(CompareSprites);
        return sprites.ToArray();
    }

    private static int CompareSprites(Sprite a, Sprite b)
    {
        var yComparison = b.rect.y.CompareTo(a.rect.y);
        return yComparison != 0 ? yComparison : a.rect.x.CompareTo(b.rect.x);
    }

    private void DrawCustomRuleElement(Rect position, int index)
    {
        var element = customRulesProperty.GetArrayElementAtIndex(index);
        var maskProperty = element.FindPropertyRelative("mask");
        var spriteProperty = element.FindPropertyRelative("sprite");

        var matrixSize = 46f;
        var matrixRect = new Rect(position.x + 6f, position.y + 6f, matrixSize, matrixSize);
        DrawCustomMaskMatrix(matrixRect, maskProperty);

        var line = EditorGUIUtility.singleLineHeight;
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var previewSize = 32f;
        var inspectorRect = new Rect(
            matrixRect.xMax + 8f,
            position.y + 6f,
            position.width - matrixSize - 20f,
            previewSize);
        var maskRect = new Rect(
            inspectorRect.x,
            inspectorRect.y,
            inspectorRect.width - previewSize - 6f,
            line);
        var spriteRect = new Rect(
            inspectorRect.x,
            maskRect.yMax + spacing,
            inspectorRect.width - previewSize - 6f,
            line);
        var previewRect = new Rect(
            inspectorRect.xMax - previewSize,
            inspectorRect.y,
            previewSize,
            previewSize);

        EditorGUI.LabelField(maskRect, "Mask", maskProperty.intValue.ToString());
        GUI.Label(new Rect(spriteRect.x, spriteRect.y, 42f, line), "Sprite");
        EditorGUI.PropertyField(
            new Rect(spriteRect.x + 42f, spriteRect.y, spriteRect.width - 42f, line),
            spriteProperty,
            GUIContent.none);
        DrawSpritePreview(previewRect, spriteProperty.objectReferenceValue as Sprite);
    }

    private void DrawCustomMaskMatrix(Rect rect, SerializedProperty maskProperty)
    {
        if (((GameGridTileSet)target).TileType == GameGridTileType.Isometric)
        {
            DrawIsometricCustomMaskMatrix(rect, maskProperty);
            return;
        }

        var e = Event.current;
        var cellSize = Mathf.Min(rect.width, rect.height) / 3f;
        var oldColor = GUI.color;

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var bit = GetCustomMaskBit(row, column);
                if (bit == 0)
                {
                    DrawCustomMatrixCell(new Rect(
                        rect.x + column * cellSize,
                        rect.y + row * cellSize,
                        cellSize,
                        cellSize), string.Empty, Color.gray, Color.gray);
                    continue;
                }

                var position = new Vector2Int(column - 1, 1 - row);
                var isSet = (maskProperty.intValue & bit) != 0;
                var cellRect = new Rect(
                    rect.x + column * cellSize,
                    rect.y + row * cellSize,
                    cellSize,
                    cellSize);

                DrawCustomMatrixCell(
                    cellRect,
                    isSet ? GetCustomMaskArrowText(position) : string.Empty,
                    isSet ? Color.green : Color.white,
                    isSet
                        ? Color.green
                        : EditorGUIUtility.isProSkin
                            ? new Color(1f, 1f, 1f, 0.06f)
                            : new Color(0f, 0f, 0f, 0.06f));

                if (e.type == EventType.MouseDown && cellRect.Contains(e.mousePosition))
                {
                    if (e.button == 0)
                        maskProperty.intValue ^= bit;
                    else if (e.button == 1)
                        maskProperty.intValue &= ~bit;

                    GUI.changed = true;
                    e.Use();
                }
            }
        }

        GUI.color = oldColor;
    }

    private static void DrawIsometricCustomMaskMatrix(Rect rect, SerializedProperty maskProperty)
    {
        var e = Event.current;
        var half = Mathf.Min(rect.width, rect.height) / (3f * Mathf.Sqrt(2f));
        var oldHandlesColor = Handles.color;
        var oldGuiColor = GUI.color;
        var labelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 9
        };

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var bit = GetCustomMaskBit(row, column);
                var gridPosition = new Vector2Int(column - 1, 1 - row);

                // Treat the rectangle matrix as a square anchored at its bottom-left
                // corner and rotate it 45 degrees counter-clockwise. This matches the
                // renderer's isometric projection, where +X points up-right and +Y
                // points up-left on screen.
                var rotated = IsometricMatrixOffset(gridPosition);
                var center = new Vector2(
                    rect.center.x + rotated.x * half,
                    rect.center.y + rotated.y * half);
                var isCenter = bit == 0;
                var isSet = !isCenter && (maskProperty.intValue & bit) != 0;

                var fill = isCenter
                    ? Color.gray
                    : isSet
                        ? Color.green
                        : EditorGUIUtility.isProSkin
                            ? new Color(1f, 1f, 1f, 0.06f)
                            : new Color(0f, 0f, 0f, 0.06f);
                DrawIsometricDiamond(center, half, fill);

                if (isCenter) continue;

                GUI.color = isSet ? Color.green : Color.white;
                GUI.Label(
                    new Rect(center.x - half, center.y - half, half * 2f, half * 2f),
                    isSet ? GetIsometricMaskArrowText(gridPosition) : string.Empty,
                    labelStyle);

                if (e.type == EventType.MouseDown &&
                    IsPointInDiamond(e.mousePosition, center, half))
                {
                    if (e.button == 0)
                        maskProperty.intValue ^= bit;
                    else if (e.button == 1)
                        maskProperty.intValue &= ~bit;

                    GUI.changed = true;
                    e.Use();
                }
            }
        }

        Handles.color = oldHandlesColor;
        GUI.color = oldGuiColor;
    }

    private static void DrawIsometricDiamond(Vector2 center, float half, Color color)
    {
        var points = new[]
        {
            new Vector3(center.x, center.y - half, 0f),
            new Vector3(center.x + half, center.y, 0f),
            new Vector3(center.x, center.y + half, 0f),
            new Vector3(center.x - half, center.y, 0f)
        };

        Handles.color = color;
        Handles.DrawAAConvexPolygon(points);
        Handles.color = Color.black;
        Handles.DrawAAPolyLine(
            points[0],
            points[1],
            points[2],
            points[3],
            points[0]);
    }

    private static bool IsPointInDiamond(Vector2 point, Vector2 center, float half)
    {
        var offset = point - center;
        return Mathf.Abs(offset.x) + Mathf.Abs(offset.y) <= half + 0.5f;
    }

    /// <summary>
    /// Rotates a rectangle-matrix cell offset 45 degrees counter-clockwise and
    /// converts it to screen space, where the y axis grows downward.
    /// </summary>
    private static Vector2 IsometricMatrixOffset(Vector2Int gridPosition)
    {
        return new Vector2(
            gridPosition.x - gridPosition.y,
            -(gridPosition.x + gridPosition.y));
    }

    private static string GetIsometricMaskArrowText(Vector2Int gridPosition)
    {
        var rotated = IsometricMatrixOffset(gridPosition);
        if (rotated.x > 0f)
            return rotated.y > 0f ? "↘" : rotated.y < 0f ? "↗" : "→";
        if (rotated.x < 0f)
            return rotated.y > 0f ? "↙" : rotated.y < 0f ? "↖" : "←";
        return rotated.y > 0f ? "↓" : "↑";
    }

    private static void DrawCustomMatrixCell(Rect rect, string text, Color textColor, Color fillColor)
    {
        var oldColor = GUI.color;
        GUI.color = fillColor;
        EditorGUI.DrawRect(rect, fillColor);
        GUI.color = textColor;

        var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 9
        };
        GUI.Label(rect, text, style);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), Color.black);
        EditorGUI.DrawRect(new Rect(rect.xMax, rect.y, 1, rect.height), Color.black);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax, rect.width, 1), Color.black);
        GUI.color = oldColor;
    }

    private static void DrawSpritePreview(Rect rect, Sprite sprite)
    {
        var border = EditorGUIUtility.isProSkin
            ? new Color(0.4f, 0.4f, 0.4f, 1f)
            : new Color(0.55f, 0.55f, 0.55f, 1f);
        var background = EditorGUIUtility.isProSkin
            ? new Color(0.12f, 0.12f, 0.12f, 1f)
            : new Color(0.9f, 0.9f, 0.9f, 1f);

        EditorGUI.DrawRect(rect, border);
        var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        EditorGUI.DrawRect(inner, background);

        if (sprite == null || sprite.texture == null)
        {
            GUI.Label(inner, "None", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var sourceSize = sprite.rect.size;
        var scale = Mathf.Min(
            (inner.width - 4f) / Mathf.Max(1f, sourceSize.x),
            (inner.height - 4f) / Mathf.Max(1f, sourceSize.y));
        var previewSize = sourceSize * scale;
        var previewRect = new Rect(
            inner.center.x - previewSize.x * 0.5f,
            inner.center.y - previewSize.y * 0.5f,
            previewSize.x,
            previewSize.y);
        var texture = sprite.texture;
        var spriteRect = sprite.textureRect;
        var uvRect = new Rect(
            spriteRect.x / texture.width,
            spriteRect.yMin / texture.height,
            spriteRect.width / texture.width,
            spriteRect.height / texture.height);
        GUI.DrawTextureWithTexCoords(previewRect, texture, uvRect, true);
    }

    private static int GetCustomMaskBit(int row, int column)
    {
        var x = column - 1;
        var y = 1 - row;

        if (x == 0 && y == 0) return 0;
        if (x == 0) return y > 0 ? 1 : 16;
        if (y == 0) return x > 0 ? 4 : 64;
        if (x > 0 && y > 0) return 2;
        if (x > 0 && y < 0) return 8;
        if (x < 0 && y < 0) return 32;
        return 128;
    }

    private static string GetCustomMaskArrowText(Vector2Int position)
    {
        if (position.x == 1 && position.y == 0) return "→";
        if (position.x == 1 && position.y == 1) return "↗";
        if (position.x == 0 && position.y == 1) return "↑";
        if (position.x == -1 && position.y == 1) return "↖";
        if (position.x == -1 && position.y == 0) return "←";
        if (position.x == -1 && position.y == -1) return "↙";
        if (position.x == 0 && position.y == -1) return "↓";
        return "↘";
    }

    private void DrawTileSlot(Rect slot, SerializedProperty tileProperty, string label, int mask)
    {
        var current = tileProperty.objectReferenceValue as Sprite;
        HandleTileSlotDrag(slot, tileProperty);

        var controlId = EditorGUIUtility.GetControlID(FocusType.Passive);
        HandleObjectPickerCommand(controlId, tileProperty);

        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && slot.Contains(e.mousePosition))
        {
            EditorGUIUtility.ShowObjectPicker<Sprite>(current, false, string.Empty, controlId);
            e.Use();
            return;
        }

        if (e.type == EventType.ContextClick && slot.Contains(e.mousePosition))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("添加MaskTileVariant"), false, () => AddMaskVariant(mask));
            menu.ShowAsContext();
            e.Use();
            return;
        }

        DrawTileSlotPreview(slot, current, label);
    }

    private void AddMaskVariant(int mask)
    {
        if (!((GameGridTileSet)target).AddMaskVariant(mask)) return;
        serializedObject.Update();
        EditorUtility.SetDirty(target);
        GUI.changed = true;
    }

    private static void HandleObjectPickerCommand(int controlId, SerializedProperty tileProperty)
    {
        var e = Event.current;
        if (e.type != EventType.ExecuteCommand || e.commandName != "ObjectSelectorUpdated") return;
        if (EditorGUIUtility.GetObjectPickerControlID() != controlId) return;

        tileProperty.objectReferenceValue = EditorGUIUtility.GetObjectPickerObject() as Sprite;
        GUI.changed = true;
        e.Use();
    }

    private static void DrawTileSlotPreview(Rect slot, Sprite sprite, string label)
    {
        var background = EditorGUIUtility.isProSkin
            ? new Color(0.13f, 0.13f, 0.13f, 1f)
            : new Color(0.88f, 0.88f, 0.88f, 1f);
        var border = EditorGUIUtility.isProSkin
            ? new Color(0.42f, 0.42f, 0.42f, 1f)
            : new Color(0.55f, 0.55f, 0.55f, 1f);

        EditorGUI.DrawRect(slot, border);
        EditorGUI.DrawRect(new Rect(slot.x + 1f, slot.y + 1f, slot.width - 2f, slot.height - 2f), background);

        if (sprite != null && sprite.texture != null)
        {
            var previewArea = new Rect(
                slot.x + 3f,
                slot.y + 3f,
                slot.width - 6f,
                slot.height - 18f);
            var sourceSize = sprite.rect.size;
            var scale = Mathf.Min(
                previewArea.width / Mathf.Max(1f, sourceSize.x),
                previewArea.height / Mathf.Max(1f, sourceSize.y));
            var previewSize = sourceSize * scale;
            var previewRect = new Rect(
                previewArea.center.x - previewSize.x * 0.5f,
                previewArea.center.y - previewSize.y * 0.5f,
                previewSize.x,
                previewSize.y);

            var texture = sprite.texture;
            var spriteRect = sprite.textureRect;
            var uvRect = new Rect(
                spriteRect.x / texture.width,
                spriteRect.yMin / texture.height,
                spriteRect.width / texture.width,
                spriteRect.height / texture.height);
            GUI.DrawTextureWithTexCoords(previewRect, texture, uvRect, true);
        }
        else
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(new Rect(slot.x, slot.y + 5f, slot.width, slot.height - 25f), "None", style);
        }

        var labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        GUI.Label(new Rect(slot.x + 2f, slot.yMax - 15f, slot.width - 4f, 13f), label, labelStyle);
    }

    private static void HandleTileSlotDrag(Rect slot, SerializedProperty tileProperty)
    {
        var e = Event.current;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
        if (!slot.Contains(e.mousePosition)) return;

        var sprite = GetDraggedSprite();
        if (sprite == null) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            tileProperty.objectReferenceValue = sprite;
            GUI.changed = true;
        }

        e.Use();
    }

    private static Sprite GetDraggedSprite()
    {
        foreach (var reference in DragAndDrop.objectReferences)
        {
            if (reference is Sprite sprite) return sprite;

            if (reference is Texture2D texture)
            {
                var path = AssetDatabase.GetAssetPath(texture);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Sprite candidate)
                        return candidate;
            }
        }

        return null;
    }
}

[CustomPropertyDrawer(typeof(GameGridTileSet.MaskTileVariant))]
public sealed class MaskTileVariantDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return height;

        var spacing = EditorGUIUtility.standardVerticalSpacing;
        height += spacing + GetFieldHeight(property, "mask");
        height += spacing + GetFieldHeight(property, "mode");

        if (GetMode(property) == MaskTileVariantMode.Animation)
        {
            height += spacing + GetFieldHeight(property, "framesPerSecond");
            height += spacing + GetFieldHeight(property, "loop");
            height += spacing + GetFieldHeight(property, "phaseOffset");
        }
        else
        {
            height += spacing + GetFieldHeight(property, "randomSeed");
        }

        height += spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("sprites"), true);
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var rect = new Rect(
            position.x,
            position.y,
            position.width,
            EditorGUIUtility.singleLineHeight);

        EditorGUI.BeginProperty(position, label, property);
        property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            // EditorGUI.indentLevel++;
            DrawField(ref rect, spacing, property, "mask", "Mask");
            DrawField(ref rect, spacing, property, "mode", "Mode");

            var mode = GetMode(property);
            if (mode == MaskTileVariantMode.Animation)
            {
                DrawField(ref rect, spacing, property, "framesPerSecond", "Frames Per Second");
                DrawField(ref rect, spacing, property, "loop", "Loop");
                DrawField(ref rect, spacing, property, "phaseOffset", "Phase Offset");
            }
            else
            {
                DrawField(ref rect, spacing, property, "randomSeed", "Random Seed");
            }

            var sprites = property.FindPropertyRelative("sprites");
            var spritesHeight = EditorGUI.GetPropertyHeight(sprites, true);
            rect.y += rect.height + spacing;
            rect = EditorGUI.IndentedRect(new Rect(rect.x, rect.y, rect.width, spritesHeight));
            HandleFramesMultiDrag(rect, sprites);
            EditorGUI.PropertyField(
                rect,
                sprites,
                new GUIContent(mode == MaskTileVariantMode.Animation ? "Frames" : "Sprites"),
                true);
            // EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    // Multi-sprite drops fill the whole frames list in sheet order; single
    // sprite drops keep the default ReorderableList behavior.
    private static void HandleFramesMultiDrag(Rect rect, SerializedProperty spritesProperty)
    {
        var e = Event.current;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
        if (!rect.Contains(e.mousePosition)) return;

        var sprites = GetDraggedSprites();
        if (sprites == null || sprites.Count <= 1) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            Undo.RecordObject(spritesProperty.serializedObject.targetObject, "Assign Tile Frames");
            spritesProperty.arraySize = sprites.Count;
            for (var i = 0; i < sprites.Count; i++)
                spritesProperty.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            GUI.changed = true;
        }

        e.Use();
    }

    private static List<Sprite> GetDraggedSprites()
    {
        List<Sprite> sprites = null;
        foreach (var reference in DragAndDrop.objectReferences)
        {
            if (reference is Sprite sprite)
            {
                sprites ??= new List<Sprite>();
                if (!sprites.Contains(sprite)) sprites.Add(sprite);
            }
            else if (reference is Texture2D texture)
            {
                var path = AssetDatabase.GetAssetPath(texture);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Sprite sheetSprite)
                    {
                        sprites ??= new List<Sprite>();
                        if (!sprites.Contains(sheetSprite)) sprites.Add(sheetSprite);
                    }
                }
            }
        }

        if (sprites == null) return null;

        // Match the auto-assign convention: one texture after another, each
        // scanned top-to-bottom, left-to-right.
        sprites.Sort((a, b) =>
        {
            var aTextureId = a.texture != null ? a.texture.GetInstanceID() : 0;
            var bTextureId = b.texture != null ? b.texture.GetInstanceID() : 0;
            var textureComparison = aTextureId.CompareTo(bTextureId);
            if (textureComparison != 0) return textureComparison;

            var yComparison = b.rect.y.CompareTo(a.rect.y);
            return yComparison != 0 ? yComparison : a.rect.x.CompareTo(b.rect.x);
        });
        return sprites;
    }

    private static MaskTileVariantMode GetMode(SerializedProperty property)
    {
        return (MaskTileVariantMode)property.FindPropertyRelative("mode").intValue;
    }

    private static float GetFieldHeight(SerializedProperty property, string name)
    {
        return EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name), true);
    }

    private static void DrawField(
        ref Rect rect,
        float spacing,
        SerializedProperty property,
        string name,
        string label)
    {
        var child = property.FindPropertyRelative(name);
        var height = EditorGUI.GetPropertyHeight(child, true);
        rect.y += rect.height + spacing;
        rect = EditorGUI.IndentedRect(new Rect(rect.x, rect.y, rect.width, height));
        EditorGUI.PropertyField(rect, child, new GUIContent(label), true);
        rect = new Rect(rect.x, rect.y, rect.width, height);
    }
}
