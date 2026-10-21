using System;
using System.Collections.Generic;
using System.IO;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace FreshGuard.Editor
{
    public sealed class MapEditorWindow : EditorWindow, ISerializationCallbackReceiver
    {
        private const string DEFAULT_DIRECTORY = "Assets/AssetRaw/Configs/Maps";
        private const string BALL_PREFAB = "Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab";
        private static readonly MapEditorBrush[] BASE_PALETTE =
        {
            MapEditorBrush.Track, MapEditorBrush.Empty
        };
        private static readonly MapEditorBrush[] OBSTACLE_PALETTE =
        {
            MapEditorBrush.RemoveObstacle, MapEditorBrush.GlassNormal, MapEditorBrush.GlassColored
        };

        [SerializeField] private MapEditorDocument _document;
        // 保存基线属于窗口而非Undo对象，保存后撤销仍能正确标记未保存。
        [SerializeField, FormerlySerializedAs("_savedMap")] private MapEditorMapSnapshot _savedState;
        [NonSerialized] private MapData _savedMap;
        [SerializeField] private int _sourceVersion;
        [SerializeField] private bool _memoryMigrationNotice;
        private string _recoveryError;
        [SerializeField] private string _filePath = "";
        [SerializeField] private bool _newUnsaved;
        [SerializeField] private MapEditorBrush _brush = MapEditorBrush.Track;
        [SerializeField] private bool _rectangleTool;
        [SerializeField] private int _requestedColumns = 11;
        [SerializeField] private int _requestedRows = 9;
        [SerializeField] private float _cellPixels = 36f;
        [SerializeField] private Vector2 _gridScroll;
        [SerializeField] private Vector2 _issueScroll;

        private readonly List<EditorIssue> _issues = new List<EditorIssue>();
        private MapEditorGlassAssets _glassAssets;
        private bool _assetsInvalidated;
        private bool _drawing;
        private bool _gestureRectangle;
        private MapEditorBrush _gestureBrush;
        private int _gestureButton;
        private BoardCoordinate _gestureStart;
        private BoardCoordinate _gestureLast;
        private int _undoGroup = -1;
        private int _gridControl;
        private BoardCoordinate? _highlight;
        private GUIStyle _coordinateStyle;
        private string _ballGeometryDescription;
        private string _ballGeometryError;

        private sealed class EditorIssue
        {
            public bool IsError;
            public string Message;
            public BoardCoordinate? Coordinate;
        }

        [MenuItem("FreshGuard/Map Editor")]
        public static void Open()
        {
            GetWindow<MapEditorWindow>("Map Editor").Show();
        }

        public void OnBeforeSerialize()
        {
            if (_savedMap != null) _savedState = MapEditorMapSnapshot.Capture(_savedMap);
        }

        public void OnAfterDeserialize() { _savedMap = null; }

        private void OnEnable()
        {
            minSize = new Vector2(640f, 500f);
            titleContent = new GUIContent("Map Editor");
            saveChangesMessage = "地图有未保存修改。是否保存为Map_*.bytes？";
            _glassAssets = new MapEditorGlassAssets();
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
            // 已有窗口丢失文档引用也不能静默新建，保存基线/路径仍是恢复线索。
            if (_document == null && _savedState == null && string.IsNullOrEmpty(_filePath) && !_newUnsaved && !hasUnsavedChanges)
            {
                _document = CreateInstance<MapEditorDocument>();
                _document.name = "Map Editor Document";
                _document.hideFlags = HideFlags.HideAndDontSave;
                _document.Replace(MapData.Create());
                _savedMap = _document.Data.Clone();
                _newUnsaved = false;
                _filePath = "";
            }
            if (!TryRestoreMemory()) return;
            SyncDimensions();
            ValidateDocument(true);
        }

        private bool TryRestoreMemory()
        {
            try
            {
                if (_document == null || _document.Data == null)
                    throw new InvalidDataException("窗口内存文档已缺失。");
                var map = _document.Data;
                if (map.Cells == null || map.Cells.Length != map.Rows * map.Columns)
                    throw new InvalidDataException("窗口内存文档的Cells缺失或不完整。");
                if (_savedMap == null)
                {
                    if (_savedState == null) throw new InvalidDataException("窗口保存基线已缺失，无法安全判断未保存内容。");
                    _savedMap = _savedState.Restore(out var migratedBaseline);
                    _memoryMigrationNotice |= migratedBaseline;
                    if (migratedBaseline && !string.IsNullOrEmpty(_filePath) && _sourceVersion == 0) _sourceVersion = 1;
                }
                _memoryMigrationNotice |= _document.WasLegacyMemoryMigrated;
                _recoveryError = null;
                return true;
            }
            catch (Exception exception)
            {
                _recoveryError = exception.Message + " V3升级时旧内存可能已无法恢复；请重新载入地图文件。" +
                    "当前快照已保留，未自动重置；磁盘文件不包含未保存的编辑，请确认后再放弃。";
                hasUnsavedChanges = true;
                return false;
            }
        }

        private void OnDisable()
        {
            FinishGesture();
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnDestroy()
        {
            if (_document == null) return;
            Undo.ClearUndo(_document);
            DestroyImmediate(_document);
        }

        private void OnLostFocus()
        {
            // 鼠标在窗口外抬起或对话框夺焦时也收束为同一个Undo。
            FinishGesture();
        }

        private void OnProjectChanged()
        {
            _assetsInvalidated = true;
            Repaint();
        }

        private void OnUndoRedo()
        {
            _drawing = false;
            _undoGroup = -1;
            if (GUIUtility.hotControl == _gridControl) GUIUtility.hotControl = 0;
            if (!TryRestoreMemory()) { Repaint(); return; }
            SyncDimensions();
            ValidateDocument(true);
            Repaint();
        }

        public override void SaveChanges()
        {
            if (TrySave(false)) base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            FinishGesture();
            // 恢复失败时仅允许用户明确关闭并放弃；不替换仍保留着的快照。
            if (_recoveryError != null) { base.DiscardChanges(); return; }
            Undo.ClearUndo(_document);
            _document.Replace(_savedMap);
            _newUnsaved = false;
            SyncDimensions();
            ValidateDocument(true);
            base.DiscardChanges();
        }

        private void OnGUI()
        {
            if (_recoveryError != null)
            {
                EditorGUILayout.HelpBox(_recoveryError, MessageType.Error);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("重新载入 .bytes")) LoadDocument();
                    if (GUILayout.Button("明确放弃并新建")) NewDocument();
                }
                return;
            }
            if (_document == null || _document.Data == null) return;
            if (_assetsInvalidated) ValidateDocument(true);
            HandleShortcuts();
            DrawToolbar();
            DrawSettings();
            DrawPalette();
            EditorGUILayout.LabelField(_ballGeometryDescription ?? "球体净空基准尚未读取。", EditorStyles.miniLabel);
            if (_sourceVersion == 1)
                EditorGUILayout.HelpBox("源文件 V1：仅在内存中迁移为两层，加载不标记未保存，也未写回原文件；保存/导出统一写 V2。", MessageType.Info);
            else if (_memoryMigrationNotice)
                EditorGUILayout.HelpBox("已恢复旧窗口内存并迁移两层；原有未保存内容和保存基线分别保留，保存统一写 V2。", MessageType.Info);
            EditorGUILayout.LabelField("左键修改当前层 / 右键仅移除障碍；矩形拖放填充；原点(0,0)在左下角。", EditorStyles.miniLabel);
            DrawGrid();
            DrawIssues();
            var source = _sourceVersion == 0 ? "新建/来源版本未知" : "源文件 V" + _sourceVersion;
            EditorGUILayout.LabelField((string.IsNullOrEmpty(_filePath) ? "尚未保存到文件" : _filePath) + "　" + source + "；保存 V2", EditorStyles.miniLabel);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("新建", EditorStyles.toolbarButton)) NewDocument();
                if (GUILayout.Button("加载 .bytes", EditorStyles.toolbarButton)) LoadDocument();
                if (GUILayout.Button("保存", EditorStyles.toolbarButton)) TrySave(false);
                if (GUILayout.Button("另存 / 导出", EditorStyles.toolbarButton)) TrySave(true);
                GUILayout.Space(12f);
                if (GUILayout.Button("撤销", EditorStyles.toolbarButton)) { FinishGesture(); Undo.PerformUndo(); }
                if (GUILayout.Button("重做", EditorStyles.toolbarButton)) { FinishGesture(); Undo.PerformRedo(); }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("校验", EditorStyles.toolbarButton)) ValidateDocument(true);
            }
        }

        private void DrawSettings()
        {
            var map = _document.Data;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("列", GUILayout.Width(18));
                _requestedColumns = Mathf.Clamp(EditorGUILayout.IntField(_requestedColumns, GUILayout.Width(48)), 1, 64);
                GUILayout.Label("行", GUILayout.Width(18));
                _requestedRows = Mathf.Clamp(EditorGUILayout.IntField(_requestedRows, GUILayout.Width(48)), 1, 64);
                using (new EditorGUI.DisabledScope(map.Columns == _requestedColumns && map.Rows == _requestedRows))
                    if (GUILayout.Button("调整尺寸", GUILayout.Width(80))) ResizeDocument();
                GUILayout.Label("格距", GUILayout.Width(30));
                EditorGUI.BeginChangeCheck();
                var cellSize = EditorGUILayout.DelayedFloatField(map.CellSize, GUILayout.Width(64));
                GUILayout.Label("普通ID", GUILayout.Width(44));
                var normalId = EditorGUILayout.DelayedIntField(map.NormalGlassId, GUILayout.Width(44));
                GUILayout.Label("彩色ID", GUILayout.Width(44));
                var coloredId = EditorGUILayout.DelayedIntField(map.ColoredGlassId, GUILayout.Width(44));
                if (EditorGUI.EndChangeCheck())
                {
                    ChangeDocument("修改地图参数", () =>
                    {
                        _document.Data.CellSize = cellSize;
                        _document.Data.NormalGlassId = normalId;
                        _document.Data.ColoredGlassId = coloredId;
                    }, true);
                }
            }
        }

        private void DrawPalette()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("基础", GUILayout.Width(30));
                foreach (var brush in BASE_PALETTE) DrawBrushButton(brush);
                GUILayout.Label("障碍", GUILayout.Width(30));
                foreach (var brush in OBSTACLE_PALETTE) DrawBrushButton(brush);
                DrawBrushButton(MapEditorBrush.ResetCell);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("当前：" + BrushLabel(_brush), GUILayout.Width(160));
                GUILayout.FlexibleSpace();
                _rectangleTool = GUILayout.Toggle(_rectangleTool, "矩形", "Button", GUILayout.Width(48));
                if (GUILayout.Button("全图填充", GUILayout.Width(72)))
                {
                    var brush = _brush;
                    ChangeDocument("全图填充：" + BrushLabel(brush), () => _document.Fill(brush));
                }
                if (GUILayout.Button("重置全图", GUILayout.Width(72)) &&
                    EditorUtility.DisplayDialog("重置地图", "全部格子将同时重置为Empty + None，可撤销。", "重置", "取消"))
                    ChangeDocument("重置地图", () => _document.Fill(MapEditorBrush.ResetCell));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{_document.Data.Columns}列 × {_document.Data.Rows}行", GUILayout.Width(110));
                GUILayout.Label("缩放", GUILayout.Width(30));
                _cellPixels = GUILayout.HorizontalSlider(_cellPixels, 18f, 64f, GUILayout.Width(130));
                GUILayout.FlexibleSpace();
                GUILayout.Label("底色：基础地形；玻璃：覆盖并阻挡；部署须Empty + None", EditorStyles.miniLabel);
            }
        }

        private void DrawBrushButton(MapEditorBrush brush)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = BrushColor(brush);
            var obstacle = brush == MapEditorBrush.GlassNormal ? ObstacleType.GlassNormal :
                brush == MapEditorBrush.GlassColored ? ObstacleType.GlassColored : ObstacleType.None;
            var prefab = _glassAssets.GetPrefab(obstacle);
            var thumbnail = prefab == null ? null : AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
            if (GUILayout.Toggle(_brush == brush, new GUIContent(BrushLabel(brush), thumbnail),
                "Button", GUILayout.Height(36), GUILayout.MinWidth(72))) _brush = brush;
            GUI.backgroundColor = previousColor;
        }

        private void DrawGrid()
        {
            var map = _document.Data;
            const float leftMargin = 36f;
            const float topMargin = 24f;
            _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll, GUILayout.ExpandHeight(true));
            var content = GUILayoutUtility.GetRect(leftMargin + map.Columns * _cellPixels + 8f,
                topMargin + map.Rows * _cellPixels + 8f, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            var grid = new Rect(content.x + leftMargin, content.y + topMargin, map.Columns * _cellPixels, map.Rows * _cellPixels);
            _gridControl = GUIUtility.GetControlID("FreshGuardMapGrid".GetHashCode(), FocusType.Passive, grid);
            if (_coordinateStyle == null)
                _coordinateStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            for (var column = 0; column < map.Columns; column++)
                GUI.Label(new Rect(grid.x + column * _cellPixels, content.y, _cellPixels, topMargin), column.ToString(), _coordinateStyle);
            for (var row = 0; row < map.Rows; row++)
            {
                var y = grid.y + (map.Rows - 1 - row) * _cellPixels;
                GUI.Label(new Rect(content.x, y, leftMargin, _cellPixels), row.ToString(), _coordinateStyle);
                for (var column = 0; column < map.Columns; column++)
                {
                    var coordinate = new BoardCoordinate(column, row);
                    var rect = CellRect(grid, coordinate);
                    var terrain = map.GetBaseTerrain(coordinate);
                    var obstacle = map.GetObstacle(coordinate);
                    EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.15f));
                    var inner = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
                    EditorGUI.DrawRect(inner, BaseTerrainColor(terrain));
                    var glyph = terrain == BaseTerrainType.Track ? "T" : "·";
                    if (obstacle != ObstacleType.None)
                    {
                        // 图标始终居中收进格子，四周保留可辨认的基础底色。
                        var inset = Mathf.Max(4f, _cellPixels * 0.18f);
                        var icon = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
                        var prefab = _glassAssets.GetPrefab(obstacle);
                        var thumbnail = prefab == null ? null : AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                        if (thumbnail != null) GUI.DrawTexture(icon, thumbnail, ScaleMode.ScaleToFit);
                        else
                        {
                            EditorGUI.DrawRect(icon, ObstacleColor(obstacle));
                            GUI.Label(icon, obstacle == ObstacleType.GlassNormal ? "G" : "C", _coordinateStyle);
                        }
                        glyph = "";
                    }
                    GUI.Label(inner, new GUIContent(glyph,
                        $"({column},{row}) 基础：{terrain}；障碍：{obstacle}"), _coordinateStyle);
                    if (_highlight.HasValue && _highlight.Value.Equals(coordinate)) DrawOutline(rect, Color.yellow, 3f);
                }
            }
            if (_drawing && _gestureRectangle)
            {
                var start = CellRect(grid, _gestureStart);
                var end = CellRect(grid, _gestureLast);
                var preview = Rect.MinMaxRect(Math.Min(start.xMin, end.xMin), Math.Min(start.yMin, end.yMin),
                    Math.Max(start.xMax, end.xMax), Math.Max(start.yMax, end.yMax));
                var tint = BrushColor(_gestureBrush);
                tint.a = 0.35f;
                EditorGUI.DrawRect(preview, tint);
                DrawOutline(preview, Color.white, 2f);
            }
            HandleGridInput(grid);
            EditorGUILayout.EndScrollView();
        }

        private Rect CellRect(Rect grid, BoardCoordinate cell)
        {
            return new Rect(grid.x + cell.Column * _cellPixels,
                grid.y + (_document.Data.Rows - 1 - cell.Row) * _cellPixels, _cellPixels, _cellPixels);
        }

        private BoardCoordinate CoordinateAt(Rect grid, Vector2 position)
        {
            return new BoardCoordinate(
                Mathf.Clamp(Mathf.FloorToInt((position.x - grid.x) / _cellPixels), 0, _document.Data.Columns - 1),
                Mathf.Clamp(_document.Data.Rows - 1 - Mathf.FloorToInt((position.y - grid.y) / _cellPixels), 0, _document.Data.Rows - 1));
        }

        private void HandleGridInput(Rect grid)
        {
            var current = Event.current;
            if (!_drawing && current.type == EventType.MouseDown && (current.button == 0 || current.button == 1) && grid.Contains(current.mousePosition))
            {
                GUI.FocusControl(null);
                // 整笔固定按钮、画笔和矩形模式；切换调色板不能改变已开始的手势。
                _gestureButton = current.button;
                _gestureBrush = current.button == 1 ? MapEditorBrush.RemoveObstacle : _brush;
                _gestureRectangle = _rectangleTool;
                _gestureStart = _gestureLast = CoordinateAt(grid, current.mousePosition);
                BeginEdit(_gestureRectangle ? "地图矩形填充" : "地图连续绘制");
                _drawing = true;
                GUIUtility.hotControl = _gridControl;
                if (!_gestureRectangle)
                {
                    _document.Paint(_gestureStart, _gestureBrush);
                    ValidateDocument(false);
                }
                current.Use();
                Repaint();
            }
            else if (_drawing && current.type == EventType.MouseDrag && current.button == _gestureButton)
            {
                var coordinate = CoordinateAt(grid, current.mousePosition);
                if (!_gestureRectangle)
                {
                    _document.PaintLine(_gestureLast, coordinate, _gestureBrush);
                    ValidateDocument(false);
                }
                _gestureLast = coordinate;
                current.Use();
                Repaint();
            }
            else if (_drawing && current.rawType == EventType.MouseUp && current.button == _gestureButton)
            {
                var coordinate = CoordinateAt(grid, current.mousePosition);
                if (_gestureRectangle) _document.FillRectangle(_gestureStart, coordinate, _gestureBrush);
                else _document.PaintLine(_gestureLast, coordinate, _gestureBrush);
                FinishGesture();
                current.Use();
            }
            else if (_drawing && current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                // 矩形在抬起鼠标前只做预览；Esc取消预览，画笔则保留已经画出的格子。
                FinishGesture();
                current.Use();
            }
        }

        private void BeginEdit(string label)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(_document, label);
        }

        private void EndEdit()
        {
            EditorUtility.SetDirty(_document);
            if (_undoGroup >= 0) Undo.CollapseUndoOperations(_undoGroup);
            _undoGroup = -1;
            // 防止下一次编辑与本次手势自动合并。
            Undo.IncrementCurrentGroup();
        }

        private void FinishGesture()
        {
            if (!_drawing) return;
            _drawing = false;
            if (GUIUtility.hotControl == _gridControl) GUIUtility.hotControl = 0;
            EndEdit();
            ValidateDocument(false);
            Repaint();
        }

        private void ChangeDocument(string label, Action action, bool reloadAssets = false)
        {
            FinishGesture();
            BeginEdit(label);
            try { action(); }
            catch (Exception exception) { ShowFailure("地图修改失败", exception); }
            finally { EndEdit(); ValidateDocument(reloadAssets); Repaint(); }
        }

        private void ResizeDocument()
        {
            var map = _document.Data;
            var retainedColumns = Math.Min(map.Columns, _requestedColumns);
            var retainedRows = Math.Min(map.Rows, _requestedRows);
            var removedCells = map.Columns * map.Rows - retainedColumns * retainedRows;
            if ((_requestedColumns < map.Columns || _requestedRows < map.Rows) &&
                !EditorUtility.DisplayDialog("确认裁剪地图",
                    $"保留左下角{retainedColumns}列×{retainedRows}行的完整两层，移除右侧/上方共{removedCells}个格子；扩展格子填Empty + None。可撤销。", "调整", "取消"))
            {
                SyncDimensions();
                return;
            }
            ChangeDocument("调整地图尺寸", () => _document.Resize(_requestedColumns, _requestedRows));
            _highlight = null;
        }

        private void SyncDimensions()
        {
            if (_document == null || _document.Data == null) return;
            _requestedColumns = _document.Data.Columns;
            _requestedRows = _document.Data.Rows;
        }

        private void ValidateDocument(bool reloadAssets)
        {
            if (_document == null || _document.Data == null) return;
            _issues.Clear();
            foreach (var issue in MapValidator.Validate(_document.Data))
                _issues.Add(new EditorIssue
                {
                    IsError = issue.Severity == MapValidationSeverity.Error,
                    Message = issue.Message,
                    Coordinate = issue.Coordinate
                });
            if (reloadAssets)
            {
                _glassAssets.Reload(_document.Data);
                ReloadBallGeometry();
                _assetsInvalidated = false;
            }
            var hasDataError = false;
            foreach (var issue in _issues) if (issue.IsError) hasDataError = true;
            if (!string.IsNullOrEmpty(_ballGeometryError))
                _issues.Add(new EditorIssue { IsError = true, Message = _ballGeometryError });
            // 行列或格距变化时重算双方倍率，不缓存上一张地图的最终半径。
            if (!hasDataError) ValidateSceneBallClearance();
            else _ballGeometryDescription = "球体净空：请先修复地图数据错误。";
            foreach (var error in _glassAssets.Errors)
                _issues.Add(new EditorIssue { IsError = true, Message = error });
            hasUnsavedChanges = _newUnsaved || !MapEditorDocument.ContentEquals(_document.Data, _savedMap);
        }

        private void ReloadBallGeometry()
        {
            _ballGeometryError = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BALL_PREFAB);
                var ball = prefab == null ? null : prefab.GetComponent<PhysicalCollisionBall>();
                if (ball == null) throw new InvalidDataException("缺少能源球Prefab或PhysicalCollisionBall组件：" + BALL_PREFAB);
                if (!IsPositiveFinite(ball.WorldRadius))
                    throw new InvalidDataException("能源球半径或Prefab缩放无效。");
            }
            catch (Exception exception)
            {
                _ballGeometryError = "能源球资源错误：" + exception.Message;
            }
        }

        private void ValidateSceneBallClearance()
        {
            var descriptions = new List<string>();
            var foundHost = false;
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var host in root.GetComponentsInChildren<BattleShopSceneController>(true))
                    {
                        foundHost = true;
                        // EditMode 中 PlayerGlass 尚未创建；只读序列化绑定，不初始化战场。
                        using (var serialized = new SerializedObject(host))
                        {
                            var player = serialized.FindProperty("_playerBoard")?.objectReferenceValue as PlacementBoardView;
                            var opponent = serialized.FindProperty("_opponentBoard")?.objectReferenceValue as PlacementBoardView;
                            var bootstrap = host.Bootstrap;
                            var controller = host.GetComponent<BattleMapLayoutController>();
                            var label = scene.name + "/" + host.name;
                            ValidateSideBallClearance(label + " 玩家侧", host, controller, player,
                                bootstrap != null ? bootstrap.PlayerBall : null, descriptions);
                            ValidateSideBallClearance(label + " 对手侧", host, controller, opponent,
                                bootstrap != null ? bootstrap.OpponentBall : null, descriptions);
                        }
                    }
            }
            if (!foundHost)
                _issues.Add(new EditorIssue
                {
                    Message = "双方球体净空未预检：当前已加载场景缺少BattleShopSceneController，请在运行时复检。"
                });
            _ballGeometryDescription = descriptions.Count == 0
                ? "球体净空：缺少场景基准，详见警告；开局按实际碰撞体复检。"
                : "球体净空（棋盘局部半径）：" + string.Join("；", descriptions) + "；开局按实际碰撞体复检。";
        }

        private void ValidateSideBallClearance(string side, BattleShopSceneController host,
            BattleMapLayoutController controller, PlacementBoardView board, PhysicalCollisionBall ball,
            List<string> descriptions)
        {
            float radiusInBoardUnits;
            try
            {
                if (board == null || ball == null)
                    throw new InvalidOperationException("缺少绑定的棋盘或Bootstrap能源球。");
                var referenceCellSize = board.ReferenceCellSize;
                var originalWorldMatrix = board.transform.localToWorldMatrix;
                if (Application.isPlaying)
                {
                    if (controller != null && controller.TryGetReference(board, out var originalLocalScale, out var capturedCellSize))
                    {
                        referenceCellSize = capturedCellSize;
                        // 父层级不变；仅在矩阵中恢复原始局部缩放，绝不写Transform。
                        var parentMatrix = board.transform.parent != null
                            ? board.transform.parent.localToWorldMatrix : Matrix4x4.identity;
                        originalWorldMatrix = parentMatrix * Matrix4x4.TRS(Vector3.zero,
                            board.transform.localRotation, originalLocalScale);
                    }
                    else if (host.UsesTerrainMap || !Mathf.Approximately(ball.MapScale, 1f))
                        throw new InvalidOperationException("运行中尚无已捕获的棋盘原始基准。");
                }
                // 与运行时 ValidateAxes 一致；非法基准只警告，不据此计算净空硬错误。
                var x = originalWorldMatrix.MultiplyVector(Vector3.right);
                var y = originalWorldMatrix.MultiplyVector(Vector3.up);
                if (!IsPositiveFinite(x.magnitude) || !IsPositiveFinite(y.magnitude) ||
                    Mathf.Abs(x.magnitude - y.magnitude) > Mathf.Max(x.magnitude, y.magnitude) * 0.0001f ||
                    Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > 0.0001f)
                    throw new InvalidOperationException("棋盘世界XY已有非等比缩放或剪切，无法保持正方形格子：" + board.name + "。");
                var originalWorldScale = originalWorldMatrix.lossyScale;
                var boardScale = Mathf.Min(Mathf.Abs(originalWorldScale.x), Mathf.Abs(originalWorldScale.y));
                if (!IsPositiveFinite(referenceCellSize) || !IsPositiveFinite(boardScale) || !IsPositiveFinite(ball.MapScale))
                    throw new InvalidOperationException("参考格距、棋盘原始缩放或能源球运行倍率无效。");
                var map = _document.Data;
                var fitScale = MapGridMath.GetFitScale(map.Columns, map.Rows, map.CellSize, referenceCellSize);
                var cellScale = fitScale * map.CellSize / referenceCellSize;
                var originalRadius = ball.WorldRadius / ball.MapScale;
                radiusInBoardUnits = originalRadius * cellScale / (boardScale * fitScale);
                if (!IsPositiveFinite(fitScale) || !IsPositiveFinite(cellScale) || !IsPositiveFinite(radiusInBoardUnits))
                    throw new InvalidOperationException("能源球原始半径或地图适配倍率无效。");
            }
            catch (Exception exception)
            {
                descriptions.Add(side + "未预检");
                _issues.Add(new EditorIssue { Message = side + "球体净空未预检：" + exception.Message + "请在运行时复检。" });
                return;
            }
            descriptions.Add(side + " " + radiusInBoardUnits.ToString("G4"));
            foreach (var issue in MapValidator.ValidateBallClearance(_document.Data, radiusInBoardUnits))
                _issues.Add(new EditorIssue
                {
                    IsError = issue.Severity == MapValidationSeverity.Error,
                    Message = side + "：" + issue.Message,
                    Coordinate = issue.Coordinate
                });
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private void DrawIssues()
        {
            var errors = 0;
            foreach (var issue in _issues) if (issue.IsError) errors++;
            EditorGUILayout.LabelField($"校验：{errors}个错误，{_issues.Count - errors}个警告（错误禁止保存；点击坐标定位）", EditorStyles.boldLabel);
            if (_issues.Count == 0) return;
            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.Height(Math.Min(140, 28 * _issues.Count + 10)));
            foreach (var issue in _issues)
            {
                var prefix = issue.IsError ? "错误：" : "警告：";
                if (issue.Coordinate.HasValue)
                {
                    var cell = issue.Coordinate.Value;
                    prefix += $"({cell.Column},{cell.Row}) ";
                }
                var content = new GUIContent(prefix + issue.Message, issue.Message);
                if (GUILayout.Button(content, EditorStyles.miniButton) && issue.Coordinate.HasValue)
                {
                    _highlight = issue.Coordinate;
                    var cell = issue.Coordinate.Value;
                    _gridScroll = new Vector2(Mathf.Max(0f, (cell.Column - 3) * _cellPixels),
                        Mathf.Max(0f, (_document.Data.Rows - 1 - cell.Row - 3) * _cellPixels));
                    Repaint();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool ConfirmReplace()
        {
            FinishGesture();
            if (_recoveryError != null)
                return EditorUtility.DisplayDialog("确认放弃无法恢复的内存文档",
                    _recoveryError + "\n重新载入或新建会放弃当前保留的内存快照，未保存内容可能丢失。", "放弃并继续", "取消");
            if (!hasUnsavedChanges) return true;
            var choice = EditorUtility.DisplayDialogComplex("地图尚未保存", "是否先保存当前地图？", "保存", "取消", "不保存");
            return choice == 2 || (choice == 0 && TrySave(false));
        }

        private void NewDocument()
        {
            if (!ConfirmReplace()) return;
            SetDocument(MapData.Create(), "", true);
        }

        private void LoadDocument()
        {
            var initialDirectory = !string.IsNullOrEmpty(_filePath) ? Path.GetDirectoryName(_filePath) :
                (AssetDatabase.IsValidFolder(DEFAULT_DIRECTORY) ? DEFAULT_DIRECTORY : Application.dataPath);
            var path = EditorUtility.OpenFilePanel("加载地图二进制", initialDirectory, "bytes");
            if (string.IsNullOrEmpty(path)) return;
            MapData candidate;
            ushort sourceVersion;
            try
            {
                if (!string.Equals(Path.GetExtension(path), ".bytes", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("仅支持.bytes地图二进制。");
                candidate = MapBinarySerializer.Deserialize(File.ReadAllBytes(path), out sourceVersion);
            }
            catch (Exception exception)
            {
                ShowFailure("加载失败", exception);
                return;
            }
            if (!ConfirmReplace()) return;
            SetDocument(candidate, Path.GetFullPath(path), false, sourceVersion);
        }

        private void SetDocument(MapData data, string path, bool isNew, ushort sourceVersion = 0)
        {
            FinishGesture();
            if (_document == null)
            {
                _document = CreateInstance<MapEditorDocument>();
                _document.name = "Map Editor Document";
                _document.hideFlags = HideFlags.HideAndDontSave;
            }
            Undo.ClearUndo(_document);
            _document.Replace(data);
            _savedMap = data.Clone();
            _savedState = MapEditorMapSnapshot.Capture(_savedMap);
            _sourceVersion = sourceVersion;
            _memoryMigrationNotice = false;
            _recoveryError = null;
            _filePath = path;
            _newUnsaved = isNew;
            _highlight = null;
            _gridScroll = Vector2.zero;
            SyncDimensions();
            ValidateDocument(true);
            Repaint();
        }

        private bool TrySave(bool saveAs)
        {
            FinishGesture();
            if (_recoveryError != null)
            {
                EditorUtility.DisplayDialog("无法保存地图", _recoveryError, "确定");
                return false;
            }
            ValidateDocument(true);
            foreach (var issue in _issues)
                if (issue.IsError)
                {
                    EditorUtility.DisplayDialog("无法保存地图", "请先修复校验列表中的错误。警告允许导出。\n" + issue.Message, "确定");
                    return false;
                }
            try
            {
                if (_document.Data.Version != 2)
                    throw new InvalidDataException("编辑器仅允许保存V2地图，请重新载入并迁移。");
                var bytes = MapBinarySerializer.Serialize(_document.Data);
                if (bytes.Length < MapBinarySerializer.HEADER_SIZE || bytes[4] != 2 || bytes[5] != 0)
                    throw new InvalidDataException("序列化器未输出FGMP V2，已阻止写入。");
                var path = _filePath;
                if (saveAs || string.IsNullOrEmpty(path) || !ValidSaveName(path))
                {
                    EnsureAssetFolder(DEFAULT_DIRECTORY);
                    var initialName = string.IsNullOrEmpty(path) ? "Map_New" : Path.GetFileNameWithoutExtension(path);
                    if (!initialName.StartsWith("Map_", StringComparison.Ordinal)) initialName = "Map_" + initialName;
                    path = EditorUtility.SaveFilePanelInProject("保存地图二进制", initialName, "bytes",
                        "文件名必须是Map_*.bytes。", DEFAULT_DIRECTORY);
                    if (string.IsNullOrEmpty(path)) return false;
                }
                if (!ValidSaveName(path)) throw new InvalidDataException("地图文件名必须是Map_*.bytes，且Map_后必须有名称。");
                path = Path.GetFullPath(path);
                WriteAtomically(path, bytes);
                // 只导入用户此次保存的文件，不触发全工程Refresh或保存其他资源。
                var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.ImportAsset("Assets/" + path.Substring(assetsRoot.Length).Replace('\\', '/'));
                _filePath = path;
                _savedMap = _document.Data.Clone();
                _savedState = MapEditorMapSnapshot.Capture(_savedMap);
                _sourceVersion = 2;
                _memoryMigrationNotice = false;
                _newUnsaved = false;
                hasUnsavedChanges = false;
                ShowNotification(new GUIContent("已保存 V2：" + Path.GetFileName(path)));
                return true;
            }
            catch (Exception exception)
            {
                ShowFailure("保存失败", exception);
                return false;
            }
        }

        private static bool ValidSaveName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith("Map_", StringComparison.Ordinal) && name.Length > 4 &&
                string.Equals(Path.GetExtension(path), ".bytes", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            if (slash <= 0) throw new IOException("无效的Assets目录：" + path);
            var parent = path.Substring(0, slash);
            EnsureAssetFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, path.Substring(slash + 1))))
                throw new IOException("无法创建地图目录：" + path);
        }

        private static void WriteAtomically(string path, byte[] bytes)
        {
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private void HandleShortcuts()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown || (!current.control && !current.command) || current.keyCode != KeyCode.S) return;
            TrySave(current.shift);
            current.Use();
        }

        private static void ShowFailure(string title, Exception exception)
        {
            Debug.LogWarning("[Map Editor] " + title + "：" + exception.Message);
            EditorUtility.DisplayDialog(title, exception.Message, "确定");
        }

        private static Color BaseTerrainColor(BaseTerrainType terrain)
        {
            switch (terrain)
            {
                case BaseTerrainType.Track: return new Color(0.49f, 0.46f, 0.38f);
                case BaseTerrainType.Empty: return new Color(0.25f, 0.40f, 0.30f);
                default: return Color.red;
            }
        }

        private static Color ObstacleColor(ObstacleType obstacle)
        {
            switch (obstacle)
            {
                case ObstacleType.None: return new Color(0.55f, 0.55f, 0.55f);
                case ObstacleType.GlassNormal: return new Color(0.35f, 0.77f, 0.89f);
                case ObstacleType.GlassColored: return new Color(0.82f, 0.44f, 0.78f);
                default: return Color.red;
            }
        }

        private static Color BrushColor(MapEditorBrush brush)
        {
            switch (brush)
            {
                case MapEditorBrush.Track: return BaseTerrainColor(BaseTerrainType.Track);
                case MapEditorBrush.Empty: return BaseTerrainColor(BaseTerrainType.Empty);
                case MapEditorBrush.GlassNormal: return ObstacleColor(ObstacleType.GlassNormal);
                case MapEditorBrush.GlassColored: return ObstacleColor(ObstacleType.GlassColored);
                case MapEditorBrush.RemoveObstacle: return ObstacleColor(ObstacleType.None);
                case MapEditorBrush.ResetCell: return new Color(0.7f, 0.65f, 0.5f);
                default: return Color.red;
            }
        }

        private static string BrushLabel(MapEditorBrush brush)
        {
            switch (brush)
            {
                case MapEditorBrush.Track: return "轨道 Track";
                case MapEditorBrush.Empty: return "空位 Empty";
                case MapEditorBrush.RemoveObstacle: return "无障碍 None";
                case MapEditorBrush.GlassNormal: return "普通玻璃";
                case MapEditorBrush.GlassColored: return "彩色玻璃";
                case MapEditorBrush.ResetCell: return "重置格子";
                default: return "?";
            }
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
