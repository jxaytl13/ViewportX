#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

using Button = UnityEngine.UIElements.Button;

namespace PrefabPreviewer
{
    public class ViewportXWindow : EditorWindow
    {
        private const string MenuPath = "Window/T·L Nexus/ViewportX";
        private const string UxmlGuid = "da9ded1f94a3b464abaefea0b9f7c365";
        private const string UssGuid = "784c8e8d65cde5540a8e61ae40dfa81a";
        private const float PerspectiveFieldOfView = 60f;

        private enum PreviewContentType
        {
            None,
            Model,
            Particle,
            UGUI
        }

        private enum UiLanguage
        {
            English,
            Chinese
        }

        [Serializable]
        private sealed class PrefabPreviewerConfig
        {
            public int uiLanguage = (int)UiLanguage.Chinese;
            public bool gridVisible = true;
            public bool lightingEnabled = true;
        }

        private void UpdateViewButtonsState()
        {
            var prefabMode = _displayMode == PreviewDisplayMode.PrefabScene;
            var sceneControls = prefabMode && _contentType != PreviewContentType.UGUI;

            void SetButtonState(Button button, ViewAxis axis)
            {
                if (button == null)
                {
                    return;
                }

                if (sceneControls && _currentViewAxis == axis)
                {
                    button.AddToClassList("view-button--active");
                }
                else
                {
                    button.RemoveFromClassList("view-button--active");
                }
            }

            static void SetToggleButtonState(Button button, bool active)
            {
                if (button == null)
                {
                    return;
                }

                if (active)
                {
                    button.AddToClassList("view-button--active");
                }
                else
                {
                    button.RemoveFromClassList("view-button--active");
                }
            }

            SetButtonState(_viewXButton, ViewAxis.X);
            SetButtonState(_viewYButton, ViewAxis.Y);
            SetButtonState(_viewZButton, ViewAxis.Z);
            SetToggleButtonState(_resetButton, sceneControls && _currentViewAxis == ViewAxis.None);
            SetToggleButtonState(_projectionButton, sceneControls && _usePerspectiveProjection);

            SetToggleButtonState(_gridButton, sceneControls && _gridVisible);
            SetToggleButtonState(_lightingButton, sceneControls && _lightingEnabled);
            SetToggleButtonState(_autoRotateButton, sceneControls && _autoRotate);

            UpdateProjectionButtonLabel();
            UpdateToolbarIcons();
        }

        private void UpdateProjectionButtonLabel()
        {
            if (_projectionButton == null)
            {
                return;
            }

            if (_iconPerspectiveN != null && _iconPerspectiveS != null)
            {
                _projectionButton.text = string.Empty;
                return;
            }

            _projectionButton.text = _usePerspectiveProjection ? "P" : "O";
        }

        private void ToggleProjection()
        {
            _usePerspectiveProjection = !_usePerspectiveProjection;

            if (_displayMode == PreviewDisplayMode.PrefabScene && _contentType != PreviewContentType.UGUI)
            {
                if (!_usePerspectiveProjection)
                {
                    UpdateOrthographicSizeForBounds();
                }

                var cam = _previewUtility?.camera;
                if (cam != null)
                {
                    cam.orthographic = !_usePerspectiveProjection;
                    if (cam.orthographic)
                    {
                        cam.orthographicSize = Mathf.Max(_orthographicSize, 0.01f);
                    }
                }

                UpdateCameraClipPlanes();
                RequestPreviewRepaint();
            }

            UpdateViewButtonsState();
        }

        private void UpdateOrthographicSizeForBounds()
        {
            var aspect = _previewSize.y > 0f ? _previewSize.x / _previewSize.y : 1f;
            var horizontalExtent = Mathf.Max(_contentBounds.extents.x, _contentBounds.extents.z);
            var required = Mathf.Max(_contentBounds.extents.y, horizontalExtent / Mathf.Max(aspect, 0.01f));
            _orthographicSize = Mathf.Clamp(required * 1.05f, 0.01f, 20000f);
        }

        private void PersistViewAxis()
        {
            // View axis is intentionally not persisted; always default to Z on window creation.
        }

        private void LoadPersistedViewAxis()
        {
            _currentViewAxis = ViewAxis.Z;
        }

        private void ToggleGridVisibility(bool visible)
        {
            _gridVisible = visible;
            if (_config != null)
            {
                _config.gridVisible = visible;
                SaveConfig();
            }
            UpdateGridState();
            UpdateViewButtonsState();
            RequestPreviewRepaint();
        }

        private void ToggleLighting(bool enabled)
        {
            _lightingEnabled = enabled;
            if (_config != null)
            {
                _config.lightingEnabled = enabled;
                SaveConfig();
            }
            ApplyLightingState();
            UpdateViewButtonsState();
            RequestPreviewRepaint();
        }

        private void LoadPersistedGridVisibility()
        {
            _gridVisible = _config?.gridVisible ?? true;
        }

        private void LoadPersistedLightingEnabled()
        {
            _lightingEnabled = _config?.lightingEnabled ?? true;
        }

        private void ApplyLightingState()
        {
            if (_previewUtility == null)
            {
                return;
            }

            var lights = _previewUtility.lights;
            if (lights == null)
            {
                return;
            }

            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null)
                {
                    continue;
                }

