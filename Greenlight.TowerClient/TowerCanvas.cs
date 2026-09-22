using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Greenlight.TowerClient;

/// <summary>
/// Draws the site. Everything is drawn from the simulation's numbers each frame — there are no
/// controls per builder or per brick, because a hundred bricks' worth of layout and hit testing
/// would be a hundred bricks' worth of work to no purpose on a window nobody can click.
/// </summary>
/// <remarks>
/// The simulation has one orientation: tower on the right, pile on the left. A site on the left
/// of the screen is the same site drawn through a mirror, which is why every x in here goes
/// through <see cref="X"/> and nothing reads <c>worker.X</c> straight onto the screen.
/// </remarks>
public sealed class TowerCanvas : Control
{
    private static readonly IBrush Skin = new SolidColorBrush(Color.FromRgb(240, 196, 150));
    private static readonly IBrush Trousers = new SolidColorBrush(Color.FromRgb(52, 62, 86));
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
    private static readonly IBrush LadderWood = new SolidColorBrush(Color.FromRgb(150, 104, 58));
    private static readonly IBrush FlagCloth = new SolidColorBrush(Color.FromRgb(95, 208, 138));
    private static readonly IBrush FlagPole = new SolidColorBrush(Color.FromRgb(70, 70, 76));
    private static readonly IPen Mortar = new Pen(new SolidColorBrush(Color.FromArgb(120, 60, 40, 20)), 0.8);

