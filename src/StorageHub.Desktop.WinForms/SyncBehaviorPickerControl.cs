using System.ComponentModel;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>A descriptive, keyboard-accessible menu for the complete synchronization presets.</summary>
internal sealed class SyncBehaviorPickerControl : UserControl
{
    private static readonly SyncBehaviorOption[] Options =
    [
        new(SyncIpcBehavior.CopyNewFilesAToB, Ui.Sync.BehaviorCopyNewAtoB, Ui.Sync.BehaviorCopyNewAtoBSummary, Ui.Sync.BadgeCreateOnly, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.UpdateAToB, Ui.Sync.BehaviorUpdateAtoB, Ui.Sync.BehaviorUpdateAtoBSummary, Ui.Sync.BadgeDefault, SyncBehaviorRisk.Default),
        new(SyncIpcBehavior.MirrorAToB, Ui.Sync.BehaviorMirrorAtoB, Ui.Sync.BehaviorMirrorAtoBSummary, Ui.Sync.BadgeDeletions, SyncBehaviorRisk.Destructive),
        new(SyncIpcBehavior.CopyNewFilesBToA, Ui.Sync.BehaviorCopyNewBtoA, Ui.Sync.BehaviorCopyNewBtoASummary, Ui.Sync.BadgeCreateOnly, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.UpdateBToA, Ui.Sync.BehaviorUpdateBtoA, Ui.Sync.BehaviorUpdateBtoASummary, Ui.Sync.BadgeSafeUpdate, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.MirrorBToA, Ui.Sync.BehaviorMirrorBtoA, Ui.Sync.BehaviorMirrorBtoASummary, Ui.Sync.BadgeDeletions, SyncBehaviorRisk.Destructive),
        new(SyncIpcBehavior.TwoWaySync, Ui.Sync.BehaviorTwoWay, Ui.Sync.BehaviorTwoWaySummary, Ui.Sync.BadgeMerge, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.TwoWayWithDeletionPropagation, Ui.Sync.BehaviorTwoWayDeletions, Ui.Sync.BehaviorTwoWayDeletionsSummary, Ui.Sync.BadgeDeletions, SyncBehaviorRisk.Destructive),
        new(SyncIpcBehavior.CompareOnly, Ui.Sync.BehaviorCompareOnly, Ui.Sync.BehaviorCompareOnlySummary, Ui.Sync.BadgeReadOnly, SyncBehaviorRisk.ReadOnly)
    ];

    private readonly Dictionary<SyncIpcBehavior, BehaviorOptionButton> _buttons = [];
    private SyncIpcBehavior _selectedBehavior = SyncIpcBehavior.UpdateAToB;

    public SyncBehaviorPickerControl()
    {
        Name = "SyncBehaviorPicker";
        AccessibleName = Ui.Sync.BehaviorAccessibleName;
        AccessibleDescription = Ui.Sync.BehaviorHint;
        AutoSize = true;
        Dock = DockStyle.Top;
        BackColor = StorageHubTheme.Surface;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 4,
            Margin = Padding.Empty
        };
        for (var column = 0; column < 3; column++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        }

        AddHeading(grid, 0, "A  >  B", Ui.Sync.DirectionOneWayFromA);
        AddHeading(grid, 1, "B  >  A", Ui.Sync.DirectionOneWayFromB);
        AddHeading(grid, 2, "A  <->  B", Ui.Sync.DirectionBoth);
        for (var index = 0; index < Options.Length; index++)
        {
            var column = index / 3;
            var row = index % 3 + 1;
            var button = new BehaviorOptionButton(Options[index])
            {
                Margin = new Padding(column == 0 ? 0 : 5, 4, column == 2 ? 0 : 5, 4)
            };
            button.Click += OptionClicked;
            _buttons.Add(Options[index].Behavior, button);
            grid.Controls.Add(button, column, row);
        }

