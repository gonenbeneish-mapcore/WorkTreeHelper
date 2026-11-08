using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class WindowFlightTests
{
    // The window and its taskbar button as measured on a 1920x1200 screen at 125%.
    private static readonly ScreenRect Window = new(1003, 682, 1905, 1145);
    private static readonly ScreenRect Button = new(1194, 1160, 1402, 1200);

    [Fact]
    public void It_starts_on_the_window_and_solid()
    {
        var (rect, scale, alpha) = WindowFlight.Frame(Window, Button, 0, away: true);
        Assert.Equal(Window, rect);
        Assert.Equal(1, scale, 3);
        Assert.Equal(255, alpha);
    }

    [Fact]
    public void Going_away_it_ends_small_on_the_button_and_faint()
    {
        var (rect, _, alpha) = WindowFlight.Frame(Window, Button, 1, away: true);
        Assert.InRange(rect.CenterX, Button.CenterX - 1, Button.CenterX + 1);
        Assert.InRange(rect.CenterY, Button.CenterY - 1, Button.CenterY + 1);
        Assert.True(rect.Width <= Button.Height, $"still {rect.Width} wide");
        Assert.True(alpha < 64, $"alpha {alpha}");
    }

    [Fact]
    public void Coming_back_is_the_same_trip_the_other_way()
    {
        foreach (var t in new[] { 0.0, 0.3, 0.7, 1.0 })
        {
            var away = WindowFlight.Frame(Window, Button, 1 - t, away: true);
            var back = WindowFlight.Frame(Window, Button, t, away: false);
            Assert.InRange(back.Rect.Left, away.Rect.Left - 1, away.Rect.Left + 1);
            Assert.InRange(back.Rect.Width, away.Rect.Width - 1, away.Rect.Width + 1);
            Assert.Equal(away.Scale, back.Scale, 6);
            Assert.InRange(back.Alpha, away.Alpha - 1, away.Alpha + 1);
        }
        Assert.Equal(Window, WindowFlight.Frame(Window, Button, 1, away: false).Rect);
    }

    [Fact]
    public void It_keeps_the_window_s_proportions_as_it_shrinks()
    {
        var end = WindowFlight.EndRect(Window, Button);
        var window = (double)Window.Width / Window.Height;
        var shrunk = (double)end.Width / end.Height;
        Assert.InRange(shrunk, window * 0.9, window * 1.1);
    }

    [Fact]
    public void Going_away_it_only_ever_gets_smaller()
    {
        var last = int.MaxValue;
        for (var t = 0.0; t <= 1.0; t += 0.05)
        {
            var width = WindowFlight.Frame(Window, Button, t, away: true).Rect.Width;
            Assert.True(width <= last, $"grew at t={t}");
            last = width;
        }
    }

    [Fact]
    public void No_frame_moves_much_more_than_an_even_pace_would()
    {
        // What makes it look smooth: at 60 Hz over the trip, the biggest step between frames
        // is well under twice the average step, so it never seems to jump.
        const int frames = 18;
        var steps = new List<double>();
        var previous = WindowFlight.Frame(Window, Button, 0, away: true).Rect;
        for (var i = 1; i <= frames; i++)
        {
            var rect = WindowFlight.Frame(Window, Button, (double)i / frames, away: true).Rect;
            steps.Add(Math.Abs(rect.CenterX - previous.CenterX) + Math.Abs(rect.CenterY - previous.CenterY));
            previous = rect;
        }
        Assert.True(steps.Max() < 1.7 * steps.Average(), $"max {steps.Max():0.0} against average {steps.Average():0.0}");
    }

    [Fact]
    public void It_eases_in_and_out()
    {
        Assert.Equal(0, WindowFlight.Progress(0), 6);
        Assert.Equal(0.5, WindowFlight.Progress(0.5), 6);
        Assert.Equal(1, WindowFlight.Progress(1), 6);
        Assert.True(WindowFlight.Progress(0.1) < 0.1);
        Assert.True(WindowFlight.Progress(0.9) > 0.9);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void Time_outside_the_trip_is_held_at_its_ends(double t)
    {
        var held = WindowFlight.Frame(Window, Button, t, away: true);
        var end = WindowFlight.Frame(Window, Button, t < 0 ? 0 : 1, away: true);
        Assert.Equal(end, held);
    }
}

public class WindowZoomTests
{
    private static readonly ScreenRect Options = new(1063, 71, 1838, 1160);

    [Fact]
    public void Opening_starts_a_little_smaller_and_clear()
    {
        var (rect, scale, alpha) = WindowFlight.ZoomFrame(Options, 0, opening: true);
        Assert.Equal(0, alpha);
        Assert.InRange(scale, 0.9, 0.97);
        Assert.InRange(rect.CenterX, Options.CenterX - 1, Options.CenterX + 1);
        Assert.InRange(rect.CenterY, Options.CenterY - 1, Options.CenterY + 1);
    }

    [Fact]
    public void Opening_ends_as_the_window_itself()
    {
        var (rect, scale, alpha) = WindowFlight.ZoomFrame(Options, 1, opening: true);
        Assert.Equal(Options, rect);
        Assert.Equal(1, scale, 6);
        Assert.Equal(255, alpha);
    }

    [Fact]
    public void It_can_be_read_before_it_has_finished_growing()
    {
        var (_, scale, alpha) = WindowFlight.ZoomFrame(Options, 0.5, opening: true);
        Assert.Equal(255, alpha);
        Assert.True(scale < 1);
    }

    [Fact]
    public void Closing_starts_as_the_window_and_ends_small_and_clear()
    {
        Assert.Equal((Options, 1.0, (byte)255), WindowFlight.ZoomFrame(Options, 0, opening: false));
        var (_, scale, alpha) = WindowFlight.ZoomFrame(Options, 1, opening: false);
        Assert.Equal(0, alpha);
        Assert.True(scale < 0.97);
    }

    [Fact]
    public void It_only_grows_opening_and_only_shrinks_closing()
    {
        double grow = 0, shrink = 2;
        for (var t = 0.0; t <= 1.0; t += 0.05)
        {
            var opening = WindowFlight.ZoomFrame(Options, t, opening: true).Scale;
            var closing = WindowFlight.ZoomFrame(Options, t, opening: false).Scale;
            Assert.True(opening >= grow);
            Assert.True(closing <= shrink);
            (grow, shrink) = (opening, closing);
        }
    }
}

public class WindowPopTests
{
    private static readonly ScreenRect Options = new(1063, 71, 1838, 1160);
    private static readonly ScreenRect Gear = new(1720, 690, 1750, 720);

    [Fact]
    public void Opening_comes_out_of_the_gear_icon_sized_and_clear()
    {
        var (rect, _, alpha) = WindowFlight.PopFrame(Options, Gear, 0, opening: true);
        Assert.InRange(rect.CenterX, Gear.CenterX - 1, Gear.CenterX + 1);
        Assert.InRange(rect.CenterY, Gear.CenterY - 1, Gear.CenterY + 1);
        Assert.True(rect.Width < 100, $"{rect.Width} wide at the start");
        Assert.Equal(0, alpha);
    }

    [Fact]
    public void Opening_ends_exactly_as_the_window()
    {
        var (rect, scale, alpha) = WindowFlight.PopFrame(Options, Gear, 1, opening: true);
        Assert.Equal(Options, rect);
        Assert.Equal(1, scale, 6);
        Assert.Equal(255, alpha);
    }

    [Fact]
    public void It_keeps_the_window_s_shape_all_the_way()
    {
        var shape = (double)Options.Width / Options.Height;
        for (var t = 0.0; t <= 1.0; t += 0.1)
        {
            var rect = WindowFlight.PopFrame(Options, Gear, t, opening: true).Rect;
            Assert.InRange((double)rect.Width / rect.Height, shape * 0.95, shape * 1.05);
        }
    }

    [Fact]
    public void It_springs_a_little_past_its_size_and_back()
    {
        var peak = Enumerable.Range(0, 101).Select(i => WindowFlight.PopFrame(Options, Gear, i / 100.0, opening: true).Scale).Max();
        Assert.InRange(peak, 1.01, 1.05);
    }

    [Fact]
    public void The_ghost_is_big_enough_for_the_spring_and_the_gear()
    {
        var bounds = WindowFlight.PopBounds(Options, Gear);
        for (var t = 0.0; t <= 1.0; t += 0.02)
        {
            var rect = WindowFlight.PopFrame(Options, Gear, t, opening: true).Rect;
            Assert.True(rect.Left >= bounds.Left && rect.Top >= bounds.Top && rect.Right <= bounds.Right && rect.Bottom <= bounds.Bottom,
                $"{rect} outside {bounds} at t={t}");
        }
    }

    [Fact]
    public void Closing_goes_back_into_the_gear_and_out()
    {
        Assert.Equal(Options, WindowFlight.PopFrame(Options, Gear, 0, opening: false).Rect);
        var (rect, _, alpha) = WindowFlight.PopFrame(Options, Gear, 1, opening: false);
        Assert.InRange(rect.CenterX, Gear.CenterX - 1, Gear.CenterX + 1);
        Assert.InRange(rect.CenterY, Gear.CenterY - 1, Gear.CenterY + 1);
        Assert.Equal(0, alpha);
    }

    [Fact]
    public void It_is_solid_early_in_opening()
        => Assert.Equal(255, WindowFlight.PopFrame(Options, Gear, 0.35, opening: true).Alpha);
}

public class WindowPictureTests
{
    private const int Grey = 0x00808080;
    private static readonly WindowFlight.Look Look = new(Radius: 8, ShadowBlur: 10, ShadowDrop: 2, ShadowAlpha: 0.3);

    private static WindowFlight.Image Picture(int width = 100, int height = 60)
        => WindowFlight.Compose(Enumerable.Repeat(Grey, width * height).ToArray(), width, height, Look);

    private static int Alpha(int pixel) => (pixel >> 24) & 0xFF;
    private static int At(WindowFlight.Image image, int x, int y) => image.Pixels[y * image.Width + x];

    [Fact]
    public void The_picture_has_room_round_it_for_the_shadow()
    {
        var picture = Picture();
        Assert.Equal(100 + 2 * Look.Margin, picture.Width);
        Assert.Equal(60 + 2 * Look.Margin, picture.Height);
    }

    [Fact]
    public void The_middle_is_the_window_solid()
    {
        var picture = Picture();
        Assert.Equal(unchecked((int)0xFF808080), At(picture, Look.Margin + 50, Look.Margin + 30));
    }

    [Fact]
    public void The_corner_is_rounded_off_into_shadow()
    {
        var picture = Picture();
        // The very corner of the window, cut away by the radius: what is left is shadow alone,
        // dark and partly see-through.
        var corner = At(picture, Look.Margin, Look.Margin);
        Assert.InRange(Alpha(corner), 1, 200);
        Assert.Equal(0, corner & 0xFF);
    }

    [Fact]
    public void The_edges_are_antialiased_not_cut()
    {
        var picture = Picture();
        // Partway round the curve of a corner, the window part is partly covered: neither the
        // full grey nor none of it.
        var levels = Enumerable.Range(0, 8)
            .Select(i => At(picture, Look.Margin + i, Look.Margin + 1) & 0xFF)
            .Distinct().Count();
        Assert.True(levels > 2, "the corner steps straight from nothing to solid");
    }

    [Fact]
    public void The_shadow_falls_away_to_nothing()
    {
        var picture = Picture();
        Assert.Equal(0, Alpha(At(picture, 0, 0)));
        Assert.True(Alpha(At(picture, picture.Width / 2, picture.Height - 2)) < Alpha(At(picture, picture.Width / 2, picture.Height - Look.Margin + 2)));
    }

    [Fact]
    public void Halving_keeps_a_flat_colour_flat()
    {
        var flat = new WindowFlight.Image(8, 6, Enumerable.Repeat(unchecked((int)0xFF336699), 48).ToArray());
        var half = WindowFlight.Half(flat);
        Assert.Equal(4, half.Width);
        Assert.Equal(3, half.Height);
        Assert.All(half.Pixels, p => Assert.Equal(unchecked((int)0xFF336699), p));
    }

    [Fact]
    public void The_halvings_go_down_to_icon_size()
    {
        var levels = WindowFlight.Halvings(Picture(400, 240));
        Assert.True(levels.Count >= 4);
        Assert.True(levels[^1].Width <= 48);
        for (var i = 1; i < levels.Count; i++) Assert.True(levels[i].Width < levels[i - 1].Width);
    }

    [Fact]
    public void Resampling_to_the_same_size_is_a_copy()
    {
        var picture = Picture();
        var canvas = new int[picture.Width * picture.Height];
        WindowFlight.Resample(picture, canvas, picture.Width, picture.Width, picture.Height);
        Assert.Equal(picture.Pixels, canvas);
    }

    [Fact]
    public void Resampling_smaller_blends_rather_than_picks()
    {
        // Black and white columns, shrunk to two thirds: the columns that fall between are grey.
        var stripes = new WindowFlight.Image(6, 1, new[] { 0, -1, 0, -1, 0, -1 });
        var canvas = new int[6];
        WindowFlight.Resample(stripes, canvas, 6, 4, 1);
        Assert.Contains(canvas.Take(4), p => (p & 0xFF) is > 32 and < 224);
    }

    [Fact]
    public void Resampling_writes_only_its_corner_of_the_canvas()
    {
        var picture = Picture();
        var canvas = Enumerable.Repeat(7, picture.Width * picture.Height).ToArray();
        WindowFlight.Resample(picture, canvas, picture.Width, picture.Width / 2, picture.Height / 2);
        Assert.Equal(7, canvas[picture.Width - 1]);
        Assert.Equal(7, canvas[^1]);
    }

    [Fact]
    public void Distance_is_negative_inside_and_positive_outside()
    {
        Assert.True(WindowFlight.RoundedRectDistance(50, 30, 100, 60, 8) < 0);
        Assert.True(WindowFlight.RoundedRectDistance(-5, 30, 100, 60, 8) > 0);
        // The square corner is outside the rounded one.
        Assert.True(WindowFlight.RoundedRectDistance(0.5, 0.5, 100, 60, 8) > 0);
    }
}

public class TaskbarButtonTests
{
    private static readonly IntPtr Hwnd = new(0xc05c8);

    [Fact]
    public void The_id_is_the_handle_in_hex_as_the_taskbar_writes_it()
        => Assert.Equal("Window: 0xc05c8", TaskbarButton.IdFor(Hwnd));

    [Fact]
    public void A_window_s_own_button_is_matched_by_handle_whatever_it_is_called()
        => Assert.True(TaskbarButton.IsFor("Window: 0xc05c8", "Taskbar.TaskListButtonAutomationPeer",
            "Something else entirely - 1 running window", Hwnd, "Worktree Helper"));

    [Fact]
    public void Another_window_s_button_is_not_matched_even_with_the_same_name()
        => Assert.False(TaskbarButton.IsFor("Window: 0xabc", "Taskbar.TaskListButtonAutomationPeer",
            "Worktree Helper  —  repo - 1 running window", Hwnd, "Worktree Helper"));

    [Fact]
    public void A_group_button_is_matched_by_the_app_s_name()
        => Assert.True(TaskbarButton.IsFor("Appid: WorktreeHelper", "Taskbar.TaskListButtonAutomationPeer",
            "Worktree Helper - 2 running windows", Hwnd, "Worktree Helper"));

    [Fact]
    public void The_tray_icon_is_never_taken_for_the_taskbar_button()
        => Assert.False(TaskbarButton.IsFor("NotifyItemIcon", "SystemTray.AccentButton",
            "Worktree Helper 1.11.0 — repo: 3 worktrees", Hwnd, "Worktree Helper"));
}
