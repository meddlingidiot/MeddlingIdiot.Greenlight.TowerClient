namespace Greenlight.TowerClient.UnitTests;

/// <summary>
/// The building site. Worth testing despite being a desk toy: the collapse and the clean-up are
/// the parts that look perfect the first time and then, on the third red build of the day,
/// leave a brick hovering over the taskbar or a crew that never starts again — neither of
/// which anyone is going to catch by watching.
/// </summary>
public class TowerSimulationTests
{
    private const double Width = 240;
    private const double Height = 280;
    private const double Unit = 14;

    private static TowerSimulation Site(LightState light = LightState.Green, int builders = 5, int seed = 1234,
        double height = Height)
    {
        var sim = new TowerSimulation(builders, seed);
        sim.Resize(Width, height, Unit);
        sim.Light = light;
        return sim;
    }

    /// <summary>Run for a while at a steady frame rate, as the render loop does.</summary>
    private static void Run(TowerSimulation sim, double seconds, double step = 1.0 / 30)
    {
        for (var t = 0.0; t < seconds; t += step)
            sim.Advance(TimeSpan.FromSeconds(step));
    }

    /// <summary>Run until something is true, or give up after a while.</summary>
    private static bool RunUntil(TowerSimulation sim, Func<bool> done, double seconds, double step = 1.0 / 30)
    {
        for (var t = 0.0; t < seconds; t += step)
        {
            if (done()) return true;
            sim.Advance(TimeSpan.FromSeconds(step));
        }

        return done();
    }

