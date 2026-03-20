using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class PacmanBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateGameRoot()
    {
        if (Object.FindObjectOfType<PacmanGame>() != null)
        {
            return;
        }

        GameObject gameRoot = new GameObject("PacmanGame");
        gameRoot.AddComponent<PacmanGame>();
    }
}

public class PacmanGame : MonoBehaviour
{
    private enum ControlKey
    {
        Left,
        Right,
        Up,
        Down,
        A,
        D,
        W,
        S,
        Restart
    }

    private static readonly Vector2Int GridUp = new Vector2Int(0, -1);
    private static readonly Vector2Int GridDown = new Vector2Int(0, 1);

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        GridUp,
        GridDown
    };

    private static readonly string[] MazeRows =
    {
        "###################",
        "#o......#.#......o#",
        "#.###.#.#.#.#.###.#",
        "#.....#.....#.....#",
        "###.#.###-###.#.###",
        "#...#...#-#...#...#",
        "#.###.#.#-#.#.###.#",
        "#.....#-GGG-#.....#",
        "#####.#-###-#.#####",
        "#.....#.....#.....#",
        "#.###.#.###.#.###.#",
        "#o..#...#P#...#..o#",
        "###.#.#.#.#.#.#.###",
        "#.....#.....#.....#",
        "#.#########.#####.#",
        "#.................#",
        "###################"
    };

    private const float PlayerSpeed = 6f;
    private const float GhostSpeed = 5f;
    private const float FrightenedGhostSpeed = 3.8f;
    private const float PowerModeDuration = 8f;
    private const float CollisionDistance = 0.45f;

    private const int PelletScore = 10;
    private const int PowerPelletScore = 50;
    private const int GhostScore = 200;
    private const int StartingLives = 3;
    private const string WallEmoji = "🧱";
    private const string GhostEmoji = "👻";
    private const string CandyEmoji = "🍬";
    private const string CakeEmoji = "🍰";

    private readonly Dictionary<Vector2Int, GameObject> _pelletViews = new Dictionary<Vector2Int, GameObject>();
    private readonly HashSet<Vector2Int> _powerPellets = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _ghostSpawnCells = new List<Vector2Int>();
    private readonly List<GhostActor> _ghosts = new List<GhostActor>();
    private readonly List<Object> _runtimeAssets = new List<Object>();

    private int _width;
    private int _height;
    private int _score;
    private int _lives;

    private float _powerTimer;
    private float _invulnerabilityTimer;

    private bool _victory;
    private bool _gameOver;

    private char[,] _maze;
    private Vector2Int _playerSpawnCell;
    private Vector2Int _lastPlayerCell;

    private Font _emojiFont;
    private Sprite _pacmanOpenSprite;
    private Sprite _pacmanClosedSprite;

    private Transform _boardRoot;
    private Transform _actorsRoot;

    private PlayerActor _player;

    private void Awake()
    {
        _emojiFont = CreateEmojiFont();
        _pacmanOpenSprite = CreatePacmanSprite(64, true);
        _pacmanClosedSprite = CreatePacmanSprite(64, false);
        BuildLevel();
        SetupCamera();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _runtimeAssets.Count; i++)
        {
            if (_runtimeAssets[i] != null)
            {
                Destroy(_runtimeAssets[i]);
            }
        }
    }

    private void Update()
    {
        if (_player == null)
        {
            return;
        }

        if (_powerTimer > 0f)
        {
            _powerTimer = Mathf.Max(0f, _powerTimer - Time.deltaTime);
        }

        if (_invulnerabilityTimer > 0f)
        {
            _invulnerabilityTimer = Mathf.Max(0f, _invulnerabilityTimer - Time.deltaTime);
        }

        if (_gameOver || _victory)
        {
            if (IsControlDown(ControlKey.Restart))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }

            return;
        }

        _player.ReadInput();
        _player.Tick(Time.deltaTime);
        _player.UpdateVisual(Time.deltaTime);

        if (_player.CurrentCell != _lastPlayerCell)
        {
            _lastPlayerCell = _player.CurrentCell;
            TryConsumePellet(_lastPlayerCell);
        }

        bool powerMode = IsPowerMode;
        for (int i = 0; i < _ghosts.Count; i++)
        {
            _ghosts[i].SetFrightened(powerMode);
            _ghosts[i].Tick(Time.deltaTime);
        }

        if (_invulnerabilityTimer <= 0f)
        {
            CheckGhostCollisions();
        }

        if (_pelletViews.Count == 0)
        {
            _victory = true;
        }
    }

    private void OnGUI()
    {
        GUIStyle infoStyle = new GUIStyle(GUI.skin.label);
        infoStyle.fontSize = 24;
        infoStyle.normal.textColor = Color.white;

        GUI.Label(new Rect(16f, 14f, 420f, 36f), "Score: " + _score, infoStyle);
        GUI.Label(new Rect(16f, 44f, 420f, 36f), "Lives: " + _lives, infoStyle);
        GUI.Label(new Rect(16f, 74f, 420f, 36f), "Pellets Left: " + _pelletViews.Count, infoStyle);

        if (_powerTimer > 0f)
        {
            GUI.Label(new Rect(16f, 104f, 420f, 36f), "Power Mode: " + _powerTimer.ToString("0.0") + "s", infoStyle);
        }

        if (!_victory && !_gameOver)
        {
            return;
        }

        GUIStyle messageStyle = new GUIStyle(GUI.skin.label);
        messageStyle.alignment = TextAnchor.MiddleCenter;
        messageStyle.fontSize = 48;
        messageStyle.normal.textColor = _victory ? Color.yellow : new Color(1f, 0.45f, 0.45f);

        GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.alignment = TextAnchor.MiddleCenter;
        hintStyle.fontSize = 24;
        hintStyle.normal.textColor = Color.white;

        string message = _victory ? "YOU WIN!" : "GAME OVER";
        GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 60f), message, messageStyle);
        GUI.Label(new Rect(0f, Screen.height * 0.46f, Screen.width, 40f), "Press R to restart", hintStyle);
    }

    private void BuildLevel()
    {
        _score = 0;
        _lives = StartingLives;
        _powerTimer = 0f;
        _invulnerabilityTimer = 0f;
        _victory = false;
        _gameOver = false;

        _pelletViews.Clear();
        _powerPellets.Clear();
        _ghostSpawnCells.Clear();
        _ghosts.Clear();

        _height = MazeRows.Length;
        _width = MazeRows[0].Length;
        _maze = new char[_height, _width];
        _playerSpawnCell = new Vector2Int(1, 1);

        _boardRoot = new GameObject("Board").transform;
        _boardRoot.SetParent(transform, false);

        _actorsRoot = new GameObject("Actors").transform;
        _actorsRoot.SetParent(transform, false);

        for (int y = 0; y < _height; y++)
        {
            if (MazeRows[y].Length != _width)
            {
                Debug.LogError("Pacman map rows have different lengths.");
                enabled = false;
                return;
            }

            for (int x = 0; x < _width; x++)
            {
                char symbol = MazeRows[y][x];
                Vector2Int cell = new Vector2Int(x, y);

                if (symbol == '#')
                {
                    _maze[y, x] = '#';
                    CreateEmojiObject(
                        "Wall",
                        cell,
                        WallEmoji,
                        108,
                        0.09f,
                        Color.white,
                        2,
                        _boardRoot);
                    continue;
                }

                _maze[y, x] = ' ';

                if (symbol == '.')
                {
                    CreatePellet(cell, false);
                }
                else if (symbol == 'o')
                {
                    CreatePellet(cell, true);
                }
                else if (symbol == 'P')
                {
                    _playerSpawnCell = cell;
                }
                else if (symbol == 'G')
                {
                    _ghostSpawnCells.Add(cell);
                }
            }
        }

        if (_ghostSpawnCells.Count == 0)
        {
            _ghostSpawnCells.Add(_playerSpawnCell + Vector2Int.left);
        }

        SpawnActors();
    }

    private void SpawnActors()
    {
        GameObject playerView = CreateSpriteObject(
            "Pacman",
            _playerSpawnCell,
            new Vector2(0.8f, 0.8f),
            new Color(1f, 0.92f, 0.16f),
            30,
            _pacmanOpenSprite,
            _actorsRoot);

        _player = new PlayerActor(
            this,
            playerView.transform,
            _playerSpawnCell,
            PlayerSpeed,
            _pacmanOpenSprite,
            _pacmanClosedSprite);
        _lastPlayerCell = _playerSpawnCell;
        TryConsumePellet(_lastPlayerCell);

        for (int i = 0; i < _ghostSpawnCells.Count; i++)
        {
            GameObject ghostView = CreateEmojiObject(
                "Ghost_" + (i + 1),
                _ghostSpawnCells[i],
                GhostEmoji,
                110,
                0.09f,
                Color.white,
                29,
                _actorsRoot);

            GhostActor ghost = new GhostActor(
                this,
                ghostView.transform,
                _ghostSpawnCells[i],
                GhostSpeed,
                FrightenedGhostSpeed);

            _ghosts.Add(ghost);
        }
    }

    private void CreatePellet(Vector2Int cell, bool isPowerPellet)
    {
        GameObject pellet = CreateEmojiObject(
            isPowerPellet ? "PowerPellet" : "Pellet",
            cell,
            isPowerPellet ? CakeEmoji : CandyEmoji,
            isPowerPellet ? 88 : 74,
            isPowerPellet ? 0.075f : 0.06f,
            Color.white,
            12,
            _boardRoot);

        _pelletViews[cell] = pellet;
        if (isPowerPellet)
        {
            _powerPellets.Add(cell);
        }
    }

    private void TryConsumePellet(Vector2Int cell)
    {
        GameObject pelletView;
        if (!_pelletViews.TryGetValue(cell, out pelletView))
        {
            return;
        }

        bool isPowerPellet = _powerPellets.Remove(cell);
        _pelletViews.Remove(cell);
        Destroy(pelletView);

        _score += isPowerPellet ? PowerPelletScore : PelletScore;
        if (isPowerPellet)
        {
            _powerTimer = PowerModeDuration;
        }
    }

    private void CheckGhostCollisions()
    {
        for (int i = 0; i < _ghosts.Count; i++)
        {
            GhostActor ghost = _ghosts[i];
            if (Vector3.Distance(_player.Position, ghost.Position) > CollisionDistance)
            {
                continue;
            }

            if (IsPowerMode)
            {
                _score += GhostScore;
                ghost.RespawnAtSpawn();
                continue;
            }

            HandlePlayerHit();
            break;
        }
    }

    private void HandlePlayerHit()
    {
        _lives--;
        _powerTimer = 0f;

        if (_lives <= 0)
        {
            _gameOver = true;
            return;
        }

        _player.Respawn(_playerSpawnCell);
        for (int i = 0; i < _ghosts.Count; i++)
        {
            _ghosts[i].RespawnAtSpawn();
            _ghosts[i].SetFrightened(false);
        }

        _lastPlayerCell = _playerSpawnCell;
        _invulnerabilityTimer = 1.2f;
    }

    private void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            cam = cameraObj.AddComponent<Camera>();
        }

        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.03f, 0.08f);

        float centerX = (_width - 1) * 0.5f;
        float centerY = -(_height - 1) * 0.5f;
        cam.transform.position = new Vector3(centerX, centerY, -10f);

        float verticalHalf = _height * 0.5f + 1.1f;
        float horizontalHalf = (_width * 0.5f + 1.1f) / Mathf.Max(cam.aspect, 0.01f);
        cam.orthographicSize = Mathf.Max(verticalHalf, horizontalHalf);
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= _width || cell.y >= _height)
        {
            return false;
        }

        return _maze[cell.y, cell.x] != '#';
    }

    public List<Vector2Int> GetWalkableDirections(Vector2Int fromCell)
    {
        List<Vector2Int> options = new List<Vector2Int>();
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int direction = CardinalDirections[i];
            if (IsWalkable(fromCell + direction))
            {
                options.Add(direction);
            }
        }

        return options;
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x, -cell.y, 0f);
    }

    public bool IsPowerMode
    {
        get { return _powerTimer > 0f; }
    }

    public Vector2Int PlayerCell
    {
        get { return _player != null ? _player.CurrentCell : _playerSpawnCell; }
    }

    private static bool IsControlDown(ControlKey key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[ToInputSystemKey(key)].wasPressedThisFrame)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(ToLegacyKeyCode(key));
