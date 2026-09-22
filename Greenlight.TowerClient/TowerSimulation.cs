namespace Greenlight.TowerClient;

/// <summary>What the building site is doing, which is Greenlight's aggregate colour by another name.</summary>
public enum LightState
{
    /// <summary>No Greenlight attached. Tools down, and the site dims rather than pretending to know anything.</summary>
    Unknown,

    /// <summary>Everything passing. They build.</summary>
    Green,

    /// <summary>Something pending. Work stops, and the tower sways while everybody looks at it.</summary>
    Yellow,

    /// <summary>A pipeline is broken. The tower comes down and everybody runs around screaming.</summary>
    Red,
}

/// <summary>What one builder is up to.</summary>
public enum Activity
{
    /// <summary>Standing about, or wandering, waiting for there to be something to do.</summary>
    Idle,

    /// <summary>Walking to the pile for a brick, or to drop off what they are carrying.</summary>
    ToPile,

    /// <summary>Carrying a brick to the foot of the ladder.</summary>
    ToTower,

    /// <summary>Going up the ladder with a brick.</summary>
    Climbing,

    /// <summary>At the top, setting the brick down.</summary>
    Placing,

    /// <summary>Coming back down the ladder.</summary>
    Descending,

    /// <summary>Walking over to a piece of rubble to pick it up.</summary>
    ToRubble,

    /// <summary>Carrying a piece of rubble back to the pile.</summary>
    CarryingRubble,

    /// <summary>Stopped, looking up at a tower that is swaying.</summary>
    Watching,

    /// <summary>Running around screaming.</summary>
    Panicking,

    /// <summary>Was on the ladder when the tower went. Now is not.</summary>
    Falling,
}

/// <summary>What somebody has in their hands.</summary>
public enum Load
{
    None,
    Brick,
    Rubble,
}

/// <summary>One brick in the tower, by where it sits rather than by coordinates.</summary>
/// <param name="Color">Any hex Avalonia can parse.</param>
public sealed record Brick(int Row, int Column, string Color);

/// <summary>A builder. Positions are logical pixels; <see cref="Alt"/> is feet above the ground.</summary>
public sealed class Worker
{
    public required int Id { get; init; }

    /// <summary>Shirt colour. Hex.</summary>
    public required string Shirt { get; init; }

    /// <summary>Hard hat colour. Hex.</summary>
    public required string Hat { get; init; }

    public double X { get; set; }

    /// <summary>Height of their feet above the ground. Non-zero on the ladder, mid-jump and mid-fall.</summary>
    public double Alt { get; set; }

    /// <summary>+1 facing the tower side of the site, -1 facing the pile side.</summary>
    public double Facing { get; set; } = 1;

    /// <summary>The walk cycle, in radians. Only advances while they are actually going somewhere.</summary>
    public double Stride { get; set; }

    public bool IsMoving { get; internal set; }

    public Activity Activity { get; internal set; }

    public Load Load { get; internal set; }

    /// <summary>The colour of whatever they are carrying.</summary>
    public string? LoadColor { get; internal set; }

    /// <summary>On the ladder, and so swaying with the tower.</summary>
    public bool OnTower => Activity is Activity.Climbing or Activity.Placing or Activity.Descending;

    internal double TargetX;
    internal double Timer;
    internal double VX;
    internal double VAlt;
    internal double RunSpeed;
    internal double NextShout;
    internal Debris? Claim;
}

/// <summary>A brick that is no longer part of a tower: in the air, or lying in the rubble.</summary>
public sealed class Debris
{
    /// <summary>Centre, in logical pixels.</summary>
    public double X { get; set; }

