using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MIPCG;
using MIPCG_Assistant;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Assert = UnityEngine.Assertions.Assert;
using Button = UnityEngine.UIElements.Button;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

internal enum InteractionTypes
{
    Paint = 1,
    RectangleFill = 2,
    FloodFill = 3
};

public class MIPCG_Editor : EditorWindow
{
    
    public class SettingsStruct
    {
        public string assetFolderPath;

        public string customPreviewFolderPath;

        public string aiModelPath;
        public string aiModelName;

        public bool useSmoothing;
        public bool useFallback;
        public bool useDiagonalNeighborhoods;

        public List<GameObject> uniqueAssets;
    }

    private static SerializedObject _sceneSerializedSettings = null;

    public static SettingsStruct GetSettings()
    {
        if (_sceneSerializedSettings == null)
        {
            var settingsComponent = FindObjectOfType<MIPCG_SceneSettingsComponent>();
            if(settingsComponent)
                _sceneSerializedSettings = new SerializedObject(settingsComponent);
        }

        if (_sceneSerializedSettings == null) return new SettingsStruct();
        _sceneSerializedSettings.Update();

        List<GameObject> uniques = new List<GameObject>();
        var serializedProperty = _sceneSerializedSettings.FindProperty("uniqueAssets");
        for (int i = 0; i < serializedProperty.arraySize; i++)
        {
            uniques.Add(serializedProperty.GetArrayElementAtIndex(i).objectReferenceValue as GameObject);
        }
        var settings = new SettingsStruct()
        {
            assetFolderPath = _sceneSerializedSettings.FindProperty("assetFolderPath").stringValue,
            customPreviewFolderPath = _sceneSerializedSettings.FindProperty("customPreviewFolderPath").stringValue,
            aiModelPath = _sceneSerializedSettings.FindProperty("aiModelPath").stringValue,
            aiModelName = _sceneSerializedSettings.FindProperty("aiModelName").stringValue,
            useSmoothing = _sceneSerializedSettings.FindProperty("useSmoothing").boolValue,
            useFallback = _sceneSerializedSettings.FindProperty("useFallback").boolValue,
            useDiagonalNeighborhoods = _sceneSerializedSettings.FindProperty("useDiagonalNeighborhoods").boolValue,
            uniqueAssets = uniques
        };
        return settings;
    }
    
    private readonly float _zoom = 1.0f;

    //### ASSET LIST DATA
    private ListView _assetListView;
    private AssetManager _assetManager;

    private VisualElement _assetPreviewPane;

    private Assistant _assistant;
    private Board _board;
    private Vector2 _canvasOffset = Vector2.zero;

    private Button _clearButton;

    private InteractionTypes _currentInputType = InteractionTypes.Paint;
    private Rect _drawArea;

    //### DRAWING CANVAS DATA
    private VisualElement _drawBox;
    private Rect _gridArea;


    private VisualElement _gridContainer;

    private Vector2Int _hoveredCellID;
    private bool _isInitialized;

    //### INPUT DATA
    private bool _isMouseDown;

    //debug labels
    private VisualElement _label;

    private int _mButtonType;
    //### Settings

    //### GRID DATA
    private MIPCG_SceneObjectComponent _mipcgSceneObjectComponent;
    private MIPCG_SceneSettingsComponent _sceneSettingsComponent;

    private Vector2 _mouseDownStartPosition;

    private Vector2 _mousePosition;
    private bool _mouseUpEvent;
    private VisualElement _positionLabel;

    private VisualElement _rightElement;
    private int _selectedIndex = -1;
    private bool _showAutoCompletion = true;

    private bool _subscribedToSceneChange;

    //Tile size will be chosen according to window size
    private float _tileSize = 50;
    private ToolbarToggle _toolbarToggle;
    private VisualElement _UIRoot;


    private bool _useRecommendation;

    public void Awake()
    {
        Debug.Log("Awake");
    }

    public void OnDestroy()
    {
        Debug.Log("OnDestroy");
    }

    private void OnGUI()
    {
        if (!_isInitialized || _mipcgSceneObjectComponent == null) return;
        var start = EditorApplication.timeSinceStartup;
        _mousePosition = Event.current.mousePosition;
        HandleInput();
        UpdateCanvas();
        DrawGrid();
        DrawObjectPreviews();
        DrawRecommendations();
        HighlightHoveredCell();
        var end = EditorApplication.timeSinceStartup;
        // Debug.Log(end - start);
    }

