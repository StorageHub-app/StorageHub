using System.Buffers;
using System.Text.RegularExpressions;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal static partial class PaneItemNameRules
{
    private const int MaximumNameLength = 255;
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create(['<', '>', ':', '"', '/', '\\', '|', '?', '*']);

    internal static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Ui.Validation.EnterAName;
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.EndsWith('.'))
            return Ui.Validation.NamesCannotBeginOrEndWithSpaces;
        if (value.Length > MaximumNameLength)
            return Ui.Format(Ui.Validation.NamesCannotExceedCharactersFormat, MaximumNameLength);
        if (value is "." or ".." || value.Any(char.IsControl) || value.AsSpan().ContainsAny(InvalidCharacters))
            return Ui.Validation.TheNameContainsCharactersThatAreNot;
        var stem = value.Split('.')[0];
        if (ReservedWindowsName().IsMatch(stem)) return Ui.Validation.ThatNameIsReservedByWindows;
        return null;
    }

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedWindowsName();
}

internal sealed class PaneItemNameDialog : Form
{
    private readonly StorageHubTextField _name;
    private readonly Label _error;
    private readonly StorageHubButton _accept;

    internal PaneItemNameDialog(string title, string prompt, string initialName, string acceptText)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = this.LogicalWindowSize(new Size(460, 175));
        BackColor = StorageHubTheme.Surface;
        ForeColor = StorageHubTheme.Text;
        Font = new Font("Segoe UI", 9F);
        var label = new Label { Text = prompt, Left = 20, Top = 18, Width = 415, Height = 22 };
        _name = new StorageHubTextField { Text = initialName, Left = 20, Top = 45, Width = 415, AccessibleName = prompt };
        _error = new Label { Left = 20, Top = 76, Width = 415, Height = 35, ForeColor = StorageHubTheme.Danger };
        _accept = new StorageHubButton { Text = acceptText, DialogResult = DialogResult.OK, Left = 265, Top = 125, Width = 82 };
        var cancel = new StorageHubButton { Text = Ui.Dialogs.ButtonCancel, DialogResult = DialogResult.Cancel, Left = 353, Top = 125, Width = 82 };
        _accept.Variant = StorageHubButtonVariant.Primary;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        _name.TextChanged += (_, _) => ValidateName();
        Controls.AddRange([label, _name, _error, _accept, cancel]);
        AcceptButton = _accept;
        CancelButton = cancel;
        Shown += (_, _) => { _name.SelectAll(); _name.Focus(); };
        ValidateName();
        StorageHubTheme.Register(this);
        StorageHubTheme.Apply(this);
    }

    internal string ItemName => _name.Text;

    private void ValidateName()
    {
        _error.Text = PaneItemNameRules.Validate(_name.Text) ?? string.Empty;
        _accept.Enabled = _error.Text.Length == 0;
    }
}

internal sealed class BatchRenameDialog : Form
{
    private readonly IReadOnlyList<string> _sourceNames;
    private readonly HashSet<string> _occupiedNames;
    private readonly StorageHubTextField _find;
    private readonly StorageHubTextField _replace;
    private readonly ListBox _preview;
    private readonly Label _error;
    private readonly StorageHubButton _accept;
    private IReadOnlyDictionary<string, string> _renameMap = new Dictionary<string, string>();

    internal BatchRenameDialog(IReadOnlyList<string> sourceNames, IEnumerable<string> occupiedNames)
    {
        _sourceNames = sourceNames;
        _occupiedNames = new HashSet<string>(occupiedNames, StringComparer.OrdinalIgnoreCase);
        Text = Ui.Shell.BatchRenameTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = this.LogicalWindowSize(new Size(620, 450));
        BackColor = StorageHubTheme.Surface;
        ForeColor = StorageHubTheme.Text;
        Font = new Font("Segoe UI", 9F);
        Controls.Add(new Label { Text = Ui.Shell.BatchRenameFind, Left = 20, Top = 18, Width = 275 });
        Controls.Add(new Label { Text = Ui.Shell.BatchRenameReplaceWith, Left = 315, Top = 18, Width = 280 });
        _find = new StorageHubTextField { Left = 20, Top = 42, Width = 275, AccessibleName = Ui.Shell.BatchRenameFindAccessibleName };
        _replace = new StorageHubTextField { Left = 315, Top = 42, Width = 280, AccessibleName = Ui.Shell.BatchRenameReplaceAccessibleName };
        _preview = new ListBox { Left = 20, Top = 92, Width = 575, Height = 260, AccessibleName = Ui.Shell.BatchRenamePreviewAccessibleName };
        _error = new Label { Left = 20, Top = 360, Width = 575, Height = 35, ForeColor = StorageHubTheme.Danger };
        _accept = new StorageHubButton { Text = Ui.Shell.RenameWorkspaceAccept, DialogResult = DialogResult.OK, Left = 425, Top = 405, Width = 82 };
        var cancel = new StorageHubButton { Text = Ui.Dialogs.ButtonCancel, DialogResult = DialogResult.Cancel, Left = 513, Top = 405, Width = 82 };
        _accept.Variant = StorageHubButtonVariant.Primary;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        _find.TextChanged += (_, _) => UpdatePreview();
        _replace.TextChanged += (_, _) => UpdatePreview();
        Controls.AddRange([_find, _replace, _preview, _error, _accept, cancel]);
        AcceptButton = _accept;
        CancelButton = cancel;
        UpdatePreview();
        StorageHubTheme.Register(this);
        StorageHubTheme.Apply(this);
    }

    internal IReadOnlyDictionary<string, string> RenameMap => _renameMap;

    private void UpdatePreview()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? error = null;
        _preview.Items.Clear();
        foreach (var source in _sourceNames)
        {
            var target = _find.Text.Length == 0
                ? source
                : source.Replace(_find.Text, _replace.Text, StringComparison.OrdinalIgnoreCase);
            _preview.Items.Add(string.Equals(source, target, StringComparison.Ordinal)
                ? Ui.Format(Ui.Shell.BatchRenameUnchangedFormat, source)
                : Ui.Format(Ui.Shell.BatchRenameMappingFormat, source, target));
            if (string.Equals(source, target, StringComparison.Ordinal)) continue;
            error ??= PaneItemNameRules.Validate(target);
            if (!targets.Add(target)) error ??= Ui.Validation.TwoSelectedItemsWouldReceiveTheSame;
            if (_occupiedNames.Contains(target) && !_sourceNames.Contains(target, StringComparer.OrdinalIgnoreCase))
                error ??= Ui.Format(Ui.Validation.AnItemNamedAlreadyExistsFormat, target);
            if (_sourceNames.Contains(target, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                error ??= Ui.Validation.ATargetNameCollidesWithAnother;
            map[source] = target;
        }
        if (_find.Text.Length == 0) error = Ui.Validation.EnterTextToFindInTheSelected;
        else if (map.Count == 0) error = Ui.Validation.NoneOfTheSelectedNamesContain;
        _renameMap = map;
        _error.Text = error ?? Ui.Format(Ui.Shell.BatchRenameSummaryFormat, map.Count);
        _error.ForeColor = error is null ? StorageHubTheme.TextMuted : StorageHubTheme.Danger;
        _accept.Enabled = error is null;
    }
}