    /// <summary>Centre, height above the ground.</summary>
    public double Alt { get; set; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    /// <summary>Radians. Positive leans towards the pile side.</summary>
    public double Angle { get; set; }

    public required string Color { get; init; }

    /// <summary>Come to rest. Settled rubble never moves again unless somebody picks it up.</summary>
    public bool Settled { get; internal set; }

    internal double VX;
    internal double VAlt;
    internal double Spin;
    internal bool Claimed;

    /// <summary>Seconds since it stopped being part of something.</summary>
    internal double Age;

    internal double HalfWidth => (Math.Abs(Math.Cos(Angle)) * Width + Math.Abs(Math.Sin(Angle)) * Height) / 2;

    internal double HalfHeight => (Math.Abs(Math.Sin(Angle)) * Width + Math.Abs(Math.Cos(Angle)) * Height) / 2;
}

/// <summary>A speech bubble over one builder, fading out.</summary>
public sealed class Shout
{
    public required Worker Worker { get; init; }
    public required string Text { get; init; }
    public double Remaining { get; set; }
    public required double Life { get; init; }
}

/// <summary>
/// The building site: the tower, the crew, the rubble, and what happens when the light
/// changes. Deliberately free of Avalonia — it is all arithmetic, so it can be tested without a
/// window, which is the only way "the rubble never ends up underneath the ground" was ever
/// going to be checkable.
/// </summary>
/// <remarks>
/// <para>
/// One orientation only: the tower on the right-hand end of the site, the brick pile on the
/// left, and the tower falling leftwards. The canvas mirrors the whole thing for a site on the
/// left of the screen. Everything here is in logical pixels, with most sizes derived from
/// <see cref="Unit"/> — the height of one builder — so that a bigger site is the same site
/// scaled rather than a different one.
/// </para>
/// <para>
/// Not thread-safe. The render loop calls <see cref="Advance"/>, and the app sets
/// <see cref="Light"/> from the same UI thread.
/// </para>
/// </remarks>
public sealed class TowerSimulation
{
    /// <summary>Bricks per course.</summary>
    public const int Columns = 3;

    /// <summary>How far the tallest possible tower leans at the top of its sway.</summary>
    private const double MaxSway = 5 * Math.PI / 180;

    private const double SwayPeriod = 2.6;

    // Speeds, in Units per second.
    private const double WalkSpeed = 2.8;
    private const double WanderSpeed = 1.1;
    private const double ClimbSpeed = 2.4;
    private const double HurryFactor = 1.5;

    /// <summary>Units per second squared. Lower than real gravity looks — at this size real gravity is over in a blink.</summary>
    private const double Gravity = 40;

    /// <summary>How fast rubble has to be falling to bounce rather than stop.</summary>
    private const double BounceSpeed = 3.5;

    /// <summary>How long a builder spends setting a brick down at the top.</summary>
    private const double PlacingSeconds = 0.45;

    /// <summary>
    /// How long a brick coming off the tower ignores the rest of the rubble and only minds the
    /// ground.
    /// </summary>
    /// <remarks>
    /// Without it the tower does not fall — it re-stacks. The bottom course lands in the first
    /// frame, the course above is already touching it with no speed to speak of and settles on
    /// top, and so on up: a tower standing exactly where it was, now officially rubble. Every
    /// piece of a toppling tower is moving at once, so for the first instant nothing is resting
    /// on anything.
    /// </remarks>
    private const double AirborneSeconds = 0.35;

    /// <summary>How far off flat a piece of rubble may come to rest, radians.</summary>
    internal const double RestingTilt = 0.14;

    private static readonly string[] Screams =
    [
        "AAAH!", "AAAAAAH!", "EEEK!", "HELP!", "RUN!", "NOOO!", "MY TOWER!", "WHO BROKE IT?!",
        "AAAH!", "WE'RE DOOMED!",
    ];

    private static readonly string[] DefaultBrickColors = ["#C9A66B", "#B8925A", "#D8BC84", "#A9824E"];

    private static readonly string[] Shirts = ["#4FB0FF", "#F2574C", "#5FD08A", "#B07CE8", "#F2C14E", "#FF8A3D", "#48C9C0", "#E86FB0"];

    private static readonly string[] Hats = ["#FFC02E", "#FFC02E", "#FF8A1F", "#F4F4F4"];

    private readonly Random _random;
    private readonly List<Brick> _bricks = [];
    private readonly List<Debris> _debris = [];
    private readonly List<Worker> _workers = [];
    private readonly List<Shout> _shouts = [];

    /// <summary>
    /// The top of the rubble at each logical pixel across the site: where the next falling
    /// brick stops. A height map rather than brick-on-brick collision, because rubble only ever
    /// has to look like a pile, and a pile is what a height map is.
    /// </summary>
    private double[] _floor = [];

    private LightState _light = LightState.Unknown;
    private double _swayStrength;
    private int _nextWorkerId;
    private IReadOnlyList<string> _brickColors = DefaultBrickColors;

    /// <param name="builders">How many are on the crew.</param>
    /// <param name="seed">For tests. Real sites are not reproducible.</param>
    public TowerSimulation(int builders = 5, int? seed = null)
    {
        _random = seed is { } s ? new Random(s) : new Random();
        Resize(240, 280, 14);
        SetCrew(builders);
    }

