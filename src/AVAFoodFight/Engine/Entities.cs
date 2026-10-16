namespace AVAFoodFight.Engine;

/// <summary>The controls: running (each -1, 0 or 1; up the screen is -1), and throw.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Throw = false);

/// <summary>The screen: a field below the score bar, the kid starting at the left and the ice cream at the right.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 70, Bottom = Height - 20, Left = 20, Right = Width - 20;
    public const double StartX = 80, StartY = (Top + Bottom) / 2;
    public const double ConeX = 950, ConeY = (Top + Bottom) / 2;

    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    /// <summary>The eight ways to face, from right (0) round clockwise on the screen.</summary>
    public static readonly (int Dx, int Dy)[] Ways = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    public static int WayOf(int dx, int dy) => Array.IndexOf(Ways, (dx, dy));

    public static (double X, double Y) Unit(int way)
    {
        var (dx, dy) = Ways[way];
        double k = dx != 0 && dy != 0 ? Math.Sqrt(0.5) : 1;
        return (dx * k, dy * k);
    }
}

public enum FoodKind { Peas, Tomato, Banana, Pie, Melon }

public static class Foods
{
    /// <summary>What a hit with each is worth, how fast it flies, and how big it is.</summary>
    public static int Points(FoodKind f) => f switch { FoodKind.Peas or FoodKind.Tomato => 100, FoodKind.Banana or FoodKind.Pie => 200, _ => 300 };
    public static double Speed(FoodKind f) => f switch { FoodKind.Melon => 380, FoodKind.Pie => 460, _ => 540 };
    public static double Radius(FoodKind f) => f switch { FoodKind.Melon => 16, FoodKind.Pie => 13, FoodKind.Peas => 8, _ => 10 };
}

/// <summary>A heap of one kind of food on the field: walk over it to pick one up.</summary>
public sealed class Pile
{
    public FoodKind Kind;
    public double X, Y;
    /// <summary>How many are left, and how many it starts (and starts again, after a life lost) with.</summary>
    public int Left, Full;
    public const double Reach = 30;
}

/// <summary>A manhole: the cooks come up out of them, and a kid who runs into one is gone.</summary>
public sealed record Hole(double X, double Y)
{
    public const double Radius = 22;
}

/// <summary>The kid: running, carrying one thing to throw at a time.</summary>
public sealed class Kid
{
    public double X = Field.StartX, Y = Field.StartY, Vx, Vy, Walk, Safe, ThrowIn;
    public int Facing;
    public bool Alive = true;
    public FoodKind? Holding;
    public const double Radius = 14;
}

/// <summary>A cook: up out of a manhole, after the kid, picking food off the piles and throwing it.</summary>
public sealed class Cook
{
    public int Hole, Colour;
    public double X, Y, Vx, Vy, T, ThrowIn, Walk;
    /// <summary>Climbing out of the hole (until it's up), or knocked back down it (until it comes up again).</summary>
    public double Rising, Down;
    public FoodKind? Holding;
    public bool Up => Rising <= 0 && Down <= 0;
    public const double Radius = 16;
}

/// <summary>Food in the air: thrown by the kid, or at him.</summary>
public sealed class Thrown
{
    public FoodKind Kind;
    public double X, Y, Vx, Vy, Life;
    public bool ByKid;
    public double Spin;
}

public enum EffectKind { Score, Splat, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public FoodKind Food;
    public bool Done => Age >= Life;
}