    /// <summary>The tallest the sway gets over a few periods.</summary>
    private static double MaxSway(TowerSimulation sim, double seconds = 8)
    {
        var most = 0.0;
        for (var t = 0.0; t < seconds; t += 1.0 / 30)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 30));
            most = Math.Max(most, Math.Abs(sim.SwayAngle));
        }

        return most;
    }

    // ── Green ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Builds_while_green()
    {
        var sim = Site();
        Run(sim, 120);

        Assert.NotEmpty(sim.Bricks);
    }

    /// <summary>Courses go on bottom-up, left to right. A brick in mid-air is not a tower.</summary>
    [Fact]
    public void Lays_bricks_bottom_up()
    {
        var sim = Site();
        Run(sim, 240);

        for (var i = 0; i < sim.Bricks.Count; i++)
        {
            Assert.Equal(i / TowerSimulation.Columns, sim.Bricks[i].Row);
            Assert.Equal(i % TowerSimulation.Columns, sim.Bricks[i].Column);
        }
    }

    [Fact]
    public void Tops_out_and_stops()
    {
        var sim = Site(height: 100);

        Assert.True(RunUntil(sim, () => sim.IsComplete, 1200));
        Run(sim, 60);

        Assert.Equal(sim.Capacity, sim.Bricks.Count);

        // Nobody is still wandering about with a brick for a tower that is finished.
        Assert.All(sim.Workers, w => Assert.Equal(Load.None, w.Load));
        Assert.All(sim.Workers, w => Assert.False(w.OnTower));
    }

    [Fact]
    public void Says_so_when_it_is_finished()
    {
        var sim = Site(height: 100);
        var heard = false;

        RunUntil(sim, () =>
        {
            heard |= sim.Shouts.Any(s => s.Text == "Done!");
            return heard;
        }, 1200);

        Assert.True(heard);
    }

    [Fact]
    public void Works_faster_while_a_build_is_running()
    {
        var steady = Site(seed: 7);
        var hurried = Site(seed: 7);
        hurried.Hurry = true;

        Run(steady, 180);
        Run(hurried, 180);

        Assert.True(hurried.Bricks.Count > steady.Bricks.Count,
            $"hurried {hurried.Bricks.Count}, steady {steady.Bricks.Count}");
    }

    [Fact]
    public void Nothing_is_built_without_greenlight()
    {
        var sim = Site(LightState.Unknown);
        var before = sim.Workers.Select(w => w.X).ToList();

        Run(sim, 120);

        Assert.Empty(sim.Bricks);
        Assert.Equal(before, sim.Workers.Select(w => w.X).ToList());
    }

    [Fact]
    public void Does_not_sway_while_green()
    {
        var sim = Site();
        Run(sim, 200);

        Assert.Equal(0, MaxSway(sim), 6);
    }

    // ── Yellow ────────────────────────────────────────────────────────────────

    [Fact]
    public void Yellow_stops_work()
    {
        var sim = Site();
        Run(sim, 150);
        var built = sim.Bricks.Count;

        sim.Light = LightState.Yellow;
        Run(sim, 60);

        Assert.Equal(built, sim.Bricks.Count);

        // Everybody off the ladder, and looking at it.
        Assert.All(sim.Workers, w => Assert.Equal(0, w.Alt, 6));
        Assert.All(sim.Workers, w => Assert.Equal(Activity.Watching, w.Activity));
    }

    [Fact]
    public void Yellow_sways_the_tower()
    {
        var sim = Site();
        Run(sim, 200);

        sim.Light = LightState.Yellow;
        var sway = MaxSway(sim);

        Assert.True(sway > 0.005, $"swayed {sway} rad");
        Assert.True(sway <= 5 * Math.PI / 180 + 1e-9, $"swayed {sway} rad");
    }

    [Fact]
    public void A_taller_tower_sways_further()
    {
        var shortTower = Site(seed: 3);
        var tallTower = Site(seed: 3);

        Run(shortTower, 40);
        Run(tallTower, 400);
        Assert.True(tallTower.Bricks.Count > shortTower.Bricks.Count * 2);

        shortTower.Light = LightState.Yellow;
        tallTower.Light = LightState.Yellow;

        Assert.True(MaxSway(tallTower) > MaxSway(shortTower));
    }

    [Fact]
    public void Settles_upright_when_green_comes_back()
    {
        var sim = Site();
        Run(sim, 200);
        sim.Light = LightState.Yellow;
        Run(sim, 6);

        sim.Light = LightState.Green;
        Run(sim, 6);

        Assert.True(Math.Abs(sim.SwayAngle) < 0.002, $"still leaning {sim.SwayAngle} rad");
    }

    [Fact]
    public void Somebody_says_uh_oh()
    {
        var sim = Site();
        Run(sim, 120);

        sim.Light = LightState.Yellow;

        Assert.Contains(sim.Shouts, s => s.Text.StartsWith("Uh oh"));
    }

    [Fact]
    public void Picks_up_where_it_left_off_after_yellow()
    {
        var sim = Site();
        Run(sim, 150);
        sim.Light = LightState.Yellow;
        Run(sim, 20);
        var built = sim.Bricks.Count;

        sim.Light = LightState.Green;
        Run(sim, 90);

        Assert.True(sim.Bricks.Count > built);
    }

    // ── Red ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Red_brings_it_down()
    {
        var sim = Site();
        Run(sim, 200);
        var built = sim.Bricks.Count;
        Assert.True(built > 0);

        sim.Light = LightState.Red;

        Assert.Empty(sim.Bricks);

        // Every brick in the tower, plus anything anybody was carrying at the time.
        Assert.True(sim.Debris.Count >= built);
    }

    [Fact]
    public void Rubble_comes_to_rest_on_the_ground_and_inside_the_site()
    {
        var sim = Site();
        Run(sim, 300);
        sim.Light = LightState.Red;
        Run(sim, 15);

        Assert.All(sim.Debris, piece =>
        {
            Assert.True(piece.Settled);
            Assert.True(piece.Alt - piece.HalfHeight >= -0.01, "under the ground");
            Assert.True(piece.X - piece.HalfWidth >= -0.01, "off the left of the site");
            Assert.True(piece.X + piece.HalfWidth <= sim.Width + 0.01, "off the right of the site");
        });
    }

    /// <summary>
    /// Resting more or less flat. A brick balanced on its corner reads as the physics having
    /// given up, and one stood on its end is how the heap grew spires.
    /// </summary>
    [Fact]
    public void Rubble_lies_roughly_flat()
    {
        var sim = Site();
        Run(sim, 300);
        sim.Light = LightState.Red;
        Run(sim, 15);

        Assert.All(sim.Debris, piece =>
        {
            var offFlat = piece.Angle - Math.Round(piece.Angle / Math.PI) * Math.PI;
            Assert.True(Math.Abs(offFlat) <= TowerSimulation.RestingTilt + 1e-9, $"resting at {offFlat} rad");
        });
    }

    [Fact]
    public void Rubble_piles_up()
    {
        var sim = Site();
        Run(sim, 500);
        sim.Light = LightState.Red;
        Run(sim, 15);

        var top = sim.Debris.Max(d => d.Alt + d.HalfHeight);
        Assert.True(top > sim.BrickHeight * 1.5, $"pile only {top} high");
    }

    /// <summary>
    /// Away from the edge of the screen, across the site — not straight down into a heap where
    /// the tower stood, and not off into the scrollbar it was keeping clear of.
    /// </summary>
    [Fact]
    public void Topples_away_from_the_edge()
    {
        var sim = Site();
        Run(sim, 400);
        sim.Light = LightState.Red;
        Run(sim, 15);

        Assert.True(sim.Debris.Average(d => d.X) < sim.TowerCentre - sim.Unit);
    }

    [Fact]
    public void A_stalled_frame_does_not_put_rubble_through_the_floor()
    {
        var sim = Site();
        Run(sim, 300);
        sim.Light = LightState.Red;

        for (var i = 0; i < 20; i++) sim.Advance(TimeSpan.FromSeconds(5));

        Assert.All(sim.Debris, piece => Assert.True(piece.Alt - piece.HalfHeight >= -0.01));
    }

    [Fact]
    public void Red_sends_everybody_running()
    {
        var sim = Site();
        Run(sim, 150);
        sim.Light = LightState.Red;
        Run(sim, 4);

        Assert.All(sim.Workers, w =>
        {
            Assert.Equal(Activity.Panicking, w.Activity);
            Assert.True(w.IsMoving);
            Assert.InRange(w.X, 0, sim.Width);
        });
    }

    [Fact]
    public void They_run_faster_than_they_walk()
    {
        var sim = Site();
        Run(sim, 60);
        sim.Light = LightState.Red;
        Run(sim, 2);

        var before = sim.Workers.Select(w => w.X).ToList();
        sim.Advance(TimeSpan.FromSeconds(0.05));
        var speeds = sim.Workers.Select((w, i) => Math.Abs(w.X - before[i]) / 0.05).ToList();

        // Walking is under three builders' heights a second; nobody panicking is that calm.
        Assert.All(speeds, speed => Assert.True(speed > Unit * 3, $"only {speed} px/s"));
    }

    [Fact]
    public void They_scream()
    {
        var sim = Site();
        Run(sim, 60);
        sim.Light = LightState.Red;

        var heard = new HashSet<string>();
        for (var t = 0.0; t < 10; t += 1.0 / 30)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 30));
            foreach (var shout in sim.Shouts) heard.Add(shout.Text);
        }

        Assert.NotEmpty(heard);
        Assert.Contains(heard, text => text.Contains('!'));
    }

    [Fact]
    public void They_run_about_quietly_when_asked_not_to_scream()
    {
        var sim = Site();
        sim.Shouting = false;
        Run(sim, 60);
        sim.Light = LightState.Red;

        for (var t = 0.0; t < 10; t += 1.0 / 30)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 30));
            Assert.Empty(sim.Shouts);
        }

        Assert.All(sim.Workers, w => Assert.Equal(Activity.Panicking, w.Activity));
    }

    /// <summary>Screaming is a crowd noise, not a wall of speech bubbles.</summary>
    [Fact]
    public void Never_more_than_half_the_crew_screaming_at_once()
    {
        var sim = Site(builders: 8);
        Run(sim, 60);
        sim.Light = LightState.Red;

        for (var t = 0.0; t < 20; t += 1.0 / 30)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 30));
            Assert.True(sim.Shouts.Count <= 4);
        }
    }

    [Fact]
    public void Whoever_is_on_the_ladder_falls_off()
    {
        var sim = Site();
        Assert.True(RunUntil(sim, () => sim.Workers.Any(w => w.OnTower && w.Alt > sim.BrickHeight * 3), 600));
        var climber = sim.Workers.First(w => w.OnTower && w.Alt > sim.BrickHeight * 3);

        sim.Light = LightState.Red;

        Assert.Equal(Activity.Falling, climber.Activity);

        Run(sim, 3);
        Assert.Equal(0, climber.Alt, 6);
        Assert.Equal(Activity.Panicking, climber.Activity);
    }

    [Fact]
    public void Red_with_nothing_built_still_sends_everybody_running()
    {
        var sim = Site(LightState.Unknown);
        sim.Light = LightState.Red;
        Run(sim, 2);

        Assert.Empty(sim.Debris);
        Assert.All(sim.Workers, w => Assert.Equal(Activity.Panicking, w.Activity));
    }

    // ── Green again ───────────────────────────────────────────────────────────

    [Fact]
    public void Clears_the_rubble_before_building_again()
    {
        var sim = Site();
        Run(sim, 300);
        sim.Light = LightState.Red;
        Run(sim, 15);
        Assert.NotEmpty(sim.Debris);

        sim.Light = LightState.Green;

        Assert.True(RunUntil(sim, () =>
        {
            // Not one brick laid while there is rubble on the site.
            Assert.True(sim.Debris.Count == 0 || sim.Bricks.Count == 0);
            return sim.Debris.Count == 0;
        }, 1200));

        Assert.True(RunUntil(sim, () => sim.Bricks.Count > 0, 300));
        Assert.True(sim.Returned > 0);
    }

    /// <summary>
    /// Pieces are picked off the top of the heap, and whatever was resting on a piece that goes
    /// drops onto what is under it now. A brick left hanging where its support used to be is the
    /// one thing the clean-up must never leave behind.
    /// </summary>
    [Fact]
    public void Nothing_is_left_hanging_while_the_rubble_is_cleared()
    {
        var sim = Site();
        Run(sim, 500);
        sim.Light = LightState.Red;
        Run(sim, 15);
        sim.Light = LightState.Green;

        RunUntil(sim, () =>
        {
            foreach (var piece in sim.Debris.Where(d => d.Settled))
            {
                var bottom = piece.Alt - piece.HalfHeight;
                if (bottom < 0.01) continue;

                // Within a pixel sideways: the height map is kept a logical pixel at a time, so
                // an edge counts for the whole pixel it is in.
                var supported = sim.Debris.Any(other =>
                    other != piece && other.Settled &&
                    Math.Abs(other.Alt + other.HalfHeight - bottom) < 0.01 &&
                    other.X + other.HalfWidth > piece.X - piece.HalfWidth - 1 &&
                    other.X - other.HalfWidth < piece.X + piece.HalfWidth + 1);

                Assert.True(supported, $"a brick hanging {bottom} px up");
            }

            return sim.Debris.Count == 0;
        }, 1200);
    }

    [Fact]
    public void Somebody_sighs_and_starts_again()
    {
        var sim = Site();
        Run(sim, 200);
        sim.Light = LightState.Red;
        Run(sim, 10);

        sim.Light = LightState.Green;

        Assert.Contains(sim.Shouts, s => s.Text == "Right. Again.");
    }

    // ── Everything else ───────────────────────────────────────────────────────

    [Fact]
    public void The_same_seed_builds_the_same_site()
    {
        var a = Site(seed: 42);
        var b = Site(seed: 42);

        Run(a, 200);
        Run(b, 200);
        a.Light = b.Light = LightState.Red;
        Run(a, 5);
        Run(b, 5);

        Assert.Equal(a.Workers.Select(w => w.X), b.Workers.Select(w => w.X));
        Assert.Equal(a.Debris.Select(d => d.X), b.Debris.Select(d => d.X));
    }

    [Fact]
    public void The_crew_can_be_taken_on_and_laid_off()
    {
        var sim = Site(builders: 5);
        Run(sim, 60);

        sim.SetCrew(8);
        Assert.Equal(8, sim.Workers.Count);

        sim.SetCrew(2);
        Assert.Equal(2, sim.Workers.Count);

        var built = sim.Bricks.Count;
        Run(sim, 120);
        Assert.True(sim.Bricks.Count > built);
    }

    [Fact]
    public void A_smaller_site_keeps_what_fits()
    {
        var sim = Site();
        Run(sim, 400);
        Assert.True(sim.Bricks.Count > 20);

        sim.Resize(Width, 100, Unit);

        Assert.True(sim.Bricks.Count <= sim.Capacity);
        Assert.All(sim.Workers, w => Assert.InRange(w.X, 0, sim.Width));
    }

    [Fact]
    public void The_tower_stands_at_the_far_end_with_the_pile_at_the_near_one()
    {
        var sim = Site();

        Assert.True(sim.TowerRight <= sim.Width);
        Assert.True(sim.LadderX < sim.TowerLeft);
        Assert.True(sim.PileX < sim.LadderX);
    }
}