    // ── What the canvas reads ─────────────────────────────────────────────────

    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>The height of one builder. Everything else on the site is sized from this.</summary>
    public double Unit { get; private set; }

    public double BrickWidth { get; private set; }

    public double BrickHeight { get; private set; }

    public double TowerLeft { get; private set; }

    public double TowerRight { get; private set; }

    /// <summary>The middle of the tower at the ground, which is what it sways about.</summary>
    public double TowerCentre => (TowerLeft + TowerRight) / 2;

    /// <summary>Where the ladder stands, and where a climber's feet are.</summary>
    public double LadderX { get; private set; }

    /// <summary>The middle of the brick pile.</summary>
    public double PileX { get; private set; }

    /// <summary>How many courses fit on the site.</summary>
    public int Rows { get; private set; }

    public int Capacity => Rows * Columns;

    public IReadOnlyList<Brick> Bricks => _bricks;

    public IReadOnlyList<Debris> Debris => _debris;

    public IReadOnlyList<Worker> Workers => _workers;

    public IReadOnlyList<Shout> Shouts => _shouts;

    public bool IsComplete => _bricks.Count >= Capacity;

    /// <summary>The top of the highest brick.</summary>
    public double TowerHeight => (_bricks.Count + Columns - 1) / Columns * BrickHeight;

    /// <summary>Radians about <see cref="TowerCentre"/>. Positive leans towards the pile.</summary>
    public double SwayAngle { get; private set; }

    /// <summary>Seconds the site has been running, for anything that animates on its own.</summary>
    public double Time { get; private set; }

    /// <summary>
    /// How many bricks the pile has had returned to it. Only the drawing cares: a pile that
    /// grows while the rubble is cleared is what makes the clearing read as tidying up.
    /// </summary>
    public int Returned { get; private set; }

    // ── What the app sets ─────────────────────────────────────────────────────

    /// <summary>A CI build is running somewhere. The crew works faster, because somebody is watching.</summary>
    public bool Hurry { get; set; }

    /// <summary>Whether anybody says anything. Off, they still run; they just do it quietly.</summary>
    public bool Shouting { get; set; } = true;

    /// <summary>The colours new bricks are drawn from. Bricks already laid keep theirs.</summary>
    public IReadOnlyList<string> BrickColors
    {
        get => _brickColors;
        set => _brickColors = value is { Count: > 0 } ? value : DefaultBrickColors;
    }

    public LightState Light
    {
        get => _light;
        set
        {
            if (value == _light) return;

            var was = _light;
            _light = value;

            if (value == LightState.Red) Collapse();
            else if (was == LightState.Red) CalmDown();

            if (value == LightState.Yellow) StopWork(uhOh: was == LightState.Green && _bricks.Count > 0);
            if (value == LightState.Unknown) StopWork(uhOh: false);

            if (value == LightState.Green && was == LightState.Red && _debris.Count > 0)
                Say(_workers.FirstOrDefault(), "Right. Again.");
        }
    }

    /// <summary>
    /// Lay the site out for a new size. Called when the window moves or the screen changes;
    /// the tower keeps what fits and loses courses that no longer do.
    /// </summary>
    public void Resize(double width, double height, double unit)
    {
        Unit = Math.Max(4, unit);
        Width = Math.Max(width, Unit * 8);
        Height = Math.Max(height, Unit * 4);

        BrickWidth = Unit * 0.9;
        BrickHeight = Unit * 0.45;
        TowerRight = Width - Unit * 0.35;
        TowerLeft = TowerRight - Columns * BrickWidth;
        LadderX = TowerLeft - Unit * 0.3;
        PileX = Unit * 1.3;

        // Room above the finished tower for a builder standing on it and the flag.
        Rows = Math.Clamp((int)((Height - Unit * 2.4) / BrickHeight), 1, 400);
        if (_bricks.Count > Capacity) _bricks.RemoveRange(Capacity, _bricks.Count - Capacity);

        foreach (var worker in _workers)
        {
            worker.X = Math.Clamp(worker.X, Unit * 0.4, LadderX);
            if (worker.OnTower) worker.Alt = Math.Min(worker.Alt, NextCourse());
        }

        foreach (var piece in _debris)
            piece.X = Math.Clamp(piece.X, piece.HalfWidth, Width - piece.HalfWidth);

        RebuildFloor();
    }

