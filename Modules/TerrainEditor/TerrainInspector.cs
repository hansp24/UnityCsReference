// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


/*
GUILayout.TextureGrid number of horiz elements doesnt work
*/

using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.AnimatedValues;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;


namespace UnityEditor
{
    // must match Terrain.cpp TerrainTools
    internal enum TerrainTool
    {
        None = -1,
        Paint = 0,
        PlaceTree,
        PaintDetail,
        TerrainSettings,
        TerrainToolCount
    }

    internal class BrushRep
    {
        int m_Size;
        float[] m_Strength;

        Brush m_OldBrush = null;

        internal const int kMinBrushSize = 3;

        public float GetStrengthInt(int ix, int iy)
        {
            ix = Mathf.Clamp(ix, 0, m_Size - 1);
            iy = Mathf.Clamp(iy, 0, m_Size - 1);

            float s = m_Strength[iy * m_Size + ix];

            return s;
        }

        public void CreateFromBrush(Brush b, int size)
        {
            if (size == m_Size && m_OldBrush == b && m_Strength != null)
                return;

            Texture2D mask = b.texture;
            if (mask != null)
            {
                Texture2D readableTexture = new Texture2D(mask.width, mask.height, mask.format, mask.mipmapCount > 1);
                Graphics.CopyTexture(mask, readableTexture);
                readableTexture.Apply();

                float fSize = size;
                m_Size = size;
                m_Strength = new float[m_Size * m_Size];
                if (m_Size > kMinBrushSize)
                {
                    for (int y = 0; y < m_Size; y++)
                    {
                        for (int x = 0; x < m_Size; x++)
                        {
                            m_Strength[y * m_Size + x] = readableTexture.GetPixelBilinear((x + 0.5F) / fSize, (y) / fSize).a;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < m_Strength.Length; i++)
                        m_Strength[i] = 1.0F;
                }

                Object.DestroyImmediate(readableTexture);
            }
            else
            {
                m_Strength = new float[1];
                m_Strength[0] = 1.0F;
                m_Size = 1;
            }

            m_OldBrush = b;
        }
    }

    internal class DetailPainter
    {
        public int size;
        public float opacity;
        public float targetStrength;
        public Brush brush;
        static public BrushRep brushRep = null;
        public TerrainData terrainData;
        public TerrainTool tool;
        public bool randomizeDetails;
        public bool clearSelectedOnly;

        public void Paint(float xCenterNormalized, float yCenterNormalized, int detailIndex)
        {
            if (detailIndex >= terrainData.detailPrototypes.Length)
                return;

            if (brushRep == null)
                brushRep = new BrushRep();

            brushRep.CreateFromBrush(brush, size);

            int xCenter = Mathf.FloorToInt(xCenterNormalized * terrainData.detailWidth);
            int yCenter = Mathf.FloorToInt(yCenterNormalized * terrainData.detailHeight);

            int intRadius = Mathf.RoundToInt(size) / 2;
            int intFraction = Mathf.RoundToInt(size) % 2;

            int xmin = Mathf.Clamp(xCenter - intRadius, 0, terrainData.detailWidth - 1);
            int ymin = Mathf.Clamp(yCenter - intRadius, 0, terrainData.detailHeight - 1);

            int xmax = Mathf.Clamp(xCenter + intRadius + intFraction, 0, terrainData.detailWidth);
            int ymax = Mathf.Clamp(yCenter + intRadius + intFraction, 0, terrainData.detailHeight);

            int width = xmax - xmin;
            int height = ymax - ymin;

            int[] layers = { detailIndex };
            if (targetStrength < 0.0F && !clearSelectedOnly)
                layers = terrainData.GetSupportedLayers(xmin, ymin, width, height);


            for (int i = 0; i < layers.Length; i++)
            {
                int[,] alphamap = terrainData.GetDetailLayer(xmin, ymin, width, height, layers[i]);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int xBrushOffset = (xmin + x) - (xCenter - intRadius + intFraction);
                        int yBrushOffset = (ymin + y) - (yCenter - intRadius + intFraction);
                        float opa = opacity * brushRep.GetStrengthInt(xBrushOffset, yBrushOffset);

                        float t = targetStrength;
                        float targetValue = Mathf.Lerp(alphamap[y, x], t, opa);
                        alphamap[y, x] = Mathf.RoundToInt(targetValue - .5f + Random.value);
                    }
                }

