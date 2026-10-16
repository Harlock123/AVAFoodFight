namespace AVAFoodFight.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It heads for the ice cream, picking up food on the
/// way when there's a pile not far off its line and time to spare. With food in hand, a cook lined up along one of
/// the eight ways gets it. It steers round the manholes and away from the cooks, and if anything thrown is about
/// to hit it, it takes whichever of the nine ways to move (counting standing still) keeps it clearest.
/// </summary>
public sealed class KidPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private const double Lined = 22;

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }
        // A cook in line: face him and throw.
        if (p.Holding is { } food && p.ThrowIn <= 0)
        {
            double range = Foods.Speed(food) * 1.1;
            foreach (var k in g.Cooks.Where(k => k.Up).OrderBy(k => Field.Distance(k.X, k.Y, p.X, p.Y)))
            {
                for (int w = 0; w < 8; w++)
                {
                    var (ux, uy) = Field.Unit(w);
                    // Where he'll be when it gets there.
                    double t = Field.Distance(k.X, k.Y, p.X, p.Y) / Foods.Speed(food), kx = k.X + k.Vx * t, ky = k.Y + k.Vy * t;
                    double dx = kx - p.X, dy = ky - p.Y, along = dx * ux + dy * uy, off = Math.Abs(dx * uy - dy * ux);
                    if (along < 30 || along > range || off > Lined + (food == FoodKind.Peas ? 12 : 0)) continue;
                    var c = w == p.Facing ? new Controls(Throw: true) : new Controls(Field.Ways[w].Dx, Field.Ways[w].Dy, true);
                    if (Clearance(g, c) > 4) { Doing = "throwing"; return c; }
                }
            }
        }
        // Food in hand and a cook coming: step sideways to get him in line.
        if (p.Holding is { } held && g.Cooks.Where(k => k.Up).OrderBy(k => Field.Distance(k.X, k.Y, p.X, p.Y)).FirstOrDefault() is { } near
            && Field.Distance(near.X, near.Y, p.X, p.Y) < 300)
        {
            double now = OffLine(p.X, p.Y, near, held);
            Controls? best = null;
            double least = now - 6;
            foreach (var (dx, dy) in Field.Ways)
            {
                var me = new Kid { X = p.X, Y = p.Y };
                var c = new Controls(dx, dy);
                for (int t = 0; t < 12; t++) GameEngine.Step(me, c, Field.Step);
                double off = OffLine(me.X, me.Y, near, held) - dx * 2;
                if (off < least) { least = off; best = c; }
            }
            if (best is { } line && Clearance(g, line) > 6) { Doing = "lining up"; return line; }
        }
        // Where to: food on the way if there's time, else the ice cream.
        double tx = Field.ConeX, ty = Field.ConeY, toCone = Field.Distance(p.X, p.Y, tx, ty) / GameEngine.RunSpeed;
        Doing = "for the ice cream";
        if (p.Holding == null && g.Melt > toCone + 6)
        {
            var pile = g.Piles.Where(pl => pl.Left > 0 && Field.Distance(pl.X, pl.Y, p.X, p.Y) < (pl.X > p.X - 80 ? 300 : 180))
                .OrderBy(pl => Field.Distance(pl.X, pl.Y, p.X, p.Y)).FirstOrDefault();
            if (pile != null) { (tx, ty) = (pile.X, pile.Y); Doing = "for food"; }
        }
        // Towards it, pushed off the manholes and the cooks.
        double vx = tx - p.X, vy = ty - p.Y, len = Math.Max(1, Math.Sqrt(vx * vx + vy * vy));
        vx /= len;
        vy /= len;
        foreach (var h in g.Holes)
        {
            double hx = p.X - h.X, hy = p.Y - h.Y, d = Math.Max(1, Math.Sqrt(hx * hx + hy * hy));
            if (d < 80) { vx += hx / d * (80 - d) / 80 * 2.2; vy += hy / d * (80 - d) / 80 * 2.2; }
        }
        // Running out of time: the cooks get less room, and it takes its chances.
        double shy = g.Melt < toCone + 4 ? 0.5 : 1.4;
        foreach (var k in g.Cooks.Where(k => k.Up))
        {
            double cx = p.X - k.X, cy = p.Y - k.Y, d = Math.Max(1, Math.Sqrt(cx * cx + cy * cy));
            if (d < 110) { vx += cx / d * (110 - d) / 110 * shy; vy += cy / d * (110 - d) / 110 * shy; }
        }
        len = Math.Max(1e-6, Math.Sqrt(vx * vx + vy * vy));
        var want = new Controls(Math.Abs(vx / len) > 0.38 ? Math.Sign(vx) : 0, Math.Abs(vy / len) > 0.38 ? Math.Sign(vy) : 0);
        return Safe(g, want, g.Melt < toCone + 4 ? 2 : 10);
    }

    /// <summary>How far off the nearest of the eight lines from here the cook will be when food thrown now gets to him.</summary>
    private static double OffLine(double x, double y, Cook k, FoodKind food)
    {
        double t = Field.Distance(k.X, k.Y, x, y) / Foods.Speed(food), kx = k.X + k.Vx * t, ky = k.Y + k.Vy * t;
        double least = double.MaxValue;
        for (int w = 0; w < 8; w++)
        {
            var (ux, uy) = Field.Unit(w);
            double dx = kx - x, dy = ky - y, along = dx * ux + dy * uy;
            if (along > 30) least = Math.Min(least, Math.Abs(dx * uy - dy * ux));
        }
        return least;
    }

    private Controls Safe(GameEngine g, Controls want, double enough)
    {
        double here = Clearance(g, want);
        if (here > enough) return want;
        var best = want;
        double most = here;
        foreach (var (dx, dy) in Field.Ways.Append((0, 0)))
        {
            var c = new Controls(dx, dy);
            double clear = Clearance(g, c);
            if (clear > most + 2) { most = clear; best = c; }
        }
        if (best != want) Doing = "dodging";
        return best;
    }

    /// <summary>How close the nearest danger comes over the next half second or so, moving like this: food thrown at it, a cook, or a manhole.</summary>
    private static double Clearance(GameEngine g, Controls c)
    {
        var p = g.Player;
        var me = new Kid { X = p.X, Y = p.Y, Facing = p.Facing };
        double least = double.MaxValue;
        var shots = g.Flying.Where(f => !f.ByKid).ToList();
        var cooks = g.Cooks.Where(k => k.Up && Field.Distance(k.X, k.Y, p.X, p.Y) < 160).ToList();
        for (int t = 1; t <= 30; t++)
        {
            double dt = t * Field.Step;
            GameEngine.Step(me, c, Field.Step);
            foreach (var f in shots)
                least = Math.Min(least, Field.Distance(f.X + f.Vx * dt, f.Y + f.Vy * dt, me.X, me.Y) - Kid.Radius - Foods.Radius(f.Kind));
            foreach (var k in cooks)
                least = Math.Min(least, Field.Distance(k.X + k.Vx * dt, k.Y + k.Vy * dt, me.X, me.Y) - Kid.Radius - Cook.Radius);
            foreach (var h in g.Holes)
                least = Math.Min(least, Field.Distance(h.X, h.Y, me.X, me.Y) - Hole.Radius + 2);
        }
        return least;
    }
}