    /// <summary>Take on or lay off builders, without disturbing the ones already working.</summary>
    public void SetCrew(int builders)
    {
        builders = Math.Clamp(builders, 1, 20);

        while (_workers.Count < builders)
        {
            var id = _nextWorkerId++;
            var worker = new Worker
            {
                Id = id,
                Shirt = Shirts[id % Shirts.Length],
                Hat = Hats[_random.Next(Hats.Length)],
                X = PileX + Range(0, Unit * 4),
                Facing = 1,
                Timer = Range(0, 1.5),
                NextShout = Range(0.3, 2),
            };

            if (_light == LightState.Red) Panic(worker);
            _workers.Add(worker);
        }

        // The idle ones go first. Laying off somebody halfway up the ladder would leave their
        // brick hanging in the air.
        while (_workers.Count > builders)
        {
            var leaving = _workers.LastOrDefault(w => !w.OnTower && w.Load == Load.None) ?? _workers[^1];
            if (leaving.Claim is { } claim) claim.Claimed = false;
            _workers.Remove(leaving);
            _shouts.RemoveAll(s => s.Worker == leaving);
        }
    }

    /// <summary>
    /// Rotate a point on the tower by the current sway. For drawing the tower and anybody on
    /// its ladder; the ground does not sway.
    /// </summary>
    public (double X, double Alt) Sway(double x, double alt)
    {
        var (sin, cos) = Math.SinCos(SwayAngle);
        var dx = x - TowerCentre;
        return (TowerCentre + dx * cos - alt * sin, dx * sin + alt * cos);
    }

    public void Advance(TimeSpan elapsed)
    {
        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.1);   // a stalled frame must not teleport anyone
        if (dt <= 0) return;

        Time += dt;

        UpdateSway(dt);
        UpdateDebris(dt);

        foreach (var worker in _workers)
            UpdateWorker(worker, dt);

        UpdateShouts(dt);
    }

    // ── The light changing ────────────────────────────────────────────────────

    /// <summary>
    /// Down it comes. Every brick becomes rubble, thrown away from the screen edge harder the
    /// higher up it was — which is what a toppling tower does, and what spreads the rubble
    /// across the site instead of dropping it in a column where the tower stood.
    /// </summary>
    private void Collapse()
    {
        var height = Math.Max(BrickHeight, TowerHeight);

        foreach (var brick in _bricks)
        {
            var (x, alt) = Sway(
                TowerLeft + (brick.Column + 0.5) * BrickWidth,
                (brick.Row + 0.5) * BrickHeight);

            var up = alt / height;

            _debris.Add(new Debris
            {
                X = x,
                Alt = alt,
                Width = BrickWidth,
                Height = BrickHeight,
                Angle = SwayAngle,
                Color = brick.Color,
                VX = -Unit * (2 + 9 * up) * Range(0.7, 1.3) + Range(-1, 1) * Unit,
                VAlt = Unit * (Range(0, 2.5) * up + Range(0, 1)),
                Spin = Range(-8, 8),
            });
        }

        _bricks.Clear();

        foreach (var worker in _workers)
        {
            // Whatever they were carrying joins the rubble. A brick that vanished from
            // somebody's hands when the tower fell would be the one thing on screen that
            // looked like a bug.
            if (worker.Load != Load.None) Drop(worker);

            if (worker.Claim is { } claim) claim.Claimed = false;
            worker.Claim = null;

            if (worker.OnTower && worker.Alt > 0)
            {
                worker.Activity = Activity.Falling;
                worker.VX = -Unit * Range(1, 3);
                worker.VAlt = Unit * Range(0, 2);
            }
            else
            {
                Panic(worker);
            }
        }
    }

    private void Drop(Worker worker)
    {
        _debris.Add(new Debris
        {
            X = worker.X,
            Alt = worker.Alt + Unit * 0.9,
            Width = BrickWidth,
            Height = BrickHeight,
            Color = worker.LoadColor ?? _brickColors[0],
            VX = Range(-1, 1) * Unit,
            VAlt = Unit * Range(0.5, 1.5),
            Spin = Range(-6, 6),
        });

        worker.Load = Load.None;
        worker.LoadColor = null;
    }