    public void CreateGUI()
    {
        if (!_subscribedToSceneChange)
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            _subscribedToSceneChange = true;
        }

        InitUI();

        LoadSceneComponent();
        if(!_isInitialized) return;
        InitAssetManager();


        _board = new Board(_mipcgSceneObjectComponent, _assetManager);
        _assistant = new Assistant(_board, _sceneSettingsComponent,_assetManager);
        
        SerializedObject so = new SerializedObject(_mipcgSceneObjectComponent);
        _UIRoot.TrackSerializedObjectValue(so, ComponentChanged);
    }

    [MenuItem("Window/MIPCG Editor")]
    public static void ShowExample()
    {
        MIPCG_Editor wnd = GetWindow<MIPCG_Editor>();
        wnd.titleContent = new GUIContent("MIPCG_Editor");
    }

    private void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        _isInitialized = false;
        CreateGUI();
    }

    private void OnSceneClosing(Scene scene, bool removingscene)
    {
        _UIRoot.Unbind();
        rootVisualElement.Clear();
        _board = null;
        _assetManager = null;
        _assistant = null;
        _selectedIndex = -1;
        _sceneSettingsComponent = null;
        _sceneSerializedSettings = null;
    }


    private void InitUI()
    {
        _UIRoot = rootVisualElement;
        _UIRoot.focusable = true;
        _UIRoot.Focus();
        _UIRoot.RegisterCallback<KeyDownEvent>(KeyDown);

        //PaneSplits
        
        var leftSplit = new TwoPaneSplitView(0, 500, TwoPaneSplitViewOrientation.Vertical);
        var middleSplit = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
        var rightSplitSplit = new TwoPaneSplitView(0, 80, TwoPaneSplitViewOrientation.Vertical);
        var topBarSplit = new TwoPaneSplitView(1, 200, TwoPaneSplitViewOrientation.Horizontal);
        //INIT UI ELEMENTS
        var menuButtonPane = new VisualElement()
        {
            style =
            {
                alignSelf = new StyleEnum<Align>(Align.Center), overflow = new StyleEnum<Overflow>(Overflow.Hidden),
                flexWrap = new StyleEnum<Wrap>(Wrap.Wrap)
            }
        };
        var assetPane = new VisualElement();


        //###LEFT PANES###
        //asset view
        _assetListView = new ListView { selectedIndex = _selectedIndex };
        _assetListView.onSelectionChange += (items) => { _selectedIndex = _assetListView.selectedIndex; };
        var assetListLabel = new Label("Available Assets:")
        {
            style =
            {
                justifyContent = new StyleEnum<Justify>(Justify.Center),
                fontSize = 20,
                backgroundColor = new StyleColor(Color.grey)
            }
        };

        _assetPreviewPane = new VisualElement();

        //clear board button
        _clearButton = new Button { text = "Clear Board" };
        _clearButton.clicked += ClearBoard;
        //save ai button
        var saveButton = new Button()
            { text = "Save AI", tooltip = "Saves current neighborhood statistics to File" };
        saveButton.clicked += SaveModel;
        var clearAIButton = new Button()
            { text = "Clear AI", tooltip = "Reset all learned neighborhood statistics" };
        clearAIButton.clicked += ClearAI;
        
        var saveLevelButton = new Button() { text = "Save Level", tooltip = "Save the current level including currently auto completed pieces to text file." };
        saveLevelButton.clicked += SaveCurrentLevelToTextFile;
        
       


        //###RIGHT FRAME###
        _drawBox = new VisualElement
        {
            name = "draw box"
        };
        _drawBox.RegisterCallback<PointerDownEvent>(MouseDown);
        _drawBox.RegisterCallback<PointerUpEvent>(MouseUp);
        _drawBox.RegisterCallback<PointerOutEvent>(MouseExit);

        VisualElement topBarContainer = new VisualElement()
        {
            style =
            {
                alignItems = new StyleEnum<Align>(Align.Center),

                alignSelf = new StyleEnum<Align>(Align.Center), overflow = new StyleEnum<Overflow>(Overflow.Hidden),
                flexWrap = new StyleEnum<Wrap>(Wrap.Wrap)
            }
        };
        topBarContainer.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Column);
        _toolbarToggle = new ToolbarToggle
        {
            style =
            {
                borderRightWidth = 0,
                borderLeftWidth = 0,
                borderTopWidth = 0,
                borderBottomWidth = 0,
                alignSelf = new StyleEnum<Align>(Align.Center),
                alignItems = new StyleEnum<Align>(Align.Center),
                overflow = new StyleEnum<Overflow>(Overflow.Hidden),
                flexWrap = new StyleEnum<Wrap>(Wrap.Wrap)
            },
            focusable = false
        };
        _toolbarToggle.tooltip = "Tools";
        _toolbarToggle.Add(new Button(() => ToolbarHandler(1)) { text = "Paint", style = { width = 100 } });
        _toolbarToggle.Add(new Button(() => ToolbarHandler(2)) { text = "Selection Fill", style = { width = 100 } });
        _toolbarToggle.Add(new Button(() => ToolbarHandler(3)) { text = "Flood Fill", style = { width = 100 } });
        _toolbarToggle[1].style.backgroundColor = new StyleColor(Color.gray);
        var recommendationToggle = new Button()
        {
            text = "Use Tool for AI Recommendation", bindingPath = "_useRecommendation",
            style =
            {
                alignSelf = new StyleEnum<Align>(Align.Center),
                maxHeight = 200
            }
        };
        recommendationToggle.clicked += () =>
        {
            _useRecommendation = !_useRecommendation;
            if (_useRecommendation)
                recommendationToggle.style.backgroundColor = new StyleColor(Color.gray);
            else
            {
                recommendationToggle.style.backgroundColor = topBarContainer.style.backgroundColor;
                _board.ClearRecommendations();
            }
        };

        //HIERARCHY
        _UIRoot.Add(middleSplit);

        middleSplit.Add(assetPane);
        middleSplit.Add(rightSplitSplit);

        rightSplitSplit.Add(topBarSplit);
        rightSplitSplit.Add(_drawBox);

        assetPane.Add(assetListLabel);
        assetPane.Add(leftSplit);
        leftSplit.Add(_assetListView);
        leftSplit.Add(_assetPreviewPane);

        topBarSplit.Add(topBarContainer);
        topBarSplit.Add(menuButtonPane);

        menuButtonPane.Add(_clearButton);
        menuButtonPane.Add(saveButton);
        menuButtonPane.Add(clearAIButton);
        menuButtonPane.Add(saveLevelButton);

        topBarContainer.Add(_toolbarToggle);

    }


    private void OnRecommendationToggle(ChangeEvent<bool> evt)
    {
        _useRecommendation = evt.newValue;
    }

    private void LoadModel()
    {
        _assistant.Load();
    }

    private void SaveModel()
    {
        Assert.IsNotNull(_assistant);
        _assistant.Save();
    }

    void ToolbarHandler(int button)
    {
        foreach (var child in _toolbarToggle.Children())
        {
            child.style.backgroundColor = _toolbarToggle.style.backgroundColor;
        }

        _toolbarToggle[button].style.backgroundColor = new StyleColor(Color.gray);
        _currentInputType = (InteractionTypes)button;
    }

    private void KeyDown(KeyDownEvent evt)
    {
        switch (evt.keyCode)
        {
            case KeyCode.Space:
                _showAutoCompletion = !_showAutoCompletion;
                break;
            case KeyCode.KeypadEnter:
            case KeyCode.Return:
                _board.AcceptRecommendation();
                break;
        }
    }

    private void ComponentChanged(SerializedObject so)
    {
        var width = so.FindProperty("width").intValue;
        var height = so.FindProperty("height").intValue;
        
        if(width != _board.Width || height != _board.Height)
        {
            _board = new Board(_mipcgSceneObjectComponent, _assetManager);
            _mipcgSceneObjectComponent.ClearAutoCompletionObjects();
            _mipcgSceneObjectComponent.Reinit();
            RebuildAutoComplete();
            Debug.Log("Component Changed");
            Debug.Log("width: " + width);
            Debug.Log("height: " + height);
        }
    }

    private void MouseExit(PointerOutEvent evt)
    {
        _isMouseDown = false;
        _mouseDownStartPosition = _mousePosition;
    }

    private void MouseDown(PointerDownEvent e)
    {
        _isMouseDown = true;
        _mButtonType = e.button;
        _mouseDownStartPosition = _mousePosition;
    }

    private void MouseUp(PointerUpEvent e)
    {
        _isMouseDown = false;
        _mouseUpEvent = true;
    }

    private void FloodFillStart(Vector2 mousePosition, int selectedIndex)
    {
        var cell = PositionToCellID(mousePosition);
        var visited = _board.FloodFill(cell);
        _board.RemoveMultipleObjectsFromScene(visited.ToList());
        if (selectedIndex != -1)
            _board.PlaceMultiple(visited.ToList(), selectedIndex, true);
    }


    private void LoadSceneComponent()
    {
        var sceneSettings = FindObjectOfType<MIPCG_SceneSettingsComponent>();
        if (sceneSettings) _sceneSettingsComponent = sceneSettings;
        var mipcgSceneObjectComponent = FindObjectOfType<MIPCG_SceneObjectComponent>();
        if (mipcgSceneObjectComponent) _mipcgSceneObjectComponent = mipcgSceneObjectComponent;
        if (mipcgSceneObjectComponent && sceneSettings) _isInitialized = true;
        else
        {
            Debug.Log("No LevelGrid found!");
        }
    }


    private void InitAssetManager()
    {
        _assetManager = new AssetManager(_sceneSettingsComponent);
        // find all prefabs
        List<GameObject> assets = _assetManager.Assets;
        // Initialize the list view with all prefab names
        _assetListView.makeItem = () => new Label();
        _assetListView.bindItem = (item, index) => { ((Label)item).text = assets[index].name; };
        _assetListView.itemsSource = assets;
    }

    private void OnDrag(Vector2 delta)
    {
        _canvasOffset += delta / _zoom;
        _canvasOffset.x = Mathf.Clamp(_canvasOffset.x, -_gridArea.center.x, _gridArea.center.x);
        _canvasOffset.y = Mathf.Clamp(_canvasOffset.y, -_gridArea.center.y, _gridArea.center.y);
        GUI.changed = true;
    }

    private void OnWheel(Vector2 delta, Vector2 mousePosition)
    {
    }

    private void UpdateCanvas()
    {
        if (!_mipcgSceneObjectComponent) return;
        //(0,0) is top left corner

        //padding depends on shortest window side
        //tile size also
        float topPadding = 0;
        float bottomPadding = 0;
        float leftPadding = 0;
        float rightPadding = 0;

        if (_drawBox == null) return;
        Rect drawBoxArea = _drawBox.worldBound;

        //define area where draw stuff
        _drawArea = new Rect(drawBoxArea.xMin, drawBoxArea.yMin, drawBoxArea.width - (leftPadding + rightPadding),
            drawBoxArea.height - (topPadding + bottomPadding));

        const float scale = .99f;
        //orient Tile size on smallest dimension
        _tileSize = _drawArea.width * scale / _mipcgSceneObjectComponent.width <
                    _drawArea.height * scale / _mipcgSceneObjectComponent.height
            ? _drawArea.width * scale / _mipcgSceneObjectComponent.width
            : _drawArea.height * scale / _mipcgSceneObjectComponent.height;


        if (_drawArea.width < _drawArea.height)
        {
            leftPadding = rightPadding = 0.0f * _drawArea.width;
            topPadding = bottomPadding = 0.0f * (_drawArea.height - _mipcgSceneObjectComponent.height * _tileSize);
        }
        else
        {
            leftPadding = rightPadding = 0.0f * (_drawArea.width - _mipcgSceneObjectComponent.width * _tileSize);
            topPadding = bottomPadding = 0.0f * _drawArea.height;
        }

        _gridArea = new Rect(_drawArea.xMin, _drawArea.yMin,
            _mipcgSceneObjectComponent.width * _tileSize,
            _mipcgSceneObjectComponent.height * _tileSize);

        //move grid to center
        _gridArea.center = drawBoxArea.center - new Vector2(0, 15);
    }

    private void HighlightHoveredCell()
    {
        GUI.changed = true;

        if (_isMouseDown && _currentInputType == InteractionTypes.RectangleFill)
        {
            Vector2Int startCell = PositionToCellID(_mouseDownStartPosition);
            startCell.x = Math.Clamp(startCell.x, 0, _mipcgSceneObjectComponent.width - 1);
            startCell.y = Math.Clamp(startCell.y, 0, _mipcgSceneObjectComponent.height - 1);
            Vector2Int endCell = PositionToCellID(_mousePosition);
            endCell.x = Math.Clamp(endCell.x, 0, _mipcgSceneObjectComponent.width - 1);
            endCell.y = Math.Clamp(endCell.y, 0, _mipcgSceneObjectComponent.height - 1);
            var diffX = Math.Abs(endCell.x - startCell.x);
            var diffY = Math.Abs(endCell.y - startCell.y);
            var dx = Math.Sign(endCell.x - startCell.x);
            var dy = Math.Sign(endCell.y - startCell.y);

            Handles.BeginGUI();
            Handles.color = Color.green;
            for (int x = 0; x <= diffX; x++)
            {
                for (int y = 0; y <= diffY; y++)
                {
                    var cellPos = CellToPosition(startCell + new Vector2Int(x * dx, y * dy));
                    Handles.DrawWireCube(cellPos, new Vector3(_tileSize, _tileSize));
                }
            }

            Handles.EndGUI();
        }
        else
        {
            if (!_gridArea.Contains(_mousePosition)) return;
            _hoveredCellID = PositionToCellID(_mousePosition);
            Vector2 hoverPosition = CellToPosition(_hoveredCellID);

            Handles.BeginGUI();
            Handles.color = Color.green;
            Handles.DrawWireCube(hoverPosition, new Vector3(_tileSize, _tileSize));
            Handles.EndGUI();
        }
    }

    private void HandleInput()
    {
        if (_isMouseDown && _currentInputType == InteractionTypes.Paint)
        {
            switch (_mButtonType)
            {
                case 0:
                    PaintObjectToBoard(_mousePosition);
                    break;
                case 1:
                    RemoveObjectFromBoard(_mousePosition);
                    break;
            }
        }

        if (_mouseUpEvent)
        {
            if (_currentInputType == InteractionTypes.RectangleFill)
            {
                switch (_mButtonType)
                {
                    //left button
                    case 0:
                        RectangleFill(_mousePosition, _selectedIndex);
                        break;
                    //right button
                    case 1:
                        RectangleFill(_mousePosition, -1);
                        break;
                }

            }
            else if (_currentInputType == InteractionTypes.FloodFill)
            {
                switch (_mButtonType)
                {
                    //left button
                    case 0:
                        FloodFillStart(_mousePosition, _selectedIndex);
                        break;
                    //right button
                    case 1:
                        FloodFillStart(_mousePosition, -1);
                        break;
                }

            }
            
            _mouseUpEvent = false;
            RebuildAutoComplete();
            SceneView.lastActiveSceneView.Repaint();
        }
    }

    private void RemoveObjectFromBoard(Vector2 mousePosition)
    {
        if (!_gridArea.Contains(mousePosition)) return;
        //transform from window coordinates to 2d coords
        Vector2Int coords = PositionToCellID(mousePosition);
        if (coords.x < 0 || coords.y < 0 || coords.x > _mipcgSceneObjectComponent.width - 1 ||
            coords.y > _mipcgSceneObjectComponent.height - 1) return;

        _board.RemoveObjectFromScene(coords);
    }

    private void PaintObjectToBoard(Vector2 mousePosition)
    {
        if (!_gridArea.Contains(mousePosition) || _selectedIndex == -1) return;
        //transform from window coordinates to 2d coords
        Vector2Int coords = PositionToCellID(mousePosition);
        if (coords.x < 0 || coords.y < 0 || coords.x > _mipcgSceneObjectComponent.width - 1 ||
            coords.y > _mipcgSceneObjectComponent.height - 1) return;

        _board.PlaceObject(coords, _selectedIndex, true);
    }

    private void RectangleFill(Vector2 mousePosition, int assetID)
    {
        Vector2Int startCell = PositionToCellID(_mouseDownStartPosition);
        startCell.x = Math.Clamp(startCell.x, 0, _mipcgSceneObjectComponent.width - 1);
        startCell.y = Math.Clamp(startCell.y, 0, _mipcgSceneObjectComponent.height - 1);
        Vector2Int endCell = PositionToCellID(mousePosition);
        endCell.x = Math.Clamp(endCell.x, 0, _mipcgSceneObjectComponent.width - 1);
        endCell.y = Math.Clamp(endCell.y, 0, _mipcgSceneObjectComponent.height - 1);
        var diffX = Math.Abs(endCell.x - startCell.x);
        var diffY = Math.Abs(endCell.y - startCell.y);
        var dx = Math.Sign(endCell.x - startCell.x);
        var dy = Math.Sign(endCell.y - startCell.y);

        List<Vector2Int> cells = new List<Vector2Int>();
        for (int x = 0; x <= diffX; x++)
        {
            for (int y = 0; y <= diffY; y++)
            {
                cells.Add(startCell + new Vector2Int(x * dx, y * dy));
            }
        }

        if (assetID == -1)
        {
            _board.RemoveMultipleObjectsFromScene(cells);
        }
        else
        {
            _board.PlaceMultiple(cells, assetID, true);
        }
    }

    private void DrawGrid()
    {
        if (!_mipcgSceneObjectComponent) return;

        int nrOfDividersHor = _mipcgSceneObjectComponent.height + 1;
        int nrOfDividersVert = _mipcgSceneObjectComponent.width + 1;

        Handles.BeginGUI();

        GUI.color = Color.white;
        GUI.DrawTexture(_gridArea, Texture2D.grayTexture, ScaleMode.StretchToFill, true);

        Handles.color = Color.gray;
        float delta = _tileSize;

        for (int i = 0; i < nrOfDividersVert; i++)
        {
            Handles.DrawLine((new Vector3(_gridArea.xMin + i * delta, _gridArea.yMin)) * _zoom,
                new Vector3(_gridArea.xMin + i * delta, _gridArea.yMax) * _zoom);
        }

        for (int i = 0; i < nrOfDividersHor; i++)
        {
            Handles.DrawLine((new Vector3(_gridArea.xMin, _gridArea.yMin + i * delta)) * _zoom,
                new Vector3(_gridArea.xMax, _gridArea.yMin + i * delta) * _zoom);
        }

        Handles.EndGUI();
    }

    void DrawObjectPreviews()
    {
        var previews = _assetManager?.GetAssetPreviews();
        if (previews != null)
        {
            for (int x = 0; x < _mipcgSceneObjectComponent.width; x++)
            {
                for (int y = 0; y < _mipcgSceneObjectComponent.height; y++)
                {
                    var assetID = _board.At(new Vector2Int(x, y));
                    if (assetID == -1) continue;
                    var pos = CellToPosition(new Vector2Int(x, y));
                    GUI.DrawTexture(
                        new Rect(pos.x - _tileSize / 2, pos.y - _tileSize / 2, _tileSize, _tileSize),
                        previews[assetID]);
                }
            }
            if(_selectedIndex <0) return;
            GUI.DrawTexture(
                new Rect(_assetPreviewPane.worldBound.position,new Vector2(_assetPreviewPane.worldBound.width,_assetPreviewPane.worldBound.width)),
                previews[_selectedIndex]);
        }
        
        
    }

    void DrawRecommendations()
    {
        if (!_showAutoCompletion) return;
        var previews = _assetManager.GetAssetPreviews();
        //find all cells that have at least one neighbor
        if (previews == null)
        {
            return;
        }

        foreach (var recommendation in _board.RecommendationBuffer)
        {
            if (_board.At(recommendation.Key) != -1) continue;
            var pos = CellToPosition(new Vector2Int(recommendation.Key.x, recommendation.Key.y));
            Vector3 c = new Vector3(130, 140, 132);
            GUI.color = new Color(c.x / 255, c.y/ 255, c.z / 255);
            GUI.DrawTexture(
                new Rect(pos.x - _tileSize / 2, pos.y - _tileSize / 2, _tileSize, _tileSize),
                Texture2D.whiteTexture, ScaleMode.ScaleToFit, false);
            GUI.color = Color.white;
            GUI.DrawTexture(
                new Rect(pos.x - _tileSize / 2, pos.y - _tileSize / 2, _tileSize, _tileSize),
                previews[recommendation.Value], ScaleMode.ScaleToFit, true);
        }
    }


    //returns the cell containing the position
    Vector2Int PositionToCellID(Vector2 position)
    {
        //calculate position relative to the draw area of the grid
        Vector2 relative = new Vector2(_mipcgSceneObjectComponent.width, _mipcgSceneObjectComponent.height) *
                           Rect.PointToNormalized(_gridArea, position);
        return new Vector2Int(Mathf.FloorToInt(relative.x), Mathf.FloorToInt(relative.y));
    }

    //returns the cell's center position in world space
    Vector2 CellToPosition(Vector2Int cellID)
    {
        return _gridArea.min + (Vector2)cellID * _tileSize + new Vector2(_tileSize / 2, _tileSize / 2);
    }

    void ClearBoard()
    {
        Debug.Log("clear");
        _board.Clear();
        _board.ClearRecommendations();
        _mipcgSceneObjectComponent.Clear();
    }

    private void ClearAI()
    {
        _assistant?.Reset();
    }

    private int GetRecommendationByProbability(Vector2Int coords)
    {
        var settings = GetSettings();
        var smoothing = settings.useSmoothing;
        var fallback = settings.useFallback;
        var recommendationForNeighborhood =
            _assistant.GetRecommendationForNeighborhood(coords, smoothing ? SmoothingType.Softmax : SmoothingType.None,
                fallback);
        if (recommendationForNeighborhood == null || recommendationForNeighborhood.Count == 0) return -1;

        //if we use softmax recommendation probabilities then take random samples
        float randomVariable = Random.Range(0.0f, 1.0f);
        var chosenPair = recommendationForNeighborhood.First();
        float probSum = 0;
        foreach (var pair in recommendationForNeighborhood)
        {
            probSum += pair.Value;
            if (randomVariable < probSum)
            {
                chosenPair = pair;
                break;
            }
        }

        return chosenPair.Key;
    }


    private void RebuildAutoComplete()
    {
        _board.ClearRecommendations();

        //find all cells that are empty
        HashSet<Vector2Int> emptyCells = new HashSet<Vector2Int>();
        HashSet<Vector2Int> perimeter = new HashSet<Vector2Int>();
        for (int x = 0; x < _board.Width; x++)
        {
            for (int y = 0; y < _board.Height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (_board.At(cell) == -1)
                {
                    emptyCells.Add(cell);
                    foreach (var value in _board.GetNeighborhood(cell, false))
                    {
                        if (value != -1)
                        {
                            perimeter.Add(cell);
                            break;
                        }
                    }
                }
            }
        }

        //take random seed from perimeter
        if(emptyCells.Count == 0 || perimeter.Count == 0) return;
        var start = perimeter.ElementAt(Random.Range(0, perimeter.Count));

        List<Vector2Int> visited = new List<Vector2Int>();
        //do flood fill until all empty cells are visited
        while (emptyCells.Count > 0)
        {
            var fillResult = _board.FloodFill(start);
            emptyCells.ExceptWith(fillResult);
            perimeter.ExceptWith(fillResult);
            emptyCells.TrimExcess();
            visited.AddRange(fillResult);
            if (emptyCells.Count > 0)
                start = perimeter.ElementAt(Random.Range(0, perimeter.Count));
        }

        // _mipcgSceneObjectComponent.ClearAutoCompletionObjects();
        foreach (var cell in visited)
        {
            if (_board.At(cell) != -1) Debug.Log("here");
            var recommendation = GetRecommendationByProbability(cell);
            _board.AddToRecommendations(cell, recommendation);
        }
    }

    private void SaveCurrentLevelToTextFile()
    {
        var settings = GetSettings();
        var saveFolderPath = settings.aiModelPath;
        var saveFileName = settings.aiModelName;
        //find which number to add to file
        int counter = 1;
        while (File.Exists(saveFolderPath + "/" + saveFileName +"_level_"+ counter + ".txt"))
        {
            counter++;
        }
            
        FileStream fileStream = File.Open(saveFolderPath + "/" + saveFileName +"_level_"+ counter + ".txt", FileMode.Create);
        TextWriter textWriter = new StreamWriter(fileStream);

        textWriter.Write("" + _board.Width + " " + _board.Height+"\n");
        textWriter.Write(_board.BoardToString());
        textWriter.Write("\n");
        textWriter.Write("Asset Legend:\n");
        var assetList = _assetManager.Assets;
        for (int i = 0; i<assetList.Count;i++)
        {
            textWriter.Write("" + i + " " + assetList[i].name+"\n");
        }

        textWriter.Close();
        AssetDatabase.Refresh();

    }
}