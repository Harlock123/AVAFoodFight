using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAFoodFight.Engine;
using AVAFoodFight.Views;
using Controls = AVAFoodFight.Engine.Controls;

namespace AVAFoodFight.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set FOODFIGHT_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avafoodfight-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("FOODFIGHT_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAFoodFight-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Throw = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("PIE", 88400, 9);
        g.HighScores.Insert("AVA", 52100, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A level, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 200)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, busy, seconds));
        Shot(w, c, name);
    }

    private static bool Fighting(GameEngine g) => g.Phase == Phase.Play && g.Player.Alive && g.Player.Safe <= 0;

    [AvaloniaFact]
    public void A_throw() => Fight(2, "throw", g => Fighting(g) && g.Flying.Any(f => f.ByKid && f.Life is > 2.1 and < 2.3) && g.Cooks.Any(k => k.Up));

    [AvaloniaFact]
    public void Food_both_ways() => Fight(6, "food-fight", g => Fighting(g) && g.Flying.Any(f => f.ByKid) && g.Flying.Any(f => !f.ByKid) && g.Cooks.Count(k => k.Up) >= 3, seconds: 300);

    [AvaloniaFact]
    public void A_cook_climbing_out() => Fight(3, "climbing", g => Fighting(g) && g.Cooks.Any(k => k.Rising is > 0 and < 0.5 && k.Down <= 0) && g.Effects.Any(e => e.Kind == EffectKind.Splat));

    [AvaloniaFact]
    public void The_ice_cream_nearly_gone() => Fight(8, "melting", g => Fighting(g) && g.Melt < 7 && g.Cooks.Count(k => k.Up) >= 3, seconds: 400);

    /// <summary>The computer is hard to hit, so this one runs blindly at the ice cream until a cook gets him.</summary>
    [AvaloniaFact]
    public void Splatted()
    {
        var (w, c, g) = Open(5);
        g.Go(5);
        g.SetLives(30);
        bool hit = false;
        for (int t = 0; t < 60 * 300 && !hit; t++)
        {
            hit = g.Phase == Phase.Dying && g.Cause is "splatted" or "caught" && g.PhaseTime > 0.4 && g.Player.Y is > 200 and < 640 && g.Player.X < 880;
            if (!hit) g.Tick(new Controls(1, (t / 40) % 3 - 1, true));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        Assert.True(hit);
        Shot(w, c, "splatted");
    }

    [AvaloniaFact]
    public void Yum() => Fight(4, "yum", g => g.Phase == Phase.Clear && g.PhaseTime > 0.5);
}
