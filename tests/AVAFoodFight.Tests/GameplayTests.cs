using AVAFoodFight.Engine;

namespace AVAFoodFight.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avafoodfight-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Level 1 in play with the field cleared: no piles, no manholes, no cooks, the kid in the middle. Calm (the
    /// cooks put there stand still and throw nothing, and the ice cream keeps) unless <paramref name="calm"/> is false.
    /// </summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.Piles.Clear();
        g.Holes.Clear();
        g.Cooks.Clear();
        g.PutKid(400, 400);
        return g;
    }

    private static Pile PutPile(GameEngine g, FoodKind kind, double x, double y, int left = 3)
    {
        var p = new Pile { Kind = kind, X = x, Y = y, Left = left };
        g.Piles.Add(p);
        return p;
    }

    private static readonly Controls ThrowButton = new(Throw: true);

    // ───────────────────────────── the field ─────────────────────────────

    [Fact]
    public void Every_level_is_the_same_field_whoever_plays_it_with_manholes_and_piles_clear_of_each_other_and_of_the_ends()
    {
        for (int level = 1; level <= 12; level++)
        {
            var a = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
            var b = new GameEngine(99, HighScoreTable.Load(TempFolder())) { Silent = true };
            a.Go(level);
            b.Go(level);
            Assert.Equal(a.Holes, b.Holes);
            Assert.Equal(a.Piles.Select(p => (p.Kind, p.X, p.Y, p.Left)), b.Piles.Select(p => (p.Kind, p.X, p.Y, p.Left)));
            Assert.InRange(a.Holes.Count, 3, 6);
            Assert.InRange(a.Piles.Count, 6, 9);
            foreach (var h in a.Holes)
            {
                Assert.True(Field.Distance(h.X, h.Y, Field.StartX, Field.StartY) > 150);
                Assert.True(Field.Distance(h.X, h.Y, Field.ConeX, Field.ConeY) > 120);
                Assert.All(a.Piles, p => Assert.True(Field.Distance(h.X, h.Y, p.X, p.Y) >= 100));
            }
        }
    }

    [Fact]
    public void Later_levels_bring_more_cooks_faster_and_a_quicker_melting_ice_cream()
    {
        var g = Game();
        var seen = new List<(int Cooks, double Speed, double Melt)>();
        foreach (int level in new[] { 1, 3, 5, 7, 9 })
        {
            g.Go(level);
            seen.Add((g.CookCount, g.CookSpeed, g.MeltSeconds));
        }
        for (int i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i].Cooks >= seen[i - 1].Cooks);
            Assert.True(seen[i].Speed > seen[i - 1].Speed);
            Assert.True(seen[i].Melt < seen[i - 1].Melt);
        }
        Assert.Equal(5, seen[^1].Cooks);
    }

    [Fact]
    public void A_level_starts_with_the_cooks_down_their_manholes_coming_up_one_after_another()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(5);
        Assert.Equal(g.CookCount, g.Cooks.Count);
        Assert.All(g.Cooks, k => Assert.False(k.Up));
        Assert.All(g.Cooks, k => Assert.Equal((g.Holes[k.Hole].X, g.Holes[k.Hole].Y), (k.X, k.Y)));
        g.Run(60, default);
        int first = g.Cooks.Count(k => k.Up);
        g.Run(120, default);
        Assert.True(first >= 1 && g.Cooks.Count(k => k.Up) > first);
    }

    // ───────────────────────────── the kid ─────────────────────────────

    [Fact]
    public void The_kid_runs_eight_ways_at_one_speed_and_stays_on_the_field()
    {
        var g = Game();
        g.Run(60, new Controls(1, 0));
        Assert.Equal(400 + GameEngine.RunSpeed, g.Player.X, 0.5);
        g.PutKid(400, 400);
        g.Run(60, new Controls(-1, 1));
        double s = GameEngine.RunSpeed * Math.Sqrt(0.5);
        Assert.Equal((400 - s, 400 + s), (g.Player.X, g.Player.Y), new TolTuple(0.5));
        Assert.Equal(Field.WayOf(-1, 1), g.Player.Facing);
        g.Run(60 * 6, new Controls(0, 1));
        Assert.Equal(Field.Bottom - Kid.Radius, g.Player.Y, 0.01);
    }

    private sealed class TolTuple(double tol) : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) a, (double, double) b) => Math.Abs(a.Item1 - b.Item1) <= tol && Math.Abs(a.Item2 - b.Item2) <= tol;
        public int GetHashCode((double, double) _) => 0;
    }

    [Fact]
    public void Running_over_a_pile_picks_up_one_thing_and_only_with_empty_hands()
    {
        var g = Game();
        var pile = PutPile(g, FoodKind.Pie, 440, 400);
        var other = PutPile(g, FoodKind.Melon, 520, 400);
        g.Run(60, new Controls(1, 0));
        Assert.Equal(FoodKind.Pie, g.Player.Holding);
        Assert.Equal((2, 3), (pile.Left, other.Left));
    }

    [Fact]
    public void Thrown_food_flies_the_way_the_kid_faces_and_the_hands_are_empty_again()
    {
        var g = Game();
        PutPile(g, FoodKind.Tomato, 400, 400, 1);
        g.Run(1, new Controls(0, -1));
        Assert.Equal(FoodKind.Tomato, g.Player.Holding);
        g.Run(1, ThrowButton);
        var f = Assert.Single(g.Flying);
        Assert.True(f.ByKid);
        Assert.Equal((0, -Foods.Speed(FoodKind.Tomato)), (f.Vx, f.Vy), new TolTuple(0.01));
        Assert.Null(g.Player.Holding);
    }

    [Fact]
    public void Peas_go_as_a_spray_of_three()
    {
        var g = Game();
        PutPile(g, FoodKind.Peas, 400, 400, 1);
        g.Run(1, ThrowButton);
        Assert.Equal(3, g.Flying.Count);
        Assert.Equal(3, g.Flying.Select(f => Math.Round(Math.Atan2(f.Vy, f.Vx), 3)).Distinct().Count());
    }

    [Fact]
    public void Nothing_in_hand_throws_nothing()
    {
        var g = Game();
        g.Run(30, ThrowButton);
        Assert.Empty(g.Flying);
    }

    [Fact]
    public void A_cook_hit_scores_by_the_food_and_goes_back_down_his_manhole_for_a_while()
    {
        foreach (var (food, points) in new[] { (FoodKind.Peas, 100), (FoodKind.Banana, 200), (FoodKind.Melon, 300) })
        {
            var g = Game();
            g.Holes.Add(new Hole(700, 200));
            var k = g.PutCook(600, 400);
            PutPile(g, food, 400, 400, 1);
            g.Run(1, ThrowButton);
            g.Run(60, default);
            Assert.Equal(points, g.Score);
            Assert.False(k.Up);
            Assert.Equal((700, 200), (k.X, k.Y));
            g.Run((int)((GameEngine.DownSeconds + GameEngine.RiseSeconds) * 60) + 4, default);
            Assert.True(k.Up);
        }
    }

    [Fact]
    public void A_cook_still_down_his_manhole_cannot_be_hit()
    {
        var g = Game();
        var k = g.PutCook(600, 400);
        k.Rising = 5;
        PutPile(g, FoodKind.Pie, 400, 400, 1);
        g.Run(1, ThrowButton);
        g.Run(60, default);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void Food_meeting_food_in_the_air_splats_both()
    {
        var g = Game(calm: false);
        g.SetMelt(100);
        var k = g.PutCook(700, 400);
        k.Holding = FoodKind.Tomato;
        PutPile(g, FoodKind.Melon, 400, 400, 1);
        g.Run(1, ThrowButton);
        g.Run(40, default);
        Assert.True(g.Player.Alive);
        Assert.Empty(g.Flying);
        Assert.Contains(g.Effects, e => e.Kind == EffectKind.Splat);
    }

    // ───────────────────────────── the cooks ─────────────────────────────

    [Fact]
    public void A_cook_runs_at_the_kid_and_catching_him_costs_a_life()
    {
        var g = Game(calm: false);
        g.SetMelt(100);
        var k = g.PutCook(700, 400);
        int lives = g.Lives;
        for (int i = 0; i < 60 * 5 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal((Phase.Dying, "caught", lives - 1), (g.Phase, g.Cause, g.Lives));
        Assert.True(Field.Distance(k.X, k.Y, 400, 400) < 40);
    }

    [Fact]
    public void A_cook_with_food_throws_it_at_the_kid_and_a_hit_costs_a_life()
    {
        var g = Game(calm: false);
        g.SetMelt(100);
        var k = g.PutCook(700, 400);
        k.Holding = FoodKind.Pie;
        g.Tick(default);
        var f = Assert.Single(g.Flying);
        Assert.False(f.ByKid);
        Assert.True(f.Vx < 0 && Math.Abs(f.Vy) < Math.Abs(f.Vx) * 0.2);
        for (int i = 0; i < 60 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal((Phase.Dying, "splatted"), (g.Phase, g.Cause));
    }

    [Fact]
    public void A_cook_too_close_or_too_far_off_keeps_his_food()
    {
        var g = Game(calm: false);
        g.SetMelt(100);
        var near = g.PutCook(470, 400);
        near.Holding = FoodKind.Pie;
        var far = g.PutCook(960, 120);
        far.Holding = FoodKind.Pie;
        g.Tick(default);
        Assert.Empty(g.Flying);
    }

    [Fact]
    public void An_empty_handed_cook_picks_up_food_he_runs_over()
    {
        var g = Game(calm: false);
        g.SetMelt(100);
        g.PutKid(100, 120);
        var k = g.PutCook(700, 600);
        PutPile(g, FoodKind.Banana, 690, 600, 1);
        g.Tick(default);
        Assert.Equal(FoodKind.Banana, k.Holding);
    }

    [Fact]
    public void From_level_two_some_cooks_stand_guard_between_the_kid_and_the_ice_cream()
    {
        var g = Game(calm: false, wave: 2);
        g.SetMelt(100);
        g.PutKid(100, 400);
        g.PutCook(100, 120);
        var guard = g.PutCook(100, 700);
        g.Run(60 * 3, default);
        Assert.InRange(guard.X, 300, 700);
    }

    // ───────────────────────────── the ice cream ─────────────────────────────

    [Fact]
    public void Running_into_an_open_manhole_costs_a_life()
    {
        var g = Game();
        g.Holes.Add(new Hole(480, 400));
        g.Run(30, new Controls(1, 0));
        Assert.Equal((Phase.Dying, "down a manhole"), (g.Phase, g.Cause));
    }

    [Fact]
    public void Reaching_the_ice_cream_clears_the_level_with_a_bonus_for_the_level_and_the_seconds_left()
    {
        var g = Game(wave: 3);
        g.SetMelt(12.5);
        g.PutKid(Field.ConeX - 60, Field.ConeY);
        g.Run(30, new Controls(1, 0));
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(3 * GameEngine.LevelBonus + 12 * GameEngine.SecondBonus, g.Bonus);
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal(g.Bonus, g.Score);
        Assert.Equal(4, g.Wave);
    }

    [Fact]
    public void An_ice_cream_left_to_melt_costs_a_life_and_the_level_starts_again()
    {
        var g = Game(calm: false);
        g.SetMelt(1);
        int lives = g.Lives;
        g.Run(62, default);
        Assert.Equal((Phase.Dying, "too late", lives - 1), (g.Phase, g.Cause, g.Lives));
        g.Run((int)(GameEngine.DyingSeconds * 60) + 2, default);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(g.MeltSeconds, g.Melt, 0.1);
        Assert.Equal((Field.StartX, Field.StartY), (g.Player.X, g.Player.Y));
        Assert.True(g.Player.Safe > 0);
    }

    [Fact]
    public void A_life_lost_fills_the_piles_up_again()
    {
        var g = new GameEngine(4, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(2);
        var full = g.Piles.Select(p => p.Left).ToList();
        foreach (var p in g.Piles) p.Left = 0;
        g.Holes.Add(new Hole(g.Player.X + 40, g.Player.Y));
        g.Run(20, new Controls(1, 0));
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(GameEngine.DyingSeconds * 60) + 2, default);
        Assert.Equal(full, g.Piles.Select(p => p.Left));
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Every_twenty_five_thousand_is_an_extra_life()
    {
        var g = Game();
        g.Holes.Add(new Hole(700, 200));
        g.SetScore(GameEngine.ExtraLifeEvery - 100);
        int lives = g.Lives;
        g.PutCook(600, 400);
        PutPile(g, FoodKind.Tomato, 400, 400, 1);
        g.Run(1, ThrowButton);
        g.Run(60, default);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_life_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.Holes.Add(new Hole(420, 400));
        g.Run(10, new Controls(1, 0));
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "PIE") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("PIE", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_vary_the_level()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var levels = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            levels.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal([1, 2], levels);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, 0), (g.Wave, g.Lives, g.Score));
    }

    [Fact]
    public void The_computer_player_clears_the_first_three_levels()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(1);
            g.SetLives(3);
            for (int i = 0; i < 60 * 240 && g.Wave < 4 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Wave >= 4, $"seed {seed}: stuck on level {g.Wave}");
        }
    }

    [Fact]
    public void Running_straight_for_the_ice_cream_and_throwing_blind_soon_loses()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(3);
        g.SetLives(3);
        for (int t = 0; t < 60 * 240 && g.State == GameState.Playing && g.Wave < 6; t++) g.Tick(new Controls(1, (t / 40) % 3 - 1, true));
        Assert.True(g.Wave < 6);
    }
}