                lights[i].enabled = _lightingEnabled;
            }
        }

        private void UpdateGridState()
        {
            if (_gridObject != null)
            {
                _gridObject.SetActive(false);
            }

            if (!_gridVisible || _displayMode != PreviewDisplayMode.PrefabScene || _previewUtility == null)
            {
                return;
            }

            EnsureGridResources();
            if (_gridObject == null)
            {
                return;
            }

            var extent = Mathf.Max(Mathf.Max(_contentBounds.extents.x, _contentBounds.extents.z), 1f);
            var spacing = Mathf.Clamp(extent / 8f, 0.1f, 10f);
            var requiredLineCount = Mathf.CeilToInt(extent / spacing);
            if (requiredLineCount > 200)
            {
                spacing = extent / 200f;
            }
            UpdateGridMesh(extent, spacing);

            var position = _contentBounds.center;
            position.y = _contentBounds.min.y;
            _gridObject.transform.position = position;
            _gridObject.transform.rotation = Quaternion.identity;
            _gridObject.transform.localScale = Vector3.one;
            _gridObject.SetActive(true);
        }

        private void EnsureGridResources()
        {
            if (_gridMesh == null)
            {
                _gridMesh = new Mesh
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "PrefabPreviewGridMesh"
                };
            }

            if (_gridMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                _gridMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _gridMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _gridMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _gridMaterial.SetInt("_Cull", (int)CullMode.Off);
                _gridMaterial.SetInt("_ZWrite", 0);
                _gridMaterial.color = new Color(1f, 1f, 1f, 0.35f);
            }

            if (_gridObject == null)
            {
                _gridObject = EditorUtility.CreateGameObjectWithHideFlags(
                    "_PrefabPreviewGrid",
                    HideFlags.HideAndDontSave,
                    typeof(MeshFilter),
                    typeof(MeshRenderer));

                var renderer = _gridObject.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.sharedMaterial = _gridMaterial;
            }

            if (_previewUtility != null && _gridObject.scene != _previewUtility.camera.scene)
            {
                SceneManager.MoveGameObjectToScene(_gridObject, _previewUtility.camera.scene);
            }

            var filter = _gridObject.GetComponent<MeshFilter>();
            filter.sharedMesh = _gridMesh;
        }

        private void UpdateGridMesh(float halfExtent, float spacing)
        {
            if (_gridMesh == null)
            {
                return;
            }

            var extent = Mathf.Ceil(halfExtent / spacing) * spacing;
            var lineCount = Mathf.Clamp(Mathf.CeilToInt(extent / spacing), 1, 200);

            _gridVertices.Clear();
            _gridColors.Clear();
            _gridIndices.Clear();
            var color = new Color(1f, 1f, 1f, 0.25f);

            void AddLine(Vector3 a, Vector3 b)
            {
                var index = _gridVertices.Count;
                _gridVertices.Add(a);
                _gridVertices.Add(b);
                _gridColors.Add(color);
                _gridColors.Add(color);
                _gridIndices.Add(index);
                _gridIndices.Add(index + 1);
            }

            for (int i = -lineCount; i <= lineCount; i++)
            {
                var offset = i * spacing;
                AddLine(new Vector3(-extent, 0f, offset), new Vector3(extent, 0f, offset));
                AddLine(new Vector3(offset, 0f, -extent), new Vector3(offset, 0f, extent));
            }

            _gridMesh.Clear();
            _gridMesh.SetVertices(_gridVertices);
            _gridMesh.SetColors(_gridColors);
            _gridMesh.SetIndices(_gridIndices, MeshTopology.Lines, 0);
        }

        private void DisposeGridResources()
        {
            if (_gridMesh != null)
            {
                DestroyImmediate(_gridMesh);
                _gridMesh = null;
            }

            if (_gridMaterial != null)
            {
                DestroyImmediate(_gridMaterial);
                _gridMaterial = null;
            }

            if (_gridObject != null)
            {
                DestroyImmediate(_gridObject);
                _gridObject = null;
            }
        }

        private enum PreviewDisplayMode
        {
            None,
            PrefabScene,
            Texture,
            AssetPreview,
            MaterialPreview
        }

        private enum ViewAxis
        {
            None = -1,
            X = 0,
            Y = 1,
            Z = 2
        }

        private VisualElement _previewHost;
        private PreviewSurfaceElement _previewSurface;
        private IMGUIContainer _materialPreviewContainer;
        private Material _materialPreviewTarget;
        private Editor _materialPreviewEditor;
        private Label _selectionLabel;
        private Label _statusLabel;
        private Button _autoRotateButton;
        private Button _gridButton;
        private Button _lightingButton;
        private Button _projectionButton;
        private Button _playButton;
        private Button _restartButton;
        private Button _frameButton;
        private Button _resetButton;
        private Button _refreshButton;
        private Button _settingsButton;
        private Button _viewXButton;
        private Button _viewYButton;
        private Button _viewZButton;
        private ViewportXSettingsOverlay _settingsOverlay;
        private ViewportXLocalization.Key _statusKey = ViewportXLocalization.Key.StatusNoAsset;
        private ViewportXLocalization.Key? _statusArgKey;
        private string _statusArgLiteral;
        private Vector2 _previewSize;
        private bool _isDragging;
        private bool _isPanning;
        private Vector2 _lastPointerPos;
        private Vector2 _lastPanPointerPos;

        private string _toolbarIconDirectory;
        private Texture2D _iconAutoRotateN;
        private Texture2D _iconAutoRotateS;
        private Texture2D _iconGridN;
        private Texture2D _iconGridS;
        private Texture2D _iconLightN;
        private Texture2D _iconLightS;
        private Texture2D _iconPlayN;
        private Texture2D _iconPlayS;
        private Texture2D _iconReplayN;
        private Texture2D _iconReplayS;
        private Texture2D _iconBreakN;
        private Texture2D _iconBreakS;
        private Texture2D _iconXyzN;
        private Texture2D _iconXyzS;
        private Texture2D _iconFocusN;
        private Texture2D _iconFocusS;
        private Texture2D _iconSettingN;
        private Texture2D _iconSettingS;
        private Texture2D _iconXN;
        private Texture2D _iconXS;
        private Texture2D _iconYN;
        private Texture2D _iconYS;
        private Texture2D _iconZN;
        private Texture2D _iconZS;
        private Texture2D _iconPerspectiveN;
        private Texture2D _iconPerspectiveS;

        private bool _restartHovered;
        private bool _refreshHovered;
        private bool _frameHovered;
        private bool _settingsHovered;

        private const string AboutVersion = "1.0.0";
        private const string AboutAuthor = "T·L";
        private const string LanguagePrefsKey = "PrefabPreviewer_UiLanguage";
        private UiLanguage _uiLanguage = UiLanguage.Chinese;

        private const string ConfigDirectoryPath = "Library/ViewportX";
        private const string ConfigFileName = "ViewportXConfig.json";
        private const string LegacyConfigDirectoryPath = "Library/PrefabPreviewer";
        private const string LegacyConfigFileName = "PrefabPreviewerConfig.json";
        private PrefabPreviewerConfig _config;

        private static string GetConfigFilePath()
        {
            return Path.Combine(ConfigDirectoryPath, ConfigFileName);
        }

        private static string GetLegacyConfigFilePath()
        {
            return Path.Combine(LegacyConfigDirectoryPath, LegacyConfigFileName);
        }

        private void LoadOrCreateConfig()
        {
            var path = GetConfigFilePath();
            var legacyPath = GetLegacyConfigFilePath();
            try
            {
                if (!Directory.Exists(ConfigDirectoryPath))
                {
                    Directory.CreateDirectory(ConfigDirectoryPath);
                }

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonUtility.FromJson<PrefabPreviewerConfig>(json);
                    _config = loaded ?? new PrefabPreviewerConfig();
                    return;
                }

                if (File.Exists(legacyPath))
                {
                    var json = File.ReadAllText(legacyPath);
                    var loaded = JsonUtility.FromJson<PrefabPreviewerConfig>(json);
                    _config = loaded ?? new PrefabPreviewerConfig();
                    SaveConfig();
                    return;
                }

                _config = new PrefabPreviewerConfig
                {
                    uiLanguage = EditorPrefs.GetInt(LanguagePrefsKey, (int)UiLanguage.Chinese),
                    gridVisible = EditorPrefs.GetInt(GridPrefsKey, 1) == 1,
                    lightingEnabled = EditorPrefs.GetInt(LightingPrefsKey, 1) == 1
                };
                SaveConfig();
            }
            catch
            {
                _config = new PrefabPreviewerConfig();
            }
        }

        private void SaveConfig()
        {
            if (_config == null)
            {
                return;
            }

            var path = GetConfigFilePath();
            try
            {
                if (!Directory.Exists(ConfigDirectoryPath))
                {
                    Directory.CreateDirectory(ConfigDirectoryPath);
                }

                var json = JsonUtility.ToJson(_config, true);
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        private PreviewRenderUtility _previewUtility;
        private GameObject _previewInstance;
        private GameObject _uiCanvasRoot;
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly List<ParticleSystem> _particleRootSystems = new();
        private PreviewContentType _contentType = PreviewContentType.None;
        private UnityEngine.Object _currentAsset;
        private Bounds _contentBounds;

        // UGUI 预览专用
        private Camera _uiPreviewCamera;
        private RenderTexture _uiRenderTexture;
        private GameObject _uiPreviewRoot;
        private Scene _uiPreviewScene;
        private Vector2 _uiCanvasSize = new(1920f, 1080f);
        private float _uiZoom = 1f;
        private Vector3 _uiOriginalScale = Vector3.one; // 预制体原始缩放
        private Vector2 _uiPanOffset = Vector2.zero;

        private Vector2 _orbitAngles = new(15f, -120f);
        private float _distance = 5f;
        private bool _autoRotate;
        private bool _particlePlaying = true;
        private double _lastUpdateTime;
        private PreviewDisplayMode _displayMode = PreviewDisplayMode.None;
        private Texture _texturePreview;
        private Rect _textureUv;
        private bool _textureHasCustomUv;
        private Texture _assetPreviewTexture;
        private UnityEngine.Object _assetPreviewSource;
        private double _assetPreviewNextPollTime;
        private int _assetPreviewSourceInstanceId;
        private const double AssetPreviewPollIntervalSeconds = 0.15;

        private bool _windowIsVisible = true;
        private Vector3 _panOffset = Vector3.zero;
        private ViewAxis _currentViewAxis = ViewAxis.Z;
        private bool _usePerspectiveProjection = true;
        private float _orthographicSize = 1.2f;
        private const string GridPrefsKey = "PrefabPreviewer_ShowGrid";
        private const string LightingPrefsKey = "PrefabPreviewer_Lighting";
        private bool _gridVisible = true;
        private bool _lightingEnabled = true;
        private GameObject _gridObject;
        private Mesh _gridMesh;
        private Material _gridMaterial;
        private readonly List<Vector3> _gridVertices = new();
        private readonly List<Color> _gridColors = new();
        private readonly List<int> _gridIndices = new();
        private bool _uiBuilt;
        private Label _previewMessageLabel;
        private string _previewMessageCached;
        private bool _previewMessageVisible;
        private bool _previewRepaintRequested;
        private double _lastPreviewRepaintTime;
        private RenderTexture _prefabRenderTexture;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow(typeof(ViewportXWindow), utility: false, title: "ViewportX", focus: true) as ViewportXWindow;
            if (window == null)
            {
                return;
            }
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            LoadOrCreateConfig();
            LoadPersistedLightingEnabled();
            CreatePreviewUtility();
            LoadPersistedGridVisibility();
            LoadPersistedLanguage();
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            _windowIsVisible = true;
            EnsureUiBuilt();
        }

        private void OnBecameVisible()
        {
            _windowIsVisible = true;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            RequestPreviewRepaint();
        }

        private void OnBecameInvisible()
        {
            _windowIsVisible = false;
        }

        private void LoadPersistedLanguage()
        {
            if (_config == null)
            {
                _uiLanguage = UiLanguage.Chinese;
                return;
            }

            _uiLanguage = (UiLanguage)_config.uiLanguage;
        }

        private void PersistLanguage()
        {
            if (_config == null)
            {
                return;
            }

            _config.uiLanguage = (int)_uiLanguage;
            SaveConfig();
        }

        private void ApplyToolbarTooltips()
        {
            var chinese = _uiLanguage == UiLanguage.Chinese;

            if (_playButton != null) _playButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipPlayPauseParticles, chinese);
            if (_restartButton != null) _restartButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipRestartParticles, chinese);
            if (_gridButton != null) _gridButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipGrid, chinese);
            if (_autoRotateButton != null) _autoRotateButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipAutoRotate, chinese);
            if (_lightingButton != null) _lightingButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipLighting, chinese);
            if (_projectionButton != null) _projectionButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipProjection, chinese);

            if (_refreshButton != null) _refreshButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipRefreshSelection, chinese);
            if (_resetButton != null) _resetButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipResetView, chinese);
            if (_viewXButton != null) _viewXButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipViewX, chinese);
            if (_viewYButton != null) _viewYButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipViewY, chinese);
            if (_viewZButton != null) _viewZButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipViewZ, chinese);
            if (_frameButton != null) _frameButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipFrame, chinese);
            if (_settingsButton != null) _settingsButton.tooltip = ViewportXLocalization.Get(ViewportXLocalization.Key.TooltipSettings, chinese);
        }

        private void ApplyLocalizedTexts()
        {
            ApplyToolbarTooltips();
            UpdatePlayButtonLabel();
            UpdateSelectionLabel();
            ApplyStatusFromState();
        }

        private void SetStatus(ViewportXLocalization.Key key)
        {
            _statusKey = key;
            _statusArgKey = null;
            _statusArgLiteral = null;
            ApplyStatusFromState();
        }

        private void SetStatus(ViewportXLocalization.Key key, ViewportXLocalization.Key argKey)
        {
            _statusKey = key;
            _statusArgKey = argKey;
            _statusArgLiteral = null;
            ApplyStatusFromState();
        }

        private void SetStatus(ViewportXLocalization.Key key, string argLiteral)
        {
            _statusKey = key;
            _statusArgKey = null;
            _statusArgLiteral = argLiteral;
            ApplyStatusFromState();
        }

        private void ApplyStatusFromState()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var chinese = _uiLanguage == UiLanguage.Chinese;
            if (_statusArgKey.HasValue)
            {
                var arg = ViewportXLocalization.Get(_statusArgKey.Value, chinese);
                _statusLabel.text = ViewportXLocalization.Format(_statusKey, chinese, arg);
                return;
            }

            if (!string.IsNullOrEmpty(_statusArgLiteral))
            {
                _statusLabel.text = ViewportXLocalization.Format(_statusKey, chinese, _statusArgLiteral);
                return;
            }

            _statusLabel.text = ViewportXLocalization.Get(_statusKey, chinese);
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
            CleanupPreview();
            DisposePreviewUtility();
            DisposeGridResources();
        }

        public void CreateGUI()
        {
            EnsureUiBuilt();
        }

        private void EnsureUiBuilt()
        {
            if (_uiBuilt && rootVisualElement.childCount > 0)
            {
                return;
            }

            _uiBuilt = true;
            BuildUi();
        }

        private void BuildUi()
        {
            rootVisualElement.Clear();

            var uxmlPath = ResolveAssetPath(UxmlGuid, "ViewportXWindow.uxml", "VisualTreeAsset");
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (tree == null)
            {
                rootVisualElement.Add(new Label(ViewportXLocalization.Format(ViewportXLocalization.Key.MissingUxml, _uiLanguage == UiLanguage.Chinese, uxmlPath)));
                return;
            }

            tree.CloneTree(rootVisualElement);
            var ussPath = ResolveAssetPath(UssGuid, "ViewportXWindow.uss", "StyleSheet");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (sheet != null)
            {
                rootVisualElement.styleSheets.Add(sheet);
            }
            else
            {
                rootVisualElement.Add(new Label(ViewportXLocalization.Format(ViewportXLocalization.Key.MissingUss, _uiLanguage == UiLanguage.Chinese, ussPath)));
            }

            _previewHost = rootVisualElement.Q<VisualElement>("preview-image");
            _previewHost?.Clear();
            _previewSurface = new PreviewSurfaceElement();
            _previewSurface.AddToClassList("preview-surface");
            if (_previewHost != null)
            {
                _previewHost.focusable = true;
                _previewHost.tabIndex = 0;
                _previewHost.pickingMode = PickingMode.Position;
                _previewHost.Add(_previewSurface);
            }

            _materialPreviewContainer = new IMGUIContainer(DrawMaterialPreviewGui);
            _materialPreviewContainer.pickingMode = PickingMode.Position;
            _materialPreviewContainer.style.position = Position.Absolute;
            _materialPreviewContainer.style.left = 0;
            _materialPreviewContainer.style.right = 0;
            _materialPreviewContainer.style.top = 0;
            _materialPreviewContainer.style.bottom = 0;
            _materialPreviewContainer.style.display = DisplayStyle.None;
            _previewHost?.Add(_materialPreviewContainer);

            _previewMessageLabel = new Label();
            _previewMessageLabel.AddToClassList("preview-message");
            _previewMessageLabel.pickingMode = PickingMode.Ignore;
            _previewMessageLabel.style.display = DisplayStyle.None;
            _previewHost?.Add(_previewMessageLabel);
            _previewMessageCached = null;
            _previewMessageVisible = false;

            _selectionLabel = rootVisualElement.Q<Label>("selection-label");
            _statusLabel = rootVisualElement.Q<Label>("status-label");
            _playButton = rootVisualElement.Q<Button>("btn-play");
            _restartButton = rootVisualElement.Q<Button>("btn-restart");
            _gridButton = rootVisualElement.Q<Button>("btn-grid");
            _autoRotateButton = rootVisualElement.Q<Button>("btn-auto-rotate");
            _lightingButton = rootVisualElement.Q<Button>("btn-lighting");
            _projectionButton = rootVisualElement.Q<Button>("btn-projection");
            _frameButton = rootVisualElement.Q<Button>("btn-frame");
            _resetButton = rootVisualElement.Q<Button>("btn-reset");
            _refreshButton = rootVisualElement.Q<Button>("btn-refresh");
            _settingsButton = rootVisualElement.Q<Button>("btn-settings");
            _viewXButton = rootVisualElement.Q<Button>("btn-view-x");
            _viewYButton = rootVisualElement.Q<Button>("btn-view-y");
            _viewZButton = rootVisualElement.Q<Button>("btn-view-z");

            _refreshButton?.RegisterCallback<ClickEvent>(_ => RefreshSelection());
            _settingsButton?.RegisterCallback<ClickEvent>(_ => ShowSettings());
            _frameButton?.RegisterCallback<ClickEvent>(_ => FrameContent(true));
            _resetButton?.RegisterCallback<ClickEvent>(_ => ResetView());
            _restartButton?.RegisterCallback<ClickEvent>(_ => RestartParticles());
            _playButton?.RegisterCallback<ClickEvent>(_ => ToggleParticlePlay());
            _gridButton?.RegisterCallback<ClickEvent>(_ => ToggleGridVisibility(!_gridVisible));
            _lightingButton?.RegisterCallback<ClickEvent>(_ => ToggleLighting(!_lightingEnabled));
            _autoRotateButton?.RegisterCallback<ClickEvent>(_ =>
            {
                _autoRotate = !_autoRotate;
                UpdateViewButtonsState();
                RequestPreviewRepaint();
            });
            _projectionButton?.RegisterCallback<ClickEvent>(_ => ToggleProjection());

            InitializeToolbarIcons(uxmlPath);

            _settingsOverlay = new ViewportXSettingsOverlay(
                AboutVersion,
                AboutAuthor,
                ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsToolIntroduction, true),
                ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsToolIntroduction, false),
                GetConfigFilePath,
                _uiLanguage == UiLanguage.Chinese,
                chinese =>
                {
                    _uiLanguage = chinese ? UiLanguage.Chinese : UiLanguage.English;
                    PersistLanguage();
                    ApplyLocalizedTexts();
                },
                (key, isChinese) => key switch
                {
                    ViewportXSettingsOverlay.TextKey.SettingsVersion => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsVersion, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsAuthor => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsAuthor, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsVisitAuthor => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsVisitAuthor, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsDocumentation => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsDocumentation, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsLanguage => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsLanguage, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsConfig => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsConfig, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsStoragePath => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsStoragePath, isChinese),
                    ViewportXSettingsOverlay.TextKey.SettingsTools => ViewportXLocalization.Get(ViewportXLocalization.Key.SettingsTools, isChinese),
                    ViewportXSettingsOverlay.TextKey.LanguageOptionEnglish => ViewportXLocalization.Get(ViewportXLocalization.Key.LanguageOptionEnglish, isChinese),
                    ViewportXSettingsOverlay.TextKey.LanguageOptionChinese => ViewportXLocalization.Get(ViewportXLocalization.Key.LanguageOptionChinese, isChinese),
                    _ => string.Empty
                });
            rootVisualElement.Add(_settingsOverlay.Root);

            ApplyLocalizedTexts();

            _viewXButton?.RegisterCallback<ClickEvent>(_ => SnapViewToAxis(ViewAxis.X));
            _viewYButton?.RegisterCallback<ClickEvent>(_ => SnapViewToAxis(ViewAxis.Y));
            _viewZButton?.RegisterCallback<ClickEvent>(_ => SnapViewToAxis(ViewAxis.Z));

            RegisterPointerEvents();

            _previewHost?.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                _previewSize = evt.newRect.size;
                RequestPreviewRepaint();
            });

            UpdateSelectionLabel();
            LoadPersistedViewAxis();
            UpdateGridState();
            UpdateViewButtonsState();
            RefreshSelection();
        }

        private void SetPreviewMessage(string message)
        {
            if (_previewMessageLabel == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                if (!_previewMessageVisible && string.IsNullOrEmpty(_previewMessageCached))
                {
                    return;
                }

                _previewMessageLabel.text = string.Empty;
                _previewMessageLabel.style.display = DisplayStyle.None;
                _previewMessageCached = null;
                _previewMessageVisible = false;
                return;
            }

            if (_previewMessageVisible && string.Equals(_previewMessageCached, message, StringComparison.Ordinal))
            {
                return;
            }

            _previewMessageLabel.text = message;
            _previewMessageLabel.style.display = DisplayStyle.Flex;
            _previewMessageCached = message;
            _previewMessageVisible = true;
        }

        private double GetPreviewIntervalSeconds(double now)
        {
            _ = now;
            return 1.0 / 30.0;
        }

        private void RequestPreviewRepaint()
        {
            _previewRepaintRequested = true;
        }

        private void ReleasePrefabRenderTexture()
        {
            if (_prefabRenderTexture == null)
            {
                return;
            }

            _prefabRenderTexture.Release();
            DestroyImmediate(_prefabRenderTexture);
            _prefabRenderTexture = null;
        }

        private RenderTexture EnsurePrefabRenderTexture(int width, int height)
        {
            width = Mathf.Max(width, 1);
            height = Mathf.Max(height, 1);

            if (_prefabRenderTexture != null && _prefabRenderTexture.width == width && _prefabRenderTexture.height == height)
            {
                return _prefabRenderTexture;
            }

            ReleasePrefabRenderTexture();

            _prefabRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "ViewportX_PrefabPreview",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            _prefabRenderTexture.Create();
            return _prefabRenderTexture;
        }

        private void InitializeToolbarIcons(string uxmlPath)
        {
            if (string.IsNullOrEmpty(uxmlPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(uxmlPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            _toolbarIconDirectory = directory.Replace('\\', '/');
            _restartHovered = false;
            _refreshHovered = false;
            _frameHovered = false;
            _settingsHovered = false;

            _iconAutoRotateN = LoadToolbarIcon("AutoRotate_N.png");
            _iconAutoRotateS = LoadToolbarIcon("AutoRotate_S.png");
            _iconGridN = LoadToolbarIcon("Grid_N.png");
            _iconGridS = LoadToolbarIcon("Grid_S.png");
            _iconLightN = LoadToolbarIcon("Light_N.png");
            _iconLightS = LoadToolbarIcon("Light_S.png");
            _iconPlayN = LoadToolbarIcon("Play_N.png");
            _iconPlayS = LoadToolbarIcon("Play_S.png");
            _iconReplayN = LoadToolbarIcon("RePlay_N.png");
            _iconReplayS = LoadToolbarIcon("RePlay_S.png");
            _iconBreakN = LoadToolbarIcon("Break_N.png");
            _iconBreakS = LoadToolbarIcon("Break_S.png");
            _iconXyzN = LoadToolbarIcon("XYZ_N.png");
            _iconXyzS = LoadToolbarIcon("XYZ_S.png");
            _iconFocusN = LoadToolbarIcon("Focus_N.png");
            _iconFocusS = LoadToolbarIcon("Focus_S.png");
            _iconSettingN = LoadToolbarIcon("Setting_N.png");
            _iconSettingS = LoadToolbarIcon("Setting_S.png");
            _iconXN = LoadToolbarIcon("X_N.png");
            _iconXS = LoadToolbarIcon("X_S.png");
            _iconYN = LoadToolbarIcon("Y_N.png");
            _iconYS = LoadToolbarIcon("Y_S.png");
            _iconZN = LoadToolbarIcon("Z_N.png");
            _iconZS = LoadToolbarIcon("Z_S.png");
            _iconPerspectiveN = LoadToolbarIcon("Perspective_N.png");
            _iconPerspectiveS = LoadToolbarIcon("Perspective_S.png");

            SetToolbarButtonBaseStyle(_playButton);
            SetToolbarButtonBaseStyle(_restartButton);
            SetToolbarButtonBaseStyle(_gridButton);
            SetToolbarButtonBaseStyle(_autoRotateButton);
            SetToolbarButtonBaseStyle(_lightingButton);
            SetToolbarButtonBaseStyle(_projectionButton);
            SetToolbarButtonBaseStyle(_refreshButton);
            SetToolbarButtonBaseStyle(_resetButton);
            SetToolbarButtonBaseStyle(_viewXButton);
            SetToolbarButtonBaseStyle(_viewYButton);
            SetToolbarButtonBaseStyle(_viewZButton);
            SetToolbarButtonBaseStyle(_frameButton);
            SetToolbarButtonBaseStyle(_settingsButton);

            RegisterHoverState(_restartButton, hovered => _restartHovered = hovered);
            RegisterHoverState(_refreshButton, hovered => _refreshHovered = hovered);
            RegisterHoverState(_frameButton, hovered => _frameHovered = hovered);
            RegisterHoverState(_settingsButton, hovered => _settingsHovered = hovered);

            UpdateToolbarIcons();
        }

        private Texture2D LoadToolbarIcon(string fileName)
        {
            if (string.IsNullOrEmpty(_toolbarIconDirectory) || string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{_toolbarIconDirectory}/{fileName}");
        }

        private static void SetToolbarButtonBaseStyle(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.text = string.Empty;
            button.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        }

        private void SetToolbarButtonIcon(Button button, Texture2D icon)
        {
            if (button == null || icon == null)
            {
                return;
            }

            button.style.backgroundImage = new StyleBackground(icon);
        }

        private void RegisterPressIconSwap(Button button, Texture2D normal, Texture2D pressed)
        {
            if (button == null || normal == null || pressed == null)
            {
                return;
            }

            SetToolbarButtonIcon(button, normal);

            button.RegisterCallback<PointerDownEvent>(_ => SetToolbarButtonIcon(button, pressed));
            button.RegisterCallback<PointerUpEvent>(_ => SetToolbarButtonIcon(button, normal));
            button.RegisterCallback<PointerLeaveEvent>(_ => SetToolbarButtonIcon(button, normal));
        }

        private void RegisterHoverState(Button button, Action<bool> setHovered)
        {
            if (button == null || setHovered == null)
            {
                return;
            }

            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                setHovered(true);
                UpdateToolbarIcons();
            });
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                setHovered(false);
                UpdateToolbarIcons();
            });
        }

        private void UpdateToolbarIcons()
        {
            var sceneControls = _displayMode == PreviewDisplayMode.PrefabScene && _contentType != PreviewContentType.UGUI;
            var particleControls = _displayMode == PreviewDisplayMode.PrefabScene && _particleSystems.Count > 0;

            SetToolbarButtonIcon(_gridButton, sceneControls && _gridVisible ? _iconGridS : _iconGridN);
            SetToolbarButtonIcon(_lightingButton, sceneControls && _lightingEnabled ? _iconLightS : _iconLightN);
            SetToolbarButtonIcon(_autoRotateButton, sceneControls && _autoRotate ? _iconAutoRotateS : _iconAutoRotateN);
            SetToolbarButtonIcon(_projectionButton, sceneControls && _usePerspectiveProjection ? _iconPerspectiveS : _iconPerspectiveN);

            var particlePlaying = particleControls && _particlePlaying;
            if (_playButton != null)
            {
                if (particlePlaying)
                {
                    _playButton.AddToClassList("view-button--active");
                }
                else
                {
                    _playButton.RemoveFromClassList("view-button--active");
                }
            }
            SetToolbarButtonIcon(_playButton, particlePlaying ? _iconPlayS : _iconPlayN);
            SetToolbarButtonIcon(_restartButton, _restartHovered ? _iconReplayS : _iconReplayN);
            SetToolbarButtonIcon(_refreshButton, _refreshHovered ? _iconBreakS : _iconBreakN);
            SetToolbarButtonIcon(_resetButton, sceneControls && _currentViewAxis == ViewAxis.None ? _iconXyzS : _iconXyzN);
            SetToolbarButtonIcon(_frameButton, _frameHovered ? _iconFocusS : _iconFocusN);
            SetToolbarButtonIcon(_settingsButton, _settingsHovered ? _iconSettingS : _iconSettingN);

            SetToolbarButtonIcon(_viewXButton, sceneControls && _currentViewAxis == ViewAxis.X ? _iconXS : _iconXN);
            SetToolbarButtonIcon(_viewYButton, sceneControls && _currentViewAxis == ViewAxis.Y ? _iconYS : _iconYN);
            SetToolbarButtonIcon(_viewZButton, sceneControls && _currentViewAxis == ViewAxis.Z ? _iconZS : _iconZN);
        }

        private void RegisterPointerEvents()
        {
            if (_previewHost == null)
            {
                return;
            }

            _previewHost.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_displayMode != PreviewDisplayMode.PrefabScene)
                {
                    return;
                }

                if (evt.button == 0)
                {
                    _isDragging = true;
                    _lastPointerPos = evt.position;
                    _previewHost.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
                else if (evt.button == 2)
                {
                    _isPanning = true;
                    _lastPanPointerPos = evt.position;
                    _previewHost.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            _previewHost.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isDragging)
                {
                    var currentPos = (Vector2)evt.position;
                    var delta = currentPos - _lastPointerPos;
                    _lastPointerPos = currentPos;
                    Orbit(delta);
                    evt.StopPropagation();
                }
                else if (_isPanning)
                {
                    var currentPos = (Vector2)evt.position;
                    var delta = currentPos - _lastPanPointerPos;
                    _lastPanPointerPos = currentPos;
                    Pan(delta);
                    evt.StopPropagation();
                }
            });

            _previewHost.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isDragging && evt.button == 0)
                {
                    _isDragging = false;
                    _previewHost.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
                else if (_isPanning && evt.button == 2)
                {
                    _isPanning = false;
                    _previewHost.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            _previewHost.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _isDragging = false;
                _isPanning = false;
            });

            _previewHost.RegisterCallback<WheelEvent>(evt =>
            {
                if (_displayMode != PreviewDisplayMode.PrefabScene)
                {
                    return;
                }

                var delta = Mathf.Clamp(evt.delta.y * 0.08f, -0.25f, 0.25f);
                if (Mathf.Approximately(delta, 0f))
                {
                    delta = evt.delta.y > 0f ? 0.15f : -0.15f;
                }

                Zoom(delta);
                evt.StopPropagation();
            });

            _previewHost.RegisterCallback<KeyDownEvent>(evt =>
            {
                switch (evt.keyCode)
                {
                    case KeyCode.F:
                        FrameContent(true);
                        evt.StopPropagation();
                        break;
                    case KeyCode.A:
                        ResetView();
                        evt.StopPropagation();
                        break;
                }
            });
        }

        private void ShowSettings()
        {
            if (_settingsOverlay == null)
            {
                return;
            }

            _settingsOverlay.Show();
        }

        private void CreatePreviewUtility()
        {
            if (_previewUtility != null)
            {
                return;
            }

            _previewUtility = TryCreatePreviewRenderUtility();
            if (_previewUtility == null)
            {
                return;
            }
            _previewUtility.camera.nearClipPlane = 0.01f;
            _previewUtility.camera.farClipPlane = 500f;
            _previewUtility.camera.fieldOfView = PerspectiveFieldOfView;
            _previewUtility.camera.clearFlags = CameraClearFlags.Color;
            _previewUtility.camera.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1f);
            _previewUtility.camera.transform.position = new Vector3(0, 0, -5f);
            _previewUtility.camera.transform.rotation = Quaternion.identity;

            if (_previewUtility.lights != null && _previewUtility.lights.Length > 0 && _previewUtility.lights[0] != null)
            {
                _previewUtility.lights[0].intensity = 1.3f;
                _previewUtility.lights[0].transform.rotation = Quaternion.Euler(50f, 50f, 0f);
            }

            if (_previewUtility.lights != null && _previewUtility.lights.Length > 1 && _previewUtility.lights[1] != null)
            {
                _previewUtility.lights[1].intensity = 0.8f;
                _previewUtility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            }
            ApplyLightingState();
        }

        private void DisposePreviewUtility()
        {
            _previewUtility?.Cleanup();
            _previewUtility = null;
        }

        private void RefreshSelection()
        {
            var asset = Selection.activeObject;
            LoadAsset(asset);
        }

        private void OnSelectionChanged()
        {
            UpdateSelectionLabel();
            if (!_windowIsVisible)
            {
                return;
            }

            RefreshSelection();
        }

        private void LoadAsset(UnityEngine.Object asset)
        {
            CleanupPreview();
            ResetPreviewTextures();
            _currentAsset = asset;
            _particlePlaying = true;
            _displayMode = PreviewDisplayMode.None;
            _contentType = PreviewContentType.None;
            _panOffset = Vector3.zero;

            if (asset == null)
            {
                SetStatus(ViewportXLocalization.Key.StatusNoAsset);
                ToggleParticleControlsVisibility(forceDisable: true);
                UpdateControlStates();
                RequestPreviewRepaint();
                return;
            }

            if (asset is GameObject prefab)
            {
                if (_previewUtility == null)
                {
                    CreatePreviewUtility();
                }

                if (_previewUtility == null)
                {
                    SetStatus(ViewportXLocalization.Key.StatusInitPreviewRendererFailed);
                    ToggleParticleControlsVisibility(forceDisable: true);
                    UpdateControlStates();
                    return;
                }

                _previewInstance = _previewUtility.InstantiatePrefabInScene(prefab);
                if (_previewInstance == null)
                {
                    SetStatus(ViewportXLocalization.Key.StatusInstantiatePrefabFailed);
                    ToggleParticleControlsVisibility(forceDisable: true);
                    UpdateControlStates();
                    RequestPreviewRepaint();
                    return;
                }

                _previewInstance.name = prefab.name;
                _contentType = DetermineContentType(_previewInstance);
                _displayMode = PreviewDisplayMode.PrefabScene;

                switch (_contentType)
                {
                    case PreviewContentType.UGUI:
                        SetupUiPreview();
                        break;
                    default:
                        SetupDefaultPreview();
                        break;
                }

                CacheParticleSystems();
                if (_contentType == PreviewContentType.Particle)
                {
                    WarmUpParticlesForBounds();
                }

                FrameContent(true);
                SetOrbitAnglesForAxis(_currentViewAxis);
                RestartParticles();
                UpdateStatusText();
                ToggleParticleControlsVisibility();
                UpdateControlStates();
                RequestPreviewRepaint();
                UpdateGridState();
                return;
            }

            ToggleParticleControlsVisibility(forceDisable: true);

            switch (asset)
            {
                case Sprite sprite:
                    SetupSpritePreview(sprite);
                    break;
                case Texture texture:
                    SetupTexturePreview(texture, ViewportXLocalization.Key.AssetTypeTexture);
                    break;
                case Material material:
                    SetupMaterialPreview(material, ViewportXLocalization.Key.AssetTypeMaterial);
                    break;
                case Mesh mesh:
                    SetupAssetPreview(mesh, ViewportXLocalization.Key.AssetTypeMesh);
                    break;
                default:
                    SetupAssetPreview(asset, asset.GetType().Name);
                    break;
            }

            UpdateControlStates();
            RequestPreviewRepaint();
        }

        private PreviewContentType DetermineContentType(GameObject root)
        {
            if (root == null)
            {
                return PreviewContentType.None;
            }

            // 检测 Canvas 或 RectTransform（UGUI 元素）
            if (root.GetComponentInChildren<Canvas>(true) != null)
            {
                return PreviewContentType.UGUI;
            }

            // 检测是否有 RectTransform（UI 预制体通常没有 Canvas，但有 RectTransform）
            if (root.GetComponent<RectTransform>() != null)
            {
                // 进一步检测是否有 UI 组件（Image、RawImage、Text 等）
                var graphicType = Type.GetType("UnityEngine.UI.Graphic, UnityEngine.UI");
                if (graphicType != null && root.GetComponentInChildren(graphicType, true) != null)
                {
                    return PreviewContentType.UGUI;
                }

                // 检测 TextMeshPro
                var tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
                if (tmpType != null && root.GetComponentInChildren(tmpType, true) != null)
                {
                    return PreviewContentType.UGUI;
                }

                // 有 RectTransform 但没有 UI 组件，也当作 UGUI 处理
                return PreviewContentType.UGUI;
            }

            if (root.GetComponentInChildren<ParticleSystem>(true) != null)
            {
                return PreviewContentType.Particle;
            }

            return PreviewContentType.Model;
        }

        private void SetupDefaultPreview()
        {
            _contentBounds = CalculateRendererBounds(_previewInstance);
            _uiCanvasRoot = null;
            _previewUtility.camera.orthographic = false;
        }

        /// <summary>
        /// 设置 UGUI 预览环境
        /// 使用独立的 Camera + Canvas + RenderTexture 实现真正的 UI 渲染
        /// </summary>
        private void SetupUiPreview()
        {
            CleanupUiPreview();

            // 固定分辨率 1920x1080
            _uiCanvasSize = new Vector2(1920f, 1080f);

            // 先创建 RenderTexture
            _uiRenderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            _uiRenderTexture.antiAliasing = 4;
            _uiRenderTexture.Create();

            // 创建独立的预览场景，避免在主场景 Scene 窗口中显示
            _uiPreviewScene = EditorSceneManager.NewPreviewScene();

            // 创建预览根对象
            _uiPreviewRoot = new GameObject("_UIPreviewRoot");
            _uiPreviewRoot.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(_uiPreviewRoot, _uiPreviewScene);

            // 创建预览相机
            var cameraGo = new GameObject("_UIPreviewCamera", typeof(Camera));
            cameraGo.hideFlags = HideFlags.HideAndDontSave;
            cameraGo.transform.SetParent(_uiPreviewRoot.transform, false);

            _uiPreviewCamera = cameraGo.GetComponent<Camera>();
            _uiPreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            _uiPreviewCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _uiPreviewCamera.orthographic = true;
            _uiPreviewCamera.orthographicSize = 5f;
            _uiPreviewCamera.nearClipPlane = 0.1f;
            _uiPreviewCamera.farClipPlane = 1000f;
            _uiPreviewCamera.depth = -100;
            _uiPreviewCamera.enabled = false; // 手动渲染
            _uiPreviewCamera.targetTexture = _uiRenderTexture;
            _uiPreviewCamera.cullingMask = (1 << 5) | GetLayerMaskForHierarchy(_previewInstance); // UI layer + prefab layers
            _uiPreviewCamera.scene = _uiPreviewScene;

            // 创建 Canvas
            var components = new List<Type> { typeof(RectTransform), typeof(Canvas) };

            var canvasScalerType = Type.GetType("UnityEngine.UI.CanvasScaler, UnityEngine.UI");
            if (canvasScalerType != null)
            {
                components.Add(canvasScalerType);
            }

            var graphicRaycasterType = Type.GetType("UnityEngine.UI.GraphicRaycaster, UnityEngine.UI");
            if (graphicRaycasterType != null)
            {
                components.Add(graphicRaycasterType);
            }

            var canvasGo = EditorUtility.CreateGameObjectWithHideFlags(
                "_UIPreviewCanvas",
                HideFlags.HideAndDontSave,
                components.ToArray());
            canvasGo.transform.SetParent(_uiPreviewRoot.transform, false);
            canvasGo.layer = 5; // UI layer
            _uiCanvasRoot = canvasGo;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _uiPreviewCamera;
            canvas.planeDistance = 100f;

            // 配置 CanvasScaler
            if (canvasScalerType != null)
            {
                var scaler = canvasGo.GetComponent(canvasScalerType);
                if (scaler != null)
                {
                    var uiScaleModeProperty = canvasScalerType.GetProperty("uiScaleMode", BindingFlags.Instance | BindingFlags.Public);
                    var referenceResolutionProperty = canvasScalerType.GetProperty("referenceResolution", BindingFlags.Instance | BindingFlags.Public);
                    var screenMatchModeProperty = canvasScalerType.GetProperty("screenMatchMode", BindingFlags.Instance | BindingFlags.Public);
                    var matchWidthOrHeightProperty = canvasScalerType.GetProperty("matchWidthOrHeight", BindingFlags.Instance | BindingFlags.Public);

                    // ScaleMode.ScaleWithScreenSize = 1
                    uiScaleModeProperty?.SetValue(scaler, 1);
                    referenceResolutionProperty?.SetValue(scaler, _uiCanvasSize);
                    // ScreenMatchMode.MatchWidthOrHeight = 0
                    screenMatchModeProperty?.SetValue(scaler, 0);
                    matchWidthOrHeightProperty?.SetValue(scaler, 0.5f);
                }
            }

            // 将预制体实例放入 Canvas
            if (_previewInstance != null)
            {
                // 设置所有子对象的 layer 为 UI
                _previewInstance.transform.SetParent(canvas.transform, false);

                // 重置位置到中心，避免预制体原始坐标导致的偏移
                // Keep prefab RectTransform values (no zeroing).
            }

            // 缓存粒子系统（UGUI 内可能包含粒子特效）
            CacheParticleSystems();

            // 初始化粒子（保持暂停，由窗口手动 Simulate 推进，避免 Unity 编辑器自动模拟导致变速/叠加）
            if (_particleSystems.Count > 0)
            {
                foreach (var ps in _particleRootSystems)
                {
                    if (ps != null)
                    {
                        ps.Simulate(0f, true, true, false);
                        ps.Pause(true);
                    }
                }
            }

            // 保存原始缩放，重置缩放系数和平移
            _uiOriginalScale = _previewInstance != null ? _previewInstance.transform.localScale : Vector3.one;
            _uiZoom = 1f;
            _uiPanOffset = Vector2.zero;

            // 设置内容边界
            _contentBounds = new Bounds(Vector3.zero, new Vector3(_uiCanvasSize.x, _uiCanvasSize.y, 1f));
        }

        /// <summary>
        /// 递归设置 GameObject 及其所有子对象的 layer
        /// </summary>
        /// <remarks>Get layer mask for hierarchy.</remarks>
        private static int GetLayerMaskForHierarchy(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            var mask = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }

                var layer = t.gameObject.layer;
                if (layer >= 0 && layer < 32)
                {
                    mask |= 1 << layer;
                }
            }

            return mask;
        }

        /// <summary>
        /// 清理 UGUI 预览资源
        /// </summary>
        private void CleanupUiPreview()
        {
            if (_uiRenderTexture != null)
            {
                _uiRenderTexture.Release();
                DestroyImmediate(_uiRenderTexture);
                _uiRenderTexture = null;
            }

            if (_uiPreviewRoot != null)
            {
                DestroyImmediate(_uiPreviewRoot);
                _uiPreviewRoot = null;
            }

            // 关闭预览场景
            if (_uiPreviewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_uiPreviewScene);
                _uiPreviewScene = default;
            }

            _uiPreviewCamera = null;
            _uiCanvasRoot = null;
        }

        private void CleanupUiRoot()
        {
            CleanupUiPreview();
        }

        private void CleanupPreview()
        {
            DestroyPreviewObject(ref _previewInstance);
            CleanupUiRoot();
            ReleasePrefabRenderTexture();
            CleanupMaterialPreview();
            _particleSystems.Clear();
            _particleRootSystems.Clear();
            _contentType = PreviewContentType.None;
            _displayMode = PreviewDisplayMode.None;
            _particlePlaying = true;
            UpdateGridState();
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;

            if (!_windowIsVisible)
            {
                return;
            }

            var interval = GetPreviewIntervalSeconds(now);
            if (_displayMode == PreviewDisplayMode.PrefabScene && _particleSystems.Count > 0 && _particlePlaying)
            {
                interval = 0;
            }
            var tickDue = (now - _lastUpdateTime) >= interval;
            var delta = tickDue ? (float)(now - _lastUpdateTime) : 0f;
            if (tickDue)
            {
                _lastUpdateTime = now;
            }

            if (tickDue)
            {
                switch (_displayMode)
                {
                    case PreviewDisplayMode.PrefabScene when _contentType == PreviewContentType.UGUI:
                        // UGUI 预览：检查是否有粒子系统需要更新
                        if (_particleSystems.Count > 0 && _particlePlaying)
                        {
                            AdvanceParticleSimulation(delta);

                            _previewRepaintRequested = true;
                        }
                        break;
                    case PreviewDisplayMode.PrefabScene when _previewUtility != null:
                        var needsRepaint = false;

                        if (_autoRotate && _contentType != PreviewContentType.UGUI)
                        {
                            _orbitAngles.y += delta * 15f;
                            needsRepaint = true;
                        }

                        if (_contentType == PreviewContentType.Particle && _particlePlaying)
                        {
                            AdvanceParticleSimulation(delta);

                            needsRepaint = true;
                        }

                        if (needsRepaint)
                        {
                            _previewRepaintRequested = true;
                        }
                        break;
                    case PreviewDisplayMode.AssetPreview:
                        if (UpdateAssetPreviewTexture())
                        {
                            _previewRepaintRequested = true;
                        }
                        break;
                    case PreviewDisplayMode.MaterialPreview:
                        if (_materialPreviewTarget != null && _materialPreviewEditor != null)
                        {
                            _previewRepaintRequested = true;
                        }
                        break;
                }
            }

            var frameElement = _previewSurface;
            if (_previewRepaintRequested && frameElement != null && (now - _lastPreviewRepaintTime) >= interval)
            {
                _previewRepaintRequested = false;
                _lastPreviewRepaintTime = now;
                UpdatePreviewFrame(frameElement.contentRect);
                frameElement.MarkDirtyRepaint();

                if (_displayMode == PreviewDisplayMode.MaterialPreview)
                {
                    _materialPreviewContainer?.MarkDirtyRepaint();
                }
            }
        }

        private void UpdatePreviewFrame(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(null);
                return;
            }

            switch (_displayMode)
            {
                case PreviewDisplayMode.PrefabScene:
                    HideMaterialPreview();
                    UpdatePrefabFrame(rect);
                    break;
                case PreviewDisplayMode.Texture:
                    HideMaterialPreview();
                    UpdateTextureFrame();
                    break;
                case PreviewDisplayMode.AssetPreview:
                    HideMaterialPreview();
                    UpdateAssetPreviewFrame();
                    break;
                case PreviewDisplayMode.MaterialPreview:
                    UpdateMaterialPreviewFrame(rect);
                    break;
                default:
                    HideMaterialPreview();
                    _previewSurface?.ClearFrame();
                    SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.HintSelectPreviewableAsset, _uiLanguage == UiLanguage.Chinese));
                    break;
            }
        }

        private void UpdateMaterialPreviewFrame(Rect rect)
        {
            _previewSurface?.ClearFrame();

            if (_materialPreviewContainer == null)
            {
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.PreviewRendererUnavailable, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            if (_materialPreviewTarget == null || _materialPreviewEditor == null || rect.width <= 0f || rect.height <= 0f)
            {
                _materialPreviewContainer.style.display = DisplayStyle.None;
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.HintGeneratingPreview, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            _materialPreviewContainer.style.display = DisplayStyle.Flex;
            SetPreviewMessage(null);
        }

        private void UpdatePrefabFrame(Rect rect)
        {
            if (_previewInstance == null)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.HintSelectPrefab, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            if (_contentType == PreviewContentType.UGUI)
            {
                UpdateUguiFrame();
                return;
            }

            if (_previewUtility == null)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.PreviewRendererUnavailable, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            var cam = _previewUtility.camera;
            if (cam == null)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.PreviewRendererUnavailable, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            ConfigureCamera();
            UpdatePreviewLightingForCamera();

            var rt = EnsurePrefabRenderTexture(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));

            var previousTargetTexture = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = rt.height > 0 ? rt.width / (float)rt.height : 1f;
            cam.pixelRect = new Rect(0f, 0f, rt.width, rt.height);
            cam.Render();
            cam.targetTexture = previousTargetTexture;

            _previewSurface?.SetFrame(rt, new Rect(0f, 0f, 1f, 1f), PreviewSurfaceElement.FitMode.ScaleToFit);
            SetPreviewMessage(null);
        }

        private void UpdateUguiFrame()
        {
            if (_uiPreviewCamera == null || _uiCanvasRoot == null || _uiRenderTexture == null)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.PreviewRendererUnavailable, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            Canvas.ForceUpdateCanvases();
            _uiPreviewCamera.Render();

            _previewSurface?.SetFrame(_uiRenderTexture, new Rect(0f, 0f, 1f, 1f), PreviewSurfaceElement.FitMode.ScaleToFit);
            SetPreviewMessage(null);
        }

        private void UpdateTextureFrame()
        {
            if (_texturePreview == null)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.HintTextureUnavailable, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            var uv = _textureHasCustomUv ? _textureUv : new Rect(0f, 0f, 1f, 1f);
            _previewSurface?.SetFrame(_texturePreview, uv, PreviewSurfaceElement.FitMode.ScaleToFit);
            SetPreviewMessage(null);
        }

        private void UpdateAssetPreviewFrame()
        {
            if (_assetPreviewTexture == null)
            {
                _previewSurface?.ClearFrame();
                SetPreviewMessage(ViewportXLocalization.Get(ViewportXLocalization.Key.HintGeneratingPreview, _uiLanguage == UiLanguage.Chinese));
                return;
            }

            _previewSurface?.SetFrame(_assetPreviewTexture, new Rect(0f, 0f, 1f, 1f), PreviewSurfaceElement.FitMode.ScaleToFit);
            SetPreviewMessage(null);
        }

        private void ConfigureCamera()
        {
            if (_contentType == PreviewContentType.UGUI)
            {
                _previewUtility.camera.orthographic = true;
                _previewUtility.camera.orthographicSize = 1.2f;
                _previewUtility.camera.transform.position = new Vector3(0f, 0f, -4f) + _panOffset;
                _previewUtility.camera.transform.rotation = Quaternion.identity;
                return;
            }

            var target = _contentBounds.center + _panOffset;
            var rotation = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
            var direction = rotation * Vector3.forward;
            var distance = Mathf.Max(_distance, 0.1f);
            var position = target - direction * distance;

            _previewUtility.camera.orthographic = !_usePerspectiveProjection;
            if (_previewUtility.camera.orthographic)
            {
                _previewUtility.camera.orthographicSize = Mathf.Max(_orthographicSize, 0.01f);
            }
            else
            {
                _previewUtility.camera.fieldOfView = PerspectiveFieldOfView;
            }
            _previewUtility.camera.transform.position = position;
            _previewUtility.camera.transform.LookAt(target);
        }

        private void UpdatePreviewLightingForCamera()
        {
            if (!_lightingEnabled || _previewUtility == null)
            {
                return;
            }

            var cam = _previewUtility.camera;
            if (cam == null)
            {
                return;
            }

            var lights = _previewUtility.lights;
            if (lights == null || lights.Length == 0)
            {
                return;
            }

            var cameraRotation = cam.transform.rotation;

            if (lights[0] != null)
            {
                lights[0].transform.rotation = cameraRotation * Quaternion.Euler(35f, 35f, 0f);
            }

            if (lights.Length > 1 && lights[1] != null)
            {
                lights[1].transform.rotation = cameraRotation * Quaternion.Euler(-20f, 200f, 0f);
            }
        }

        private void FrameContent(bool recenter = false)
        {
            if (_previewInstance == null)
            {
                return;
            }

            // UGUI 使用独立的帧定位逻辑
            if (_contentType == PreviewContentType.UGUI)
            {
                if (recenter)
                {
                    _uiZoom = 1f;
                    _uiPanOffset = Vector2.zero;
                }
                RequestPreviewRepaint();
                return;
            }

            _contentBounds = CalculateRendererBounds(_previewInstance);

            if (recenter)
            {
                _panOffset = Vector3.zero;
            }

            if (!_usePerspectiveProjection)
            {
                UpdateOrthographicSizeForBounds();
                UpdateCameraClipPlanes();
                RequestPreviewRepaint();
                return;
            }

            var radius = Mathf.Max(_contentBounds.extents.magnitude, 0.001f);
            var cam = _previewUtility?.camera;
            if (cam != null)
            {
                var verticalFovRad = Mathf.Max(0.1f, cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
                var aspect = _previewSize.y > 0f ? _previewSize.x / _previewSize.y : 1f;
                var horizontalFovRad = Mathf.Max(0.1f, Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, aspect) * Mathf.Deg2Rad * 0.5f);

                var distanceVertical = radius / Mathf.Sin(verticalFovRad);
                var distanceHorizontal = radius / Mathf.Sin(horizontalFovRad);
                var targetDistance = Mathf.Max(distanceVertical, distanceHorizontal) * 1.05f;
                _distance = Mathf.Clamp(targetDistance, 0.05f, 20000f);
            }
            else
            {
                _distance = Mathf.Clamp(radius * 2.2f, 0.05f, 20000f);
            }
            UpdateCameraClipPlanes();
            RequestPreviewRepaint();
        }

        private void ResetView()
        {
            _orbitAngles = new Vector2(15f, -120f);
            _currentViewAxis = ViewAxis.None;
            PersistViewAxis();
            UpdateViewButtonsState();
            FrameContent(true);
        }

        private void Orbit(Vector2 delta)
        {
            if (_displayMode != PreviewDisplayMode.PrefabScene || _contentType == PreviewContentType.UGUI)
            {
                return;
            }

            if (_currentViewAxis != ViewAxis.None)
            {
                _currentViewAxis = ViewAxis.None;
                PersistViewAxis();
                UpdateViewButtonsState();
            }

            _orbitAngles.x = Mathf.Clamp(_orbitAngles.x + delta.y * 0.2f, -80f, 80f);
            _orbitAngles.y += delta.x * 0.2f;
            RequestPreviewRepaint();
        }

        private void Zoom(float delta)
        {
            if (_displayMode != PreviewDisplayMode.PrefabScene)
            {
                return;
            }

            if (_contentType == PreviewContentType.UGUI)
            {
                // UGUI 通过调整预制体实例的 localScale 实现缩放
                var factor = 1f - delta;
                _uiZoom *= factor;
                _uiZoom = Mathf.Clamp(_uiZoom, 0.1f, 5f);
                ApplyUiZoom();
            }
            else
            {
                if (!_usePerspectiveProjection)
                {
                    _orthographicSize *= 1f + delta;
                    _orthographicSize = Mathf.Clamp(_orthographicSize, 0.01f, 20000f);
                }
                else
                {
                    _distance *= 1f + delta;
                    _distance = Mathf.Clamp(_distance, 0.05f, 20000f);
                }
            }

            RequestPreviewRepaint();
            UpdateCameraClipPlanes();
        }

        /// <summary>
        /// 应用 UI 缩放到预制体实例
        /// </summary>
        private void ApplyUiZoom()
        {
            if (_previewInstance == null) return;

            // 使用乘法：原始缩放 * 缩放系数
            _previewInstance.transform.localScale = _uiOriginalScale * _uiZoom;
        }

        private void CacheParticleSystems()
        {
            _particleSystems.Clear();
            _particleRootSystems.Clear();
            if (_previewInstance == null)
            {
                return;
            }

            _previewInstance.GetComponentsInChildren(_particleSystems);

            foreach (var ps in _particleSystems)
            {
                if (ps == null)
                {
                    continue;
                }

                var root = GetParticleSystemRoot(ps);
                if (root != null && !_particleRootSystems.Contains(root))
                {
                    _particleRootSystems.Add(root);
                }
            }
        }

        private static ParticleSystem GetParticleSystemRoot(ParticleSystem ps)
        {
            if (ps == null)
            {
                return null;
            }

            var current = ps.transform;
            while (current.parent && current.parent.gameObject.GetComponent<ParticleSystem>() != null)
            {
                current = current.parent;
            }

            return current.gameObject.GetComponent<ParticleSystem>();
        }

        private void AdvanceParticleSimulation(float delta)
        {
            if (_particleRootSystems.Count == 0 || delta <= 0f)
            {
                return;
            }

            foreach (var ps in _particleRootSystems)
            {
                if (ps == null)
                {
                    continue;
                }

                ps.Simulate(delta, true, false, false);
            }
        }

        private void RestartParticles()
        {
            if (_displayMode != PreviewDisplayMode.PrefabScene || _particleSystems.Count == 0)
            {
                return;
            }

            foreach (var ps in _particleRootSystems)
            {
                if (ps == null)
                {
                    continue;
                }

                ps.Simulate(0f, true, true, false);
                ps.Pause(true);
            }

            _particlePlaying = true;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            UpdatePlayButtonLabel();
            RequestPreviewRepaint();
        }

        private void WarmUpParticlesForBounds(float totalTime = 0.8f, int steps = 6)
        {
            if (_particleSystems.Count == 0)
            {
                return;
            }

            foreach (var ps in _particleRootSystems)
            {
                if (ps == null)
                {
                    continue;
                }

                ps.Simulate(0f, true, true, false);
                ps.Pause(true);
            }

            var step = steps > 0 ? Mathf.Max(totalTime / steps, 0.01f) : totalTime;
            if (step <= 0f)
            {
                step = 0.05f;
            }

            for (var i = 0; i < steps; i++)
            {
                foreach (var ps in _particleRootSystems)
                {
                    if (ps == null)
                    {
                        continue;
                    }

                    ps.Simulate(step, true, false, false);
                }
            }

            foreach (var ps in _particleRootSystems)
            {
                if (ps != null)
                {
                    ps.Pause(true);
                }
            }
        }

        private void ToggleParticlePlay()
        {
            if (_displayMode != PreviewDisplayMode.PrefabScene || _particleSystems.Count == 0)
            {
                return;
            }

            _particlePlaying = !_particlePlaying;
            if (_particlePlaying)
            {
                _lastUpdateTime = EditorApplication.timeSinceStartup;
            }

            foreach (var ps in _particleRootSystems)
            {
                if (ps != null)
                {
                    ps.Pause(true);
                }
            }

            UpdatePlayButtonLabel();
            RequestPreviewRepaint();
        }

        private void UpdatePlayButtonLabel()
        {
            if (_playButton == null)
            {
                return;
            }

            _playButton.text = string.Empty;
            UpdateToolbarIcons();
        }

        private Bounds CalculateRendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.one);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return new Bounds(bounds.center, bounds.size);
        }

        private void UpdateSelectionLabel()
        {
            if (_selectionLabel == null)
            {
                return;
            }

            var chinese = _uiLanguage == UiLanguage.Chinese;
            var obj = Selection.activeObject;
            _selectionLabel.text = obj == null
                ? ViewportXLocalization.Get(ViewportXLocalization.Key.SelectionNone, chinese)
                : ViewportXLocalization.Format(ViewportXLocalization.Key.SelectionSelected, chinese, obj.name);
        }

        private void SnapViewToAxis(ViewAxis axis)
        {
            if (_displayMode != PreviewDisplayMode.PrefabScene || _previewUtility == null)
            {
                _currentViewAxis = axis;
                PersistViewAxis();
                UpdateViewButtonsState();
                return;
            }

            _autoRotate = false;
            UpdateViewButtonsState();

            _panOffset = Vector3.zero;
            SetOrbitAnglesForAxis(axis);

            _currentViewAxis = axis;
            PersistViewAxis();
            UpdateViewButtonsState();
            RequestPreviewRepaint();
        }

        private void SetOrbitAnglesForAxis(ViewAxis axis)
        {
            switch (axis)
            {
                case ViewAxis.X:
                    _orbitAngles = new Vector2(0f, -90f);
                    break;
                case ViewAxis.Y:
                    _orbitAngles = new Vector2(-90f, 0f);
                    break;
                case ViewAxis.Z:
                    _orbitAngles = new Vector2(0f, 0f);
                    break;
            }
        }

        private void Pan(Vector2 screenDelta)
        {
            if (_displayMode != PreviewDisplayMode.PrefabScene || _previewSize.x <= 0f || _previewSize.y <= 0f)
            {
                return;
            }

            if (_contentType == PreviewContentType.UGUI)
            {
                // UGUI 通过移动预制体实例的 anchoredPosition 实现平移
                if (_previewInstance == null) return;

                var instanceRect = _previewInstance.GetComponent<RectTransform>();
                if (instanceRect != null)
                {
                    // 计算预览区域与 Canvas 的比例
                    // 预览区域保持 16:9 比例
                    var sourceAspect = 1920f / 1080f;
                    var rectAspect = _previewSize.x / _previewSize.y;

                    float drawWidth, drawHeight;
                    if (rectAspect > sourceAspect)
                    {
                        drawHeight = _previewSize.y;
                        drawWidth = drawHeight * sourceAspect;
                    }
                    else
                    {
                        drawWidth = _previewSize.x;
                        drawHeight = drawWidth / sourceAspect;
                    }

                    // 屏幕像素到 Canvas 坐标的转换
                    var scaleX = _uiCanvasSize.x / drawWidth;
                    var scaleY = _uiCanvasSize.y / drawHeight;

                    // 应用平移（localScale 不影响 anchoredPosition 的效果）
                    var deltaX = screenDelta.x * scaleX;
                    var deltaY = -screenDelta.y * scaleY;

                    instanceRect.anchoredPosition += new Vector2(deltaX, deltaY);
                }
                RequestPreviewRepaint();
                return;
            }

            if (_previewUtility == null)
            {
                return;
            }

            var cam = _previewUtility.camera;
            if (cam == null)
            {
                return;
            }

            var distance = Mathf.Max(_distance, 0.1f);
            var viewHeight = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            if (cam.orthographic)
            {
                viewHeight = cam.orthographicSize * 2f;
            }

            var viewWidth = viewHeight * cam.aspect;
            var right = cam.transform.right;
            var up = cam.transform.up;

            var offset = (-screenDelta.x / _previewSize.x) * viewWidth * right
                         + (screenDelta.y / _previewSize.y) * viewHeight * up;
            _panOffset += offset;

            RequestPreviewRepaint();
        }

        private void SetupSpritePreview(Sprite sprite)
        {
            if (sprite == null)
            {
                SetupTexturePreview(null, ViewportXLocalization.Key.AssetTypeSprite);
                return;
            }

            SetupTexturePreview(sprite.texture, ViewportXLocalization.Key.AssetTypeSprite);
            if (sprite.texture != null)
            {
                var tex = sprite.texture;
                var texRect = sprite.textureRect;
                _textureUv = new Rect(
                    texRect.x / tex.width,
                    texRect.y / tex.height,
                    texRect.width / tex.width,
                    texRect.height / tex.height);
                _textureHasCustomUv = true;
            }
        }

        private void SetupTexturePreview(Texture texture, ViewportXLocalization.Key typeKey)
        {
            HideMaterialPreview();
            _displayMode = PreviewDisplayMode.Texture;
            _texturePreview = texture;
            _textureHasCustomUv = false;
            _textureUv = new Rect(0f, 0f, 1f, 1f);

            _statusArgKey = typeKey;
            _statusArgLiteral = null;
            _statusKey = texture == null
                ? ViewportXLocalization.Key.StatusTypeUnavailable
                : ViewportXLocalization.Key.StatusTypeName;
            ApplyStatusFromState();
        }

        private void SetupTexturePreview(Texture texture, string typeName)
        {
            HideMaterialPreview();
            _displayMode = PreviewDisplayMode.Texture;
            _texturePreview = texture;
            _textureHasCustomUv = false;
            _textureUv = new Rect(0f, 0f, 1f, 1f);

            _statusArgKey = null;
            _statusArgLiteral = typeName;
            _statusKey = texture == null
                ? ViewportXLocalization.Key.StatusTypeUnavailable
                : ViewportXLocalization.Key.StatusTypeName;
            ApplyStatusFromState();
        }

        private void SetupAssetPreview(UnityEngine.Object target, ViewportXLocalization.Key labelKey)
        {
            HideMaterialPreview();
            _displayMode = PreviewDisplayMode.AssetPreview;
            _assetPreviewSource = target;
            _assetPreviewTexture = AssetPreview.GetAssetPreview(target) ?? AssetPreview.GetMiniThumbnail(target);
            _assetPreviewSourceInstanceId = target != null ? target.GetInstanceID() : 0;
            _assetPreviewNextPollTime = 0;

            _statusKey = ViewportXLocalization.Key.StatusTypeName;
            _statusArgKey = labelKey;
            _statusArgLiteral = null;
            ApplyStatusFromState();
        }

        private void SetupAssetPreview(UnityEngine.Object target, string label)
        {
            HideMaterialPreview();
            _displayMode = PreviewDisplayMode.AssetPreview;
            _assetPreviewSource = target;
            _assetPreviewTexture = AssetPreview.GetAssetPreview(target) ?? AssetPreview.GetMiniThumbnail(target);
            _assetPreviewSourceInstanceId = target != null ? target.GetInstanceID() : 0;
            _assetPreviewNextPollTime = 0;

            _statusKey = ViewportXLocalization.Key.StatusTypeName;
            _statusArgKey = null;
            _statusArgLiteral = label;
            ApplyStatusFromState();
        }

        private void ResetPreviewTextures()
        {
            _texturePreview = null;
            _textureHasCustomUv = false;
            _textureUv = new Rect(0f, 0f, 1f, 1f);
            _assetPreviewTexture = null;
            _assetPreviewSource = null;
            _assetPreviewSourceInstanceId = 0;
            _assetPreviewNextPollTime = 0;
        }

        private void SetupMaterialPreview(Material material, ViewportXLocalization.Key labelKey)
        {
            CleanupMaterialPreview();

            _displayMode = PreviewDisplayMode.MaterialPreview;
            _materialPreviewTarget = material;

            if (material != null)
            {
                Editor.CreateCachedEditor(material, typeof(MaterialEditor), ref _materialPreviewEditor);
            }

            _statusKey = ViewportXLocalization.Key.StatusTypeName;
            _statusArgKey = labelKey;
            _statusArgLiteral = null;
            ApplyStatusFromState();
        }

        private void CleanupMaterialPreview()
        {
            _materialPreviewTarget = null;
            if (_materialPreviewEditor != null)
            {
                DestroyImmediate(_materialPreviewEditor);
                _materialPreviewEditor = null;
            }

            HideMaterialPreview();
        }

        private void HideMaterialPreview()
        {
            if (_materialPreviewContainer != null)
            {
                _materialPreviewContainer.style.display = DisplayStyle.None;
            }
        }

        private void DrawMaterialPreviewGui()
        {
            if (_displayMode != PreviewDisplayMode.MaterialPreview)
            {
                return;
            }

            if (_materialPreviewContainer == null || _materialPreviewTarget == null || _materialPreviewEditor == null)
            {
                return;
            }

            var rect = _materialPreviewContainer.contentRect;
            rect.position = Vector2.zero;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            _materialPreviewEditor.OnInteractivePreviewGUI(rect, GUIStyle.none);

            if (_materialPreviewEditor.RequiresConstantRepaint())
            {
                RequestPreviewRepaint();
            }
        }

        private void SetStatusText(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }

        private bool UpdateAssetPreviewTexture()
        {
            if (_assetPreviewSource == null)
            {
                return false;
            }

            if (_assetPreviewSourceInstanceId == 0)
            {
                _assetPreviewSourceInstanceId = _assetPreviewSource.GetInstanceID();
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _assetPreviewNextPollTime)
            {
                return AssetPreview.IsLoadingAssetPreview(_assetPreviewSourceInstanceId);
            }

            _assetPreviewNextPollTime = now + AssetPreviewPollIntervalSeconds;

            var preview = AssetPreview.GetAssetPreview(_assetPreviewSource);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(_assetPreviewSource);
            }

            if (preview == null)
            {
                return AssetPreview.IsLoadingAssetPreview(_assetPreviewSourceInstanceId);
            }

            if (_assetPreviewTexture == preview)
            {
                return false;
            }

            _assetPreviewTexture = preview;
            return true;
        }

        private void UpdateControlStates()
        {
            var prefabMode = _displayMode == PreviewDisplayMode.PrefabScene;
            var sceneControls = prefabMode && _contentType != PreviewContentType.UGUI;
            _frameButton?.SetEnabled(prefabMode);
            _resetButton?.SetEnabled(prefabMode);
            var allowAutoRotate = prefabMode && _contentType != PreviewContentType.UGUI;
            _autoRotateButton?.SetEnabled(allowAutoRotate);
            if (!allowAutoRotate && _autoRotateButton != null)
            {
                _autoRotate = false;
                UpdateViewButtonsState();
            }
            _gridButton?.SetEnabled(sceneControls);
            _lightingButton?.SetEnabled(sceneControls);
            _projectionButton?.SetEnabled(sceneControls);
            _viewXButton?.SetEnabled(sceneControls);
            _viewYButton?.SetEnabled(sceneControls);
            _viewZButton?.SetEnabled(sceneControls);
            var particleControls = prefabMode && _particleSystems.Count > 0;
            _restartButton?.SetEnabled(particleControls);
            _playButton?.SetEnabled(particleControls);

            UpdateViewButtonsState();
        }

        private void ToggleParticleControlsVisibility()
        {
            ToggleParticleControlsVisibility(false);
        }

        private void ToggleParticleControlsVisibility(bool forceDisable)
        {
            var show = !forceDisable && _displayMode == PreviewDisplayMode.PrefabScene && _particleSystems.Count > 0;
            _playButton?.SetEnabled(show);
            _restartButton?.SetEnabled(show);
        }

        private void UpdateStatusText()
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusKey = ViewportXLocalization.Key.StatusPrefix;
            _statusArgLiteral = null;
            _statusArgKey = _contentType switch
            {
                PreviewContentType.Model => ViewportXLocalization.Key.ContentTypeModel,
                PreviewContentType.Particle => ViewportXLocalization.Key.ContentTypeParticle,
                PreviewContentType.UGUI => ViewportXLocalization.Key.ContentTypeUGUI,
                _ => ViewportXLocalization.Key.ContentTypeUnknown
            };
            ApplyStatusFromState();
        }

        private void OnInspectorUpdate()
        {
            if (!_windowIsVisible)
            {
                return;
            }

            if (_displayMode == PreviewDisplayMode.AssetPreview
                && _assetPreviewSource != null
                && _assetPreviewSourceInstanceId != 0
                && AssetPreview.IsLoadingAssetPreview(_assetPreviewSourceInstanceId))
            {
                RequestPreviewRepaint();
            }
        }

        private void DestroyPreviewObject(ref GameObject go)
        {
            if (go == null)
            {
                return;
            }

            DestroyImmediate(go);
            go = null;
        }

        private static PreviewRenderUtility TryCreatePreviewRenderUtility()
        {
            try
            {
                var type = typeof(PreviewRenderUtility);
                var boolCtor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(bool) },
                    modifiers: null);
                if (boolCtor != null)
                {
                    return (PreviewRenderUtility)boolCtor.Invoke(new object[] { true });
                }

                var defaultCtor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (defaultCtor != null)
                {
                    return (PreviewRenderUtility)defaultCtor.Invoke(null);
                }
            }
            catch
            {
                // Swallow to keep the window functional even if PreviewRenderUtility changes across Unity versions.
            }

            return null;
        }

        private static void ApplyCanvasScalerSettings(GameObject canvasGo, Vector2 referenceResolution)
        {
            if (canvasGo == null)
            {
                return;
            }

            var canvasScalerType = Type.GetType("UnityEngine.UI.CanvasScaler, UnityEngine.UI");
            if (canvasScalerType == null)
            {
                return;
            }

            Component scalerComponent;
            try
            {
                scalerComponent = canvasGo.GetComponent(canvasScalerType);
            }
            catch
            {
                return;
            }

            if (scalerComponent == null)
            {
                return;
            }

            try
            {
                var scaleModeType = canvasScalerType.GetNestedType("ScaleMode", BindingFlags.Public | BindingFlags.NonPublic);
                var uiScaleModeProp = canvasScalerType.GetProperty("uiScaleMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (scaleModeType != null && uiScaleModeProp != null && uiScaleModeProp.CanWrite)
                {
                    var value = Enum.Parse(scaleModeType, "ScaleWithScreenSize");
                    uiScaleModeProp.SetValue(scalerComponent, value);
                }

                var referenceResolutionProp = canvasScalerType.GetProperty("referenceResolution", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (referenceResolutionProp != null && referenceResolutionProp.CanWrite)
                {
                    referenceResolutionProp.SetValue(scalerComponent, referenceResolution);
                }
            }
            catch
            {
                // Ignore; CanvasScaler is optional.
            }
        }

        private void UpdateCameraClipPlanes()
        {
            if (_previewUtility == null)
            {
                return;
            }

            var cam = _previewUtility.camera;
            if (cam == null)
            {
                return;
            }

            var target = _contentBounds.center + _panOffset;
            var centerDistance = (_contentType == PreviewContentType.UGUI || cam.orthographic)
                ? Vector3.Distance(cam.transform.position, target)
                : Mathf.Max(_distance, 0.1f);
            var radius = Mathf.Max(_contentBounds.extents.magnitude, 0.01f);

            var paddingMultiplier = _contentType == PreviewContentType.Particle ? 2f : 1.25f;
            var padding = Mathf.Max(radius * paddingMultiplier, 0.1f);

            var nearPlane = Mathf.Max(0.01f, centerDistance - padding);
            var farPlane = Mathf.Max(nearPlane + 0.1f, centerDistance + padding);

            cam.nearClipPlane = nearPlane;
            cam.farClipPlane = farPlane;
        }

        private static string ResolveAssetPath(string guid, string fileName, string typeFilter)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            var guids = AssetDatabase.FindAssets($"{System.IO.Path.GetFileNameWithoutExtension(fileName)} t:{typeFilter}");
            foreach (var foundGuid in guids)
            {
                var candidate = AssetDatabase.GUIDToAssetPath(foundGuid);
                if (candidate.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return fileName;
        }

        private sealed class PreviewSurfaceElement : VisualElement
        {
            public enum FitMode
            {
                StretchToFill,
                ScaleToFit
            }

            private Texture _texture;
            private Rect _uv = new Rect(0f, 0f, 1f, 1f);
            private FitMode _fitMode = FitMode.ScaleToFit;

            public PreviewSurfaceElement()
            {
                pickingMode = PickingMode.Ignore;
                focusable = false;
                generateVisualContent += OnGenerateVisualContent;
            }

            public void SetFrame(Texture texture, Rect uv, FitMode fitMode)
            {
                if (ReferenceEquals(_texture, texture) && _uv == uv && _fitMode == fitMode)
                {
                    return;
                }

                _texture = texture;
                _uv = uv;
                _fitMode = fitMode;
            }

            public void ClearFrame()
            {
                if (_texture == null && _uv == new Rect(0f, 0f, 1f, 1f) && _fitMode == FitMode.ScaleToFit)
                {
                    return;
                }

                _texture = null;
                _uv = new Rect(0f, 0f, 1f, 1f);
                _fitMode = FitMode.ScaleToFit;
            }

            private void OnGenerateVisualContent(MeshGenerationContext mgc)
            {
                if (_texture == null)
                {
                    return;
                }

                var targetRect = contentRect;
                if (_fitMode == FitMode.ScaleToFit)
                {
                    var sourceWidth = Mathf.Max(_texture.width * _uv.width, 1f);
                    var sourceHeight = Mathf.Max(_texture.height * _uv.height, 1f);
                    var sourceAspect = sourceWidth / sourceHeight;
                    var rectAspect = targetRect.height > 0f ? targetRect.width / targetRect.height : sourceAspect;

                    if (rectAspect > sourceAspect)
                    {
                        var width = targetRect.height * sourceAspect;
                        targetRect = new Rect(targetRect.x + (targetRect.width - width) * 0.5f, targetRect.y, width, targetRect.height);
                    }
                    else
                    {
                        var height = targetRect.width / sourceAspect;
                        targetRect = new Rect(targetRect.x, targetRect.y + (targetRect.height - height) * 0.5f, targetRect.width, height);
                    }
                }

                DrawTexturedQuad(mgc, targetRect, _texture, _uv);
            }

            private static void DrawTexturedQuad(MeshGenerationContext mgc, Rect rect, Texture texture, Rect uv)
            {
                var mesh = mgc.Allocate(4, 6, texture);

                var vertex = new Vertex
                {
                    tint = new Color32(255, 255, 255, 255)
                };

                vertex.position = new Vector3(rect.xMin, rect.yMin, 0f);
                vertex.uv = new Vector2(uv.xMin, uv.yMax);
                mesh.SetNextVertex(vertex);

                vertex.position = new Vector3(rect.xMax, rect.yMin, 0f);
                vertex.uv = new Vector2(uv.xMax, uv.yMax);
                mesh.SetNextVertex(vertex);

                vertex.position = new Vector3(rect.xMax, rect.yMax, 0f);
                vertex.uv = new Vector2(uv.xMax, uv.yMin);
                mesh.SetNextVertex(vertex);

                vertex.position = new Vector3(rect.xMin, rect.yMax, 0f);
                vertex.uv = new Vector2(uv.xMin, uv.yMin);
                mesh.SetNextVertex(vertex);

                mesh.SetNextIndex(0);
                mesh.SetNextIndex(1);
                mesh.SetNextIndex(2);
                mesh.SetNextIndex(0);
                mesh.SetNextIndex(2);
                mesh.SetNextIndex(3);
            }
        }
    }
}
#endif