                terrainData.SetDetailLayer(xmin, ymin, layers[i], alphamap);
            }
        }
    }

    internal class TreePainter
    {
        public static float brushSize = 40;
        public static float spacing = .8f;

        public static bool lockWidthToHeight = true;
        public static bool randomRotation = true;

        public static bool allowHeightVar = true;
        public static bool allowWidthVar = true;

        public static float treeColorAdjustment = .4f;
        public static float treeHeight = 1;
        public static float treeHeightVariation = .1f;
        public static float treeWidth = 1;
        public static float treeWidthVariation = .1f;

        public static int selectedTree = -1;

        static Color GetTreeColor()
        {
            Color c = Color.white * Random.Range(1.0F, 1.0F - treeColorAdjustment);
            c.a = 1;
            return c;
        }

        static float GetTreeHeight()
        {
            float v = allowHeightVar ? treeHeightVariation : 0.0f;
            return treeHeight * Random.Range(1.0F - v, 1.0F + v);
        }

        static float GetTreeWidth()
        {
            float v = allowWidthVar ? treeWidthVariation : 0.0f;
            return treeWidth * Random.Range(1.0F - v, 1.0F + v);
        }

        static float GetTreeRotation()
        {
            return randomRotation ? Random.Range(0, 2 * Mathf.PI) : 0;
        }

        public static void PlaceTrees(Terrain terrain, float xBase, float yBase)
        {
            int prototypeCount = TerrainInspectorUtil.GetPrototypeCount(terrain.terrainData);
            if (selectedTree == -1 || selectedTree >= prototypeCount)
                return;

            if (!TerrainInspectorUtil.PrototypeIsRenderable(terrain.terrainData, selectedTree))
                return;

            int placedTreeCount = 0;

            // Plant a single tree first. At the location of the mouse
            TreeInstance instance = new TreeInstance();
            instance.position = new Vector3(xBase, 0, yBase);
            instance.color = GetTreeColor();
            instance.lightmapColor = Color.white;
            instance.prototypeIndex = selectedTree;
            instance.heightScale = GetTreeHeight();
            instance.widthScale = lockWidthToHeight ? instance.heightScale : GetTreeWidth();
            instance.rotation = GetTreeRotation();

            // When painting single tree
            // And just clicking we always place it, so you can do overlapping trees

            bool checkTreeDistance = Event.current.type == EventType.MouseDrag || brushSize > 1;
            if (!checkTreeDistance || TerrainInspectorUtil.CheckTreeDistance(terrain.terrainData, instance.position, instance.prototypeIndex, spacing))
            {
                terrain.AddTreeInstance(instance);
                placedTreeCount++;
            }

            Vector3 size = TerrainInspectorUtil.GetPrototypeExtent(terrain.terrainData, selectedTree);
            size.y = 0;
            float treeCountOneAxis = brushSize / (size.magnitude * spacing * .5f);
            int treeCount = (int)((treeCountOneAxis * treeCountOneAxis) * .5f);
            treeCount = Mathf.Clamp(treeCount, 0, 100);
            // Plant a bunch of trees
            for (int i = 1; i < treeCount && placedTreeCount < treeCount; i++)
            {
                Vector2 randomOffset = 0.5f * Random.insideUnitCircle;
                randomOffset.x *= brushSize / terrain.terrainData.size.x;
                randomOffset.y *= brushSize / terrain.terrainData.size.z;
                Vector3 position = new Vector3(xBase + randomOffset.x, 0, yBase + randomOffset.y);
                if (position.x >= 0 && position.x <= 1 && position.z >= 0 && position.z <= 1
                    && TerrainInspectorUtil.CheckTreeDistance(terrain.terrainData, position, selectedTree, spacing * .5f))
                {
                    instance = new TreeInstance();

                    instance.position = position;

                    instance.color = GetTreeColor();
                    instance.lightmapColor = Color.white;
                    instance.prototypeIndex = selectedTree;
                    instance.heightScale = GetTreeHeight();
                    instance.widthScale = lockWidthToHeight ? instance.heightScale : GetTreeWidth();
                    instance.rotation = GetTreeRotation();

                    terrain.AddTreeInstance(instance);
                    placedTreeCount++;
                }
            }
        }

        public static void RemoveTrees(Terrain terrain, float xBase, float yBase, bool clearSelectedOnly)
        {
            float radius = 0.5f * brushSize / terrain.terrainData.size.x;
            terrain.RemoveTrees(new Vector2(xBase, yBase), radius, clearSelectedOnly ? selectedTree : -1);
        }

        public static void MassPlaceTrees(TerrainData terrainData, int numberOfTrees, bool randomTreeColor, bool keepExistingTrees)
        {
            int nbPrototypes = terrainData.treePrototypes.Length;
            if (nbPrototypes == 0)
            {
                Debug.Log("Can't place trees because no prototypes are defined");
                return;
            }

            Undo.RegisterCompleteObjectUndo(terrainData, "Mass Place Trees");

            TreeInstance[] instances = new TreeInstance[numberOfTrees];
            int i = 0;
            while (i < instances.Length)
            {
                TreeInstance instance = new TreeInstance();
                instance.position = new Vector3(Random.value, 0, Random.value);
                if (terrainData.GetSteepness(instance.position.x, instance.position.z) < 30)
                {
                    instance.color = randomTreeColor ? GetTreeColor() : Color.white;
                    instance.lightmapColor = Color.white;
                    instance.prototypeIndex = Random.Range(0, nbPrototypes);

                    instance.heightScale = GetTreeHeight();
                    instance.widthScale = lockWidthToHeight ? instance.heightScale : GetTreeWidth();

                    instance.rotation = GetTreeRotation();

                    instances[i++] = instance;
                }
            }

            if (keepExistingTrees)
            {
                var existingTrees = terrainData.treeInstances;
                var allTrees = new TreeInstance[existingTrees.Length + instances.Length];
                System.Array.Copy(existingTrees, 0, allTrees, 0, existingTrees.Length);
                System.Array.Copy(instances, 0, allTrees, existingTrees.Length, instances.Length);
                instances = allTrees;
            }

            terrainData.treeInstances = instances;
            terrainData.RecalculateTreePositions();
        }
    }

    [CustomEditor(typeof(Terrain))]
    internal class TerrainInspector : Editor
    {
        class Styles
        {
            public GUIStyle gridList = "GridList";
            public GUIStyle gridListText = "GridListText";
            public GUIStyle largeSquare = "Button";
            public GUIStyle command = "Command";
            public Texture settingsIcon = EditorGUIUtility.IconContent("SettingsIcon").image;

            // List of tools supported by the editor
            public GUIContent[] toolIcons =
            {
                EditorGUIUtility.TrIconContent("TerrainInspector.TerrainToolSplat", "Paint Terrain"),
                EditorGUIUtility.TrIconContent("TerrainInspector.TerrainToolTrees", "Paint Trees"),
                EditorGUIUtility.TrIconContent("TerrainInspector.TerrainToolPlants", "Paint Details"),
                EditorGUIUtility.TrIconContent("TerrainInspector.TerrainToolSettings", "Terrain Settings")
            };

            public GUIContent[] toolNames =
            {
                EditorGUIUtility.TrTextContent("Paint Terrain", "Select a tool from the drop down list"),
                EditorGUIUtility.TrTextContent("Paint Trees", "Click to paint trees.\n\nHold shift and click to erase trees.\n\nHold Ctrl and click to erase only trees of the selected type."),
                EditorGUIUtility.TrTextContent("Paint Details", "Click to paint details.\n\nHold shift and click to erase details.\n\nHold Ctrl and click to erase only details of the selected type."),
                EditorGUIUtility.TrTextContent("Terrain Settings")
            };

            public GUIContent brushSize = EditorGUIUtility.TrTextContent("Brush Size", "Size of the brush used to paint.");
            public GUIContent opacity = EditorGUIUtility.TrTextContent("Opacity", "Strength of the applied effect.");
            public GUIContent targetStrength = EditorGUIUtility.TrTextContent("Target Strength", "Maximum opacity you can reach by painting continuously.");
            public GUIContent settings = EditorGUIUtility.TrTextContent("Settings");
            public GUIContent mismatchedTerrainData = EditorGUIUtility.TextContentWithIcon(
                "The TerrainData used by the TerrainCollider component is different from this terrain. Would you like to assign the same TerrainData to the TerrainCollider component?",
                "console.warnicon");

            public GUIContent assign = EditorGUIUtility.TrTextContent("Assign");

            // Textures
            public GUIContent terrainLayers = EditorGUIUtility.TrTextContent("Terrain Layers");
            public GUIContent editTerrainLayers = EditorGUIUtility.TrTextContent("Edit Terrain Layers...");

            // Trees
            public GUIContent trees = EditorGUIUtility.TrTextContent("Trees");
            public GUIContent noTrees = EditorGUIUtility.TrTextContent("No Trees defined", "Use edit button below to add new tree types.");
            public GUIContent editTrees = EditorGUIUtility.TrTextContent("Edit Trees...", "Add/remove tree types.");
            public GUIContent treeDensity = EditorGUIUtility.TrTextContent("Tree Density", "How dense trees are you painting");
            public GUIContent treeHeight = EditorGUIUtility.TrTextContent("Tree Height", "Height of the planted trees");
            public GUIContent treeHeightRandomLabel = EditorGUIUtility.TrTextContent("Random?", "Enable random variation in tree height (variation)");
            public GUIContent treeHeightRandomToggle = EditorGUIUtility.TrTextContent("", "Enable random variation in tree height (variation)");
            public GUIContent lockWidth = EditorGUIUtility.TrTextContent("Lock Width to Height", "Let the tree width be the same with height");
            public GUIContent treeWidth = EditorGUIUtility.TrTextContent("Tree Width", "Width of the planted trees");
            public GUIContent treeWidthRandomLabel = EditorGUIUtility.TrTextContent("Random?", "Enable random variation in tree width (variation)");
            public GUIContent treeWidthRandomToggle = EditorGUIUtility.TrTextContent("", "Enable random variation in tree width (variation)");
            public GUIContent treeColorVar = EditorGUIUtility.TrTextContent("Color Variation", "Amount of random shading applied to trees");
            public GUIContent treeRotation = EditorGUIUtility.TrTextContent("Random Tree Rotation", "Enable?");
            public GUIContent massPlaceTrees = EditorGUIUtility.TrTextContent("Mass Place Trees", "The Mass Place Trees button is a very useful way to create an overall covering of trees without painting over the whole landscape. Following a mass placement, you can still use painting to add or remove trees to create denser or sparser areas.");
            public GUIContent treeLightmapStatic = EditorGUIUtility.TrTextContent("Tree Lightmap Static", "The state of the Lightmap Static flag for the tree prefab root GameObject. The flag can be changed on the prefab. When disabled, this tree will not be visible to the lightmapper. When enabled, any child GameObjects which also have the static flag enabled, will be present in lightmap calculations. Regardless of the Static flag, each tree instance receives its own light probe and no lightmap texels.");

            // Details
            public GUIContent details = EditorGUIUtility.TrTextContent("Details");
            public GUIContent editDetails = EditorGUIUtility.TrTextContent("Edit Details...", "Add/remove detail meshes");
            public GUIContent detailTargetStrength = EditorGUIUtility.TrTextContent("Target Strength", "Target amount");

            // Heightmaps
            public GUIContent height = EditorGUIUtility.TrTextContent("Height", "You can set the Height property manually or you can shift-click on the terrain to sample the height at the mouse position (rather like the \"eyedropper\" tool in an image editor).");

            public GUIContent textures = EditorGUIUtility.TrTextContent("Texture Resolutions (Require resampling on change)");
            public GUIContent importRaw  = EditorGUIUtility.TrTextContent("Import Raw...", "The Import Raw button allows you to set the terrain's heightmap from an image file in the RAW grayscale format. RAW format can be generated by third party terrain editing tools (such as Bryce) and can also be opened, edited and saved by Photoshop. This allows for sophisticated generation and editing of terrains outside Unity.");
            public GUIContent exportRaw = EditorGUIUtility.TrTextContent("Export Raw...", "The Export Raw button allows you to save the terrain's heightmap to an image file in the RAW grayscale format. RAW format can be generated by third party terrain editing tools (such as Bryce) and can also be opened, edited and saved by Photoshop. This allows for sophisticated generation and editing of terrains outside Unity.");
            public GUIContent flatten = EditorGUIUtility.TrTextContent("Flatten", "The Flatten button levels the whole terrain to the chosen height.");

            public GUIContent bakeLightProbesForTrees = EditorGUIUtility.TrTextContent("Bake Light Probes For Trees", "If the option is enabled, Unity will create internal light probes at the position of each tree (these probes are internal and will not affect other renderers in the scene) and apply them to tree renderers for lighting. Otherwise trees are still affected by LightProbeGroups. The option is only effective for trees that have LightProbe enabled on their prototype prefab.");
            public GUIContent deringLightProbesForTrees = EditorGUIUtility.TrTextContent("Remove Light Probe Ringing", "When enabled, removes visible overshooting often observed as ringing on objects affected by intense lighting at the expense of reduced contrast.");
            public GUIContent refresh = EditorGUIUtility.TrTextContent("Refresh", "When you save a tree asset from the modelling app, you will need to click the Refresh button (shown in the inspector when the tree painting tool is selected) in order to see the updated trees on your terrain.");

            // Settings
            public GUIContent drawTerrain = EditorGUIUtility.TrTextContent("Draw", "Toggle the rendering of terrain");
            public GUIContent drawInstancedTerrain = EditorGUIUtility.TrTextContent("Draw Instanced" , "Toggle terrain instancing rendering");
            public GUIContent pixelError = EditorGUIUtility.TrTextContent("Pixel Error", "The accuracy of the mapping between the terrain maps (heightmap, textures, etc) and the generated terrain; higher values indicate lower accuracy but lower rendering overhead.");
            public GUIContent baseMapDist = EditorGUIUtility.TrTextContent("Base Map Dist.", "The maximum distance at which terrain textures will be displayed at full resolution. Beyond this distance, a lower resolution composite image will be used for efficiency.");
            public GUIContent castShadows = EditorGUIUtility.TrTextContent("Cast Shadows", "Does the terrain cast shadows?");
            public GUIContent material = EditorGUIUtility.TrTextContent("Material", "The material used to render the terrain. This will affect how the color channels of a terrain texture are interpreted.");
            public GUIContent reflectionProbes = EditorGUIUtility.TrTextContent("Reflection Probes", "How reflection probes are used on terrain. Only effective when using built-in standard material or a custom material which supports rendering with reflection.");
            public GUIContent thickness = EditorGUIUtility.TrTextContent("Thickness", "How much the terrain collision volume should extend along the negative Y-axis. Objects are considered colliding with the terrain from the surface to a depth equal to the thickness. This helps prevent high-speed moving objects from penetrating into the terrain without using expensive continuous collision detection.");
            public GUIContent preserveTreePrototypeLayers = EditorGUIUtility.TextContent("Preserve Tree Prototype Layers|Enable this option if you want your tree instances to take on the layer values of their prototype prefabs, rather than the terrain GameObject's layer.");
            public GUIContent drawTrees = EditorGUIUtility.TrTextContent("Draw", "Should trees, grass and details be drawn?");
            public GUIContent detailObjectDistance = EditorGUIUtility.TrTextContent("Detail Distance", "The distance (from camera) beyond which details will be culled.");
            public GUIContent collectDetailPatches = EditorGUIUtility.TrTextContent("Collect Detail Patches", "Should detail patches in the Terrain be removed from memory when not visible?");
            public GUIContent detailObjectDensity = EditorGUIUtility.TrTextContent("Detail Density", "The number of detail/grass objects in a given unit of area. The value can be set lower to reduce rendering overhead.");
            public GUIContent treeDistance = EditorGUIUtility.TrTextContent("Tree Distance", "The distance (from camera) beyond which trees will be culled.");
            public GUIContent treeBillboardDistance = EditorGUIUtility.TrTextContent("Billboard Start", "The distance (from camera) at which 3D tree objects will be replaced by billboard images.");
            public GUIContent treeCrossFadeLength = EditorGUIUtility.TrTextContent("Fade Length", "Distance over which trees will transition between 3D objects and billboards.");
            public GUIContent treeMaximumFullLODCount = EditorGUIUtility.TrTextContent("Max Mesh Trees", "The maximum number of visible trees that will be represented as solid 3D meshes. Beyond this limit, trees will be replaced with billboards.");

            public GUIContent wavingGrassStrength = EditorGUIUtility.TrTextContent("Speed", "The speed of the wind as it blows grass.");
            public GUIContent wavingGrassSpeed = EditorGUIUtility.TrTextContent("Size", "The size of the 'ripples' on grassy areas as the wind blows over them.");
            public GUIContent wavingGrassAmount = EditorGUIUtility.TrTextContent("Bending", "The degree to which grass objects are bent over by the wind.");
            public GUIContent wavingGrassTint = EditorGUIUtility.TrTextContent("Grass Tint", "Overall color tint applied to grass objects.");
        }
        static Styles styles;

        private const string kDisplayLightingKey = "TerrainInspector.Lighting.ShowSettings";

        static float PercentSlider(GUIContent content, float valueInPercent, float minVal, float maxVal)
        {
            EditorGUI.BeginChangeCheck();
            float v = EditorGUILayout.Slider(content, Mathf.Round(valueInPercent * 100f), minVal * 100f, maxVal * 100f);

            if (EditorGUI.EndChangeCheck())
            {
                return v / 100f;
            }
            return valueInPercent;
        }

        // TODO LOOK INTO THESE!!!
        internal static PrefKey[] s_ToolKeys =
        {
            new PrefKey("Terrain/Raise Height", "f1"),
            new PrefKey("Terrain/Set Height", "f2"),
            new PrefKey("Terrain/Smooth Height", "f3"),
            new PrefKey("Terrain/Texture Paint", "f4"),
            new PrefKey("Terrain/Tree Brush", "f5"),
            new PrefKey("Terrain/Detail Brush", "f6")
        };
        internal static PrefKey s_PrevBrush = new PrefKey("Terrain/Previous Brush", ",");
        internal static PrefKey s_NextBrush = new PrefKey("Terrain/Next Brush", ".");
        internal static PrefKey s_PrevTexture = new PrefKey("Terrain/Previous Detail", "#,");
        internal static PrefKey s_NextTexture = new PrefKey("Terrain/Next Detail", "#.");
        // Source terrain
        Terrain m_Terrain;
        TerrainCollider m_TerrainCollider;

        GUIContent[]    m_TreeContents = null;
        GUIContent[]    m_DetailContents = null;

        float m_Strength;
        int m_Size;
        float m_SplatAlpha;
        float m_DetailOpacity;
        float m_DetailStrength;

        int m_SelectedDetail = 0;

        int m_CurrentHeightmapResolution = 0;
        int m_CurrentControlTextureResolution = 0;

        const float kHeightmapBrushScale = 0.01F;
        const float kMinBrushStrength = (1.1F / ushort.MaxValue) / kHeightmapBrushScale;

        BrushList m_BrushList;

        BrushList brushList {  get { if (m_BrushList == null) { m_BrushList = new BrushList(); } return m_BrushList; } }


        internal int m_ActivePaintToolIndex = 0;
        internal ITerrainPaintTool[] m_Tools = null;
        internal string[] m_ToolNames = null;

        ITerrainPaintTool GetActiveTool()
        {
            if (m_ActivePaintToolIndex >= m_Tools.Length)
                m_ActivePaintToolIndex = 0;

            return m_Tools[m_ActivePaintToolIndex];
        }

        static int s_TerrainEditorHash = "TerrainEditor".GetHashCode();

        // The instance ID of the active inspector.
        // It's defined as the last inspector that had one of its terrain tools selected.
        // If a terrain inspector is the only one when created, it also becomes active.
        static int s_activeTerrainInspector = 0;

        List<ReflectionProbeBlendInfo> m_BlendInfoList = new List<ReflectionProbeBlendInfo>();

        private AnimBool m_ShowBuiltinSpecularSettings = new AnimBool();
        private AnimBool m_ShowCustomMaterialSettings = new AnimBool();
        private AnimBool m_ShowReflectionProbesGUI = new AnimBool();

        bool m_LODTreePrototypePresent = false;

        private LightingSettingsInspector m_Lighting;


        void CheckKeys()
        {
            // If there is an active inspector, hot keys are exclusive to it.
            if (s_activeTerrainInspector != 0 && s_activeTerrainInspector != GetInstanceID())
                return;

            for (int i = 0; i < s_ToolKeys.Length; i++)
            {
                if (s_ToolKeys[i].activated)
                {
                    selectedTool = (TerrainTool)i;
                    Repaint();
                    Event.current.Use();
                }
            }

            if (s_PrevBrush.activated)
            {
                brushList.SelectPrevBrush();
                Repaint();
                Event.current.Use();
            }

            if (s_NextBrush.activated)
            {
                brushList.SelectNextBrush();
                Repaint();
                Event.current.Use();
            }
            int delta = 0;
            if (s_NextTexture.activated)
                delta = 1;
            if (s_PrevTexture.activated)
                delta = -1;

            if (delta != 0)
            {
                switch (selectedTool)
                {
                    case TerrainTool.PaintDetail:
                        m_SelectedDetail = (int)Mathf.Repeat(m_SelectedDetail + delta, m_Terrain.terrainData.detailPrototypes.Length);
                        Event.current.Use();
                        Repaint();
                        break;
                    case TerrainTool.PlaceTree:
                        if (TreePainter.selectedTree >= 0)
                            TreePainter.selectedTree = (int)Mathf.Repeat(TreePainter.selectedTree + delta, m_TreeContents.Length);
                        else if (delta == -1 && m_TreeContents.Length > 0)
                            TreePainter.selectedTree = m_TreeContents.Length - 1;
                        else if (delta == 1 && m_TreeContents.Length > 0)
                            TreePainter.selectedTree = 0;
                        Event.current.Use();
                        Repaint();
                        break;
                }
            }
        }

        void ResetPaintTools()
        {
            m_Tools = null;
            m_ToolNames = null;

            var arrTools = new List<ITerrainPaintTool>();
            var arrNames = new List<string>();
            foreach (var klass in EditorAssemblies.SubclassesOfGenericType(typeof(TerrainPaintTool<>)))
            {
                if (klass.IsAbstract)
                    continue;

                var instanceProperty = klass.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var mi = instanceProperty.GetGetMethod();
                var tool = (ITerrainPaintTool)mi.Invoke(null, null);
                arrTools.Add(tool);
                arrNames.Add(tool.GetName());
            }
            m_Tools = arrTools.ToArray();
            m_ToolNames = arrNames.ToArray();
        }

        void Initialize()
        {
            m_Terrain = target as Terrain;
        }

        void LoadInspectorSettings()
        {
            m_Strength = EditorPrefs.GetFloat("TerrainBrushStrength", 0.5f);
            m_Size = EditorPrefs.GetInt("TerrainBrushSize", 25);
            m_SplatAlpha = EditorPrefs.GetFloat("TerrainBrushSplatAlpha", 1.0f);
            m_DetailOpacity = EditorPrefs.GetFloat("TerrainDetailOpacity", 1.0f);
            m_DetailStrength = EditorPrefs.GetFloat("TerrainDetailStrength", 0.8f);

            int selected = EditorPrefs.GetInt("TerrainSelectedBrush", 0);
            m_SelectedDetail = EditorPrefs.GetInt("TerrainSelectedDetail", 0);

            m_ActivePaintToolIndex = EditorPrefs.GetInt("TerraiActivePaintToolIndex", 0);
            if (m_ActivePaintToolIndex > m_Tools.Length)
                m_ActivePaintToolIndex = 0;

            brushList.UpdateSelection(selected);
        }

        void SaveInspectorSettings()
        {
            EditorPrefs.SetInt("TerrainSelectedDetail", m_SelectedDetail);
            EditorPrefs.SetInt("TerrainSelectedBrush", brushList.selectedIndex);

            EditorPrefs.SetFloat("TerrainDetailStrength", m_DetailStrength);
            EditorPrefs.SetFloat("TerrainDetailOpacity", m_DetailOpacity);
            EditorPrefs.SetFloat("TerrainBrushSplatAlpha", m_SplatAlpha);
            EditorPrefs.SetInt("TerrainBrushSize", m_Size);
            EditorPrefs.SetFloat("TerrainBrushStrength", m_Strength);

            EditorPrefs.SetInt("TerraiActivePaintToolIndex", m_ActivePaintToolIndex);
        }

        public void OnEnable()
        {
            // Acquire active inspector ownership if there is no other inspector active.
            if (s_activeTerrainInspector == 0)
                s_activeTerrainInspector = GetInstanceID();

            m_ShowBuiltinSpecularSettings.valueChanged.AddListener(Repaint);
            m_ShowCustomMaterialSettings.valueChanged.AddListener(Repaint);
            m_ShowReflectionProbesGUI.valueChanged.AddListener(Repaint);

            var terrain = target as Terrain;
            if (terrain != null)
            {
                m_ShowBuiltinSpecularSettings.value = terrain.materialType == Terrain.MaterialType.BuiltInLegacySpecular;
                m_ShowCustomMaterialSettings.value = terrain.materialType == Terrain.MaterialType.Custom;
                m_ShowReflectionProbesGUI.value = terrain.materialType == Terrain.MaterialType.BuiltInStandard || terrain.materialType == Terrain.MaterialType.Custom;
            }

            ResetPaintTools();

            LoadInspectorSettings();

            InitializeLightingFields();

            SceneView.onSceneGUIDelegate += OnSceneGUICallback;
        }

        public void OnDisable()
        {
            TerrainPaintUtility.FlushAllPaints();
            SceneView.onSceneGUIDelegate -= OnSceneGUICallback;

            SaveInspectorSettings();

            m_ShowReflectionProbesGUI.valueChanged.RemoveListener(Repaint);
            m_ShowCustomMaterialSettings.valueChanged.RemoveListener(Repaint);
            m_ShowBuiltinSpecularSettings.valueChanged.RemoveListener(Repaint);

            // Return active inspector ownership.
            if (s_activeTerrainInspector == GetInstanceID())
                s_activeTerrainInspector = 0;
        }

        SavedInt m_SelectedTool = new SavedInt("TerrainSelectedTool", (int)TerrainTool.Paint);
        TerrainTool selectedTool
        {
            get
            {
                if (Tools.current == Tool.None && GetInstanceID() == s_activeTerrainInspector)
                    return (TerrainTool)m_SelectedTool.value;
                return TerrainTool.None;
            }
            set
            {
                if (value != TerrainTool.None)
                    Tools.current = Tool.None;
                m_SelectedTool.value = (int)value;
                s_activeTerrainInspector = GetInstanceID();
            }
        }

        public void MenuButton(GUIContent title, string menuName, int userData)
        {
            GUIContent t = new GUIContent(title.text, styles.settingsIcon, title.tooltip);
            Rect r = GUILayoutUtility.GetRect(t, styles.largeSquare);
            if (GUI.Button(r, t, styles.largeSquare))
            {
                MenuCommand context = new MenuCommand(m_Terrain, userData);
                EditorUtility.DisplayPopupMenu(new Rect(r.x, r.y, 0, 0), menuName, context);
            }
        }

        public static int AspectSelectionGrid(int selected, Texture[] textures, int approxSize, GUIStyle style, GUIContent errorMessage, out bool doubleClick)
        {
            GUILayout.BeginVertical("box", GUILayout.MinHeight(approxSize));
            int retval = 0;

            doubleClick = false;

            if (textures.Length != 0)
            {
                int columns = (int)(EditorGUIUtility.currentViewWidth - 150) / approxSize;
                int rows = (int)Mathf.Ceil((textures.Length + columns - 1) / columns);
                Rect r = GUILayoutUtility.GetAspectRect((float)columns / (float)rows);
                Event evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.clickCount == 2 && r.Contains(evt.mousePosition))
                {
                    doubleClick = true;
                    evt.Use();
                }

                retval = GUI.SelectionGrid(r, System.Math.Min(selected, textures.Length - 1), textures, (int)columns, style);
            }
            else
            {
                GUILayout.Label(errorMessage);
            }

            GUILayout.EndVertical();
            return retval;
        }

        static Rect GetBrushAspectRect(int elementCount, int approxSize, int extraLineHeight, out int xCount)
        {
            xCount = (int)Mathf.Ceil((EditorGUIUtility.currentViewWidth - 20) / approxSize);
            int yCount = elementCount / xCount;
            if (elementCount % xCount != 0)
                yCount++;
            Rect r1 = GUILayoutUtility.GetAspectRect(xCount / (float)yCount);
            Rect r2 = GUILayoutUtility.GetRect(10, extraLineHeight * yCount);
            r1.height += r2.height;
            return r1;
        }

        public static int AspectSelectionGridImageAndText(int selected, GUIContent[] textures, int approxSize, GUIStyle style, string emptyString, out bool doubleClick)
        {
            EditorGUILayout.BeginVertical(GUIContent.none, EditorStyles.helpBox, GUILayout.MinHeight(10));
            int retval = 0;

            doubleClick = false;

            if (textures.Length != 0)
            {
                int xCount = 0;
                Rect rect = GetBrushAspectRect(textures.Length, approxSize, 12, out xCount);

                Event evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.clickCount == 2 && rect.Contains(evt.mousePosition))
                {
                    doubleClick = true;
                    evt.Use();
                }
                retval = GUI.SelectionGrid(rect, System.Math.Min(selected, textures.Length - 1), textures, xCount, style);
            }
            else
            {
                GUILayout.Label(emptyString);
            }

            GUILayout.EndVertical();
            return retval;
        }

        void LoadTreeIcons()
        {
            // Locate the proto types asset preview textures
            TreePrototype[] trees = m_Terrain.terrainData.treePrototypes;

            m_TreeContents = new GUIContent[trees.Length];
            for (int i = 0; i < m_TreeContents.Length; i++)
            {
                m_TreeContents[i] = new GUIContent();
                Texture tex = AssetPreview.GetAssetPreview(trees[i].prefab);
                if (tex != null)
                    m_TreeContents[i].image = tex;

                if (trees[i].prefab != null)
                {
                    m_TreeContents[i].text = trees[i].prefab.name;
                    m_TreeContents[i].tooltip = m_TreeContents[i].text;
                }
                else
                    m_TreeContents[i].text = "Missing";
            }
        }

        void LoadDetailIcons()
        {
            // Locate the proto types asset preview textures
            DetailPrototype[] prototypes = m_Terrain.terrainData.detailPrototypes;
            m_DetailContents = new GUIContent[prototypes.Length];
            for (int i = 0; i < m_DetailContents.Length; i++)
            {
                m_DetailContents[i] = new GUIContent();

                if (prototypes[i].usePrototypeMesh)
                {
                    Texture tex = AssetPreview.GetAssetPreview(prototypes[i].prototype);
                    if (tex != null)
                        m_DetailContents[i].image = tex;

                    if (prototypes[i].prototype != null)
                        m_DetailContents[i].text = prototypes[i].prototype.name;
                    else
                        m_DetailContents[i].text = "Missing";
                }
                else
                {
                    Texture tex = prototypes[i].prototypeTexture;
                    if (tex != null)
                        m_DetailContents[i].image = tex;
                    if (tex != null)
                        m_DetailContents[i].text = tex.name;
                    else
                        m_DetailContents[i].text = "Missing";
                }
            }
        }

        public void ShowTrees()
        {
            LoadTreeIcons();

            // Tree picker
            GUI.changed = false;

            ShowUpgradeTreePrototypeScaleUI();

            GUILayout.Label(styles.trees, EditorStyles.boldLabel);
            bool doubleClick;
            TreePainter.selectedTree = AspectSelectionGridImageAndText(TreePainter.selectedTree, m_TreeContents, 64, styles.gridListText, "No trees defined", out doubleClick);

            if (TreePainter.selectedTree >= m_TreeContents.Length)
                TreePainter.selectedTree = -1;

            if (doubleClick)
            {
                TerrainTreeContextMenus.EditTree(new MenuCommand(m_Terrain, TreePainter.selectedTree));
                GUIUtility.ExitGUI();
            }

            GUILayout.BeginHorizontal();
            ShowMassPlaceTrees();
            GUILayout.FlexibleSpace();
            MenuButton(styles.editTrees, "CONTEXT/TerrainEngineTrees", TreePainter.selectedTree);
            ShowRefreshPrototypes();
            GUILayout.EndHorizontal();

            if (TreePainter.selectedTree == -1)
                return;

            GUILayout.Label(styles.settings, EditorStyles.boldLabel);
            // Placement distance
            TreePainter.brushSize = EditorGUILayout.Slider(styles.brushSize, TreePainter.brushSize, 1, Mathf.Min(m_Terrain.terrainData.size.x, m_Terrain.terrainData.size.z)); // former string formatting: ""
            float oldDens = (3.3f - TreePainter.spacing) / 3f;
            float newDens = PercentSlider(styles.treeDensity, oldDens, .1f, 1);
            // Only set spacing when value actually changes. Otherwise
            // it will lose precision because we're constantly doing math
            // back and forth with it.
            if (newDens != oldDens)
                TreePainter.spacing = (1.1f - newDens) * 3f;

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label(styles.treeHeight, GUILayout.Width(EditorGUIUtility.labelWidth - 6));
            GUILayout.Label(styles.treeHeightRandomLabel, GUILayout.ExpandWidth(false));
            TreePainter.allowHeightVar = GUILayout.Toggle(TreePainter.allowHeightVar, styles.treeHeightRandomToggle, GUILayout.ExpandWidth(false));
            if (TreePainter.allowHeightVar)
            {
                EditorGUI.BeginChangeCheck();
                float min = TreePainter.treeHeight * (1.0f - TreePainter.treeHeightVariation);
                float max = TreePainter.treeHeight * (1.0f + TreePainter.treeHeightVariation);
                EditorGUILayout.MinMaxSlider(ref min, ref max, 0.01f, 2.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    TreePainter.treeHeight = (min + max) * 0.5f;
                    TreePainter.treeHeightVariation = (max - min) / (min + max);
                }
            }
            else
            {
                TreePainter.treeHeight = EditorGUILayout.Slider(TreePainter.treeHeight, 0.01f, 2.0f);
                TreePainter.treeHeightVariation = 0.0f;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            TreePainter.lockWidthToHeight = EditorGUILayout.Toggle(styles.lockWidth, TreePainter.lockWidthToHeight);
            if (TreePainter.lockWidthToHeight)
            {
                TreePainter.treeWidth = TreePainter.treeHeight;
                TreePainter.treeWidthVariation = TreePainter.treeHeightVariation;
                TreePainter.allowWidthVar = TreePainter.allowHeightVar;
            }

            GUILayout.Space(5);

            using (new EditorGUI.DisabledScope(TreePainter.lockWidthToHeight))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(styles.treeWidth, GUILayout.Width(EditorGUIUtility.labelWidth - 6));
                GUILayout.Label(styles.treeWidthRandomLabel, GUILayout.ExpandWidth(false));
                TreePainter.allowWidthVar = GUILayout.Toggle(TreePainter.allowWidthVar, styles.treeWidthRandomToggle, GUILayout.ExpandWidth(false));
                if (TreePainter.allowWidthVar)
                {
                    EditorGUI.BeginChangeCheck();
                    float min = TreePainter.treeWidth * (1.0f - TreePainter.treeWidthVariation);
                    float max = TreePainter.treeWidth * (1.0f + TreePainter.treeWidthVariation);
                    EditorGUILayout.MinMaxSlider(ref min, ref max, 0.01f, 2.0f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        TreePainter.treeWidth = (min + max) * 0.5f;
                        TreePainter.treeWidthVariation = (max - min) / (min + max);
                    }
                }
                else
                {
                    TreePainter.treeWidth = EditorGUILayout.Slider(TreePainter.treeWidth, 0.01f, 2.0f);
                    TreePainter.treeWidthVariation = 0.0f;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(5);

            if (TerrainEditorUtility.IsLODTreePrototype(m_Terrain.terrainData.treePrototypes[TreePainter.selectedTree].m_Prefab))
                TreePainter.randomRotation = EditorGUILayout.Toggle(styles.treeRotation, TreePainter.randomRotation);
            else
                TreePainter.treeColorAdjustment = EditorGUILayout.Slider(styles.treeColorVar, TreePainter.treeColorAdjustment, 0, 1);

            GameObject prefab = m_Terrain.terrainData.treePrototypes[TreePainter.selectedTree].m_Prefab;
            if (prefab != null)
            {
                StaticEditorFlags staticEditorFlags = GameObjectUtility.GetStaticEditorFlags(prefab);
                bool lightmapStatic = (staticEditorFlags & StaticEditorFlags.LightmapStatic) != 0;
                using (new EditorGUI.DisabledScope(true))   // Always disabled, because we don't want to edit the prefab.
                    lightmapStatic = EditorGUILayout.Toggle(styles.treeLightmapStatic, lightmapStatic);
            }
        }

        public void ShowDetails()
        {
            LoadDetailIcons();
            ShowBrushes();
            // Brush size

            // Detail picker
            GUI.changed = false;

            GUILayout.Label(styles.details, EditorStyles.boldLabel);
            bool doubleClick;
            m_SelectedDetail = AspectSelectionGridImageAndText(m_SelectedDetail, m_DetailContents, 64, styles.gridListText, "No Detail Objects defined", out doubleClick);
            if (doubleClick)
            {
                TerrainDetailContextMenus.EditDetail(new MenuCommand(m_Terrain, m_SelectedDetail));
                GUIUtility.ExitGUI();
            }

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            MenuButton(styles.editDetails, "CONTEXT/TerrainEngineDetails", m_SelectedDetail);
            ShowRefreshPrototypes();
            GUILayout.EndHorizontal();

            GUILayout.Label(styles.settings, EditorStyles.boldLabel);

            // Brush size
            m_Size = Mathf.RoundToInt(EditorGUILayout.Slider(styles.brushSize, m_Size, 1, 100)); // former string formatting: ""
            m_DetailOpacity = EditorGUILayout.Slider(styles.opacity, m_DetailOpacity, 0, 1); // former string formatting: "%"

            // Strength
            m_DetailStrength = EditorGUILayout.Slider(styles.detailTargetStrength, m_DetailStrength, 0, 1); // former string formatting: "%"
            m_DetailStrength = Mathf.Round(m_DetailStrength * 16.0f) / 16.0f;
        }

        public void ShowSettings()
        {
            TerrainData terrainData = m_Terrain.terrainData;

            EditorGUI.BeginChangeCheck();

            GUILayout.Label(L10n.Tr("Base Terrain"), EditorStyles.boldLabel);
            m_Terrain.drawHeightmap = EditorGUILayout.Toggle(styles.drawTerrain, m_Terrain.drawHeightmap);
            m_Terrain.drawInstanced = EditorGUILayout.Toggle(styles.drawInstancedTerrain, m_Terrain.drawInstanced);
            m_Terrain.heightmapPixelError = EditorGUILayout.Slider(styles.pixelError, m_Terrain.heightmapPixelError, 1, 200); // former string formatting: ""
            m_Terrain.basemapDistance = EditorGUILayout.Slider(styles.baseMapDist, m_Terrain.basemapDistance, 0, 2000); // former string formatting: ""
            m_Terrain.castShadows = EditorGUILayout.Toggle(styles.castShadows, m_Terrain.castShadows);

            m_Terrain.materialType = (Terrain.MaterialType)EditorGUILayout.EnumPopup(styles.material, m_Terrain.materialType);

            // Make sure we don't reference any custom material if we are to use a built-in shader.
            if (m_Terrain.materialType != Terrain.MaterialType.Custom)
                m_Terrain.materialTemplate = null;

            m_ShowBuiltinSpecularSettings.target = m_Terrain.materialType == Terrain.MaterialType.BuiltInLegacySpecular;
            m_ShowCustomMaterialSettings.target = m_Terrain.materialType == Terrain.MaterialType.Custom;
            m_ShowReflectionProbesGUI.target = m_Terrain.materialType == Terrain.MaterialType.BuiltInStandard || m_Terrain.materialType == Terrain.MaterialType.Custom;

            if (EditorGUILayout.BeginFadeGroup(m_ShowBuiltinSpecularSettings.faded))
            {
                EditorGUI.indentLevel++;
                m_Terrain.legacySpecular = EditorGUILayout.ColorField("Specular Color", m_Terrain.legacySpecular);
                m_Terrain.legacyShininess = EditorGUILayout.Slider("Shininess", m_Terrain.legacyShininess, 0.03f, 1.0f);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(m_ShowCustomMaterialSettings.faded))
            {
                EditorGUI.indentLevel++;
                m_Terrain.materialTemplate = EditorGUILayout.ObjectField("Custom Material", m_Terrain.materialTemplate, typeof(Material), false) as Material;

                // Warn if shader needs tangent basis
                if (m_Terrain.materialTemplate != null)
                {
                    Shader s = m_Terrain.materialTemplate.shader;
                    if (ShaderUtil.HasTangentChannel(s))
                    {
                        GUIContent c = EditorGUIUtility.TrTextContent("Can't use materials with shaders which need tangent geometry on terrain, use shaders in Nature/Terrain instead.");
                        EditorGUILayout.HelpBox(c.text, MessageType.Warning, false);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(m_ShowReflectionProbesGUI.faded))
            {
                m_Terrain.reflectionProbeUsage = (ReflectionProbeUsage)EditorGUILayout.EnumPopup(styles.reflectionProbes, m_Terrain.reflectionProbeUsage);
                if (m_Terrain.reflectionProbeUsage != ReflectionProbeUsage.Off)
                {
                    EditorGUI.indentLevel++;
                    m_Terrain.GetClosestReflectionProbes(m_BlendInfoList);
                    RendererEditorBase.Probes.ShowClosestReflectionProbes(m_BlendInfoList);
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndFadeGroup();

            terrainData.thickness = EditorGUILayout.FloatField(styles.thickness, terrainData.thickness);

            GUILayout.Label("Tree & Detail Objects", EditorStyles.boldLabel);
            m_Terrain.drawTreesAndFoliage = EditorGUILayout.Toggle(styles.drawTrees, m_Terrain.drawTreesAndFoliage);
            m_Terrain.bakeLightProbesForTrees = EditorGUILayout.Toggle(styles.bakeLightProbesForTrees, m_Terrain.bakeLightProbesForTrees);
            using (new EditorGUI.DisabledScope(!m_Terrain.bakeLightProbesForTrees))
                m_Terrain.deringLightProbesForTrees = EditorGUILayout.Toggle(styles.deringLightProbesForTrees, m_Terrain.deringLightProbesForTrees);
            m_Terrain.preserveTreePrototypeLayers = EditorGUILayout.Toggle(styles.preserveTreePrototypeLayers, m_Terrain.preserveTreePrototypeLayers);
            m_Terrain.detailObjectDistance = EditorGUILayout.Slider(styles.detailObjectDistance, m_Terrain.detailObjectDistance, 0, 250); // former string formatting: ""
            m_Terrain.collectDetailPatches = EditorGUILayout.Toggle(styles.collectDetailPatches, m_Terrain.collectDetailPatches);
            m_Terrain.detailObjectDensity = EditorGUILayout.Slider(styles.detailObjectDensity, m_Terrain.detailObjectDensity, 0.0f, 1.0f);
            m_Terrain.treeDistance = EditorGUILayout.Slider(styles.treeDistance, m_Terrain.treeDistance, 0, 2000); // former string formatting: ""
            m_Terrain.treeBillboardDistance = EditorGUILayout.Slider(styles.treeBillboardDistance, m_Terrain.treeBillboardDistance, 5, 2000); // former string formatting: ""
            m_Terrain.treeCrossFadeLength = EditorGUILayout.Slider(styles.treeCrossFadeLength, m_Terrain.treeCrossFadeLength, 0, 200); // former string formatting: ""
            m_Terrain.treeMaximumFullLODCount = EditorGUILayout.IntSlider(styles.treeMaximumFullLODCount, m_Terrain.treeMaximumFullLODCount, 0, 10000);

            // Only do this check once per frame.
            if (Event.current.type == EventType.Layout)
            {
                m_LODTreePrototypePresent = false;
                for (int i = 0; i < terrainData.treePrototypes.Length; ++i)
                {
                    if (TerrainEditorUtility.IsLODTreePrototype(terrainData.treePrototypes[i].prefab))
                    {
                        m_LODTreePrototypePresent = true;
                        break;
                    }
                }
            }

            if (m_LODTreePrototypePresent)
                EditorGUILayout.HelpBox("Tree Distance, Billboard Start, Fade Length and Max Mesh Trees have no effect on SpeedTree trees. Please use the LOD Group component on the tree prefab to control LOD settings.", MessageType.Info);

            if (EditorGUI.EndChangeCheck())
            {
                EditorApplication.SetSceneRepaintDirty();
                EditorUtility.SetDirty(m_Terrain);

                if (!EditorApplication.isPlaying)
                    SceneManagement.EditorSceneManager.MarkSceneDirty(m_Terrain.gameObject.scene);
            }

            EditorGUI.BeginChangeCheck();

            GUILayout.Label("Wind Settings for Grass", EditorStyles.boldLabel);
            float wavingGrassStrength = EditorGUILayout.Slider(styles.wavingGrassStrength, terrainData.wavingGrassStrength, 0, 1); // former string formatting: "%"
            float wavingGrassSpeed = EditorGUILayout.Slider(styles.wavingGrassSpeed, terrainData.wavingGrassSpeed, 0, 1); // former string formatting: "%"
            float wavingGrassAmount = EditorGUILayout.Slider(styles.wavingGrassAmount, terrainData.wavingGrassAmount, 0, 1); // former string formatting: "%"
            Color wavingGrassTint = EditorGUILayout.ColorField(styles.wavingGrassTint, terrainData.wavingGrassTint);

            if (EditorGUI.EndChangeCheck())
            {
                // Apply terrain settings only when something has changed. Otherwise we needlessly dirty the object and it will show up as modified.
                terrainData.wavingGrassStrength = wavingGrassStrength;
                terrainData.wavingGrassSpeed = wavingGrassSpeed;
                terrainData.wavingGrassAmount = wavingGrassAmount;
                terrainData.wavingGrassTint = wavingGrassTint;

                // In cases where terrain data is embedded in the scene (i.e. it's not an asset),
                // we need to dirty the scene if terrainData has changed.
                if (!EditorUtility.IsPersistent(terrainData) && !EditorApplication.isPlaying)
                    SceneManagement.EditorSceneManager.MarkSceneDirty(m_Terrain.gameObject.scene);
            }

            ShowResolution();
            ShowTextures();

            RenderLightingFields();
        }

        public void ShowPaint()
        {
            if (m_Tools != null && m_Tools.Length > 1 && m_ToolNames != null)
            {
                EditorGUI.BeginChangeCheck();
                m_ActivePaintToolIndex = EditorGUILayout.Popup(m_ActivePaintToolIndex, m_ToolNames);
                if (EditorGUI.EndChangeCheck())
                    Repaint();
                ITerrainPaintTool activeTool = GetActiveTool();

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(activeTool.GetDesc());
                GUILayout.EndVertical();

                activeTool.OnInspectorGUI(m_Terrain);

                if (activeTool.ShouldShowBrushes())
                {
                    GUILayout.Space(5);
                    ShowBrushes();
                }
            }
        }

        public void ShowBrushes()
        {
            bool repaint = brushList.ShowGUI();

            m_Size = Mathf.RoundToInt(EditorGUILayout.Slider(styles.brushSize, m_Size, 1, Mathf.Min(m_Terrain.terrainData.size.x, m_Terrain.terrainData.size.z)));
            m_Strength = PercentSlider(styles.opacity, m_Strength, kMinBrushStrength, 1); // former string formatting: "0.0%"

            brushList.ShowEditGUI();

            if (repaint)
                Repaint();
        }

        void ResizeControlTexture(int newResolution)
        {
            RenderTexture oldRT = RenderTexture.active;

            TerrainData td = m_Terrain.terrainData;
            RenderTexture[] oldAlphaMaps = new RenderTexture[td.alphamapTextureCount];
            for (int i = 0; i < oldAlphaMaps.Length; i++)
            {
                td.alphamapTextures[i].filterMode = FilterMode.Bilinear;
                oldAlphaMaps[i] = RenderTexture.GetTemporary(newResolution, newResolution, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(td.alphamapTextures[i], oldAlphaMaps[i]);
            }

            Undo.RegisterCompleteObjectUndo(m_Terrain.terrainData, "Resize Heightmap");

            m_Terrain.terrainData.alphamapResolution = newResolution;
            for (int i = 0; i < oldAlphaMaps.Length; i++)
            {
                RenderTexture.active = oldAlphaMaps[i];
                td.alphamapTextures[i].ReadPixels(new Rect(0, 0, newResolution, newResolution), 0, 0);
                td.alphamapTextures[i].Apply();
            }
            RenderTexture.active = oldRT;
            for (int i = 0; i < oldAlphaMaps.Length; i++)
                RenderTexture.ReleaseTemporary(oldAlphaMaps[i]);

            td.SetBasemapDirty(true);
            Repaint();
        }

        void ResizeHeightmap(int newResolution)
        {
            RenderTexture oldRT = RenderTexture.active;

            RenderTexture oldHeightmap = RenderTexture.GetTemporary(m_Terrain.terrainData.heightmapTexture.descriptor);
            Graphics.Blit(m_Terrain.terrainData.heightmapTexture, oldHeightmap);

            Undo.RegisterCompleteObjectUndo(m_Terrain.terrainData, "Resize Heightmap");

            Vector3 oldSize = m_Terrain.terrainData.size;
            m_Terrain.terrainData.heightmapResolution = newResolution;
            m_Terrain.terrainData.size = oldSize;

            oldHeightmap.filterMode = FilterMode.Bilinear;
            Graphics.Blit(oldHeightmap, m_Terrain.terrainData.heightmapTexture);
            RenderTexture.ReleaseTemporary(oldHeightmap);

            RenderTexture.active = oldRT;

            m_Terrain.terrainData.UpdateDirtyRegion(0, 0, m_Terrain.terrainData.heightmapTexture.width, m_Terrain.terrainData.heightmapTexture.height);
            m_Terrain.Flush();
            m_Terrain.ApplyDelayedHeightmapModification();

            Repaint();
        }

        public void ShowTextures()
        {
            GUILayout.Label(styles.textures, EditorStyles.boldLabel);

            GUILayout.BeginVertical();

            // base texture
            GUILayout.BeginHorizontal();
            int baseTextureResolution = EditorGUILayout.DelayedIntField(EditorGUIUtility.TrTextContent("Base Texture Resolution", "Resolution of the composite texture used on the terrain when viewed from a distance greater than the Basemap Distance."), m_Terrain.terrainData.baseMapResolution);
            baseTextureResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(baseTextureResolution), 16, 2048);
            if (m_Terrain.terrainData.baseMapResolution != baseTextureResolution)
                m_Terrain.terrainData.baseMapResolution = baseTextureResolution;
            GUILayout.EndHorizontal();

            // splatmap control texture
            GUILayout.BeginHorizontal();
            if (m_CurrentControlTextureResolution == 0)
                m_CurrentControlTextureResolution = m_Terrain.terrainData.alphamapResolution;
            m_CurrentControlTextureResolution = EditorGUILayout.IntField(EditorGUIUtility.TrTextContent("Control Texture Resolution", "Resolution of the \"splatmap\" that controls the blending of the different terrain textures."), m_CurrentControlTextureResolution);
            m_CurrentControlTextureResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(m_CurrentControlTextureResolution), 16, 2048);
            if (m_CurrentControlTextureResolution != m_Terrain.terrainData.alphamapResolution && GUILayout.Button("Resize", GUILayout.Width(128)))
                ResizeControlTexture(m_CurrentControlTextureResolution);

            GUILayout.EndHorizontal();

            // heightmap texture
            GUILayout.BeginHorizontal();
            if (m_CurrentHeightmapResolution == 0)
                m_CurrentHeightmapResolution = m_Terrain.terrainData.heightmapResolution;
            m_CurrentHeightmapResolution = EditorGUILayout.IntField(EditorGUIUtility.TrTextContent("Heightmap Resolution", "Pixel resolution of the terrain's heightmap (should be a power of two plus one, eg, 513 = 512 + 1)."), m_CurrentHeightmapResolution);
            const int kMinimumResolution = 33; // 33 is the minimum that GetAdjustedSize will allow
            const int kMaximumResolution = 4097; // if you want to change the maximum value, also change it in SetResolutionWizard
            m_CurrentHeightmapResolution = Mathf.Clamp(m_CurrentHeightmapResolution, kMinimumResolution, kMaximumResolution);
            m_CurrentHeightmapResolution = m_Terrain.terrainData.GetAdjustedSize(m_CurrentHeightmapResolution);
            if (m_CurrentHeightmapResolution != m_Terrain.terrainData.heightmapResolution && GUILayout.Button("Resize", GUILayout.Width(128)))
                ResizeHeightmap(m_CurrentHeightmapResolution);

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(styles.importRaw))
            {
                TerrainMenus.ImportRaw();
            }

            if (GUILayout.Button(styles.exportRaw))
            {
                TerrainMenus.ExportHeightmapRaw();
            }
            GUILayout.EndHorizontal();
        }

        public void ShowResolution()
        {
            GUILayout.Label("Mesh Resolutions", EditorStyles.boldLabel);

            float terrainWidth = m_Terrain.terrainData.size.x;
            float terrainHeight = m_Terrain.terrainData.size.y;
            float terrainLength = m_Terrain.terrainData.size.z;
            int detailResolution = m_Terrain.terrainData.detailResolution;
            int detailResolutionPerPatch = m_Terrain.terrainData.detailResolutionPerPatch;

            EditorGUI.BeginChangeCheck();

            const int kMaxTerrainSize = 100000;
            const int kMaxTerrainHeight = 10000;
            terrainWidth = EditorGUILayout.DelayedFloatField(EditorGUIUtility.TrTextContent("Terrain Width", "Size of the terrain object in its X axis (in world units)."), terrainWidth);
            if (terrainWidth <= 0) terrainWidth = 1;
            if (terrainWidth > kMaxTerrainSize) terrainWidth = kMaxTerrainSize;

            terrainLength = EditorGUILayout.DelayedFloatField(EditorGUIUtility.TrTextContent("Terrain Length", "Size of the terrain object in its Z axis (in world units)."), terrainLength);
            if (terrainLength <= 0) terrainLength = 1;
            if (terrainLength > kMaxTerrainSize) terrainLength = kMaxTerrainSize;

            terrainHeight = EditorGUILayout.DelayedFloatField(EditorGUIUtility.TrTextContent("Terrain Height", "Difference in Y coordinate between the lowest possible heightmap value and the highest (in world units)."), terrainHeight);
            if (terrainHeight <= 0) terrainHeight = 1;
            if (terrainHeight > kMaxTerrainHeight) terrainHeight = kMaxTerrainHeight;

            detailResolutionPerPatch = EditorGUILayout.DelayedIntField(EditorGUIUtility.TrTextContent("Detail Patch Count (N * N)", "The terrain tile will be divided into \"patch count\" * \"patch count\". Each patch is drawn in a single draw call."), detailResolutionPerPatch);
            detailResolutionPerPatch = Mathf.Clamp(detailResolutionPerPatch, 8, 128);

            detailResolution = EditorGUILayout.DelayedIntField(EditorGUIUtility.TrTextContent("Max Detail Placements / Patch", "Number of placements of details/grass per patch. Higher resolution gives smaller and more detailed patches."), detailResolution);
            detailResolution = Mathf.Clamp(detailResolution, 0, 4048);

            if (EditorGUI.EndChangeCheck())
            {
                var undoObjects = new List<UnityEngine.Object>();
                undoObjects.Add(m_Terrain.terrainData);
                undoObjects.AddRange(m_Terrain.terrainData.alphamapTextures);

                Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), "Set Resolution");

                m_Terrain.terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);

                if (m_Terrain.terrainData.detailResolution != detailResolution || detailResolutionPerPatch != m_Terrain.terrainData.detailResolutionPerPatch)
                    ResizeDetailResolution(m_Terrain.terrainData, detailResolution, detailResolutionPerPatch);

                m_Terrain.Flush();
            }
        }

        void ResizeDetailResolution(TerrainData terrainData, int resolution, int resolutionPerPatch)
        {
            if (resolution == terrainData.detailResolution)
            {
                var layers = new List<int[, ]>();
                for (int i = 0; i < terrainData.detailPrototypes.Length; i++)
                    layers.Add(terrainData.GetDetailLayer(0, 0, terrainData.detailWidth, terrainData.detailHeight, i));

                terrainData.SetDetailResolution(resolution, resolutionPerPatch);

                for (int i = 0; i < layers.Count; i++)
                    terrainData.SetDetailLayer(0, 0, i, layers[i]);
            }
            else
            {
                terrainData.SetDetailResolution(resolution, resolutionPerPatch);
            }
        }

        public void ShowUpgradeTreePrototypeScaleUI()
        {
            if (m_Terrain.terrainData != null && m_Terrain.terrainData.NeedUpgradeScaledTreePrototypes())
            {
                var msgContent = EditorGUIUtility.TempContent(
                    "Some of your prototypes have scaling values on the prefab. Since Unity 5.2 these scalings will be applied to terrain tree instances. Do you want to upgrade to this behaviour?",
                    EditorGUIUtility.GetHelpIcon(MessageType.Warning));
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(msgContent, EditorStyles.wordWrappedLabel);
                GUILayout.Space(3);
                if (GUILayout.Button("Upgrade", GUILayout.ExpandWidth(false)))
                {
                    m_Terrain.terrainData.UpgradeScaledTreePrototype();
                    TerrainMenus.RefreshPrototypes();
                }
                GUILayout.Space(3);
                GUILayout.EndVertical();
            }
        }

        public void ShowRefreshPrototypes()
        {
            if (GUILayout.Button(styles.refresh))
            {
                TerrainMenus.RefreshPrototypes();
            }
        }

        public void ShowMassPlaceTrees()
        {
            using (new EditorGUI.DisabledScope(TreePainter.selectedTree == -1))
            {
                if (GUILayout.Button(styles.massPlaceTrees))
                {
                    TerrainMenus.MassPlaceTrees();
                }
            }
        }

        public void InitializeLightingFields()
        {
            m_Lighting = new LightingSettingsInspector(serializedObject);
        }

        public void RenderLightingFields()
        {
            m_Lighting.RenderTerrainSettings();
        }

        void OnInspectorUpdate()
        {
            if (AssetPreview.HasAnyNewPreviewTexturesAvailable())
                Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Initialize();

            if (styles == null)
            {
                styles = new Styles();
            }

            if (!m_Terrain.terrainData)
            {
                GUI.enabled = false;
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Toolbar(-1, styles.toolIcons, styles.command);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("Terrain Asset Missing");
                m_Terrain.terrainData = EditorGUILayout.ObjectField("Assign:", m_Terrain.terrainData, typeof(TerrainData), false) as TerrainData;
                GUILayout.EndVertical();
                return;
            }


            if (Event.current.type == EventType.Layout)
                m_TerrainCollider = m_Terrain.gameObject.GetComponent<TerrainCollider>();

            if (m_TerrainCollider && m_TerrainCollider.terrainData != m_Terrain.terrainData)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(styles.mismatchedTerrainData, EditorStyles.wordWrappedLabel);
                GUILayout.Space(3);
                if (GUILayout.Button(styles.assign, GUILayout.ExpandWidth(false)))
                {
                    Undo.RecordObject(m_TerrainCollider, "Assign TerrainData");
                    m_TerrainCollider.terrainData = m_Terrain.terrainData;
                }
                GUILayout.Space(3);
                GUILayout.EndVertical();
            }

            // Show the master tool selector
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();          // flexible space on either end centers the toolbar
            GUI.changed = false;
            int tool = (int)selectedTool;
            int newlySelectedTool = GUILayout.Toolbar(tool, styles.toolIcons, styles.command);

            if (newlySelectedTool != tool)
            {
                selectedTool = (TerrainTool)newlySelectedTool;

                // Need to repaint other terrain inspectors as their previously selected tool is now deselected.
                InspectorWindow.RepaintAllInspectors();

                if (Toolbar.get != null)
                    Toolbar.get.Repaint();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            CheckKeys();

            if (tool != (int)TerrainTool.Paint)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                if (tool >= 0 && tool < styles.toolIcons.Length)
                {
                    GUILayout.Label(styles.toolNames[tool].text);
                    GUILayout.Label(styles.toolNames[tool].tooltip, EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    // TODO: Fix these somehow sensibly
                    GUILayout.Label("No tool selected");
                    GUILayout.Label("Please select a tool", EditorStyles.wordWrappedMiniLabel);
                }
                GUILayout.EndVertical();
            }

            switch ((TerrainTool)tool)
            {
                case TerrainTool.Paint:
                    ShowPaint();
                    break;
                case TerrainTool.PlaceTree:
                    ShowTrees();
                    break;
                case TerrainTool.PaintDetail:
                    ShowDetails();
                    break;
                case TerrainTool.TerrainSettings:
                    ShowSettings();
                    break;
            }
        }

        public bool Raycast(out Vector2 uv, out Vector3 pos)
        {
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            RaycastHit hit;
            if (m_Terrain.GetComponent<Collider>().Raycast(mouseRay, out hit, Mathf.Infinity))
            {
                uv = hit.textureCoord;
                pos = hit.point;
                return true;
            }

            uv = Vector2.zero;
            pos = Vector3.zero;
            return false;
        }

        public bool HasFrameBounds()
        {
            // When selecting terrains using Scene search, they may be framed right after selecting, when the editor
            // is not initialized yet. Just return empty bounds in that case.
            return m_Terrain != null;
        }

        public Bounds OnGetFrameBounds()
        {
            Vector2 uv;
            Vector3 pos;

            // It's possible that something other the scene view invoked OnGetFrameBounds (e.g. double clicking the terrain in the hierarchy)
            // In this case we can't do a raycast, because there is no active camera.
            if (Camera.current && m_Terrain.terrainData && Raycast(out uv, out pos))
            {
                // Avoid locking when framing terrain (case 527647)
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.viewIsLockedToObject = false;

                // Use height editing tool for calculating the bounds by default
                Bounds bounds = new Bounds();
                float brushSize = selectedTool == TerrainTool.PlaceTree ? TreePainter.brushSize : m_Size;
                Vector3 size;
                size.x = brushSize / m_Terrain.terrainData.heightmapWidth * m_Terrain.terrainData.size.x;
                size.z = brushSize / m_Terrain.terrainData.heightmapHeight * m_Terrain.terrainData.size.z;
                size.y = (size.x + size.z) * 0.5F;
                bounds.center = pos;
                bounds.size = size;
                // detail painting needs to be much closer
                if (selectedTool == TerrainTool.PaintDetail && m_Terrain.terrainData.detailWidth != 0)
                {
                    size.x = brushSize / m_Terrain.terrainData.detailWidth * m_Terrain.terrainData.size.x;
                    size.z = brushSize / m_Terrain.terrainData.detailHeight * m_Terrain.terrainData.size.z;
                    size.y = 0;
                    bounds.size = size;
                }

                return bounds;
            }
            else
            {
                // We don't return bounds from the collider, because apparently they are not immediately
                // updated after changing the position. So if terrain is repositioned, then bounds will
                // still be wrong until after some times PhysX updates them (it gets them through some
                // pruning interface and not directly from heightmap shape).
                //return m_Terrain.collider.bounds;

                Vector3 position = m_Terrain.transform.position;

                if (m_Terrain.terrainData == null)
                {
                    return new Bounds(position, Vector3.zero);
                }

                Vector3 size = m_Terrain.terrainData.size;

                float[,] heights = m_Terrain.terrainData.GetHeights(0, 0, m_Terrain.terrainData.heightmapWidth, m_Terrain.terrainData.heightmapHeight);

                float maxHeight = float.MinValue;
                for (int y = 0; y < m_Terrain.terrainData.heightmapHeight; y++)
                    for (int x = 0; x < m_Terrain.terrainData.heightmapWidth; x++)
                        maxHeight = Mathf.Max(maxHeight, heights[x, y]);

                size.y = maxHeight * size.y;

                return new Bounds(position + size * 0.5f, size);
            }
        }

        private bool IsModificationToolActive()
        {
            if (!m_Terrain)
                return false;
            TerrainTool st = selectedTool;
            if (st == TerrainTool.TerrainSettings)
                return false;
            if ((int)st < 0 || st >= TerrainTool.TerrainToolCount)
                return false;
            return true;
        }

        bool IsBrushPreviewVisible()
        {
            if (!IsModificationToolActive())
                return false;

            Vector3 pos;
            Vector2 uv;
            return Raycast(out uv, out pos);
        }

        private bool RaycastTerrain(Terrain terrain, Ray mouseRay, out RaycastHit hit, out Terrain hitTerrain)
        {
            if (terrain.GetComponent<Collider>().Raycast(mouseRay, out hit, Mathf.Infinity))
            {
                hitTerrain = terrain;
                return true;
            }

            hitTerrain = null;
            return false;
        }

        public void OnSceneGUICallback(SceneView sceneView)
        {
            Initialize();

            if (m_Terrain == null || m_Terrain.terrainData == null)
                return;

            Event e = Event.current;

            CheckKeys();

            if (selectedTool == TerrainTool.Paint)
            {
                ITerrainPaintTool activeTool = GetActiveTool();
                activeTool.OnSceneGUI(sceneView, m_Terrain, brushList.GetActiveBrush().texture, m_Strength, m_Size);
            }
            else if (selectedTool == TerrainTool.PaintDetail || selectedTool == TerrainTool.PlaceTree)
            {
                int brushSize = selectedTool == TerrainTool.PlaceTree ? (int)TreePainter.brushSize : m_Size;
                TerrainPaintUtilityEditor.ShowDefaultPreviewBrush(m_Terrain, brushList.GetActiveBrush().texture, m_Strength, brushSize, 0.0f);
            }


            int id = GUIUtility.GetControlID(s_TerrainEditorHash, FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    if (!IsModificationToolActive())
                        return;
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseMove:
                    if (IsBrushPreviewVisible())
                        HandleUtility.Repaint();
                    break;

                case EventType.MouseDown:
                case EventType.MouseDrag:
                {
                    // Don't do anything at all if someone else owns the hotControl. Fixes case 677541.
                    if (EditorGUIUtility.hotControl != 0 && EditorGUIUtility.hotControl != id)
                        return;

                    // Don't do anything on MouseDrag if we don't own the hotControl.
                    if (e.GetTypeForControl(id) == EventType.MouseDrag && EditorGUIUtility.hotControl != id)
                        return;

                    // If user is ALT-dragging, we want to return to main routine
                    if (Event.current.alt)
                        return;

                    // Allow painting with LMB only
                    if (e.button != 0)
                        return;

                    if (!IsModificationToolActive())
                        return;

                    if (HandleUtility.nearestControl != id)
                        return;

                    if (e.type == EventType.MouseDown)
                        EditorGUIUtility.hotControl = id;

                    Vector2 uv;
                    Vector3 pos;

                    Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                    Terrain hitTerrain;
                    RaycastHit hit;
                    if (m_Terrain.GetComponent<Collider>().Raycast(mouseRay, out hit, Mathf.Infinity))
                    {
                        uv = hit.textureCoord;
                        pos = hit.point;

                        if (selectedTool == TerrainTool.PlaceTree)
                        {
                            if (e.type == EventType.MouseDown)
                                Undo.RegisterCompleteObjectUndo(m_Terrain.terrainData, "Place Tree");

                            if (!Event.current.shift && !Event.current.control)
                                TreePainter.PlaceTrees(m_Terrain, uv.x, uv.y);
                            else
                                TreePainter.RemoveTrees(m_Terrain, uv.x, uv.y, Event.current.control);
                        }
                        else if (selectedTool == TerrainTool.PaintDetail)
                        {
                            if (e.type == EventType.MouseDown)
                                Undo.RegisterCompleteObjectUndo(m_Terrain.terrainData, "Detail Edit");
                            // Setup detail painter
                            DetailPainter detailPainter = new DetailPainter();
                            detailPainter.size = (int)Mathf.Max(1.0f, ((float)m_Size * ((float)m_Terrain.terrainData.detailResolution / m_Terrain.terrainData.size.x)));
                            detailPainter.targetStrength = m_DetailStrength * 16F;
                            detailPainter.opacity = m_DetailOpacity;
                            if (Event.current.shift || Event.current.control)
                                detailPainter.targetStrength *= -1;
                            detailPainter.clearSelectedOnly = Event.current.control;
                            detailPainter.terrainData = m_Terrain.terrainData;
                            detailPainter.brush = brushList.GetActiveBrush();
                            detailPainter.tool = selectedTool;
                            detailPainter.randomizeDetails = true;

                            detailPainter.Paint(uv.x, uv.y, m_SelectedDetail);
                        }
                        else
                        {
                            ITerrainPaintTool activeTool = GetActiveTool();

                            if (activeTool.DoesPaint())
                            {
                                // height map modification modes
                                m_Terrain.editorRenderFlags = TerrainRenderFlags.Heightmap;
                                m_Terrain.terrainData.SetBasemapDirty(false);
                                if (activeTool.Paint(m_Terrain, brushList.GetActiveBrush().texture, uv, m_Strength, m_Size))
                                    InspectorWindow.RepaintAllInspectors();
                            }
                        }

                        e.Use();
                    }
                    else
                    {
                        if ((m_Terrain.leftNeighbor && RaycastTerrain(m_Terrain.leftNeighbor, mouseRay, out hit, out hitTerrain))
                            || (m_Terrain.rightNeighbor && RaycastTerrain(m_Terrain.rightNeighbor, mouseRay, out hit, out hitTerrain))
                            || (m_Terrain.topNeighbor && RaycastTerrain(m_Terrain.topNeighbor, mouseRay, out hit, out hitTerrain))
                            || (m_Terrain.bottomNeighbor && RaycastTerrain(m_Terrain.bottomNeighbor, mouseRay, out hit, out hitTerrain)))
                        {
                            Selection.activeObject = hitTerrain;
                            Repaint();
                        }
                    }
                }
                break;

                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl != id)
                    {
                        return;
                    }

                    // Release hot control
                    GUIUtility.hotControl = 0;

                    if (!IsModificationToolActive())
                        return;

                    m_Terrain.editorRenderFlags = TerrainRenderFlags.All;
                    TerrainPaintUtility.FlushAllPaints();

                    e.Use();
                }
                break;
            }
        }
    }
} //namespace