#else
        return false;
#endif
    }

    private static bool IsControlPressed(ControlKey key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[ToInputSystemKey(key)].isPressed)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(ToLegacyKeyCode(key));
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static Key ToInputSystemKey(ControlKey key)
    {
        switch (key)
        {
            case ControlKey.Left:
                return Key.LeftArrow;
            case ControlKey.Right:
                return Key.RightArrow;
            case ControlKey.Up:
                return Key.UpArrow;
            case ControlKey.Down:
                return Key.DownArrow;
            case ControlKey.A:
                return Key.A;
            case ControlKey.D:
                return Key.D;
            case ControlKey.W:
                return Key.W;
            case ControlKey.S:
                return Key.S;
            default:
                return Key.R;
        }
    }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
    private static KeyCode ToLegacyKeyCode(ControlKey key)
    {
        switch (key)
        {
            case ControlKey.Left:
                return KeyCode.LeftArrow;
            case ControlKey.Right:
                return KeyCode.RightArrow;
            case ControlKey.Up:
                return KeyCode.UpArrow;
            case ControlKey.Down:
                return KeyCode.DownArrow;
            case ControlKey.A:
                return KeyCode.A;
            case ControlKey.D:
                return KeyCode.D;
            case ControlKey.W:
                return KeyCode.W;
            case ControlKey.S:
                return KeyCode.S;
            default:
                return KeyCode.R;
        }
    }