    private static readonly IBrush BubbleBrush = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
    private static readonly IBrush BubbleTextBrush = new SolidColorBrush(Color.FromRgb(28, 28, 32));
    private static readonly IPen BubblePen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1);
    private static readonly Typeface ShoutFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private readonly Dictionary<string, IBrush> _brushes = [];

    public TowerCanvas(TowerSimulation simulation)
    {
        Simulation = simulation;
        IsHitTestVisible = false;
    }

    public TowerSimulation Simulation { get; }

    /// <summary>Draw the site the other way round, for a site on the left of the screen.</summary>
    public bool Mirrored { get; set; }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Height <= 1) return;

        // Dimmed rather than hidden with no Greenlight: an empty patch of taskbar looks like the
        // app crashed, where a faded site with its tools down looks like what it is.
        using var _ = context.PushOpacity(Simulation.Light == LightState.Unknown ? 0.4 : 1.0);

        DrawPile(context);
        DrawTower(context);

        foreach (var piece in Simulation.Debris)
            DrawDebris(context, piece);

        foreach (var worker in Simulation.Workers.Where(w => !w.OnTower))
            DrawWorker(context, worker, X(worker.X), Y(worker.Alt), Mirrored ? -worker.Facing : worker.Facing);

        foreach (var shout in Simulation.Shouts)
            DrawShout(context, shout);
    }

    // ── Coordinates ───────────────────────────────────────────────────────────

    private double X(double x) => Mirrored ? Simulation.Width - x : x;

    private double Y(double alt) => Simulation.Height - alt;

    /// <summary>Where a builder's feet are on screen, swaying with the tower if they are on it.</summary>
    private Point Feet(Worker worker)
    {
        var (x, alt) = worker.OnTower ? Simulation.Sway(worker.X, worker.Alt) : (worker.X, worker.Alt);
        return new Point(X(x), Y(alt));
    }

    // ── The pile ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A little pyramid of bricks where the crew fetch them from. It grows a course or two as
    /// rubble is carried back to it, which is what makes the clearing-up read as tidying rather
    /// than as bricks vanishing.
    /// </summary>
    private void DrawPile(DrawingContext context)
    {
        var sim = Simulation;
        var bw = sim.BrickWidth;
        var bh = sim.BrickHeight;
        var courses = 3 + Math.Min(3, sim.Returned / 12);
        var colors = sim.BrickColors;

        for (var row = 0; row < courses; row++)
        {
            var count = courses - row;
            var left = sim.PileX - count * bw / 2;

            for (var i = 0; i < count; i++)
            {
                var x = left + i * bw;
                var rect = new Rect(Math.Min(X(x), X(x + bw)), Y((row + 1) * bh), bw, bh);
                context.DrawRectangle(Brush(colors[(row * 3 + i) % colors.Count]), Mortar, rect);
            }
        }
    }

    // ── The tower ─────────────────────────────────────────────────────────────

    private void DrawTower(DrawingContext context)
    {
        var sim = Simulation;
        var climbers = sim.Workers.Where(w => w.OnTower).ToList();
        if (sim.Bricks.Count == 0 && climbers.Count == 0) return;

        // Everything on the tower is drawn about its base, rotated by the sway. Positive sway
        // leans towards the pile, which on screen is to the left — anticlockwise — unless the
        // whole site is mirrored.
        var pivot = new Point(X(sim.TowerCentre), Y(0));
        var turn = Mirrored ? sim.SwayAngle : -sim.SwayAngle;

        using (context.PushTransform(Matrix.CreateRotation(turn) * Matrix.CreateTranslation(pivot.X, pivot.Y)))
        {
            var flip = Mirrored ? -1 : 1;
            double LocalX(double x) => (x - sim.TowerCentre) * flip;

            foreach (var brick in sim.Bricks)
            {
                var left = LocalX(sim.TowerLeft + brick.Column * sim.BrickWidth);
                var right = LocalX(sim.TowerLeft + (brick.Column + 1) * sim.BrickWidth);
                var top = -(brick.Row + 1) * sim.BrickHeight;

                context.DrawRectangle(Brush(brick.Color), Mortar,
                    new Rect(Math.Min(left, right), top, sim.BrickWidth, sim.BrickHeight));
            }

            DrawLadder(context, LocalX(sim.LadderX), climbers);

            if (sim.IsComplete) DrawFlag(context, 0, -sim.TowerHeight, flip);

            foreach (var climber in climbers)
                DrawWorker(context, climber, LocalX(climber.X), -climber.Alt, flip);
        }
    }

    /// <summary>
    /// Up the side of the tower, from the ground to a little past the top of whatever is being
    /// climbed — the course being laid, or the highest climber.
    /// </summary>
    private void DrawLadder(DrawingContext context, double x, List<Worker> climbers)
    {
        var sim = Simulation;
        var top = Math.Max(sim.TowerHeight, climbers.Count > 0 ? climbers.Max(c => c.Alt) : 0) + sim.Unit * 0.5;
        if (sim.IsComplete && climbers.Count == 0) return;   // finished: the ladder is taken away

        var half = sim.Unit * 0.18;
        var rail = Math.Max(1, sim.Unit * 0.07);

        context.DrawRectangle(LadderWood, null, new Rect(x - half - rail / 2, -top, rail, top));
        context.DrawRectangle(LadderWood, null, new Rect(x + half - rail / 2, -top, rail, top));

        for (var y = sim.Unit * 0.25; y < top; y += sim.Unit * 0.3)
            context.DrawRectangle(LadderWood, null, new Rect(x - half, -y, half * 2, rail * 0.8));
    }

    /// <summary>Topped out: a flag on the roof, flapping.</summary>
    private void DrawFlag(DrawingContext context, double x, double top, int flip)
    {
        var unit = Simulation.Unit;
        var pole = unit * 1.4;

        context.DrawRectangle(FlagPole, null, new Rect(x - 0.6, top - pole, 1.2, pole));

        // A pennant whose tip wanders a little, which is all it takes to look like wind.
        var flap = Math.Sin(Simulation.Time * 5) * unit * 0.12;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, top - pole), true);
            g.LineTo(new Point(x - flip * unit * 0.9, top - pole + unit * 0.25 + flap));
            g.LineTo(new Point(x, top - pole + unit * 0.5));
            g.EndFigure(true);
        }

        context.DrawGeometry(FlagCloth, null, geometry);
    }

    // ── Rubble ────────────────────────────────────────────────────────────────

    private void DrawDebris(DrawingContext context, Debris piece)
    {
        var turn = Mirrored ? piece.Angle : -piece.Angle;
        var centre = new Point(X(piece.X), Y(piece.Alt));

        using (context.PushTransform(Matrix.CreateRotation(turn) * Matrix.CreateTranslation(centre.X, centre.Y)))
        {
            context.DrawRectangle(Brush(piece.Color), Mortar,
                new Rect(-piece.Width / 2, -piece.Height / 2, piece.Width, piece.Height));
        }
    }

    // ── People ────────────────────────────────────────────────────────────────

    /// <summary>
    /// One builder, feet at (<paramref name="x"/>, <paramref name="feet"/>), in whatever pose
    /// their job calls for.
    /// </summary>
    /// <param name="facing">+1 facing right on screen, -1 facing left.</param>
    private void DrawWorker(DrawingContext context, Worker worker, double x, double feet, double facing)
    {
        var u = Simulation.Unit;
        var limb = new Pen(Brush(worker.Shirt), Math.Max(1.1, u * 0.11), lineCap: PenLineCap.Round);
        var legs = new Pen(Trousers, Math.Max(1.2, u * 0.12), lineCap: PenLineCap.Round);

        var hip = new Point(x, feet - u * 0.38);
        var shoulder = new Point(x, feet - u * 0.72);
        var head = new Point(x + facing * u * 0.03, feet - u * 0.86);

        if (worker.Alt <= 0.5)
            context.DrawEllipse(Shadow, null, new Point(x, feet + 0.5), u * 0.28, u * 0.07);

        // Legs: a stride while going somewhere, together while standing.
        var swing = worker.IsMoving ? Math.Sin(worker.Stride) * u * 0.2 : u * 0.05;
        var run = worker.Activity == Activity.Panicking ? 1.5 : 1;
        context.DrawLine(legs, hip, new Point(x + swing * run, feet));
        context.DrawLine(legs, hip, new Point(x - swing * run, feet));

        // Body.
        context.DrawLine(new Pen(Brush(worker.Shirt), Math.Max(2, u * 0.26), lineCap: PenLineCap.Round), hip, shoulder);

        // Arms, which carry most of the acting.
        var (left, right) = Arms(worker, shoulder, facing, u);
        context.DrawLine(limb, shoulder, left);
        context.DrawLine(limb, shoulder, right);

        // Head and hard hat.
        var r = u * 0.13;
        context.DrawEllipse(Skin, null, head, r, r);

        var hat = new StreamGeometry();
        using (var g = hat.Open())
        {
            g.BeginFigure(new Point(head.X - r * 1.25, head.Y - r * 0.15), true);
            g.ArcTo(new Point(head.X + r * 1.25, head.Y - r * 0.15), new Size(r * 1.2, r * 1.1), 0, false, SweepDirection.Clockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(Brush(worker.Hat), null, hat);

        // Whatever is in their hands, held up over the hat.
        if (worker.Load != Load.None)
        {
            var bw = Simulation.BrickWidth * 0.8;
            var bh = Simulation.BrickHeight * 0.8;
            var over = new Rect(x - bw / 2, head.Y - r - bh - u * 0.06, bw, bh);
            context.DrawRectangle(Brush(worker.LoadColor ?? "#C9A66B"), Mortar, over);
        }
    }

    private (Point Left, Point Right) Arms(Worker worker, Point shoulder, double facing, double u)
    {
        var reach = u * 0.3;

        switch (worker.Activity)
        {
            case Activity.Panicking:
            case Activity.Falling:
                // Arms in the air, flapping. The universal sign.
                var flap = Math.Sin(Simulation.Time * 18 + worker.Id) * u * 0.1;
                return (new Point(shoulder.X - reach * 0.7, shoulder.Y - reach + flap),
                        new Point(shoulder.X + reach * 0.7, shoulder.Y - reach - flap));

            case Activity.Climbing:
            case Activity.Descending:
                // Hand over hand on the rungs.
                var climb = Math.Sin(worker.Stride) * u * 0.1;
                return (new Point(shoulder.X - u * 0.12, shoulder.Y - reach * 0.8 + climb),
                        new Point(shoulder.X + u * 0.12, shoulder.Y - reach * 0.8 - climb));

            case Activity.Placing:
                return (new Point(shoulder.X + facing * reach, shoulder.Y - u * 0.05),
                        new Point(shoulder.X + facing * reach * 0.9, shoulder.Y + u * 0.02));

            case Activity.Watching:
                // A hand shading the eyes, looking up at it.
                return (new Point(shoulder.X + facing * u * 0.08, shoulder.Y - u * 0.16),
                        new Point(shoulder.X - facing * u * 0.06, shoulder.Y + reach * 0.9));

            default:
                if (worker.Load != Load.None)
                    return (new Point(shoulder.X - u * 0.12, shoulder.Y - reach * 0.75),
                            new Point(shoulder.X + u * 0.12, shoulder.Y - reach * 0.75));

                var sway = worker.IsMoving ? Math.Sin(worker.Stride) * u * 0.12 : 0;
                return (new Point(shoulder.X - u * 0.06 + sway, shoulder.Y + reach),
                        new Point(shoulder.X + u * 0.06 - sway, shoulder.Y + reach));
        }
    }

    // ── Speech ────────────────────────────────────────────────────────────────

    private void DrawShout(DrawingContext context, Shout shout)
    {
        var u = Simulation.Unit;
        var fade = Math.Clamp(shout.Remaining / shout.Life, 0, 1);

        // Rises as it fades, so three at once read as three separate screams rather than as
        // clutter.
        var rise = (1 - fade) * u * 0.8;

        var text = new FormattedText(shout.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ShoutFace, Math.Max(7, u * 0.62), BubbleTextBrush);

        var padX = u * 0.22;
        var padY = u * 0.1;
        var width = text.Width + padX * 2;
        var height = text.Height + padY * 2;

        var feet = Feet(shout.Worker);
        var x = feet.X - width / 2;
        var y = feet.Y - u * 1.25 - height - rise;

        // Nudged inside the site rather than clipped: half a speech bubble off the edge looks
        // like a bug, not like somebody screaming near the edge.
        x = Math.Clamp(x, 1, Math.Max(1, Bounds.Width - width - 1));
        if (y < 1) y = 1;

        using var _ = context.PushOpacity(fade);

        context.DrawRectangle(BubbleBrush, BubblePen, new RoundedRect(new Rect(x, y, width, height), height * 0.4));

        // The tail, pointing back down at whoever is doing the screaming.
        var tipX = Math.Clamp(feet.X, x + height * 0.4, x + width - height * 0.4);
        var tail = new StreamGeometry();
        using (var g = tail.Open())
        {
            g.BeginFigure(new Point(tipX - u * 0.14, y + height - 0.5), true);
            g.LineTo(new Point(tipX, y + height + u * 0.28));
            g.LineTo(new Point(tipX + u * 0.14, y + height - 0.5));
            g.EndFigure(true);
        }
        context.DrawGeometry(BubbleBrush, null, tail);

        context.DrawText(text, new Point(x + padX, y + padY));
    }

    private IBrush Brush(string hex)
    {
        if (_brushes.TryGetValue(hex, out var brush)) return brush;

        // A colour that will not parse is somebody's typo in the file, not a reason to stop
        // drawing. Grey stands out enough to be noticed and fixed.
        brush = Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
        _brushes[hex] = brush;
        return brush;
    }
}
