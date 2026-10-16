namespace AVAFoodFight.Engine;

public sealed partial class GameEngine
{
    public const double RunSpeed = 230, ThrowGap = 0.3, ConeReach = 36, PeaSpread = 0.14;
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, SafeSeconds = 2, ClearSeconds = 3.2, RiseSeconds = 1, DownSeconds = 2.6;
    public const int ExtraLifeEvery = 25000, LevelBonus = 1000, SecondBonus = 50;

    /// <summary>The level: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Kid Player { get; } = new();
    public List<Hole> Holes { get; } = [];
    public List<Pile> Piles { get; } = [];
    public List<Cook> Cooks { get; } = [];
    public List<Thrown> Flying { get; } = [];
    /// <summary>How long the ice cream has left before it's melted away.</summary>
    public double Melt { get; private set; }
    public double MeltSeconds => Math.Max(18, 34 - 1.5 * (Wave - 1));
    public KidPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>For tests: the cooks stand where they are and throw nothing, and the ice cream doesn't melt.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;

    public double CookSpeed => Math.Min(215, 140 + 10 * (Wave - 1));
    public double CookThrowGap => Math.Max(0.7, 1.5 - 0.1 * (Wave - 1));
    public double CookShotSpeed => Math.Min(460, 340 + 15 * (Wave - 1));
    public int CookCount => Math.Min(5, 1 + (Wave + 1) / 2);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Build(1);
        SetPhase(Phase.Start);
    }

    /// <summary>
    /// A level laid out: manholes across the middle of the field, piles of food among them (the heavier food more
    /// often later on), and a cook for each of the first few holes. The same for the same level every time.
    /// </summary>
    private void Build(int level)
    {
        var rng = new Random(1983 + level * 613);
        Holes.Clear();
        Piles.Clear();
        int holes = Math.Min(6, 3 + level / 2);
        for (int tries = 0; Holes.Count < holes && tries < 500; tries++)
        {
            double x = 240 + rng.NextDouble() * 560, y = Field.Top + 70 + rng.NextDouble() * (Field.Bottom - Field.Top - 140);
            if (Holes.Any(h => Field.Distance(h.X, h.Y, x, y) < 150)) continue;
            Holes.Add(new Hole(x, y));
        }
        int piles = 6 + Math.Min(3, level / 3);
        for (int tries = 0; Piles.Count < piles && tries < 500; tries++)
        {
            double x = 160 + rng.NextDouble() * 680, y = Field.Top + 50 + rng.NextDouble() * (Field.Bottom - Field.Top - 100);
            if (Holes.Any(h => Field.Distance(h.X, h.Y, x, y) < 100) || Piles.Any(p => Field.Distance(p.X, p.Y, x, y) < 110)) continue;
            int roll = rng.Next(10 + level);
            var kind = roll < 3 ? FoodKind.Peas : roll < 6 ? FoodKind.Tomato : roll < 8 ? FoodKind.Banana : roll < 10 ? FoodKind.Pie : FoodKind.Melon;
            int full = 3 + rng.Next(4);
            Piles.Add(new Pile { Kind = kind, X = x, Y = y, Left = full, Full = full });
        }
        Restart();
    }

    /// <summary>Everyone back to the start of the level: the kid at the left, the cooks down their holes, the piles full again, a fresh ice cream.</summary>
    private void Restart()
    {
        var p = Player;
        p.X = Field.StartX;
        p.Y = Field.StartY;
        p.Facing = 0;
        p.Alive = true;
        p.Safe = SafeSeconds;
        p.Holding = null;
        p.ThrowIn = 0;
        Flying.Clear();
        foreach (var pile in Piles) pile.Left = pile.Full;
        Cooks.Clear();
        for (int i = 0; i < Math.Min(CookCount, Holes.Count); i++)
        {
            var h = Holes[i];
            Cooks.Add(new Cook { Hole = i, Colour = i, X = h.X, Y = h.Y, Rising = 0.8 + 0.8 * i, ThrowIn = CookThrowGap * 0.8 });
        }
        Melt = MeltSeconds;
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Build(wave);
        SetPhase(Phase.Start);
        Say($"LEVEL {wave}", "GET TO THE ICE CREAM BEFORE IT MELTS", StartSeconds + 0.3);
        Play(Sfx.LevelStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Run(dt, c);
                if (Phase != Phase.Play) break;
                MoveCooks(dt);
                MoveFood(dt);
                if (Phase != Phase.Play) break;
                Contact();
                if (Phase != Phase.Play) break;
                if (!Calm)
                {
                    double was = Melt;
                    Melt = Math.Max(0, Melt - dt);
                    if (Melt < 5 && Math.Floor(was) != Math.Floor(Melt)) Play(Sfx.Tick);
                    if (Melt <= 0) { Lose("too late"); Play(Sfx.Melted); }
                }
                break;
            case Phase.Dying:
                MoveFood(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Restart();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the kid ─────────────────────────────

    /// <summary>One step of running: eight ways, kept on the field.</summary>
    public static void Step(Kid p, Controls c, double dt)
    {
        p.Vx = p.Vy = 0;
        if (c.Dx == 0 && c.Dy == 0) return;
        p.Facing = Field.WayOf(Math.Sign(c.Dx), Math.Sign(c.Dy));
        var (ux, uy) = Field.Unit(p.Facing);
        p.Vx = ux * RunSpeed;
        p.Vy = uy * RunSpeed;
        p.X = Math.Clamp(p.X + ux * RunSpeed * dt, Field.Left + Kid.Radius, Field.Right - Kid.Radius);
        p.Y = Math.Clamp(p.Y + uy * RunSpeed * dt, Field.Top + Kid.Radius, Field.Bottom - Kid.Radius);
        p.Walk += dt;
    }

    private void Run(double dt, Controls c)
    {
        var p = Player;
        p.Safe = Math.Max(0, p.Safe - dt);
        p.ThrowIn -= dt;
        Step(p, c, dt);
        // Down a manhole.
        if (Holes.Any(h => Field.Distance(h.X, h.Y, p.X, p.Y) < Hole.Radius - 6)) { Lose("down a manhole"); Play(Sfx.Fall); return; }
        // Over a pile with empty hands: a handful.
        if (p.Holding == null && Piles.FirstOrDefault(pl => pl.Left > 0 && Field.Distance(pl.X, pl.Y, p.X, p.Y) < Pile.Reach) is { } pile)
        {
            pile.Left--;
            p.Holding = pile.Kind;
            Play(Sfx.Pickup);
        }
        if (c.Throw && p.Holding is { } food && p.ThrowIn <= 0)
        {
            p.ThrowIn = ThrowGap;
            p.Holding = null;
            var (ux, uy) = Field.Unit(p.Facing);
            Toss(food, p.X + ux * 16, p.Y + uy * 16, Math.Atan2(uy, ux), Foods.Speed(food), byKid: true);
            Play(Sfx.Throw);
        }
        if (Field.Distance(p.X, p.Y, Field.ConeX, Field.ConeY) < ConeReach) Reached();
    }

    /// <summary>Food thrown: a handful of peas goes as a spray of three, anything else on its own.</summary>
    private void Toss(FoodKind food, double x, double y, double angle, double speed, bool byKid)
    {
        foreach (double off in food == FoodKind.Peas ? new[] { -PeaSpread, 0, PeaSpread } : new[] { 0.0 })
            Flying.Add(new Thrown { Kind = food, X = x, Y = y, Vx = Math.Cos(angle + off) * speed, Vy = Math.Sin(angle + off) * speed, Life = 2.5, ByKid = byKid });
    }

    // ───────────────────────────── the cooks ─────────────────────────────

    /// <summary>
    /// The cooks: up out of their holes, after the kid (each a little off to its own side), grabbing food off any pile
    /// they pass (and going a little out of their way for it when they're empty-handed and the kid's far off), and
    /// throwing it at him when he's in range.
    /// </summary>
    private void MoveCooks(double dt)
    {
        var p = Player;
        foreach (var k in Cooks)
        {
            k.T += dt;
            if (k.Down > 0)
            {
                if ((k.Down -= dt) <= 0) { k.Rising = RiseSeconds; Play(Sfx.CookUp); }
                continue;
            }
            if (k.Rising > 0) { k.Rising -= dt; continue; }
            if (Calm) continue;
            k.ThrowIn -= dt;
            // Half of them chase the kid; the other half keep between him and the ice cream, to head him off.
            double tx, ty, far = Field.Distance(k.X, k.Y, p.X, p.Y);
            if (k.Colour % 2 == 1 && Wave >= 2)
            {
                double f = 0.45 + 0.15 * Math.Sin(k.T * 0.5 + k.Colour);
                tx = p.X + (Field.ConeX - p.X) * f;
                ty = p.Y + (Field.ConeY - p.Y) * f + Math.Sin(k.T * 0.8 + k.Colour) * 50;
            }
            else
            {
                // Each a little off to his own side, closing in straight at the end.
                double wide = 50 * Math.Min(1, far / 250);
                tx = p.X + Math.Sin(k.T * 0.7 + k.Colour * 1.7) * wide;
                ty = p.Y + Math.Cos(k.T * 0.6 + k.Colour * 2.3) * wide;
            }
            if (k.Holding == null && far > 200 && Piles.Where(pl => pl.Left > 0).OrderBy(pl => Field.Distance(pl.X, pl.Y, k.X, k.Y)).FirstOrDefault() is { } near
                && Field.Distance(near.X, near.Y, k.X, k.Y) < 220)
                (tx, ty) = (near.X, near.Y);
            double dx = tx - k.X, dy = ty - k.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), speed = CookSpeed * (0.9 + 0.07 * k.Colour);
            k.Vx += (dx / d * speed - k.Vx) * Math.Min(1, 3 * dt);
            k.Vy += (dy / d * speed - k.Vy) * Math.Min(1, 3 * dt);
            k.X = Math.Clamp(k.X + k.Vx * dt, Field.Left + 10, Field.Right - 10);
            k.Y = Math.Clamp(k.Y + k.Vy * dt, Field.Top + 10, Field.Bottom - 10);
            k.Walk += dt;
            if (k.Holding == null && Piles.FirstOrDefault(pl => pl.Left > 0 && Field.Distance(pl.X, pl.Y, k.X, k.Y) < Pile.Reach) is { } pile)
            {
                pile.Left--;
                k.Holding = pile.Kind;
            }
            if (k.Holding is { } food && k.ThrowIn <= 0 && p.Alive && p.Safe <= 0 && far > 110 && far < 430)
            {
                // At where the kid's going, roughly (half way there).
                double lead = far / CookShotSpeed * 0.5, ax = p.X + p.Vx * lead, ay = p.Y + p.Vy * lead;
                double a = Math.Atan2(ay - k.Y, ax - k.X) + (_rng.NextDouble() - 0.5) * 0.2;
                k.Holding = null;
                k.ThrowIn = CookThrowGap * (0.8 + _rng.NextDouble() * 0.4);
                Toss(food, k.X, k.Y, a, CookShotSpeed * (food == FoodKind.Melon ? 0.75 : 1), byKid: false);
                Play(Sfx.Throw);
            }
        }
    }

    // ───────────────────────────── food in the air ─────────────────────────────

    private void MoveFood(double dt)
    {
        var p = Player;
        foreach (var f in Flying)
        {
            f.Life -= dt;
            f.X += f.Vx * dt;
            f.Y += f.Vy * dt;
            f.Spin += dt * 10;
            if (f.X < 0 || f.X > Field.Width || f.Y < Field.Top - 10 || f.Y > Field.Height) { f.Life = 0; continue; }
            double r = Foods.Radius(f.Kind);
            if (f.ByKid)
            {
                var hit = Cooks.FirstOrDefault(k => k.Up && Field.Distance(k.X, k.Y, f.X, f.Y) < Cook.Radius + r);
                if (hit != null)
                {
                    f.Life = 0;
                    Splat(f);
                    Knock(hit, f.Kind);
                }
            }
            else if (p.Alive && Phase == Phase.Play && p.Safe <= 0 && Field.Distance(p.X, p.Y, f.X, f.Y) < Kid.Radius + r - 2)
            {
                f.Life = 0;
                Splat(f);
                Lose("splatted");
                Play(Sfx.Splat);
                return;
            }
        }
        // Food meeting food in the air: both splat.
        foreach (var a in Flying.Where(f => f.ByKid && f.Life > 0))
        foreach (var b in Flying.Where(f => !f.ByKid && f.Life > 0))
            if (Field.Distance(a.X, a.Y, b.X, b.Y) < Foods.Radius(a.Kind) + Foods.Radius(b.Kind))
            {
                a.Life = b.Life = 0;
                Splat(a);
                Play(Sfx.Splat);
            }
        Flying.RemoveAll(f => f.Life <= 0);
    }

    private void Splat(Thrown f) => Effects.Add(new Effect { Kind = EffectKind.Splat, X = f.X, Y = f.Y, Life = 0.9, Food = f.Kind });

    /// <summary>A cook hit: back down his manhole for a while, and the points by what hit him.</summary>
    private void Knock(Cook k, FoodKind food)
    {
        Award(Foods.Points(food), k.X, k.Y);
        var h = Holes[k.Hole];
        k.X = h.X;
        k.Y = h.Y;
        k.Vx = k.Vy = 0;
        k.Holding = null;
        k.Down = DownSeconds;
        Play(Sfx.CookDown);
    }

    /// <summary>A cook laying hands on the kid.</summary>
    private void Contact()
    {
        var p = Player;
        if (p.Safe > 0) return;
        if (Cooks.Any(k => k.Up && Field.Distance(k.X, k.Y, p.X, p.Y) < Cook.Radius + Kid.Radius - 4)) { Lose("caught"); Play(Sfx.Hit); }
    }

    private void Reached()
    {
        Bonus = LevelBonus * Wave + SecondBonus * (int)Melt;
        Flying.Clear();
        Play(Sfx.Yum);
        Play(Sfx.LevelClear);
        Say("YUM!", $"BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Lose(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        Lives--;
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
        else if (cause == "too late") Say("TOO LATE!", "THE ICE CREAM MELTED", DyingSeconds);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 20, Text = points.ToString(), Life = 0.8, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a level, past its number.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    internal Cook PutCook(double x, double y, int hole = 0)
    {
        var k = new Cook { Hole = hole, Colour = Cooks.Count, X = x, Y = y, ThrowIn = 0 };
        Cooks.Add(k);
        return k;
    }

    internal void PutKid(double x, double y, int facing = 0)
    {
        var p = Player;
        p.X = x;
        p.Y = y;
        p.Facing = facing;
        p.Alive = true;
        p.Safe = 0;
    }

    internal void SetMelt(double s) => Melt = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