    private void Panic(Worker worker)
    {
        worker.Activity = Activity.Panicking;
        worker.RunSpeed = Unit * Range(4.5, 6.5);
        worker.Facing = _random.Next(2) == 0 ? -1 : 1;
        worker.Timer = Range(0.4, 1.4);

        // Staggered, so the whole crew does not scream in the same frame. That reads as one
        // noise; a ragged spread of them reads as a crowd.
        worker.NextShout = Range(0.1, 1.6);
    }

    private void CalmDown()
    {
        foreach (var worker in _workers)
        {
            if (worker.Activity == Activity.Panicking) worker.Activity = Activity.Idle;
        }
    }

    /// <summary>Tools down: off the ladder, and stand still. Bricks stay in hand, for when work resumes.</summary>
    private void StopWork(bool uhOh)
    {
        foreach (var worker in _workers)
        {
            if (worker.OnTower) worker.Activity = Activity.Descending;
            else if (worker.Activity is not (Activity.Falling or Activity.Panicking))
            {
                worker.Activity = _light == LightState.Yellow ? Activity.Watching : Activity.Idle;
            }

            if (worker.Claim is { } claim) claim.Claimed = false;
            worker.Claim = null;
        }

        if (uhOh) Say(_workers.Where(w => !w.OnTower).OrderBy(_ => _random.Next()).FirstOrDefault(), "Uh oh…");
    }

    // ── Sway ──────────────────────────────────────────────────────────────────

    private void UpdateSway(double dt)
    {
        // Eased in and out rather than switched, so a tower that stops swaying settles
        // instead of snapping upright.
        var target = _light == LightState.Yellow ? 1.0 : 0.0;
        _swayStrength += (target - _swayStrength) * Math.Min(1, dt * 1.5);

        if (_bricks.Count == 0)
        {
            SwayAngle = 0;
            return;
        }

        // Even a stub sways a little, or yellow would look like nothing at all at the start of
        // a build; the tall ones sway properly.
        var tall = 0.25 + 0.75 * (TowerHeight / (Rows * BrickHeight));
        SwayAngle = _swayStrength * MaxSway * tall * Math.Sin(2 * Math.PI * Time / SwayPeriod);
    }

    // ── Rubble ────────────────────────────────────────────────────────────────

    private void UpdateDebris(double dt)
    {
        foreach (var piece in _debris)
        {
            if (piece.Settled) continue;

            piece.Age += dt;
            piece.VAlt -= Gravity * Unit * dt;
            piece.X += piece.VX * dt;
            piece.Alt += piece.VAlt * dt;
            piece.Angle += piece.Spin * dt;

            var hw = piece.HalfWidth;
            var hh = piece.HalfHeight;

            // The site has walls. Rubble bouncing off the edge of the window and vanishing would
            // be tidy, but the point of rubble is that it stays.
            if (piece.X - hw < 0)
            {
                piece.X = hw;
                piece.VX = Math.Abs(piece.VX) * 0.35;
            }
            else if (piece.X + hw > Width)
            {
                piece.X = Width - hw;
                piece.VX = -Math.Abs(piece.VX) * 0.35;
            }

            var floor = piece.Age < AirborneSeconds ? 0 : FloorUnder(piece.X, hw);
            if (piece.Alt - hh > floor || piece.VAlt > 0) continue;

            if (-piece.VAlt > Unit * BounceSpeed)
            {
                piece.Alt = floor + hh;
                piece.VAlt = -piece.VAlt * 0.3;
                piece.VX *= 0.55;
                piece.Spin *= 0.5;
            }
            else
            {
                Settle(piece);
            }
        }
    }

