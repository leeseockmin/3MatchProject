using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ThreeMatch
{
    public sealed class Match3Game : MonoBehaviour
    {
        private static readonly Color SpecialTileBaseColor = new(0.78f, 0.82f, 0.9f);

        [SerializeField] private int width = 8;
        [SerializeField] private int height = 8;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float specialEffectDelay = 1f;
        [SerializeField] private float colorLinePreviewDuration = 2f;
        [SerializeField] private float comboPopupLifetime = 0.9f;
        [SerializeField] private Vector3 comboPopupOffset = new(0f, 0.65f, 0f);
        [SerializeField] private AudioClip swapClip;
        [SerializeField] private AudioClip matchClip;
        [SerializeField] private AudioClip comboClip;
        [SerializeField] private AudioClip specialClip;

        private readonly Queue<TileView> _tilePool = new();
        private readonly Dictionary<SpecialTileType, Sprite> _icons = new();
        private readonly List<ComboPopup> _comboPopups = new();

        private TileData[,] _board;
        private TileView[,] _views;
        private Transform _tileRoot;
        private Transform _effectRoot;
        private Camera _mainCamera;
        private AudioSource _audioSource;
        private Sprite _tileSprite;
        private Sprite _effectSprite;
        private bool _isBusy;
        private TileView _selected;
        private TileView _pressedTile;
        private bool _pendingSpecialTap;
        private bool _dragConsumed;
        private int _score;
        private int _displayMultiplier = 1;
        private string _effectLabel = "Idle";
        private string _comboStatus = "Ready";
        private GUIStyle _hudPanelStyle;
        private GUIStyle _hudLabelStyle;
        private GUIStyle _hudValueStyle;
        private GUIStyle _hudScoreStyle;
        private GUIStyle _popupStyle;

        private void Awake()
        {
            _mainCamera = Camera.main;
            _tileSprite = BuildSolidSprite(32, Color.white);
            _effectSprite = BuildRingSprite();
            _icons[SpecialTileType.ColorClear] = LoadOrBuildIcon("Match3Icons/icon_match3_colorclear_64", BuildColorClearIcon);
            _icons[SpecialTileType.RowClear] = LoadOrBuildIcon("Match3Icons/icon_match3_rowclear_64", BuildRowClearIcon);
            _icons[SpecialTileType.ColumnClear] = LoadOrBuildIcon("Match3Icons/icon_match3_columnclear_64", BuildColumnClearIcon);

            _tileRoot = new GameObject("TileRoot").transform;
            _tileRoot.SetParent(transform, false);
            _effectRoot = new GameObject("EffectRoot").transform;
            _effectRoot.SetParent(transform, false);
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;

            _board = new TileData[width, height];
            _views = new TileView[width, height];

            FitCamera();
            BuildBoard();
        }

        private void Update()
        {
            UpdateComboPopups();
            if (_isBusy) return;

            if (Input.GetMouseButtonDown(0))
            {
                HandlePointerDown();
            }

            if (Input.GetMouseButton(0))
            {
                HandlePointerDrag();
            }

            if (Input.GetMouseButtonUp(0))
            {
                HandlePointerUp();
            }
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            DrawHud();
            DrawComboPopups();
        }

        private IEnumerator TrySwap(TileView a, TileView b)
        {
            _isBusy = true;
            Deselect();

            Vector2Int aPos = a.GridPosition;
            Vector2Int bPos = b.GridPosition;

            SwapBoardValues(aPos, bPos);
            UpdateViewMapping(a, b, aPos, bPos);
            RefreshTileVisual(a, _board[bPos.x, bPos.y]);
            RefreshTileVisual(b, _board[aPos.x, aPos.y]);
            TriggerSound(swapClip);
            yield return AnimateSwap(a, b);

            ActivationResult activation = BuildActivation(aPos, bPos);
            if (activation.Cells.Count > 0)
            {
                yield return ResolveBoard(activation.Cells, null, activation.Multiplier, activation.Origins, activation.Label, true, activation.SuppressedSpecials, activation.PreviewCells, activation.PreviewSpecial, activation.PreviewDuration);
                _isBusy = false;
                yield break;
            }

            List<MatchPattern> matches = FindMatchPatterns(aPos, bPos);
            if (matches.Count == 0)
            {
                SwapBoardValues(aPos, bPos);
                UpdateViewMapping(a, b, bPos, aPos);
                RefreshTileVisual(a, _board[aPos.x, aPos.y]);
                RefreshTileVisual(b, _board[bPos.x, bPos.y]);
                yield return AnimateSwap(a, b);
                _isBusy = false;
                yield break;
            }

            MatchPattern spawn = ChooseSpawn(matches);
            yield return ResolveBoard(BuildClearSet(matches), spawn, 1, null, "Match", false);
            _isBusy = false;
        }

        private IEnumerator ActivateTappedSpecial(TileView tile)
        {
            _isBusy = true;
            Deselect();
            _pressedTile = null;
            _pendingSpecialTap = false;
            _dragConsumed = false;

            Vector2Int origin = tile.GridPosition;
            ActivationResult activation = BuildTapActivation(origin);
            if (activation.Cells.Count > 0)
            {
                TriggerSound(specialClip);
                yield return ResolveBoard(activation.Cells, null, activation.Multiplier, activation.Origins, activation.Label, true, activation.SuppressedSpecials, activation.PreviewCells, activation.PreviewSpecial, activation.PreviewDuration);
            }

            _isBusy = false;
        }

        private void HandlePointerDown()
        {
            if (!TryGetTileUnderPointer(out TileView tile))
            {
                _pressedTile = null;
                _pendingSpecialTap = false;
                _dragConsumed = false;
                return;
            }

            _pressedTile = tile;
            _pendingSpecialTap = HasSpecial(tile.GridPosition);
            _dragConsumed = false;

            if (_selected != tile)
            {
                Select(tile);
            }
        }

        private void HandlePointerDrag()
        {
            if (_pressedTile == null || _dragConsumed || !TryGetTileUnderPointer(out TileView hoverTile)) return;
            if (hoverTile == _pressedTile) return;
            if (!AreAdjacent(_pressedTile.GridPosition, hoverTile.GridPosition)) return;

            _dragConsumed = true;
            _pendingSpecialTap = false;
            StartCoroutine(TrySwap(_pressedTile, hoverTile));
        }

        private void HandlePointerUp()
        {
            if (_dragConsumed)
            {
                _pressedTile = null;
                _pendingSpecialTap = false;
                _dragConsumed = false;
                return;
            }

            if (_pendingSpecialTap && _pressedTile != null)
            {
                StartCoroutine(ActivateTappedSpecial(_pressedTile));
                return;
            }

            if (_pressedTile != null && _selected == _pressedTile && !HasSpecial(_pressedTile.GridPosition))
            {
                _pressedTile = null;
                _pendingSpecialTap = false;
                _dragConsumed = false;
                return;
            }

            _pressedTile = null;
            _pendingSpecialTap = false;
            _dragConsumed = false;
        }

        private bool TryGetTileUnderPointer(out TileView tile)
        {
            tile = null;
            if (_mainCamera == null) return false;

            Vector3 world = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            RaycastHit2D hit = Physics2D.Raycast(world, Vector2.zero);
            return hit.collider && hit.collider.TryGetComponent(out tile);
        }

        private IEnumerator ResolveBoard(HashSet<Vector2Int> initialClear, MatchPattern spawn, int baseMultiplier, List<Vector3> origins, string label, bool specialStart, HashSet<Vector2Int> suppressedSpecials = null, HashSet<Vector2Int> previewCells = null, SpecialTileType previewSpecial = SpecialTileType.None, float previewDuration = 0f)
        {
            HashSet<Vector2Int> clearSet = initialClear;
            MatchPattern currentSpawn = spawn;
            int chain = 0;

            while (clearSet.Count > 0)
            {
                int multiplier = Mathf.Max(1, baseMultiplier + chain);
                UpdateResolutionStatus(multiplier, chain, label);
                PrepareClearSet(clearSet, currentSpawn, suppressedSpecials);
                yield return RunClearPhase(clearSet, multiplier, origins, label, chain, specialStart, previewCells, previewSpecial, previewDuration);
                ApplySpawnSpecial(currentSpawn);
                yield return RunBoardRefillPhase();
                (clearSet, currentSpawn) = FindNextCascadeState();
                chain++;
                specialStart = false;
                suppressedSpecials = null;
                previewCells = null;
                previewSpecial = SpecialTileType.None;
                previewDuration = 0f;
            }

            ResetResolutionStatus();
        }

        private void UpdateResolutionStatus(int multiplier, int chain, string label)
        {
            _displayMultiplier = multiplier;
            _effectLabel = chain == 0 ? label : $"Cascade x{multiplier}";
            _comboStatus = multiplier > 1 ? $"Combo x{multiplier}" : "Single Match";
        }

        private void PrepareClearSet(HashSet<Vector2Int> clearSet, MatchPattern currentSpawn, HashSet<Vector2Int> suppressedSpecials)
        {
            ExpandTriggeredSpecials(clearSet, suppressedSpecials);

            if (currentSpawn != null && clearSet.Contains(currentSpawn.SpawnCell))
            {
                clearSet.Remove(currentSpawn.SpawnCell);
            }
        }

        private IEnumerator RunClearPhase(HashSet<Vector2Int> clearSet, int multiplier, List<Vector3> origins, string label, int chain, bool specialStart, HashSet<Vector2Int> previewCells, SpecialTileType previewSpecial, float previewDuration)
        {
            if (specialStart && chain == 0)
            {
                TriggerSound(specialClip);
                yield return ShowPreviewIcons(previewCells, previewSpecial, previewDuration);
                yield return PlaySpecialEffect(clearSet, origins, specialEffectDelay);
            }

            _score += clearSet.Count * 10 * multiplier;
            TriggerSound(multiplier > 1 ? comboClip : matchClip);
            ShowComboPopup(multiplier, clearSet, label, chain);
            RemoveClearedCells(clearSet);
        }

        private void RemoveClearedCells(HashSet<Vector2Int> clearSet)
        {
            foreach (Vector2Int cell in clearSet)
            {
                SpawnBurst(GridToWorld(cell.x, cell.y), ToUnityColor(_board[cell.x, cell.y].Color), 0.45f);
                RemoveCell(cell);
            }
        }

        private void ApplySpawnSpecial(MatchPattern currentSpawn)
        {
            if (currentSpawn == null || currentSpawn.SpawnSpecial == SpecialTileType.None) return;

            TileData tile = _board[currentSpawn.SpawnCell.x, currentSpawn.SpawnCell.y];
            tile.Special = currentSpawn.SpawnSpecial;
            _board[currentSpawn.SpawnCell.x, currentSpawn.SpawnCell.y] = tile;
            RefreshTileVisual(_views[currentSpawn.SpawnCell.x, currentSpawn.SpawnCell.y], tile);
            SpawnBurst(GridToWorld(currentSpawn.SpawnCell.x, currentSpawn.SpawnCell.y), Color.white, 0.35f);
        }

        private IEnumerator RunBoardRefillPhase()
        {
            yield return new WaitForSeconds(0.12f);
            CollapseColumns();
            RefillColumns();
            yield return new WaitForSeconds(0.12f);
        }

        private (HashSet<Vector2Int> clearSet, MatchPattern spawn) FindNextCascadeState()
        {
            List<MatchPattern> next = FindMatchPatterns(new Vector2Int(-1, -1), new Vector2Int(-1, -1));
            return (BuildClearSet(next), ChooseSpawn(next));
        }

        private void ResetResolutionStatus()
        {
            _displayMultiplier = 1;
            _effectLabel = "Idle";
            _comboStatus = "Ready";
        }

        private ActivationResult BuildActivation(Vector2Int aPos, Vector2Int bPos)
        {
            ActivationResult result = new();
            TileData a = _board[aPos.x, aPos.y];
            TileData b = _board[bPos.x, bPos.y];

            bool aSpecial = a.Special != SpecialTileType.None;
            bool bSpecial = b.Special != SpecialTileType.None;
            if (!aSpecial && !bSpecial) return result;

            result.Origins.Add(GridToWorld(aPos.x, aPos.y));
            result.Origins.Add(GridToWorld(bPos.x, bPos.y));

            return aSpecial && bSpecial
                ? BuildFusionActivation(result, aPos, a, bPos, b)
                : BuildSingleSpecialActivation(result, aSpecial ? aPos : bPos, aSpecial ? a : b, aSpecial ? b.Color : a.Color);
        }

        private ActivationResult BuildTapActivation(Vector2Int origin)
        {
            ActivationResult result = new();
            TileData tile = _board[origin.x, origin.y];
            if (tile.Special == SpecialTileType.None) return result;

            result.Origins.Add(GridToWorld(origin.x, origin.y));
            ApplySpecialClear(result.Cells, origin, tile, GetRandomBoardColor());
            SuppressSpecial(result, origin);
            result.Multiplier = tile.Special == SpecialTileType.ColorClear ? 3 : 2;
            result.Label = tile.Special == SpecialTileType.ColorClear ? "Random Color Burst" : LabelFor(tile.Special);
            return result;
        }

        private ActivationResult BuildSingleSpecialActivation(ActivationResult result, Vector2Int origin, TileData specialTile, TileColor pairedColor)
        {
            ApplySpecialClear(result.Cells, origin, specialTile, pairedColor);
            SuppressSpecial(result, origin);
            result.Multiplier = 2;
            result.Label = LabelFor(specialTile.Special);
            return result;
        }

        private ActivationResult BuildFusionActivation(ActivationResult result, Vector2Int aPos, TileData a, Vector2Int bPos, TileData b)
        {
            result.Multiplier = 3;

            if (AreDoubleColorClear(a, b))
            {
                AddAllCells(result.Cells);
                result.Multiplier = 5;
                result.Label = "Double Color Burst";
                return result;
            }

            if (ContainsColorClear(a, b))
            {
                return BuildColorClearFusion(result, aPos, a, bPos, b);
            }

            return BuildLineFusion(result, aPos, a, bPos, b);
        }

        private ActivationResult BuildColorClearFusion(ActivationResult result, Vector2Int aPos, TileData a, Vector2Int bPos, TileData b)
        {
            TileData other = a.Special == SpecialTileType.ColorClear ? b : a;
            TileColor targetColor = GetRandomBoardColor();
            HashSet<Vector2Int> sourceCells = CollectTilesOfColor(targetColor);

            if (IsLineSpecial(other.Special))
            {
                AddLineEffectToColor(result.Cells, sourceCells, other.Special);
                CopyCells(sourceCells, result.PreviewCells);
                result.PreviewSpecial = other.Special;
                result.PreviewDuration = colorLinePreviewDuration;
                result.Label = other.Special == SpecialTileType.RowClear ? "Color Row Storm" : "Color Column Storm";
            }
            else
            {
                CopyCells(sourceCells, result.Cells);
                AddCross(result.Cells, aPos);
                AddCross(result.Cells, bPos);
                result.Label = "Color Fusion";
            }

            result.Cells.Add(aPos);
            result.Cells.Add(bPos);
            SuppressSpecial(result, aPos, bPos);
            result.Multiplier = 4;
            return result;
        }

        private ActivationResult BuildLineFusion(ActivationResult result, Vector2Int aPos, TileData a, Vector2Int bPos, TileData b)
        {
            Vector2Int fusionCenter = bPos;
            AddCross(result.Cells, fusionCenter);
            result.Cells.Add(aPos);
            result.Cells.Add(bPos);
            SuppressSpecial(result, aPos, bPos);
            result.Label = AreLinePair(a.Special, b.Special) ? "Cross Blast" : "Line Fusion";
            return result;
        }

        private void SuppressSpecial(ActivationResult result, params Vector2Int[] positions)
        {
            foreach (Vector2Int position in positions)
            {
                result.SuppressedSpecials.Add(position);
            }
        }

        private List<MatchPattern> FindMatchPatterns(Vector2Int swapA, Vector2Int swapB)
        {
            List<MatchPattern> patterns = new();
            CollectRunPatterns(patterns, true, swapA, swapB);
            CollectRunPatterns(patterns, false, swapA, swapB);
            CollectSquarePatterns(patterns);
            return patterns;
        }

        private void CollectRunPatterns(List<MatchPattern> patterns, bool horizontal, Vector2Int swapA, Vector2Int swapB)
        {
            int primaryLimit = horizontal ? height : width;
            int secondaryLimit = horizontal ? width : height;

            for (int fixedAxis = 0; fixedAxis < primaryLimit; fixedAxis++)
            {
                int runStart = 0;
                for (int offset = 1; offset <= secondaryLimit; offset++)
                {
                    bool same = offset < secondaryLimit && AreRunNeighborsMatching(horizontal, fixedAxis, offset);
                    if (same) continue;
                    AddRunPattern(patterns, runStart, offset - 1, horizontal, fixedAxis, swapA, swapB);
                    runStart = offset;
                }
            }
        }

        private bool AreRunNeighborsMatching(bool horizontal, int fixedAxis, int offset)
        {
            Vector2Int current = horizontal ? new Vector2Int(offset, fixedAxis) : new Vector2Int(fixedAxis, offset);
            Vector2Int previous = horizontal ? new Vector2Int(offset - 1, fixedAxis) : new Vector2Int(fixedAxis, offset - 1);
            return HasNormalTile(current) &&
                   HasNormalTile(previous) &&
                   _board[current.x, current.y].Color == _board[previous.x, previous.y].Color;
        }

        private void CollectSquarePatterns(List<MatchPattern> patterns)
        {
            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    TryAddSquarePattern(patterns, x, y);
                }
            }
        }

        private void TryAddSquarePattern(List<MatchPattern> patterns, int x, int y)
        {
            Vector2Int bottomLeft = new(x, y);
            Vector2Int bottomRight = new(x + 1, y);
            Vector2Int topLeft = new(x, y + 1);
            Vector2Int topRight = new(x + 1, y + 1);

            if (!HasNormalTile(bottomLeft) ||
                !HasNormalTile(bottomRight) ||
                !HasNormalTile(topLeft) ||
                !HasNormalTile(topRight))
            {
                return;
            }

            TileColor color = _board[x, y].Color;
            if (_board[x + 1, y].Color != color || _board[x, y + 1].Color != color || _board[x + 1, y + 1].Color != color) return;

            MatchPattern square = new();
            square.Cells.Add(bottomLeft);
            square.Cells.Add(bottomRight);
            square.Cells.Add(topLeft);
            square.Cells.Add(topRight);
            patterns.Add(square);
        }

        private void AddRunPattern(List<MatchPattern> patterns, int start, int end, bool horizontal, int fixedAxis, Vector2Int swapA, Vector2Int swapB)
        {
            int length = end - start + 1;
            if (length < 3) return;

            MatchPattern pattern = new();
            for (int i = start; i <= end; i++)
            {
                pattern.Cells.Add(horizontal ? new Vector2Int(i, fixedAxis) : new Vector2Int(fixedAxis, i));
            }

            if (length >= 5)
            {
                pattern.SpawnSpecial = SpecialTileType.ColorClear;
                pattern.Priority = 3;
            }
            else if (length == 4)
            {
                pattern.SpawnSpecial = horizontal ? SpecialTileType.RowClear : SpecialTileType.ColumnClear;
                pattern.Priority = 2;
            }

            pattern.SpawnCell = ChooseSpawnCell(pattern.Cells, swapA, swapB);
            patterns.Add(pattern);
        }

        private static HashSet<Vector2Int> BuildClearSet(List<MatchPattern> patterns)
        {
            HashSet<Vector2Int> clearSet = new();
            foreach (MatchPattern pattern in patterns)
            {
                foreach (Vector2Int cell in pattern.Cells) clearSet.Add(cell);
            }
            return clearSet;
        }

        private static MatchPattern ChooseSpawn(List<MatchPattern> patterns)
        {
            MatchPattern best = null;
            foreach (MatchPattern pattern in patterns)
            {
                if (pattern.SpawnSpecial == SpecialTileType.None) continue;
                if (best == null || pattern.Priority > best.Priority) best = pattern;
            }
            return best;
        }

        private void ApplySpecialClear(HashSet<Vector2Int> clearSet, Vector2Int origin, TileData tile, TileColor pairedColor)
        {
            switch (tile.Special)
            {
                case SpecialTileType.ColorClear:
                    AddTilesOfColor(clearSet, pairedColor);
                    clearSet.Add(origin);
                    break;
                case SpecialTileType.RowClear:
                    AddRow(clearSet, origin.y);
                    break;
                case SpecialTileType.ColumnClear:
                    AddColumn(clearSet, origin.x);
                    break;
            }
        }

        private void ExpandTriggeredSpecials(HashSet<Vector2Int> clearSet, HashSet<Vector2Int> suppressedSpecials = null)
        {
            Queue<Vector2Int> pending = new();
            HashSet<Vector2Int> processed = new();

            foreach (Vector2Int cell in clearSet)
            {
                if (suppressedSpecials != null && suppressedSpecials.Contains(cell)) continue;
                if (HasSpecial(cell))
                {
                    pending.Enqueue(cell);
                }
            }

            while (pending.Count > 0)
            {
                Vector2Int origin = pending.Dequeue();
                if (!processed.Add(origin) || _views[origin.x, origin.y] == null) continue;

                TileData tile = _board[origin.x, origin.y];
                if (tile.Special == SpecialTileType.None) continue;

                ApplySpecialClear(clearSet, origin, tile, GetRandomBoardColor());

                foreach (Vector2Int cell in clearSet)
                {
                    if (suppressedSpecials != null && suppressedSpecials.Contains(cell)) continue;
                    if (!processed.Contains(cell) && HasSpecial(cell))
                    {
                        pending.Enqueue(cell);
                    }
                }
            }
        }

        private void AddAllCells(HashSet<Vector2Int> clearSet)
        {
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (_views[x, y] != null) clearSet.Add(new Vector2Int(x, y));
        }

        private void AddTilesOfColor(HashSet<Vector2Int> clearSet, TileColor color)
        {
            CopyCells(CollectTilesOfColor(color), clearSet);
        }

        private void AddLineEffectToColor(HashSet<Vector2Int> clearSet, TileColor color, SpecialTileType lineType)
        {
            AddLineEffectToColor(clearSet, CollectTilesOfColor(color), lineType);
        }

        private void AddLineEffectToColor(HashSet<Vector2Int> clearSet, HashSet<Vector2Int> sourceCells, SpecialTileType lineType)
        {
            foreach (Vector2Int cell in sourceCells)
            {
                ApplyLineClear(clearSet, cell, lineType);
            }
        }

        private HashSet<Vector2Int> CollectTilesOfColor(TileColor color)
        {
            HashSet<Vector2Int> cells = new();
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (_views[x, y] != null &&
                    _board[x, y].Special == SpecialTileType.None &&
                    _board[x, y].Color == color)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            return cells;
        }

        private static void CopyCells(HashSet<Vector2Int> source, HashSet<Vector2Int> destination)
        {
            foreach (Vector2Int cell in source)
            {
                destination.Add(cell);
            }
        }

        private void ApplyLineClear(HashSet<Vector2Int> clearSet, Vector2Int origin, SpecialTileType lineType)
        {
            if (lineType == SpecialTileType.RowClear)
            {
                AddRow(clearSet, origin.y);
            }
            else if (lineType == SpecialTileType.ColumnClear)
            {
                AddColumn(clearSet, origin.x);
            }
        }

        private void AddRow(HashSet<Vector2Int> clearSet, int row)
        {
            for (int x = 0; x < width; x++) if (_views[x, row] != null) clearSet.Add(new Vector2Int(x, row));
        }

        private void AddColumn(HashSet<Vector2Int> clearSet, int col)
        {
            for (int y = 0; y < height; y++) if (_views[col, y] != null) clearSet.Add(new Vector2Int(col, y));
        }

        private void AddCross(HashSet<Vector2Int> clearSet, Vector2Int center)
        {
            AddRow(clearSet, center.y);
            AddColumn(clearSet, center.x);
        }

        private bool HasNormalTile(Vector2Int cell)
        {
            return _board[cell.x, cell.y].Special == SpecialTileType.None;
        }

        private bool HasSpecial(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width &&
                   cell.y >= 0 && cell.y < height &&
                   _views[cell.x, cell.y] != null &&
                   _board[cell.x, cell.y].Special != SpecialTileType.None;
        }

        private TileColor GetRandomBoardColor()
        {
            List<TileColor> colors = new();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (_views[x, y] == null) continue;
                    if (_board[x, y].Special != SpecialTileType.None) continue;
                    TileColor color = _board[x, y].Color;
                    if (!colors.Contains(color))
                    {
                        colors.Add(color);
                    }
                }
            }

            if (colors.Count == 0)
            {
                return TileColor.Blue;
            }

            return colors[Random.Range(0, colors.Count)];
        }

        private IEnumerator PlaySpecialEffect(HashSet<Vector2Int> clearSet, List<Vector3> origins, float duration)
        {
            if (origins != null)
            {
                foreach (Vector3 origin in origins) SpawnBurst(origin, Color.white, duration);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float pulse = 0.4f + Mathf.Abs(Mathf.Sin(elapsed * 10f)) * 0.5f;
                foreach (Vector2Int cell in clearSet)
                {
                    TileView view = _views[cell.x, cell.y];
                    if (view != null) view.SetPulse(pulse);
                }
                yield return null;
            }

            foreach (Vector2Int cell in clearSet)
            {
                TileView view = _views[cell.x, cell.y];
                if (view != null) view.SetPulse(0f);
            }
        }

        private IEnumerator ShowPreviewIcons(HashSet<Vector2Int> previewCells, SpecialTileType previewSpecial, float duration)
        {
            if (previewCells == null || previewCells.Count == 0 || previewSpecial == SpecialTileType.None || duration <= 0f)
            {
                yield break;
            }

            Sprite icon = _icons[previewSpecial];
            foreach (Vector2Int cell in previewCells)
            {
                TileView view = _views[cell.x, cell.y];
                if (view != null) view.SetPreviewIcon(icon);
            }

            yield return new WaitForSeconds(duration);

            foreach (Vector2Int cell in previewCells)
            {
                TileView view = _views[cell.x, cell.y];
                if (view != null) view.RestoreAppliedIcon();
            }
        }

        private void BuildBoard()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    TileColor color = RollColorFor(x, y);
                    TileData tile = new(color);
                    _board[x, y] = tile;
                    CreateOrReuseView(x, y, tile);
                }
            }
        }

        private void CollapseColumns()
        {
            for (int x = 0; x < width; x++)
            {
                int writeY = 0;
                for (int readY = 0; readY < height; readY++)
                {
                    if (_views[x, readY] == null) continue;
                    if (writeY != readY)
                    {
                        _board[x, writeY] = _board[x, readY];
                        _views[x, writeY] = _views[x, readY];
                        _views[x, writeY].SetGridPosition(new Vector2Int(x, writeY), GridToWorld(x, writeY));
                        _views[x, readY] = null;
                    }
                    writeY++;
                }
            }
        }

        private void RefillColumns()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (_views[x, y] != null) continue;
                    TileData tile = new((TileColor)Random.Range(0, 4));
                    _board[x, y] = tile;
                    CreateOrReuseView(x, y, tile);
                }
            }
        }

        private void RemoveCell(Vector2Int cell)
        {
            TileView view = _views[cell.x, cell.y];
            if (view == null) return;
            _board[cell.x, cell.y] = default;
            _views[cell.x, cell.y] = null;
            view.gameObject.SetActive(false);
            _tilePool.Enqueue(view);
        }

        private void CreateOrReuseView(int x, int y, TileData tile)
        {
            TileView view = _tilePool.Count > 0 ? _tilePool.Dequeue() : CreateTileView();
            view.gameObject.SetActive(true);
            view.transform.SetParent(_tileRoot, false);
            view.SetGridPosition(new Vector2Int(x, y), GridToWorld(x, y));
            view.Apply(tile, GetTileVisualColor(tile), tile.Special == SpecialTileType.None ? null : _icons[tile.Special]);
            _views[x, y] = view;
        }

        private TileView CreateTileView()
        {
            GameObject tileObject = new("Tile");
            tileObject.transform.localScale = Vector3.one * (tileSize * 0.92f);
            SpriteRenderer renderer = tileObject.AddComponent<SpriteRenderer>();
            renderer.sprite = _tileSprite;
            renderer.sortingOrder = 1;
            BoxCollider2D collider = tileObject.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;

            GameObject iconObject = new("Icon");
            iconObject.transform.SetParent(tileObject.transform, false);
            iconObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            iconObject.transform.localScale = Vector3.one * 0.9f;
            SpriteRenderer iconRenderer = iconObject.AddComponent<SpriteRenderer>();
            iconRenderer.sortingOrder = 2;

            TileView view = tileObject.AddComponent<TileView>();
            view.Initialize(renderer, iconRenderer);
            return view;
        }

        private void RefreshTileVisual(TileView view, TileData tile)
        {
            if (view == null) return;
            view.Apply(tile, GetTileVisualColor(tile), tile.Special == SpecialTileType.None ? null : _icons[tile.Special]);
        }

        private void SpawnBurst(Vector3 position, Color color, float duration)
        {
            StartCoroutine(BurstRoutine(position, color, duration));
        }

        private IEnumerator BurstRoutine(Vector3 position, Color color, float duration)
        {
            GameObject burst = new("Burst");
            burst.transform.SetParent(_effectRoot, false);
            burst.transform.position = position;
            SpriteRenderer renderer = burst.AddComponent<SpriteRenderer>();
            renderer.sprite = _effectSprite;
            renderer.sortingOrder = 4;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                burst.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.6f, t);
                renderer.color = new Color(color.r, color.g, color.b, Mathf.Lerp(0.9f, 0f, t));
                yield return null;
            }

            Destroy(burst);
        }

        private void Select(TileView tile)
        {
            if (_selected != null) _selected.SetSelected(false);
            _selected = tile;
            _selected.SetSelected(true);
        }

        private void Deselect()
        {
            if (_selected == null) return;
            _selected.SetSelected(false);
            _selected = null;
        }

        private IEnumerator AnimateSwap(TileView a, TileView b)
        {
            Vector3 startA = a.transform.position;
            Vector3 startB = b.transform.position;
            float elapsed = 0f;
            const float duration = 0.12f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                a.transform.position = Vector3.Lerp(startA, startB, t);
                b.transform.position = Vector3.Lerp(startB, startA, t);
                yield return null;
            }
            a.transform.position = startB;
            b.transform.position = startA;
        }

        private void SwapBoardValues(Vector2Int a, Vector2Int b)
        {
            (_board[a.x, a.y], _board[b.x, b.y]) = (_board[b.x, b.y], _board[a.x, a.y]);
        }

        private void UpdateViewMapping(TileView a, TileView b, Vector2Int aPos, Vector2Int bPos)
        {
            _views[aPos.x, aPos.y] = b;
            _views[bPos.x, bPos.y] = a;
            b.SetGridPosition(aPos, b.transform.position);
            a.SetGridPosition(bPos, a.transform.position);
        }

        private TileColor RollColorFor(int x, int y)
        {
            List<TileColor> available = new() { TileColor.Blue, TileColor.Red, TileColor.Green, TileColor.Yellow };
            if (x >= 2 && _board[x - 1, y].Color == _board[x - 2, y].Color) available.Remove(_board[x - 1, y].Color);
            if (y >= 2 && _board[x, y - 1].Color == _board[x, y - 2].Color) available.Remove(_board[x, y - 1].Color);
            return available[Random.Range(0, available.Count)];
        }

        private Vector3 GridToWorld(int x, int y)
        {
            float offsetX = (width - 1) * tileSize * 0.5f;
            float offsetY = (height - 1) * tileSize * 0.5f;
            return new Vector3(x * tileSize - offsetX, y * tileSize - offsetY, 0f);
        }

        private void FitCamera()
        {
            if (_mainCamera == null) return;
            _mainCamera.orthographic = true;
            _mainCamera.transform.position = new Vector3(0f, 0f, -10f);
            _mainCamera.backgroundColor = new Color(0.09f, 0.1f, 0.14f);
            float boardHeight = height * tileSize;
            float boardWidth = width * tileSize;
            float verticalSize = boardHeight * 0.65f;
            float horizontalSize = boardWidth / _mainCamera.aspect * 0.65f;
            _mainCamera.orthographicSize = Mathf.Max(verticalSize, horizontalSize, 5f);
        }

        private void EnsureGuiStyles()
        {
            if (_hudPanelStyle != null) return;

            Texture2D panelTexture = BuildSolidTexture(new Color(0.07f, 0.1f, 0.16f, 0.92f));
            _hudPanelStyle = new GUIStyle(GUI.skin.box);
            _hudPanelStyle.normal.background = panelTexture;
            _hudPanelStyle.border = new RectOffset(10, 10, 10, 10);
            _hudPanelStyle.padding = new RectOffset(16, 16, 16, 16);

            _hudLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.7f, 0.82f, 0.95f, 0.9f) }
            };

            _hudValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _hudScoreStyle = new GUIStyle(_hudValueStyle)
            {
                fontSize = 28
            };

            _popupStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold
            };
        }

        private void DrawHud()
        {
            Rect panelRect = new(16f, 16f, 300f, 132f);
            GUI.Box(panelRect, GUIContent.none, _hudPanelStyle);

            GUI.Label(new Rect(30f, 26f, 100f, 18f), "SCORE", _hudLabelStyle);
            GUI.Label(new Rect(30f, 42f, 220f, 36f), _score.ToString("N0"), _hudScoreStyle);

            GUI.Label(new Rect(30f, 82f, 110f, 18f), "COMBO", _hudLabelStyle);
            GUI.Label(new Rect(30f, 98f, 120f, 24f), $"x{_displayMultiplier}", _hudValueStyle);

            GUI.Label(new Rect(156f, 82f, 110f, 18f), "STATE", _hudLabelStyle);
            GUI.Label(new Rect(156f, 98f, 140f, 24f), _comboStatus, _hudValueStyle);

            GUI.Label(new Rect(156f, 26f, 110f, 18f), "EFFECT", _hudLabelStyle);
            GUI.Label(new Rect(156f, 42f, 140f, 24f), _effectLabel, _hudValueStyle);

            GUI.Label(new Rect(30f, 122f, 250f, 18f), "Tap special tile to trigger instantly", _hudLabelStyle);
        }

        private void DrawComboPopups()
        {
            if (_mainCamera == null || _comboPopups.Count == 0) return;

            for (int i = 0; i < _comboPopups.Count; i++)
            {
                ComboPopup popup = _comboPopups[i];
                Vector3 world = popup.WorldPosition + (Vector3.up * popup.Age * 0.75f);
                Vector3 screen = _mainCamera.WorldToScreenPoint(world);
                if (screen.z < 0f) continue;

                float progress = popup.Age / popup.Lifetime;
                float alpha = 1f - Mathf.Clamp01(progress);
                float scale = Mathf.Lerp(1.2f, 0.92f, progress);
                Rect rect = new(screen.x - 120f, Screen.height - screen.y - 28f, 240f, 56f);
                _popupStyle.normal.textColor = new Color(popup.Color.r, popup.Color.g, popup.Color.b, alpha);

                Matrix4x4 cachedMatrix = GUI.matrix;
                GUIUtility.ScaleAroundPivot(Vector2.one * scale, rect.center);
                GUI.Label(rect, popup.Text, _popupStyle);
                GUI.matrix = cachedMatrix;
            }
        }

        private void UpdateComboPopups()
        {
            for (int i = _comboPopups.Count - 1; i >= 0; i--)
            {
                ComboPopup popup = _comboPopups[i];
                popup.Age += Time.deltaTime;
                if (popup.Age >= popup.Lifetime)
                {
                    _comboPopups.RemoveAt(i);
                }
            }
        }

        private void ShowComboPopup(int multiplier, HashSet<Vector2Int> clearSet, string label, int chain)
        {
            if (clearSet == null || clearSet.Count == 0) return;

            Vector3 origin = Vector3.zero;
            foreach (Vector2Int cell in clearSet)
            {
                origin += GridToWorld(cell.x, cell.y);
            }
            origin /= clearSet.Count;

            string text = multiplier > 1
                ? $"COMBO x{multiplier}"
                : label == "Match" && chain > 0
                    ? $"CASCADE x{multiplier}"
                    : label.ToUpperInvariant();

            _comboPopups.Add(new ComboPopup
            {
                Text = text,
                WorldPosition = origin + comboPopupOffset,
                Lifetime = comboPopupLifetime,
                Color = multiplier > 1 ? new Color(1f, 0.88f, 0.3f) : Color.white
            });
        }

        private void TriggerSound(AudioClip clip)
        {
            if (_audioSource == null || clip == null) return;
            _audioSource.PlayOneShot(clip);
        }

        private static bool AreAdjacent(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

        private static bool AreLinePair(SpecialTileType a, SpecialTileType b)
        {
            return (a == SpecialTileType.RowClear && b == SpecialTileType.ColumnClear) ||
                   (a == SpecialTileType.ColumnClear && b == SpecialTileType.RowClear);
        }

        private static bool AreDoubleColorClear(TileData a, TileData b)
        {
            return a.Special == SpecialTileType.ColorClear && b.Special == SpecialTileType.ColorClear;
        }

        private static bool ContainsColorClear(TileData a, TileData b)
        {
            return a.Special == SpecialTileType.ColorClear || b.Special == SpecialTileType.ColorClear;
        }

        private static bool IsLineSpecial(SpecialTileType special)
        {
            return special == SpecialTileType.RowClear || special == SpecialTileType.ColumnClear;
        }

        private static Vector2Int ChooseSpawnCell(HashSet<Vector2Int> cells, Vector2Int swapA, Vector2Int swapB)
        {
            if (cells.Contains(swapB)) return swapB;
            if (cells.Contains(swapA)) return swapA;
            foreach (Vector2Int cell in cells) return cell;
            return new Vector2Int(-1, -1);
        }

        private static string LabelFor(SpecialTileType special)
        {
            return special switch
            {
                SpecialTileType.ColorClear => "Color Burst",
                SpecialTileType.RowClear => "Row Blast",
                SpecialTileType.ColumnClear => "Column Blast",
                _ => "Match"
            };
        }

        private static Color ToUnityColor(TileColor color)
        {
            return color switch
            {
                TileColor.Blue => new Color(0.22f, 0.5f, 0.94f),
                TileColor.Red => new Color(0.92f, 0.24f, 0.2f),
                TileColor.Green => new Color(0.22f, 0.76f, 0.34f),
                TileColor.Yellow => new Color(0.96f, 0.84f, 0.2f),
                _ => Color.white
            };
        }

        private static Color GetTileVisualColor(TileData tile)
        {
            return tile.Special == SpecialTileType.None ? ToUnityColor(tile.Color) : SpecialTileBaseColor;
        }

        private static Sprite LoadOrBuildIcon(string resourcePath, System.Func<Sprite> fallback)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                return fallback();
            }

            texture.filterMode = FilterMode.Point;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
        }

        private static Sprite BuildSolidSprite(int size, Color fill)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Texture2D BuildSolidTexture(Color fill)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, fill);
            texture.Apply();
            return texture;
        }

        private static Sprite BuildRowClearIcon() => BuildIconSprite((x, y, size) => Mathf.Abs(y - size / 2) <= size / 6);
        private static Sprite BuildColumnClearIcon() => BuildIconSprite((x, y, size) => Mathf.Abs(x - size / 2) <= size / 6);

        private static Sprite BuildColorClearIcon()
        {
            return BuildIconSprite((x, y, size) =>
            {
                int c = size / 2;
                int dx = x - c;
                int dy = y - c;
                int r = Mathf.RoundToInt(size * 0.28f);
                int d = dx * dx + dy * dy;
                bool ring = d >= (r - 3) * (r - 3) && d <= (r + 3) * (r + 3);
                bool star = Mathf.Abs(dx) <= 2 || Mathf.Abs(dy) <= 2 || Mathf.Abs(dx - dy) <= 2 || Mathf.Abs(dx + dy) <= 2;
                return ring || star;
            });
        }

        private static Sprite BuildRingSprite()
        {
            return BuildIconSprite((x, y, size) =>
            {
                int c = size / 2;
                int dx = x - c;
                int dy = y - c;
                int d = dx * dx + dy * dy;
                return d <= (c - 2) * (c - 2) && d >= (c - 7) * (c - 7);
            });
        }

        private static Sprite BuildIconSprite(System.Func<int, int, int, bool> painter)
        {
            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Color clear = new(0f, 0f, 0f, 0f);
            Color ink = new(0.98f, 0.98f, 0.98f, 0.96f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[(y * size) + x] = painter(x, y, size) ? ink : clear;
            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