#endif

    private Font CreateEmojiFont()
    {
        Font emojiFont = null;

        try
        {
            emojiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", "Twemoji Mozilla" },
                96);
        }
        catch
        {
            emojiFont = null;
        }

        if (emojiFont == null)
        {
            emojiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            Debug.LogWarning("Emoji OS font not found, using Arial fallback.");
        }

        return emojiFont;
    }

    private Sprite CreatePacmanSprite(int size, bool openMouth)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        float radiusSqr = radius * radius;
        float halfMouthAngle = 30f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distanceSqr = dx * dx + dy * dy;
                bool insideBody = distanceSqr <= radiusSqr;

                if (!insideBody)
                {
                    texture.SetPixel(x, y, Color.clear);
                    continue;
                }

                if (openMouth && dx > 0f)
                {
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (Mathf.Abs(angle) <= halfMouthAngle)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }
                }

                texture.SetPixel(x, y, Color.white);
            }
        }

        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);

        _runtimeAssets.Add(texture);
        _runtimeAssets.Add(sprite);
        return sprite;
    }

    private GameObject CreateSpriteObject(
        string objectName,
        Vector2Int cell,
        Vector2 scale,
        Color color,
        int sortingOrder,
        Sprite sprite,
        Transform parent)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        go.transform.position = CellToWorld(cell);
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        return go;
    }

    private GameObject CreateEmojiObject(
        string objectName,
        Vector2Int cell,
        string emoji,
        int fontSize,
        float characterSize,
        Color color,
        int sortingOrder,
        Transform parent)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        go.transform.position = CellToWorld(cell);

        TextMesh textMesh = go.AddComponent<TextMesh>();
        textMesh.text = emoji;
        textMesh.font = _emojiFont;
        textMesh.fontSize = fontSize;
        textMesh.characterSize = characterSize;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (_emojiFont != null)
        {
            renderer.sharedMaterial = _emojiFont.material;
        }
        renderer.sortingOrder = sortingOrder;

        return go;
    }

    private abstract class GridActor
    {
        protected readonly PacmanGame Game;
        protected readonly Transform View;
        protected readonly float BaseSpeed;

        protected Vector2Int LastMoveDirection = Vector2Int.left;

        private Vector2Int _fromCell;
        private Vector2Int _toCell;
        private float _moveProgress;
        private bool _isMoving;

        protected GridActor(PacmanGame game, Transform view, Vector2Int spawnCell, float baseSpeed)
        {
            Game = game;
            View = view;
            BaseSpeed = baseSpeed;
            CurrentCell = spawnCell;
            _fromCell = spawnCell;
            _toCell = spawnCell;
            _moveProgress = 0f;
            _isMoving = false;
            View.position = game.CellToWorld(spawnCell);
        }

        public Vector2Int CurrentCell { get; private set; }

        public Vector3 Position
        {
            get { return View.position; }
        }

        public void Tick(float deltaTime)
        {
            if (_isMoving)
            {
                _moveProgress += deltaTime * Mathf.Max(0.05f, GetMoveSpeed());
                if (_moveProgress >= 1f)
                {
                    CurrentCell = _toCell;
                    _isMoving = false;
                    _moveProgress = 0f;
                    View.position = Game.CellToWorld(CurrentCell);
                    OnReachedCell(CurrentCell);
                }
                else
                {
                    View.position = Vector3.Lerp(
                        Game.CellToWorld(_fromCell),
                        Game.CellToWorld(_toCell),
                        _moveProgress);
                }
            }

            if (_isMoving)
            {
                return;
            }

            Vector2Int direction = ChooseNextDirection();
            if (direction == Vector2Int.zero)
            {
                return;
            }

            Vector2Int nextCell = CurrentCell + direction;
            if (!Game.IsWalkable(nextCell))
            {
                return;
            }

            LastMoveDirection = direction;
            _fromCell = CurrentCell;
            _toCell = nextCell;
            _moveProgress = 0f;
            _isMoving = true;
            Face(direction);
        }

        public void Respawn(Vector2Int spawnCell)
        {
            CurrentCell = spawnCell;
            _fromCell = spawnCell;
            _toCell = spawnCell;
            _moveProgress = 0f;
            _isMoving = false;
            LastMoveDirection = Vector2Int.left;
            View.position = Game.CellToWorld(spawnCell);
            OnRespawned();
        }

        protected virtual float GetMoveSpeed()
        {
            return BaseSpeed;
        }

        protected virtual void Face(Vector2Int direction)
        {
        }

        protected virtual void OnReachedCell(Vector2Int cell)
        {
        }

        protected virtual void OnRespawned()
        {
        }

        protected abstract Vector2Int ChooseNextDirection();
    }

    private sealed class PlayerActor : GridActor
    {
        private Vector2Int _desiredDirection;
        private float _pulseTime;
        private readonly SpriteRenderer _renderer;
        private readonly Sprite _openMouthSprite;
        private readonly Sprite _closedMouthSprite;
        private float _mouthFlipTimer;
        private bool _mouthOpen = true;
        private bool _wasMoving;
        private Vector3 _previousVisualPosition;

        public PlayerActor(
            PacmanGame game,
            Transform view,
            Vector2Int spawnCell,
            float baseSpeed,
            Sprite openMouthSprite,
            Sprite closedMouthSprite)
            : base(game, view, spawnCell, baseSpeed)
        {
            _renderer = view.GetComponent<SpriteRenderer>();
            _openMouthSprite = openMouthSprite;
            _closedMouthSprite = closedMouthSprite;
            _desiredDirection = Vector2Int.left;
            _renderer.sprite = _openMouthSprite;
            _previousVisualPosition = View.position;
            _wasMoving = false;
            Face(_desiredDirection);
        }

        public void ReadInput()
        {
            if (IsControlDown(ControlKey.Left) || IsControlDown(ControlKey.A))
            {
                _desiredDirection = Vector2Int.left;
            }
            else if (IsControlDown(ControlKey.Right) || IsControlDown(ControlKey.D))
            {
                _desiredDirection = Vector2Int.right;
            }
            else if (IsControlDown(ControlKey.Up) || IsControlDown(ControlKey.W))
            {
                _desiredDirection = GridUp;
            }
            else if (IsControlDown(ControlKey.Down) || IsControlDown(ControlKey.S))
            {
                _desiredDirection = GridDown;
            }
            else
            {
                if (IsControlPressed(ControlKey.Left) || IsControlPressed(ControlKey.A))
                {
                    _desiredDirection = Vector2Int.left;
                }
                else if (IsControlPressed(ControlKey.Right) || IsControlPressed(ControlKey.D))
                {
                    _desiredDirection = Vector2Int.right;
                }
                else if (IsControlPressed(ControlKey.Up) || IsControlPressed(ControlKey.W))
                {
                    _desiredDirection = GridUp;
                }
                else if (IsControlPressed(ControlKey.Down) || IsControlPressed(ControlKey.S))
                {
                    _desiredDirection = GridDown;
                }
            }

            _pulseTime += Time.deltaTime * 12f;
            float scale = 0.76f + Mathf.Sin(_pulseTime) * 0.03f;
            View.localScale = new Vector3(scale, scale, 1f);
        }

        public void UpdateVisual(float deltaTime)
        {
            bool isMoving = (View.position - _previousVisualPosition).sqrMagnitude > 0.000001f;
            _previousVisualPosition = View.position;

            if (isMoving)
            {
                if (!_wasMoving)
                {
                    _mouthOpen = true;
                    _mouthFlipTimer = 0f;
                    _renderer.sprite = _openMouthSprite;
                }

                _mouthFlipTimer += deltaTime;
                if (_mouthFlipTimer >= 0.09f)
                {
                    _mouthFlipTimer = 0f;
                    _mouthOpen = !_mouthOpen;
                    _renderer.sprite = _mouthOpen ? _openMouthSprite : _closedMouthSprite;
                }
            }
            else
            {
                _mouthFlipTimer = 0f;
                _mouthOpen = false;
                _renderer.sprite = _closedMouthSprite;
            }

            _wasMoving = isMoving;
        }

        protected override Vector2Int ChooseNextDirection()
        {
            if (_desiredDirection != Vector2Int.zero && Game.IsWalkable(CurrentCell + _desiredDirection))
            {
                return _desiredDirection;
            }

            if (LastMoveDirection != Vector2Int.zero && Game.IsWalkable(CurrentCell + LastMoveDirection))
            {
                return LastMoveDirection;
            }

            return Vector2Int.zero;
        }

        protected override void Face(Vector2Int direction)
        {
            if (direction == Vector2Int.zero)
            {
                return;
            }

            Vector3 worldDirection = new Vector3(direction.x, -direction.y, 0f);
            if (worldDirection.sqrMagnitude > 0.01f)
            {
                View.right = worldDirection.normalized;
            }
        }

        protected override void OnRespawned()
        {
            _desiredDirection = Vector2Int.left;
            _mouthOpen = true;
            _mouthFlipTimer = 0f;
            _wasMoving = false;
            _previousVisualPosition = View.position;
            _renderer.sprite = _openMouthSprite;
            Face(_desiredDirection);
        }
    }

    private sealed class GhostActor : GridActor
    {
        private readonly float _frightenedSpeed;
        private readonly Vector2Int _spawnCell;
        private readonly TextMesh _label;

        public GhostActor(
            PacmanGame game,
            Transform view,
            Vector2Int spawnCell,
            float baseSpeed,
            float frightenedSpeed)
            : base(game, view, spawnCell, baseSpeed)
        {
            _frightenedSpeed = frightenedSpeed;
            _spawnCell = spawnCell;
            _label = view.GetComponent<TextMesh>();
        }

        public void RespawnAtSpawn()
        {
            Respawn(_spawnCell);
        }

        public void SetFrightened(bool frightened)
        {
            _label.color = frightened ? new Color(0.7f, 0.84f, 1f) : Color.white;
        }

        protected override float GetMoveSpeed()
        {
            return Game.IsPowerMode ? _frightenedSpeed : BaseSpeed;
        }

        protected override Vector2Int ChooseNextDirection()
        {
            List<Vector2Int> options = Game.GetWalkableDirections(CurrentCell);
            if (options.Count == 0)
            {
                return Vector2Int.zero;
            }

            Vector2Int reverseDirection = new Vector2Int(-LastMoveDirection.x, -LastMoveDirection.y);
            if (options.Count > 1 && reverseDirection != Vector2Int.zero)
            {
                options.RemoveAll(dir => dir == reverseDirection);
            }

            if (options.Count == 0)
            {
                return reverseDirection;
            }

            if (Game.IsPowerMode || Random.value < 0.22f)
            {
                return options[Random.Range(0, options.Count)];
            }

            Vector2Int targetCell = Game.PlayerCell;
            float bestDistance = float.MaxValue;
            Vector2Int bestDirection = options[0];

            for (int i = 0; i < options.Count; i++)
            {
                Vector2Int candidateDirection = options[i];
                Vector2Int candidateCell = CurrentCell + candidateDirection;
                float distance = Mathf.Abs(candidateCell.x - targetCell.x) + Mathf.Abs(candidateCell.y - targetCell.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestDirection = candidateDirection;
                }
            }

            return bestDirection;
        }
    }
}