        Controls.Add(grid);
        UpdateSelection();
    }

    public event EventHandler? SelectedBehaviorChanged;

    public static string GetDisplayName(SyncIpcBehavior behavior) =>
        Options.FirstOrDefault(option => option.Behavior == behavior)?.Title ?? behavior.ToString();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SyncIpcBehavior SelectedBehavior
    {
        get => _selectedBehavior;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, Ui.Sync.BehaviorUnknown);
            }

            if (_selectedBehavior == value)
            {
                return;
            }

            _selectedBehavior = value;
            UpdateSelection();
            SelectedBehaviorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void AddHeading(TableLayoutPanel grid, int column, string direction, string summary)
    {
        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(column == 0 ? 0 : 5, 0, column == 2 ? 0 : 5, 5)
        };
        heading.Controls.Add(new Label
        {
            Text = direction,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = StorageHubTheme.Text,
            Margin = Padding.Empty
        });
        heading.Controls.Add(new Label
        {
            Text = summary,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = new Padding(0, 1, 0, 0)
        });
        grid.Controls.Add(heading, column, 0);
    }

    private void OptionClicked(object? sender, EventArgs e)
    {
        if (sender is BehaviorOptionButton button)
        {
            SelectedBehavior = button.Option.Behavior;
        }
    }

    private void UpdateSelection()
    {
        foreach (var (behavior, button) in _buttons)
        {
            button.Selected = behavior == _selectedBehavior;
        }
    }

    private enum SyncBehaviorRisk
    {
        Safe,
        Default,
        Destructive,
        ReadOnly
    }

    private sealed record SyncBehaviorOption(
        SyncIpcBehavior Behavior,
        string Title,
        string Summary,
        string Badge,
        SyncBehaviorRisk Risk);

    private sealed class BehaviorOptionButton : Button
    {
        private bool _selected;
        private bool _hovered;

        public BehaviorOptionButton(SyncBehaviorOption option)
        {
            Option = option;
            Name = $"Behavior{option.Behavior}";
            AccessibleName = option.Title;
            AccessibleDescription = $"{option.Summary} {option.Badge}.";
            AccessibleRole = AccessibleRole.RadioButton;
            Dock = DockStyle.Fill;
            Height = 82;
            MinimumSize = new Size(210, 82);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = true;
            UseVisualStyleBackColor = false;
            BackColor = StorageHubTheme.Surface;
            Text = option.Title;
            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        }

        public SyncBehaviorOption Option { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                AccessibleDefaultActionDescription = value ? Ui.Sync.BehaviorSelected : Ui.Sync.BehaviorSelect;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            var fill = Selected
                ? StorageHubTheme.Selection
                : _hovered ? StorageHubTheme.SurfaceMuted : StorageHubTheme.Surface;
            var border = Selected ? StorageHubTheme.Primary : StorageHubTheme.Border;
            using var background = new SolidBrush(fill);
            using var outline = new Pen(border, Selected ? 2F : 1F);
            e.Graphics.FillRectangle(background, bounds);
            e.Graphics.DrawRectangle(outline, bounds);

            var badgeColor = Option.Risk switch
            {
                SyncBehaviorRisk.Destructive => StorageHubTheme.Warning,
                SyncBehaviorRisk.ReadOnly => StorageHubTheme.Primary,
                _ => StorageHubTheme.Success
            };
            using var titleFont = new Font("Segoe UI Semibold", 9.25F);
            using var summaryFont = new Font("Segoe UI", 8.25F);
            using var badgeFont = new Font("Segoe UI Semibold", 7F);
            using var titleBrush = new SolidBrush(StorageHubTheme.Text);
            using var summaryBrush = new SolidBrush(StorageHubTheme.TextMuted);
            using var badgeBrush = new SolidBrush(badgeColor);
            using var selectedBrush = new SolidBrush(StorageHubTheme.Primary);
            if (Selected)
            {
                e.Graphics.FillRectangle(selectedBrush, 0, 0, 4, Height);
            }

            var textLeft = 13;
            e.Graphics.DrawString(Option.Title, titleFont, titleBrush, new RectangleF(textLeft, 10, Width - 26, 20));
            e.Graphics.DrawString(Option.Summary, summaryFont, summaryBrush, new RectangleF(textLeft, 31, Width - 26, 34));
            e.Graphics.DrawString(Option.Badge, badgeFont, badgeBrush, new RectangleF(textLeft, Height - 18, Width - 26, 14));
            if (Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5));
            }
        }
    }
}
