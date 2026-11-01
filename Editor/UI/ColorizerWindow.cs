using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Viol3.Colorizer.UI
{
    public class ColorizerWindow : EditorWindow
    {
        const float SwatchSize = 28f;
        const float SwatchGap = 3f;
        const float PaletteSwatchSize = 20f;
        const float MaxAtlasSize = 256f;
        const int MaxSwatches = 1024;
        const int DetailedTextureRegions = 4096;

        static readonly Color SelectionColor = new Color(0.24f, 0.5f, 0.95f);

        [SerializeField] GameObject target;
        [SerializeField] int groupIndex;
        [SerializeField] int tolerance = 4;
        [SerializeField] bool usedOnly = true;
        [SerializeField] List<Color32> colors = new List<Color32>();
        [SerializeField] List<int> selection = new List<int>();
        [SerializeField] Vector2 scroll;
        [SerializeField] int paletteIndex;
        [SerializeField] ColorPalette customPalette;
        [SerializeField] float minSaturation = 0.25f;
        [SerializeField] float maxSaturation = 0.8f;
        [SerializeField] float minValue = 0.3f;
        [SerializeField] float maxValue = 0.95f;
        [SerializeField] bool showPalettes = true;
        [SerializeField] bool showRandom = true;

        readonly MaterialPreview materialPreview = new MaterialPreview();
        readonly List<int> visibleRegions = new List<int>();

        List<TextureGroup> groups = new List<TextureGroup>();
        AtlasData atlas;
        Texture2D previewTexture;
        Color32[] previewPixels;
        bool usageComplete;
        bool previewDirty;
        bool picking;
        string overwriteProblem;

        TextureGroup CurrentGroup => atlas != null ? groups[groupIndex] : null;

        bool HasEdits
        {
            get
            {
                if (atlas == null)
                    return false;
                for (int i = 0; i < colors.Count; i++)
                {
                    if (!AtlasAnalyzer.SameColor(colors[i], atlas.Regions[i].BaseColor))
                        return true;
                }
                return false;
            }
        }

        [MenuItem("Tools/Viol3's Colorizer")]
        public static void Open()
        {
            GetWindow<ColorizerWindow>().Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Colorizer", EditorGUIUtility.FindTexture("Grid.FillTool"));
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += RestoreAfterReload;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.delayCall -= RestoreAfterReload;
            ReleasePreview();
        }

        void RestoreAfterReload()
        {
            if (this == null)
                return;
            if (target == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                target = Selection.activeGameObject;
            LoadTarget(true);
            Repaint();
        }

        void OnUndoRedo()
        {
            previewDirty = true;
            Repaint();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                picking = false;
                ReleasePreview();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                LoadTarget(true);
            }
        }

        void OnSelectionChange()
        {
            GameObject selected = Selection.activeGameObject;
            if (picking || HasEdits || selected == null || selected == target || EditorUtility.IsPersistent(selected))
                return;

            target = selected;
            groupIndex = 0;
            LoadTarget(false);
            Repaint();
        }

        void ReleasePreview()
        {
            materialPreview.Clear();
            if (previewTexture != null)
                DestroyImmediate(previewTexture);
            previewTexture = null;
            previewPixels = null;
            atlas = null;
            SceneView.RepaintAll();
        }

        void LoadTarget(bool keepEdits)
        {
            ReleasePreview();
            visibleRegions.Clear();
            groups = target != null && !EditorUtility.IsPersistent(target)
                ? TextureGroup.Collect(target)
                : new List<TextureGroup>();

            if (groups.Count == 0)
            {
                picking = false;
                return;
            }

            groupIndex = Mathf.Clamp(groupIndex, 0, groups.Count - 1);
            TextureGroup group = groups[groupIndex];

            Color32[] pixels = AtlasReader.Read(group.Texture, out int width, out int height);
            atlas = AtlasAnalyzer.Build(pixels, width, height, tolerance);
            usageComplete = MeshUvSampler.MarkUsage(atlas, group.Slots);

            if (!keepEdits || colors.Count != atlas.Regions.Count)
            {
                colors = atlas.Regions.Select(r => r.BaseColor).ToList();
                selection.Clear();
                Undo.ClearUndo(this);
            }
            selection.RemoveAll(r => r >= atlas.Regions.Count);

            RefreshVisibleRegions();
            overwriteProblem = AtlasExporter.GetOverwriteProblem(group.Texture, atlas);

            previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, group.Texture.mipmapCount > 1, !AtlasReader.IsSrgb(group.Texture))
            {
                name = "Colorizer Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = group.Texture.filterMode,
                wrapMode = group.Texture.wrapMode,
            };
            previewPixels = new Color32[pixels.Length];
            previewDirty = true;
        }

        void RefreshVisibleRegions()
        {
            visibleRegions.Clear();
            if (atlas == null)
                return;

            bool anyUsed = usedOnly && atlas.Regions.Any(r => r.UsedPixels > 0);
            for (int i = 0; i < atlas.Regions.Count; i++)
            {
                if (!anyUsed || atlas.Regions[i].UsedPixels > 0)
                    visibleRegions.Add(i);
            }
        }

        void UpdatePreview()
        {
            if (!previewDirty || atlas == null)
                return;

            previewDirty = false;
            AtlasAnalyzer.Recolor(atlas, colors, previewPixels);
            previewTexture.SetPixels32(previewPixels);
            previewTexture.Apply(previewTexture.mipmapCount > 1);
            materialPreview.Apply(CurrentGroup.Slots, previewTexture);
            SceneView.RepaintAll();
        }

        void OnGUI()
        {
            if (atlas != null && target == null)
                LoadTarget(false);
            UpdatePreview();

            DrawTarget();
            if (atlas == null)
                return;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawAtlas();
            DrawRegions();
            DrawSelected();
            DrawPalettes();
            DrawRandom();
            EditorGUILayout.EndScrollView();
            DrawOutput();
        }

        void DrawTarget()
        {
            EditorGUI.BeginChangeCheck();
            var newTarget = (GameObject)EditorGUILayout.ObjectField("Target", target, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && ConfirmDiscard())
            {
                target = newTarget;
                groupIndex = 0;
                LoadTarget(false);
                GUIUtility.ExitGUI();
            }

            if (target == null)
            {
                EditorGUILayout.HelpBox("Select a model in the scene to start.", MessageType.Info);
                return;
            }
            if (EditorUtility.IsPersistent(target))
            {
                EditorGUILayout.HelpBox("Drag this asset into a scene or open it in Prefab Mode, then select it there.", MessageType.Info);
                return;
            }
            if (groups.Count == 0)
            {
                EditorGUILayout.HelpBox("No material with a texture was found on this object or its children.", MessageType.Warning);
                return;
            }

            if (groups.Count > 1)
            {
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup("Texture", groupIndex, groups.Select(g => g.Texture.name).ToArray());
                if (EditorGUI.EndChangeCheck() && ConfirmDiscard())
                {
                    groupIndex = newIndex;
                    LoadTarget(false);
                    GUIUtility.ExitGUI();
                }
            }
        }

        void DrawAtlas()
        {
            float size = Mathf.Min(MaxAtlasSize, EditorGUIUtility.currentViewWidth - 24f);
            float height = size * atlas.Height / atlas.Width;
            Rect line = GUILayoutUtility.GetRect(size, height);
            var rect = new Rect(line.x + (line.width - size) * 0.5f, line.y, size, height);

            GUI.DrawTexture(rect, previewTexture, ScaleMode.StretchToFill, false);
            foreach (int region in selection)
                DrawRegionOutline(rect, atlas.Regions[region]);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                int x = Mathf.Clamp((int)((e.mousePosition.x - rect.x) / rect.width * atlas.Width), 0, atlas.Width - 1);
                int y = Mathf.Clamp((int)((rect.yMax - e.mousePosition.y) / rect.height * atlas.Height), 0, atlas.Height - 1);
                Select(atlas.RegionMap[y * atlas.Width + x], EditorGUI.actionKey);
                e.Use();
            }

            EditorGUILayout.Space(4f);
        }

        void DrawRegionOutline(Rect atlasRect, AtlasRegion region)
        {
            float scaleX = atlasRect.width / atlas.Width;
            float scaleY = atlasRect.height / atlas.Height;
            var outline = new Rect(
                atlasRect.x + region.MinX * scaleX,
                atlasRect.yMax - (region.MaxY + 1) * scaleY,
                (region.MaxX - region.MinX + 1) * scaleX,
                (region.MaxY - region.MinY + 1) * scaleY);
            DrawFrame(outline, Color.white);
        }

        void DrawRegions()
        {
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int newTolerance = EditorGUILayout.IntSlider(new GUIContent("Tolerance", "How different two pixels can be and still count as one region. Raise it for JPG or compressed textures."), tolerance, 0, 64);
            if (EditorGUI.EndChangeCheck() && ConfirmDiscard())
            {
                tolerance = newTolerance;
                LoadTarget(false);
                GUIUtility.ExitGUI();
            }

            EditorGUI.BeginChangeCheck();
            usedOnly = EditorGUILayout.Toggle(new GUIContent("Used By Model Only", "Hide regions that the model's UVs never touch."), usedOnly);
            if (EditorGUI.EndChangeCheck())
                RefreshVisibleRegions();

            if (!usageComplete)
                EditorGUILayout.HelpBox("Read/Write is off on a mesh of this model. All regions may be listed and Pick From Model won't work on it. Enable Read/Write in the model's import settings.", MessageType.Warning);
            if (atlas.Regions.Count > DetailedTextureRegions)
                EditorGUILayout.HelpBox("This texture has many small regions and may not be a color atlas. Viol3's Colorizer works best on flat color cells.", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                picking = GUILayout.Toggle(picking, new GUIContent("Pick From Model", "Click the model in the Scene view to select the region under the cursor. Ctrl/Cmd adds to the selection, Esc stops."), "Button");
                using (new EditorGUI.DisabledScope(selection.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Select Same Color", "Add every visible region with the selected region's original color.")))
                        SelectSameColor();
                }
                if (GUILayout.Button("Select None"))
                    selection.Clear();
            }

            int count = Mathf.Min(visibleRegions.Count, MaxSwatches);
            if (visibleRegions.Count > MaxSwatches)
                EditorGUILayout.HelpBox($"Showing the first {MaxSwatches} of {visibleRegions.Count} regions.", MessageType.Info);

            float width = EditorGUIUtility.currentViewWidth - 24f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width + SwatchGap) / (SwatchSize + SwatchGap)));
            int rows = Mathf.CeilToInt((float)count / columns);
            Rect grid = GUILayoutUtility.GetRect(width, rows * (SwatchSize + SwatchGap));

            for (int i = 0; i < count; i++)
            {
                int region = visibleRegions[i];
                var rect = new Rect(
                    grid.x + i % columns * (SwatchSize + SwatchGap),
                    grid.y + i / columns * (SwatchSize + SwatchGap),
                    SwatchSize,
                    SwatchSize);
                DrawRegionSwatch(rect, region);
            }
            EditorGUILayout.Space(4f);
        }

        void DrawRegionSwatch(Rect rect, int region)
        {
            AtlasRegion data = atlas.Regions[region];
            Color32 opaque = colors[region];
            opaque.a = 255;
            EditorGUI.DrawRect(rect, opaque);

            // A corner of the original color marks regions that were changed.
            if (!AtlasAnalyzer.SameColor(colors[region], data.BaseColor))
            {
                Color32 original = data.BaseColor;
                original.a = 255;
                EditorGUI.DrawRect(new Rect(rect.xMax - 10f, rect.yMax - 10f, 10f, 10f), original);
            }

            if (selection.Contains(region))
                DrawFrame(rect, SelectionColor);

            GUI.Label(rect, new GUIContent(string.Empty, $"{data.PixelCount} pixels\nRead by the model: {data.UsedPixels}"));

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                Select(region, EditorGUI.actionKey);
                e.Use();
            }
        }

        void DrawSelected()
        {
            if (selection.Count == 0)
            {
                EditorGUILayout.HelpBox("Click a swatch, the atlas or the model to select a region. Without a selection, palettes and random actions change all listed regions.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(selection.Count == 1 ? "Selected Region" : $"{selection.Count} Selected Regions", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            Color color = EditorGUILayout.ColorField(new GUIContent("Color"), colors[selection[0]], true, false, false);
            if (EditorGUI.EndChangeCheck())
            {
                RecordEdit("Colorizer Change Color");
                foreach (int region in selection)
                    ColorRandomizer.Set(atlas, colors, region, color);
                previewDirty = true;
            }

            if (GUILayout.Button("Reset Selected"))
            {
                RecordEdit("Colorizer Reset Selected");
                foreach (int region in selection)
                    colors[region] = atlas.Regions[region].BaseColor;
                previewDirty = true;
            }
            EditorGUILayout.Space(4f);
        }

        void DrawPalettes()
        {
            showPalettes = EditorGUILayout.Foldout(showPalettes, "Palettes", true, EditorStyles.foldoutHeader);
            if (!showPalettes)
                return;

            paletteIndex = EditorGUILayout.Popup("Built-in", paletteIndex, BuiltInPalettes.Names);
            customPalette = (ColorPalette)EditorGUILayout.ObjectField(new GUIContent("Custom", "Overrides the built-in palette. Create one from Assets > Create > Viol3's Colorizer > Color Palette."), customPalette, typeof(ColorPalette), false);

            List<Color> palette = customPalette != null ? customPalette.colors : BuiltInPalettes.Get(paletteIndex);
            DrawPaletteSwatches(palette);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(palette.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Map By Brightness", "Dark regions get dark palette colors, bright regions get bright ones.")))
                    Edit("Colorizer Map Palette", r => ColorRandomizer.MapByBrightness(atlas, colors, r, palette));
                if (GUILayout.Button(new GUIContent("Shuffle", "Give each region a random palette color.")))
                    Edit("Colorizer Shuffle Palette", r => ColorRandomizer.ShufflePalette(atlas, colors, r, palette));
            }
            EditorGUILayout.Space(4f);
        }

        void DrawPaletteSwatches(List<Color> palette)
        {
            float width = EditorGUIUtility.currentViewWidth - 24f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width + SwatchGap) / (PaletteSwatchSize + SwatchGap)));
            int rows = Mathf.CeilToInt((float)palette.Count / columns);
            Rect grid = GUILayoutUtility.GetRect(width, rows * (PaletteSwatchSize + SwatchGap));
            Event e = Event.current;

            for (int i = 0; i < palette.Count; i++)
            {
                var rect = new Rect(
                    grid.x + i % columns * (PaletteSwatchSize + SwatchGap),
                    grid.y + i / columns * (PaletteSwatchSize + SwatchGap),
                    PaletteSwatchSize,
                    PaletteSwatchSize);
                Color opaque = palette[i];
                opaque.a = 1f;
                EditorGUI.DrawRect(rect, opaque);
                GUI.Label(rect, new GUIContent(string.Empty, "Click to apply to the selected regions"));

                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    if (selection.Count > 0)
                    {
                        RecordEdit("Colorizer Apply Palette Color");
                        foreach (int region in selection)
                            ColorRandomizer.Set(atlas, colors, region, palette[i]);
                        previewDirty = true;
                    }
                    e.Use();
                }
            }
        }

        void DrawRandom()
        {
            showRandom = EditorGUILayout.Foldout(showRandom, "Random Variants", true, EditorStyles.foldoutHeader);
            if (!showRandom)
                return;

            DrawRange("Saturation", ref minSaturation, ref maxSaturation);
            DrawRange("Brightness", ref minValue, ref maxValue);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Shift Hue", "Rotate all hues by one random amount, keeping how the colors relate.")))
                    Edit("Colorizer Shift Hue", r => ColorRandomizer.ShiftHue(atlas, colors, r));
                if (GUILayout.Button(new GUIContent("Random Colors", "Give each region a random color inside the ranges above.")))
                    Edit("Colorizer Random Colors", r => ColorRandomizer.Randomize(atlas, colors, r, minSaturation, maxSaturation, minValue, maxValue));
            }
            EditorGUILayout.Space(4f);
        }

        static void DrawRange(string label, ref float min, ref float max)
        {
            EditorGUILayout.MinMaxSlider(new GUIContent($"{label} ({min:0.00} - {max:0.00})"), ref min, ref max, 0f, 1f);
        }

        void DrawOutput()
        {
            EditorGUILayout.Space(2f);
            using (new EditorGUI.DisabledScope(!HasEdits))
            {
                if (GUILayout.Button("Reset All"))
                {
                    RecordEdit("Colorizer Reset All");
                    colors = atlas.Regions.Select(r => r.BaseColor).ToList();
                    previewDirty = true;
                }

                if (GUILayout.Button(new GUIContent("Save New Variant", "Create a new texture and material, assign them to this object and keep the originals."), GUILayout.Height(26f)))
                    SaveVariant();

                using (new EditorGUI.DisabledScope(overwriteProblem != null))
                {
                    if (GUILayout.Button(new GUIContent("Overwrite Texture", overwriteProblem ?? "Replace the source texture file. This affects everything that uses it.")))
                        OverwriteTexture();
                }
            }

            if (HasEdits && overwriteProblem != null)
                EditorGUILayout.HelpBox(overwriteProblem, MessageType.None);
        }

        void SaveVariant()
        {
            TextureGroup group = CurrentGroup;
            string sourcePath = AssetDatabase.GetAssetPath(group.Texture);
            string folder = sourcePath.StartsWith("Assets/") ? Path.GetDirectoryName(sourcePath).Replace('\\', '/') : "Assets";
            string path = EditorUtility.SaveFilePanelInProject("Save New Variant", group.Texture.name + "_Colorized", "png", "Choose where to save the new texture. Its material is saved next to it.", folder);
            if (string.IsNullOrEmpty(path))
                return;
            if (path == sourcePath)
            {
                EditorUtility.DisplayDialog("Viol3's Colorizer", "Choose a different file. To replace the source texture, use Overwrite Texture.", "OK");
                return;
            }

            UpdatePreview();
            AtlasExporter.SaveVariant(group, previewTexture, path);
            LoadTarget(false);
            GUIUtility.ExitGUI();
        }

        void OverwriteTexture()
        {
            TextureGroup group = CurrentGroup;
            if (!EditorUtility.DisplayDialog("Overwrite Texture", $"Replace '{group.Texture.name}'? Everything that uses this texture changes and this can't be undone in Unity.", "Overwrite", "Cancel"))
                return;

            UpdatePreview();
            AtlasExporter.Overwrite(group.Texture, previewTexture);
            LoadTarget(false);
            GUIUtility.ExitGUI();
        }

        void OnSceneGui(SceneView view)
        {
            if (!picking || atlas == null)
                return;

            Event e = Event.current;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                picking = false;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                int region = MeshUvSampler.PickRegion(atlas, CurrentGroup.Slots, HandleUtility.GUIPointToWorldRay(e.mousePosition));
                if (region >= 0)
                    Select(region, EditorGUI.actionKey);
                e.Use();
                Repaint();
            }
        }

        void Select(int region, bool additive)
        {
            GUI.FocusControl(null);
            if (!additive)
            {
                selection.Clear();
                selection.Add(region);
            }
            else if (!selection.Remove(region))
            {
                selection.Add(region);
            }
            Repaint();
        }

        void SelectSameColor()
        {
            Color32 original = atlas.Regions[selection[0]].BaseColor;
            foreach (int region in visibleRegions)
            {
                if (!selection.Contains(region) && AtlasAnalyzer.SameColor(atlas.Regions[region].BaseColor, original))
                    selection.Add(region);
            }
        }

        void Edit(string undoName, System.Action<IList<int>> action)
        {
            RecordEdit(undoName);
            action(selection.Count > 0 ? selection : visibleRegions);
            previewDirty = true;
        }

        void RecordEdit(string undoName)
        {
            Undo.RecordObject(this, undoName);
        }

        bool ConfirmDiscard()
        {
            return !HasEdits || EditorUtility.DisplayDialog("Viol3's Colorizer", "Discard the unsaved color changes?", "Discard", "Cancel");
        }

        static void DrawFrame(Rect rect, Color color)
        {
            const float thickness = 2f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