    /// <summary>
    /// Bring a piece to rest: lying flat, never balanced on a corner or stood on its end, sitting
    /// on top of whatever is already there.
    /// </summary>
    /// <remarks>
    /// Flat rather than whichever square angle is nearest. Bricks stood on end are narrow, and a
    /// heap with some of them in it grows spires a brick wide — rubble that looks like a
    /// skyline, which is exactly what it is meant to have stopped being.
    /// </remarks>
    private void Settle(Debris piece)
    {
        // Flat, give or take a few degrees. Dead flat on a staggered heap is brickwork, and the
        // tower has just stopped being brickwork.
        piece.Angle = Math.Round(piece.Angle / Math.PI) * Math.PI + Range(-RestingTilt, RestingTilt);
        piece.VX = piece.VAlt = piece.Spin = 0;

        var hw = piece.HalfWidth;
        piece.X = Math.Clamp(piece.X, hw, Width - hw);

        // Slide down the side of the heap while there is a drop of more than a course a brick
        // away. Without this every brick lands on whatever is under its middle, and rubble builds
        // a sheer column taller than the tower it came from; with it, a heap slumps into a heap.
        var reach = BrickWidth;
        for (var step = 0; step < 80; step++)
        {
            var here = FloorUnder(piece.X, hw);
            var left = piece.X - reach >= hw ? FloorUnder(piece.X - reach, hw) : double.MaxValue;
            var right = piece.X + reach <= Width - hw ? FloorUnder(piece.X + reach, hw) : double.MaxValue;
            var lower = Math.Min(left, right);

            if (lower >= here - BrickHeight * 0.9) break;

            piece.X = Math.Clamp(piece.X + (left < right ? -hw : hw), hw, Width - hw);
        }

        piece.Alt = FloorUnder(piece.X, hw) + piece.HalfHeight;
        piece.Settled = true;

        RaiseFloor(piece);
    }

    /// <summary>
    /// The top of the rubble under a piece. Looked up across the middle of it rather than the
    /// whole width, so that a brick overlapping the edge of the pile by a pixel sits down beside
    /// it instead of perching on the corner.
    /// </summary>
    private double FloorUnder(double x, double halfWidth)
    {
        if (_floor.Length == 0) return 0;

        var from = Math.Clamp((int)(x - halfWidth * 0.6), 0, _floor.Length - 1);
        var to = Math.Clamp((int)Math.Ceiling(x + halfWidth * 0.6), 0, _floor.Length - 1);

        var top = 0.0;
        for (var i = from; i <= to; i++) top = Math.Max(top, _floor[i]);
        return top;
    }

    private void RaiseFloor(Debris piece)
    {
        var from = Math.Clamp((int)(piece.X - piece.HalfWidth), 0, _floor.Length - 1);
        var to = Math.Clamp((int)Math.Ceiling(piece.X + piece.HalfWidth), 0, _floor.Length - 1);
        var top = piece.Alt + piece.HalfHeight;

        for (var i = from; i <= to; i++) _floor[i] = Math.Max(_floor[i], top);
    }

    /// <summary>
    /// Work the height map out again from the rubble that is left, letting anything that was
    /// resting on a piece somebody has just carried away drop onto what is under it now.
    /// </summary>
    private void RebuildFloor()
    {
        _floor = new double[(int)Math.Ceiling(Width) + 1];

        foreach (var piece in _debris.Where(d => d.Settled).OrderBy(d => d.Alt - d.HalfHeight))
        {
            piece.Alt = FloorUnder(piece.X, piece.HalfWidth) + piece.HalfHeight;
            RaiseFloor(piece);
        }
    }

    // ── The crew ──────────────────────────────────────────────────────────────

