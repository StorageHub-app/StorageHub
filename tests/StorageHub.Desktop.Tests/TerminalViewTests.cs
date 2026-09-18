using System.Reflection;

namespace StorageHub.Desktop.Tests;

public sealed class TerminalViewTests
{
    [Fact]
    public void The_grid_is_measured_from_real_cell_metrics()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(80, 24);
            using var view = new TerminalView(emulator, font);

            var cell = view.MeasureGrid();

            // Measuring a single "M" rounds its width up and costs a column or two at every size,
            // so the measurement is taken across a run and divided.
            Assert.True(cell.Width > 0);
            Assert.True(cell.Height > cell.Width);

            using var host = new Form { Size = new Size(900, 600) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            var (columns, rows) = view.GetGridSize();
            Assert.InRange(columns, 20, 400);
            Assert.InRange(rows, 5, 200);
        });
    }

    [Fact]
    public void Output_arriving_while_scrolled_back_leaves_the_viewport_where_it_was()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(40, 10);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(700, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            for (var index = 0; index < 200; index++)
            {
                emulator.Feed($"line {index}\r\n");
            }

            view.ApplyDamage();
            view.ScrollByPages(-3);
            var parked = ViewportTop(view);

            // This is the behaviour that made the old terminal unusable: it called ScrollToCaret
            // unconditionally, so reading back through history was impossible while output flowed.
            for (var index = 200; index < 260; index++)
            {
                emulator.Feed($"line {index}\r\n");
            }

            view.ApplyDamage();

            Assert.Equal(parked, ViewportTop(view));
        });
    }

    [Fact]
    public void Scrolling_back_to_the_bottom_re_arms_following_the_output()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(40, 10);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(700, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            for (var index = 0; index < 100; index++)
            {
                emulator.Feed($"line {index}\r\n");
            }

            view.ApplyDamage();
            view.ScrollByPages(-2);
            view.ScrollToBottom();
            var atBottom = ViewportTop(view);

            emulator.Feed("more\r\n");
            view.ApplyDamage();

            Assert.True(ViewportTop(view) > atBottom);
        });
    }

    [Fact]
    public void A_selection_is_wrap_aware_and_right_trimmed()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(10, 6);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(600, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            // Twenty characters at ten columns: one logical line across two physical rows.
            emulator.Feed("abcdefghijklmnopqrst");
            view.ApplyDamage();

            var top = emulator.Document.ScreenTopLineNumber;
            SetSelection(view, top, 0, top + 1, 10);

            // The break between the rows belongs to the window width, not to what was written, so
            // copying a wrapped command and pasting it back must not insert a newline into it.
            Assert.Equal("abcdefghijklmnopqrst", view.GetSelectedText());
        });
    }

    [Fact]
    public void A_selection_across_a_real_line_break_keeps_the_break()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(20, 6);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(600, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            emulator.Feed("first\r\nsecond\r\n");
            view.ApplyDamage();

            var top = emulator.Document.ScreenTopLineNumber;
            SetSelection(view, top, 0, top + 1, 20);

            Assert.Equal($"first{Environment.NewLine}second", view.GetSelectedText());
        });
    }

    [Fact]
    public void A_selection_survives_output_that_repaints_underneath_it()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(20, 6);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(600, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            emulator.Feed("selectable text\r\n");
            view.ApplyDamage();
            var top = emulator.Document.ScreenTopLineNumber;
            SetSelection(view, top, 0, top, 15);
            var before = view.GetSelectedText();

            // Anchoring in absolute line numbers rather than an offset into a rebuilt string is
            // what makes this hold; the old renderer destroyed the selection on every frame.
            for (var index = 0; index < 30; index++)
            {
                emulator.Feed($"noise {index}\r\n");
            }

            view.ApplyDamage();

            Assert.Equal(before, view.GetSelectedText());
        });
    }

    [Fact]
    public void A_red_cell_paints_a_red_pixel()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(20, 6);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(600, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            // A row of spaces on red: background-colour-erase is what htop's meter bars are made of.
            emulator.Feed("[41m" + new string(' ', 20) + "[0m");
            view.ApplyDamage();

            using var bitmap = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(bitmap, new Rectangle(0, 0, view.Width, view.Height));

            var pixel = bitmap.GetPixel(view.CellSize.Width / 2, view.CellSize.Height / 2);
            Assert.True(pixel.R > pixel.G && pixel.R > pixel.B, $"expected a red pixel, got {pixel}");
        });
    }

    [Fact]
    public void The_viewport_text_is_exposed_to_a_screen_reader()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(30, 6);
            using var view = new TerminalView(emulator, font);
            using var host = new Form { Size = new Size(600, 400) };
            host.Controls.Add(view);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            emulator.Feed("audible output\r\n");
            view.ApplyDamage();

            // A RichTextBox gave screen readers the text for free; a custom control gives them
            // nothing unless it is exposed deliberately.
            Assert.Equal(AccessibleRole.Text, view.AccessibilityObject.Role);
            Assert.Contains("audible output", view.AccessibilityObject.Value!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Tab_and_the_arrows_are_treated_as_terminal_input()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var font = new Font("Consolas", 12F);
            var emulator = new VtTerminalEmulator(20, 6);
            using var view = new TerminalView(emulator, font);

            var isInputKey = typeof(TerminalView).GetMethod(
                "IsInputKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // Without this WinForms swallows them to move focus and they never reach the remote.
            foreach (var key in new[] { Keys.Tab, Keys.Up, Keys.Down, Keys.Left, Keys.Right })
            {
                Assert.True((bool)isInputKey.Invoke(view, [key])!, $"{key} should reach the terminal");
            }
        });
    }

    private static long ViewportTop(TerminalView view) =>
        (long)typeof(TerminalView)
            .GetField("_viewportTopLine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(view)!;

    private static void SetSelection(TerminalView view, long anchorLine, int anchorColumn, long focusLine, int focusColumn)
    {
        void Set(string name, object value) => typeof(TerminalView)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(view, value);

        Set("_selectionAnchorLine", anchorLine);
        Set("_selectionAnchorColumn", anchorColumn);
        Set("_selectionFocusLine", focusLine);
        Set("_selectionFocusColumn", focusColumn);
    }
}
