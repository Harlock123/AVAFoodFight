using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAFoodFight.Engine;
using Effect = AVAFoodFight.Engine.Effect;

namespace AVAFoodFight.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAFoodFight/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cream = 0xFFFFF4D8, Pink = 0xFFFF8AB8;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    /// <summary>The cooks' scarves, by colour.</summary>
    private static readonly uint[] Scarves = [0xFFE03A3A, 0xFF3A7AE0, 0xFFF0B020, 0xFF9A4AD0, 0xFF2AB070];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101410), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawLawn(dc);
            foreach (var h in g.Holes) DrawHole(dc, h.X, h.Y, g.Cooks.Any(k => k.Hole == g.Holes.IndexOf(h) && !k.Up));
            foreach (var pl in g.Piles) DrawPile(dc, pl.Kind, pl.X, pl.Y, pl.Left);
            if (g.Phase != Phase.Clear) DrawCone(dc, Field.ConeX, Field.ConeY, g.MeltSeconds > 0 ? g.Melt / g.MeltSeconds : 1, 1.2);
            // Back to front, so the nearer ones stand in front.
            var people = new List<(double Y, Action Draw)>();
            foreach (var k in g.Cooks)
            {
                if (k.Down > 0) continue;
                double up = k.Rising > 0 ? 1 - Math.Min(1, k.Rising / GameEngine.RiseSeconds) : 1;
                if (up <= 0) continue;
                var cook = k;
                people.Add((k.Y, () => DrawCook(dc, cook.X, cook.Y, cook.Vx, cook.Walk, cook.Colour, cook.Holding, up, 1)));
            }
            people.Add((g.Player.Y, () => DrawPlayer(dc)));
            foreach (var (_, draw) in people.OrderBy(p => p.Y)) draw();
            foreach (var f in g.Flying)
            {
                dc.DrawEllipse(Brush(0x40000000), null, new Point(f.X, f.Y + 18), Foods.Radius(f.Kind) * 0.8, 4);
                DrawFood(dc, f.Kind, f.X, f.Y, 1, f.Spin);
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 16), 26, Yellow, center: true);
    }

    // ───────────────────────────── the field ─────────────────────────────

    /// <summary>A mown lawn in stripes, with flecks, and a white line round it.</summary>
    private static void DrawLawn(DrawingContext dc)
    {
        double h = Field.Height - Field.Top;
        for (int i = 0; i < 16; i++)
            dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF3E8A3Au : 0xFF46963F), new Rect(i * 64, Field.Top, 64, h));
        for (int i = 0; i < 700; i++)
        {
            uint q = Hash(i, 5, 3);
            dc.FillRectangle(Brush(q % 3 == 0 ? 0xFF2E6E2Eu : 0xFF5AA84Au), new Rect(q % 1024, Field.Top + (q >> 10) % (uint)h, 2, 4));
        }
        for (int i = 0; i < 40; i++)
        {
            uint q = Hash(i, 9, 7);
            double x = 30 + q % 960, y = Field.Top + 20 + (q >> 12) % (uint)(h - 40);
            uint c = (q >> 4) % 3 == 0 ? 0xFFFFF4A0 : (q >> 4) % 3 == 1 ? 0xFFFFFFFF : 0xFFFFA0C8;
            for (int j = 0; j < 5; j++)
            {
                double a = j * Math.PI * 2 / 5;
                dc.DrawEllipse(Brush(c), null, new Point(x + Math.Cos(a) * 2.5, y + Math.Sin(a) * 2.5), 1.8, 1.8);
            }
            dc.DrawEllipse(Brush(0xFFF0B020), null, new Point(x, y), 1.4, 1.4);
        }
        dc.DrawRectangle(null, Pen(0x90FFFFFF, 3), new Rect(Field.Left - 6, Field.Top + 4, Field.Right - Field.Left + 12, Field.Bottom - Field.Top + 10));
    }

    private static void DrawHole(DrawingContext dc, double x, double y, bool open)
    {
        var c = new Point(x, y);
        dc.DrawEllipse(Brush(0xFF6A6A66), Pen(0xFF3A3A36, 2), c, Hole.Radius + 5, (Hole.Radius + 5) * 0.8);
        dc.DrawEllipse(Brush(0xFF0A0A0A), null, c, Hole.Radius, Hole.Radius * 0.8);
        if (open) return;
        // The lid, a little off when someone's under it.
        dc.DrawEllipse(Brush(0xFF5A5A58), Pen(0xFF2A2A28, 1.5), c, Hole.Radius - 2, (Hole.Radius - 2) * 0.8);
        for (int i = -2; i <= 2; i++)
        {
            double w = Math.Sqrt(Math.Max(0, 1 - i * i / 9.0)) * (Hole.Radius - 6);
            dc.DrawLine(Pen(0xFF3A3A38, 2), new Point(x - w, y + i * 5), new Point(x + w, y + i * 5));
        }
    }

    private static void DrawPile(DrawingContext dc, FoodKind kind, double x, double y, int left)
    {
        if (left <= 0) return;
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x, y + 10), 26, 8);
        // A heap: three along the bottom, two on them, one on top.
        (double X, double Y)[] spots = [(-14, 4), (0, 6), (14, 4), (-7, -6), (7, -6), (0, -15)];
        double r = Foods.Radius(kind);
        double k = Math.Min(1, 12 / r);
        for (int i = 0; i < Math.Min(left, spots.Length); i++)
            DrawFood(dc, kind, x + spots[i].X * (kind == FoodKind.Melon ? 1.3 : 1), y + spots[i].Y, k, i * 0.7);
    }

    /// <summary>One of the five foods, about its middle; spin turns it as it flies.</summary>
    private static void DrawFood(DrawingContext dc, FoodKind kind, double x, double y, double k, double spin)
    {
        double r = Foods.Radius(kind) * k;
        var c = new Point(x, y);
        switch (kind)
        {
            case FoodKind.Peas:
                for (int i = 0; i < 3; i++)
                {
                    double a = spin + i * Math.PI * 2 / 3;
                    var at = new Point(x + Math.Cos(a) * r * 0.55, y + Math.Sin(a) * r * 0.55);
                    dc.DrawEllipse(Brush(0xFF5AC83A), Pen(0xFF2A7A1A, 1), at, r * 0.5, r * 0.5);
                    dc.DrawEllipse(Brush(0xFFB0F090), null, new Point(at.X - r * 0.15, at.Y - r * 0.15), r * 0.14, r * 0.14);
                }
                break;
            case FoodKind.Tomato:
                dc.DrawEllipse(Brush(0xFFE8302A), Pen(0xFF8A1A14, 1.2), c, r, r * 0.9);
                dc.DrawEllipse(Brush(0x90FFB0A0), null, new Point(x - r * 0.35, y - r * 0.3), r * 0.25, r * 0.18);
                for (int i = 0; i < 5; i++)
                {
                    double a = spin + i * Math.PI * 2 / 5;
                    dc.DrawLine(Pen(0xFF2A8A2A, 2 * k), new Point(x, y - r * 0.6), new Point(x + Math.Cos(a) * r * 0.45, y - r * 0.6 + Math.Sin(a) * r * 0.3));
                }
                break;
            case FoodKind.Banana:
            {
                double cs = Math.Cos(spin), sn = Math.Sin(spin);
                Point At(double px, double py) => new(x + (px * cs - py * sn) * r, y + (px * sn + py * cs) * r);
                var pts = new List<Point>();
                for (int i = 0; i <= 8; i++) { double a = Math.PI * (0.15 + 0.7 * i / 8); pts.Add(At(Math.Cos(a) * 1.3, Math.Sin(a) * 1.3 - 0.7)); }
                for (int i = 8; i >= 0; i--) { double a = Math.PI * (0.15 + 0.7 * i / 8); pts.Add(At(Math.Cos(a) * 0.8, Math.Sin(a) * 0.65 - 0.5)); }
                Poly(dc, 0xFFFFE04A, 0xFF9A7A1A, 1.2, [.. pts]);
                dc.DrawEllipse(Brush(0xFF5A3A1A), null, pts[0], 2 * k, 2 * k);
                break;
            }
            case FoodKind.Pie:
                dc.DrawEllipse(Brush(0xFFC88A3A), Pen(0xFF7A4A1A, 1.2), c, r, r * 0.85);
                dc.DrawEllipse(Brush(0xFFFFF6E8), null, new Point(x, y - r * 0.08), r * 0.7, r * 0.58);
                dc.DrawEllipse(Brush(0xFFE03A4A), null, new Point(x, y - r * 0.25), r * 0.18, r * 0.18);
                break;
            default:
                dc.DrawEllipse(Brush(0xFF3A9A3A), Pen(0xFF1A4A1A, 1.5), c, r, r * 0.82);
                for (int i = -1; i <= 1; i++)
                {
                    double w = r * (i == 0 ? 0.95 : 0.7);
                    double off = i * r * 0.45 + Math.Sin(spin) * r * 0.15;
                    dc.DrawLine(Pen(0xFF1E6A1E, 2.2 * k), new Point(x + off, y - w * 0.8), new Point(x + off, y + w * 0.8));
                }
                dc.DrawEllipse(Brush(0x60D0FFA0), null, new Point(x - r * 0.4, y - r * 0.4), r * 0.25, r * 0.15);
                break;
        }
    }

    /// <summary>The prize: a waffle cone with two scoops, getting smaller and dripping as it melts (left: 1 whole, 0 gone).</summary>
    private static void DrawCone(DrawingContext dc, double x, double y, double left, double k)
    {
        left = Math.Clamp(left, 0, 1);
        double melted = 1 - left;
        // The puddle.
        if (melted > 0.02) dc.DrawEllipse(Brush(Alpha(Pink, 0.85)), Pen(0xFFD05A8A, 1), new Point(x + 4 * k, y + 34 * k), (8 + 28 * melted) * k, (3 + 9 * melted) * k);
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x, y + 34 * k), 14 * k, 4 * k);
        Poly(dc, 0xFFD8A050, 0xFF8A5A1A, 1.5, new Point(x - 15 * k, y - 4 * k), new Point(x + 15 * k, y - 4 * k), new Point(x, y + 34 * k));
        for (int i = 1; i <= 3; i++)
        {
            dc.DrawLine(Pen(0xFF9A6A2A, 1), new Point(x - 15 * k + i * 7.5 * k, y - 4 * k), new Point(x - 15 * k + (i * 7.5 - 15) * k / 2 + 15 * k / 2 + i * 1.5 * k, y + 34 * k - (3 - i) * 8 * k));
            dc.DrawLine(Pen(0xFF9A6A2A, 1), new Point(x + 15 * k - i * 7.5 * k, y - 4 * k), new Point(x - (i * 1.5) * k, y + 34 * k - (3 - i) * 8 * k));
        }
        double s = 0.45 + 0.55 * left;
        dc.DrawEllipse(Brush(Cream), Pen(0xFFC8B890, 1), new Point(x, y - 10 * k), 16 * k * (0.7 + 0.3 * s), 11 * k * (0.7 + 0.3 * s));
        dc.DrawEllipse(Brush(Pink), Pen(0xFFD05A8A, 1), new Point(x, y - (14 + 14 * s) * k), 14 * k * s, 12 * k * s);
        dc.DrawEllipse(Brush(0xFFE02A3A), null, new Point(x, y - (20 + 22 * s) * k), 4 * k, 4 * k);
        // Drips down the cone.
        if (melted > 0.15)
            for (int i = 0; i < 3; i++)
            {
                double dx = (i - 1) * 9 * k, len = (4 + 14 * melted * (1 + i % 2)) * k;
                dc.DrawLine(Pen(Pink, 3 * k), new Point(x + dx, y - 4 * k), new Point(x + dx * 0.8, y - 4 * k + len));
                dc.DrawEllipse(Brush(Pink), null, new Point(x + dx * 0.8, y - 4 * k + len), 2.2 * k, 2.6 * k);
            }
    }

    // ───────────────────────────── people ─────────────────────────────

    private void DrawPlayer(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        if (!p.Alive)
        {
            DrawSplatted(dc, p.X, p.Y, g.Cause);
            return;
        }
        if (g.Phase == Phase.Clear)
        {
            DrawKid(dc, p.X, p.Y, 0, 0, null, 1, licking: true);
            return;
        }
        if (p.Safe > 0 && (int)(_clock * 12) % 2 == 0) return;
        bool moving = Math.Abs(p.Vx) + Math.Abs(p.Vy) > 1;
        DrawKid(dc, p.X, p.Y, Field.Ways[p.Facing].Dx, moving ? p.Walk : 0, p.Holding, 1);
    }

    /// <summary>The kid: sneakers, shorts, a striped top and a cap with its peak the way he faces; what he holds over his head.</summary>
    private static void DrawKid(DrawingContext dc, double x, double y, int facing, double walk, FoodKind? holding, double k, bool licking = false)
    {
        double swing = Math.Sin(walk * 14) * 5 * k;
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x, y + 20 * k), 13 * k, 4 * k);
        // Legs.
        dc.DrawLine(Pen(0xFFF0C8A0, 4 * k), new Point(x - 4 * k, y + 6 * k), new Point(x - 4 * k + swing, y + 17 * k));
        dc.DrawLine(Pen(0xFFF0C8A0, 4 * k), new Point(x + 4 * k, y + 6 * k), new Point(x + 4 * k - swing, y + 17 * k));
        dc.DrawEllipse(Brush(White), Pen(0xFF3A3A5A, 1), new Point(x - 4 * k + swing + facing * 1.5 * k, y + 19 * k), 3.6 * k, 2.4 * k);
        dc.DrawEllipse(Brush(White), Pen(0xFF3A3A5A, 1), new Point(x + 4 * k - swing + facing * 1.5 * k, y + 19 * k), 3.6 * k, 2.4 * k);
        // Shorts and top.
        dc.FillRectangle(Brush(0xFF2A4A9A), new Rect(x - 8 * k, y + 1 * k, 16 * k, 7 * k), (float)(2 * k));
        dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x - 9 * k, y - 11 * k, 18 * k, 13 * k), (float)(4 * k));
        dc.FillRectangle(Brush(0xFFE04A3A), new Rect(x - 9 * k, y - 7 * k, 18 * k, 3 * k));
        dc.FillRectangle(Brush(0xFFE04A3A), new Rect(x - 9 * k, y - 1 * k, 18 * k, 3 * k));
        // Arms: up holding something, or swinging.
        if (holding != null || licking)
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 3.5 * k), new Point(x - 8 * k, y - 9 * k), new Point(x - 9 * k, y - 26 * k));
            dc.DrawLine(Pen(0xFFF0C8A0, 3.5 * k), new Point(x + 8 * k, y - 9 * k), new Point(x + 9 * k, y - 26 * k));
        }
        else
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 3.5 * k), new Point(x - 8 * k, y - 8 * k), new Point(x - 12 * k - swing * 0.4, y + 3 * k));
            dc.DrawLine(Pen(0xFFF0C8A0, 3.5 * k), new Point(x + 8 * k, y - 8 * k), new Point(x + 12 * k + swing * 0.4, y + 3 * k));
        }
        // Head and cap.
        dc.DrawEllipse(Brush(0xFFF0C8A0), Pen(0xFF9A6A4A, 1), new Point(x, y - 18 * k), 8 * k, 8 * k);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 3 * k + facing * 2 * k, y - 18 * k), 1.3 * k, 1.6 * k);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + 3 * k + facing * 2 * k, y - 18 * k), 1.3 * k, 1.6 * k);
        dc.DrawLine(Pen(0xFF9A3A2A, 1.2 * k), new Point(x - 2.5 * k + facing * 2 * k, y - 14 * k), new Point(x + 2.5 * k + facing * 2 * k, y - 14 * k));
        Poly(dc, 0xFF2A6AE0, 0xFF1A3A8A, 1, new Point(x - 8.5 * k, y - 21 * k), new Point(x - 6 * k, y - 27 * k), new Point(x + 6 * k, y - 27 * k), new Point(x + 8.5 * k, y - 21 * k));
        double peak = facing == 0 ? 0 : facing * 7 * k;
        dc.FillRectangle(Brush(0xFF1A4AB0), new Rect(x - 7 * k + peak, y - 22.5 * k, 14 * k, 3 * k), (float)(1.5 * k));
        if (licking) DrawCone(dc, x, y - 40 * k, 1, 0.55 * k);
        else if (holding is { } f) DrawFood(dc, f, x, y - 34 * k, k, 0);
    }

    /// <summary>A cook: white jacket, a scarf in his colour, a tall hat; coming up out of his manhole as up goes 0 to 1.</summary>
    private static void DrawCook(DrawingContext dc, double x, double y, double vx, double walk, int colour, FoodKind? holding, double up, double k)
    {
        // Climbing out: drawn lower, and cut off at the rim.
        double sink = (1 - up) * 44 * k;
        using var clip = up < 1 ? dc.PushClip(new Rect(x - 40 * k, y - 120 * k, 80 * k, 120 * k)) : default(DrawingContext.PushedState?);
        y += sink;
        int facing = Math.Abs(vx) < 5 ? 0 : Math.Sign(vx);
        double swing = Math.Sin(walk * 11) * 5 * k;
        if (up >= 1) dc.DrawEllipse(Brush(0x40000000), null, new Point(x, y + 22 * k), 15 * k, 4 * k);
        dc.DrawLine(Pen(0xFF3A3A44, 5 * k), new Point(x - 5 * k, y + 8 * k), new Point(x - 5 * k + swing, y + 20 * k));
        dc.DrawLine(Pen(0xFF3A3A44, 5 * k), new Point(x + 5 * k, y + 8 * k), new Point(x + 5 * k - swing, y + 20 * k));
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 5 * k + swing + facing * 2 * k, y + 21 * k), 4 * k, 2.5 * k);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + 5 * k - swing + facing * 2 * k, y + 21 * k), 4 * k, 2.5 * k);
        // A round white jacket with buttons, and the apron.
        dc.DrawEllipse(Brush(0xFFF6F6F2), Pen(0xFFA0A0A8, 1.2), new Point(x, y - 2 * k), 13 * k, 13 * k);
        dc.FillRectangle(Brush(0xFFE6E6E0), new Rect(x - 8 * k, y + 1 * k, 16 * k, 10 * k), (float)(2 * k));
        dc.DrawEllipse(Brush(0xFF6A6A70), null, new Point(x - 3 * k, y - 6 * k), 1.3 * k, 1.3 * k);
        dc.DrawEllipse(Brush(0xFF6A6A70), null, new Point(x + 3 * k, y - 6 * k), 1.3 * k, 1.3 * k);
        if (holding != null)
        {
            dc.DrawLine(Pen(0xFFF6F6F2, 5 * k), new Point(x - 10 * k, y - 8 * k), new Point(x - 11 * k, y - 32 * k));
            dc.DrawLine(Pen(0xFFF6F6F2, 5 * k), new Point(x + 10 * k, y - 8 * k), new Point(x + 11 * k, y - 32 * k));
        }
        else
        {
            dc.DrawLine(Pen(0xFFF6F6F2, 5 * k), new Point(x - 11 * k, y - 6 * k), new Point(x - 17 * k - swing * 0.3, y + 4 * k));
            dc.DrawLine(Pen(0xFFF6F6F2, 5 * k), new Point(x + 11 * k, y - 6 * k), new Point(x + 17 * k + swing * 0.3, y + 4 * k));
        }
        // The scarf, its ends flying behind.
        uint scarf = Scarves[colour % Scarves.Length];
        dc.FillRectangle(Brush(scarf), new Rect(x - 7 * k, y - 15 * k, 14 * k, 4 * k), (float)(2 * k));
        Poly(dc, scarf, null, 0, new Point(x - facing * 4 * k, y - 13 * k), new Point(x - facing * 12 * k - 3 * k, y - 6 * k + swing * 0.3), new Point(x - facing * 10 * k + 3 * k, y - 4 * k));
        // Face: round and red-cheeked, with a moustache.
        dc.DrawEllipse(Brush(0xFFF2C098), Pen(0xFF9A6A4A, 1), new Point(x, y - 23 * k), 9 * k, 9 * k);
        dc.DrawEllipse(Brush(0x60FF5A5A), null, new Point(x - 5 * k, y - 20 * k), 2.5 * k, 2 * k);
        dc.DrawEllipse(Brush(0x60FF5A5A), null, new Point(x + 5 * k, y - 20 * k), 2.5 * k, 2 * k);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 3 * k + facing * 2 * k, y - 25 * k), 1.3 * k, 1.6 * k);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + 3 * k + facing * 2 * k, y - 25 * k), 1.3 * k, 1.6 * k);
        Poly(dc, 0xFF4A2A1A, null, 0, new Point(x - 6 * k + facing * 2 * k, y - 19 * k), new Point(x + facing * 2 * k, y - 21.5 * k), new Point(x + 6 * k + facing * 2 * k, y - 19 * k), new Point(x + facing * 2 * k, y - 20 * k));
        // The hat: a band and a puffed top.
        dc.FillRectangle(Brush(0xFFF6F6F2), new Rect(x - 8 * k, y - 36 * k, 16 * k, 6 * k));
        dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(0xFFB0B0B8, 1), new Point(x - 5 * k, y - 41 * k), 6 * k, 6 * k);
        dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(0xFFB0B0B8, 1), new Point(x + 5 * k, y - 41 * k), 6 * k, 6 * k);
        dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(0xFFB0B0B8, 1), new Point(x, y - 45 * k), 7 * k, 6.5 * k);
        dc.DrawLine(Pen(0xFFC0C0C8, 1), new Point(x - 8 * k, y - 32 * k), new Point(x + 8 * k, y - 32 * k));
        if (holding is { } f) DrawFood(dc, f, x, y - 58 * k, k, 0);
    }

    /// <summary>The kid after it went wrong: under a splat, down a hole, or with his ice cream gone.</summary>
    private void DrawSplatted(DrawingContext dc, double x, double y, string cause)
    {
        if (cause == "down a manhole")
        {
            double t = Math.Min(1, Engine.PhaseTime / 0.5);
            if (t < 1) using (dc.PushClip(new Rect(x - 30, y - 80, 60, 80))) DrawKid(dc, x, y + t * 50, 0, 0, null, 1);
            Text(dc, "OOPS!", new Point(x, y - 50), 26, White, center: true);
            return;
        }
        // Flat on his back, with a mess on him.
        using (dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(x, y + 6)))
            DrawKid(dc, x, y, 0, 0, null, 1);
        if (cause == "too late") return;
        uint c = cause == "caught" ? 0xFFF6F6F2 : 0xFFE8302A;
        DrawBlob(dc, x - 8, y + 2, 16, c, 1, 11);
        for (int i = 0; i < 4; i++)
        {
            double a = Math.PI * 2 * (0.15 + i * 0.25) + Engine.PhaseTime * 3;
            dc.DrawEllipse(Brush(Yellow), null, new Point(x - 8 + Math.Cos(a) * 20, y - 14 + Math.Sin(a) * 6), 3, 3);
        }
    }

    /// <summary>A splodge: a round middle with lumps round its edge.</summary>
    private static void DrawBlob(DrawingContext dc, double x, double y, double r, uint colour, double alpha, int salt)
    {
        uint c = Alpha(colour, alpha);
        dc.DrawEllipse(Brush(c), null, new Point(x, y), r, r * 0.8);
        for (int i = 0; i < 7; i++)
        {
            uint q = Hash(i, salt, 4);
            double a = i * Math.PI * 2 / 7 + (q % 100) / 100.0, d = r * (0.8 + (q >> 8) % 50 / 100.0), s = r * (0.25 + (q >> 16) % 30 / 100.0);
            dc.DrawEllipse(Brush(c), null, new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d * 0.8), s, s * 0.8);
        }
    }

    private static uint FoodColour(FoodKind f) => f switch
    {
        FoodKind.Peas => 0xFF5AC83A,
        FoodKind.Tomato => 0xFFE8302A,
        FoodKind.Banana => 0xFFFFE04A,
        FoodKind.Pie => 0xFFFFF6E8,
        _ => 0xFFF0506A,
    };

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        if (e.Kind == EffectKind.Splat && !InGame) return;
        switch (e.Kind)
        {
            case EffectKind.Splat:
                DrawBlob(dc, e.X, e.Y, Foods.Radius(e.Food) * (0.9 + 0.6 * Math.Min(1, t * 4)), FoodColour(e.Food), 1 - t * t, (int)(e.X + e.Y));
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3 + e.X, d = 10 + 30 * t;
                    dc.DrawEllipse(Brush(Alpha(FoodColour(e.Food), 1 - t)), null, new Point(e.X + Math.Cos(a) * d, e.Y + Math.Sin(a) * d * 0.7 + 20 * t * t), 2.5, 2.5);
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 24), 24, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF1A2418), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFFD05A8A), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 18), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(90, 18), 32, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 18), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 18), 28, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"LEVEL {g.Wave}", new Point(380, 18), 28, Cream, center: true);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++)
        {
            double x = 26 + i * 22;
            dc.DrawEllipse(Brush(0xFFF0C8A0), null, new Point(x, 50), 6, 6);
            Poly(dc, 0xFF2A6AE0, null, 0, new Point(x - 6.5, 48), new Point(x - 4.5, 42), new Point(x + 4.5, 42), new Point(x + 6.5, 48));
        }
        // How long the ice cream has left.
        double left = g.MeltSeconds > 0 ? Math.Clamp(g.Melt / g.MeltSeconds, 0, 1) : 0;
        bool hurry = g.Melt < 5 && g.Phase == Phase.Play;
        Text(dc, "MELTING", new Point(560, 18), 20, hurry && (int)(_clock * 4) % 2 == 0 ? Red : Dim, right: true);
        dc.FillRectangle(Brush(0xFF0A0A0A), new Rect(570, 10, 240, 18), 4);
        dc.FillRectangle(Brush(hurry ? Red : Pink), new Rect(572, 12, 236 * left, 14), 3);
        Text(dc, $"{Math.Ceiling(g.Melt):0}", new Point(830, 18), 24, hurry ? Red : Cream);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 250;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xD0FFF4D8), new Rect(cx - w / 2, cy - 46, w, h), 10);
        dc.DrawRectangle(null, Pen(0xFFD05A8A, 3), new Rect(cx - w / 2, cy - 46, w, h), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? 0xFFC0202A : 0xFFD0407A, center: true, shadow: false);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, 0xFF3A2A2A, center: true, shadow: false);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawLawn(dc);
        dc.FillRectangle(Brush(0x70102010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        string title = "AVA FOOD FIGHT";
        uint[] letters = [0xFFFF6A9A, Cream, 0xFFFFE04A, 0xFF7AE05A];
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 24 + 24 + i * 48, 128 + bob), 100, letters[i % letters.Length], center: true);
        }
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 190), 28, Cream, center: true);
        // A chase across the screen: the kid for the cone, a cook after him, a tomato in the air.
        double run = (_clock * 0.25) % 1, kx = 120 + run * 660;
        DrawCone(dc, 880, 280, 1 - run * 0.6, 1.4);
        DrawKid(dc, kx, 290, 1, _clock, null, 1.5);
        DrawCook(dc, kx - 150, 292, 100, _clock, 0, FoodKind.Tomato, 1, 1.5);
        Text(dc, "RACE TO THE ICE CREAM BEFORE IT MELTS - THE CHEFS WANT YOU FOR LUNCH", new Point(cx, 336), 24, White, center: true);
        FoodKind[] foods = [FoodKind.Peas, FoodKind.Tomato, FoodKind.Banana, FoodKind.Pie, FoodKind.Melon];
        string[] names = ["PEAS", "TOMATO", "BANANA", "PIE", "MELON"];
        for (int i = 0; i < foods.Length; i++)
        {
            double lx = cx - 320 + i * 160;
            DrawFood(dc, foods[i], lx, 404, 2, _clock * (i % 2 == 0 ? 1 : -1) * 0.8);
            Text(dc, names[i], new Point(lx, 450), 22, Cream, center: true);
            Text(dc, Foods.Points(foods[i]).ToString(), new Point(lx, 474), 22, Yellow, center: true);
        }
        Text(dc, "RUN OVER A PILE TO PICK UP FOOD - HIT A CHEF AND HE'S BACK DOWN HIS MANHOLE", new Point(cx, 512), 21, Cream, center: true);
        Text(dc, "MIND THE OPEN MANHOLES - AND DON'T GET SPLATTED", new Point(cx, 538), 21, Cream, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "ARROWS: RUN (8 WAYS)    SPACE: THROW THE WAY YOU FACE", new Point(cx, 656), 22, Cream, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, A", new Point(cx, 686), 22, Cream, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1E2A1A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "THE MESSIEST", new Point(cx, 140), 68, 0xFFFF6A9A, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1E2A1A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