    private void UpdateWorker(Worker worker, double dt)
    {
        worker.IsMoving = false;

        var pace = Hurry ? HurryFactor : 1;
        var walk = Unit * WalkSpeed * pace;
        var climb = Unit * ClimbSpeed * pace;

        switch (worker.Activity)
        {
            case Activity.Falling:
                worker.VAlt -= Gravity * Unit * dt;
                worker.X = Math.Clamp(worker.X + worker.VX * dt, Unit * 0.4, Width - Unit * 0.4);
                worker.Alt += worker.VAlt * dt;
                if (worker.Alt <= 0)
                {
                    worker.Alt = 0;
                    if (_light == LightState.Red) Panic(worker);
                    else worker.Activity = Activity.Idle;
                }
                break;

            case Activity.Panicking:
                RunAround(worker, dt);
                break;

            case Activity.Climbing:
                worker.X = LadderX;
                worker.Alt += climb * dt;
                worker.IsMoving = true;
                worker.Stride += climb * dt / (Unit * 0.3);

                if (worker.Alt >= NextCourse())
                {
                    worker.Alt = NextCourse();
                    worker.Activity = Activity.Placing;
                    worker.Timer = PlacingSeconds;
                }
                break;

            case Activity.Placing:
                worker.Timer -= dt;
                if (worker.Timer > 0) break;

                if (worker.Load == Load.Brick && !IsComplete)
                {
                    _bricks.Add(new Brick(_bricks.Count / Columns, _bricks.Count % Columns,
                        worker.LoadColor ?? _brickColors[0]));
                    worker.Load = Load.None;
                    worker.LoadColor = null;

                    if (IsComplete) Say(worker, "Done!");
                }

                worker.Activity = Activity.Descending;
                break;

            case Activity.Descending:
                // Down faster than up, and faster still when the tower has started moving.
                var down = climb * (_light == LightState.Green ? 1.3 : 2.2);
                worker.X = LadderX;
                worker.Alt -= down * dt;
                worker.IsMoving = true;
                worker.Stride += down * dt / (Unit * 0.3);

                if (worker.Alt <= 0)
                {
                    worker.Alt = 0;
                    worker.Activity = _light == LightState.Yellow ? Activity.Watching : Activity.Idle;
                }
                break;

            case Activity.ToPile:
                if (!WalkTo(worker, PileX + Range(-0.2, 0.2) * Unit, walk, dt)) break;

                if (worker.Load == Load.Rubble) Returned++;
                worker.Load = Load.None;
                worker.LoadColor = null;

                if (_light == LightState.Green && _debris.Count == 0 && Needed(worker) > 0)
                {
                    worker.Load = Load.Brick;
                    worker.LoadColor = _brickColors[_random.Next(_brickColors.Count)];
                    worker.Activity = Activity.ToTower;
                }
                else
                {
                    worker.Activity = Activity.Idle;
                    worker.Timer = Range(0.5, 2);
                }
                break;

            case Activity.ToTower:
                if (!WalkTo(worker, LadderX, walk, dt)) break;
                worker.Activity = _light == LightState.Green ? Activity.Climbing : Activity.Idle;
                break;

            case Activity.ToRubble:
                if (worker.Claim is not { } claim || !_debris.Contains(claim))
                {
                    worker.Claim = null;
                    worker.Activity = Activity.Idle;
                    break;
                }

                if (!WalkTo(worker, Math.Clamp(claim.X, Unit * 0.4, Width - Unit * 0.4), walk, dt)) break;

                _debris.Remove(claim);
                RebuildFloor();
                worker.Claim = null;
                worker.Load = Load.Rubble;
                worker.LoadColor = claim.Color;
                worker.Activity = Activity.CarryingRubble;
                break;

            case Activity.CarryingRubble:
                if (!WalkTo(worker, PileX + Range(-0.2, 0.2) * Unit, walk, dt)) break;
                Returned++;
                worker.Load = Load.None;
                worker.LoadColor = null;
                worker.Activity = Activity.Idle;
                break;

            case Activity.Watching:
                // Everybody looks at the tower. The one direction a crowd watching something
                // cannot face is away from it.
                worker.Facing = 1;
                if (_light == LightState.Green) worker.Activity = Activity.Idle;
                break;

            case Activity.Idle:
                if (_light == LightState.Yellow)
                {
                    worker.Activity = Activity.Watching;
                    break;
                }

                if (_light == LightState.Green && FindWork(worker)) break;

                // No Greenlight: nobody moves. Otherwise a slow potter about the site.
                if (_light != LightState.Unknown) Wander(worker, dt);
                break;
        }

        // Somebody knocked mid-jump by a change of light still has to come down.
        if (!worker.OnTower && worker.Activity is not (Activity.Falling or Activity.Panicking) && worker.Alt > 0)
        {
            worker.Alt = Math.Max(0, worker.Alt - Gravity * Unit * dt * 0.1);
        }
    }

    /// <summary>
    /// Give an idle builder a job, if there is one. Rubble first: nobody lays a course on top
    /// of the last tower.
    /// </summary>
    private bool FindWork(Worker worker)
    {
        // Somebody who stopped with something in their hands carries on with it.
        if (worker.Load == Load.Brick)
        {
            worker.Activity = IsComplete || _debris.Count > 0 ? Activity.ToPile : Activity.ToTower;
            return true;
        }

        if (worker.Load == Load.Rubble)
        {
            worker.Activity = Activity.CarryingRubble;
            return true;
        }

        if (_debris.Count > 0)
        {
            // Highest first, so what is left is never propped up on nothing.
            var piece = _debris
                .Where(d => d.Settled && !d.Claimed)
                .OrderByDescending(d => d.Alt)
                .FirstOrDefault();

            if (piece is null) return false;

            piece.Claimed = true;
            worker.Claim = piece;
            worker.Activity = Activity.ToRubble;
            return true;
        }

        if (_workers.Any(w => w.Load == Load.Rubble)) return false;
        if (Needed(worker) <= 0) return false;

        worker.Activity = Activity.ToPile;
        return true;
    }

    /// <summary>
    /// Bricks still wanted, counting the ones already on their way. Without the second part the
    /// whole crew sets off with a brick for the last slot and five people climb down still
    /// holding one.
    /// </summary>
    /// <param name="asking">The builder wondering whether to fetch one, who does not count themselves.</param>
    private int Needed(Worker asking)
    {
        var onTheWay = _workers.Count(w =>
            w != asking && (w.Load == Load.Brick || (w.Activity == Activity.ToPile && w.Load == Load.None)));

        return Capacity - _bricks.Count - onTheWay;
    }

    /// <summary>The height a climber stops at: the base of the course being laid.</summary>
    private double NextCourse() => Math.Min(_bricks.Count / Columns, Rows - 1) * BrickHeight;

    private void Wander(Worker worker, double dt)
    {
        if (Math.Abs(worker.TargetX - worker.X) > 0.5 && worker.TargetX > 0)
        {
            WalkTo(worker, worker.TargetX, Unit * WanderSpeed, dt);
            return;
        }

        worker.Timer -= dt;
        if (worker.Timer > 0) return;

        worker.Timer = Range(2, 6);
        worker.TargetX = _random.NextDouble() < 0.5
            ? Range(PileX, Math.Max(PileX, LadderX - Unit))
            : worker.X;
    }

    private void RunAround(Worker worker, double dt)
    {
        worker.X += worker.Facing * worker.RunSpeed * dt;
        worker.IsMoving = true;
        worker.Stride += worker.RunSpeed * dt / (Unit * 0.3);

        var left = Unit * 0.4;
        var right = Width - Unit * 0.4;
        if (worker.X < left) { worker.X = left; worker.Facing = 1; }
        if (worker.X > right) { worker.X = right; worker.Facing = -1; }

        worker.Timer -= dt;
        if (worker.Timer <= 0)
        {
            worker.Timer = Range(0.5, 1.8);
            if (_random.NextDouble() < 0.6) worker.Facing = -worker.Facing;

            // The odd leap. Panic is not a straight line.
            if (worker.Alt <= 0 && _random.NextDouble() < 0.25) worker.VAlt = Unit * Range(4, 6);
        }

        if (worker.Alt > 0 || worker.VAlt > 0)
        {
            worker.VAlt -= Gravity * Unit * dt;
            worker.Alt += worker.VAlt * dt;
            if (worker.Alt <= 0) worker.Alt = worker.VAlt = 0;
        }

        worker.NextShout -= dt;
        if (worker.NextShout > 0) return;

        worker.NextShout = Range(1.2, 3.5);

        // Not over the top of somebody else's bubble, and not the same scream twice at once:
        // two bubbles stacked on each other read as one mess, and two of the same read as an echo.
        var scream = Screams[_random.Next(Screams.Length)];
        var room = _shouts.All(s =>
            s.Worker != worker && s.Text != scream && Math.Abs(s.Worker.X - worker.X) > Unit * 4.5);

        if (room && _shouts.Count < Math.Max(2, _workers.Count / 2)) Say(worker, scream);
    }

    /// <summary>Walk towards a spot. True once they are there.</summary>
    private bool WalkTo(Worker worker, double target, double speed, double dt)
    {
        var distance = target - worker.X;
        if (Math.Abs(distance) <= speed * dt)
        {
            worker.X = target;
            worker.TargetX = 0;
            return true;
        }

        var step = Math.Sign(distance) * speed * dt;
        worker.X += step;
        worker.Facing = Math.Sign(distance);
        worker.IsMoving = true;
        worker.Stride += Math.Abs(step) / (Unit * 0.3);
        return false;
    }

    // ── Speech ────────────────────────────────────────────────────────────────

    private void Say(Worker? worker, string text)
    {
        if (!Shouting || worker is null) return;

        _shouts.RemoveAll(s => s.Worker == worker);
        _shouts.Add(new Shout { Worker = worker, Text = text, Life = 1.4, Remaining = 1.4 });
    }

    private void UpdateShouts(double dt)
    {
        if (!Shouting)
        {
            _shouts.Clear();
            return;
        }

        foreach (var shout in _shouts) shout.Remaining -= dt;
        _shouts.RemoveAll(s => s.Remaining <= 0 || !_workers.Contains(s.Worker));
    }

    private double Range(double min, double max) => min + _random.NextDouble() * (max - min);
}
