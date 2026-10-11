using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The question behind <see cref="PermissionsWindow"/>: which permission bits an item should have.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to say the same twelve bits, kept in step: nine checkboxes for read, write and execute
/// by owner, group and others, and the octal value people type from habit. Ticking a box rewrites
/// the octal; typing octal ticks the boxes. The special bits (set-user-ID, set-group-ID, sticky)
/// have no box and are carried in the octal's first digit, so a mode that has them keeps them
/// unless somebody types them away.
/// </para>
/// <para>
/// Octal that is not one to four digits from 0 to 7 dims Apply with the reason underneath, as the
/// name prompt does, rather than letting somebody press Apply onto a refusal.
/// </para>
/// </remarks>
internal sealed class PermissionsModel : INotifyPropertyChanged
{
    private const int OwnerShift = 6;
    private const int GroupShift = 3;
    private const int OthersShift = 0;
    private const int ReadBit = 4;
    private const int WriteBit = 2;
    private const int ExecuteBit = 1;

    private readonly string _itemName;
    private int _mode;
    private string _octal;
    private bool _octalUnderstood = true;

    internal PermissionsModel(string itemName, int mode)
    {
        _itemName = itemName ?? throw new ArgumentNullException(nameof(itemName));
        _mode = mode & Contracts.Ipc.ItemAttributeIpcLimits.MaximumUnixMode;
        _octal = UnixPermissionText.ToOctal(_mode);

        AcceptCommand = new RelayCommand(_ => Finish(_mode), _ => !HasError);
        CancelCommand = new RelayCommand(_ => Finish(null));
    }

    public string Title => Ui.Format(Ui.Inspector.PermissionsTitleFormat, _itemName);

    public string Intro => Ui.Format(Ui.Inspector.PermissionsIntroFormat, _itemName);

    public bool OwnerRead { get => Has(ReadBit, OwnerShift); set => Put(ReadBit, OwnerShift, value); }

    public bool OwnerWrite { get => Has(WriteBit, OwnerShift); set => Put(WriteBit, OwnerShift, value); }

    public bool OwnerExecute { get => Has(ExecuteBit, OwnerShift); set => Put(ExecuteBit, OwnerShift, value); }

    public bool GroupRead { get => Has(ReadBit, GroupShift); set => Put(ReadBit, GroupShift, value); }

    public bool GroupWrite { get => Has(WriteBit, GroupShift); set => Put(WriteBit, GroupShift, value); }

    public bool GroupExecute { get => Has(ExecuteBit, GroupShift); set => Put(ExecuteBit, GroupShift, value); }

    public bool OthersRead { get => Has(ReadBit, OthersShift); set => Put(ReadBit, OthersShift, value); }

    public bool OthersWrite { get => Has(WriteBit, OthersShift); set => Put(WriteBit, OthersShift, value); }

    public bool OthersExecute { get => Has(ExecuteBit, OthersShift); set => Put(ExecuteBit, OthersShift, value); }

    /// <summary>The mode as typed, or as the boxes last wrote it.</summary>
    public string Octal
    {
        get => _octal;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_octal, value, StringComparison.Ordinal)) return;
            _octal = value;
            _octalUnderstood = UnixPermissionText.TryParseOctal(value, out var parsed);
            if (_octalUnderstood) _mode = parsed;
            Raise(nameof(Octal));
            RaiseMode();
        }
    }

    /// <summary>The mode as <c>ls -l</c> shows it, special bits included.</summary>
    public string Symbolic => UnixPermissionText.Format(_mode);

    /// <summary>The mode Apply would send.</summary>
    internal int Mode => _mode;

    public string Error => _octalUnderstood ? string.Empty : Ui.Inspector.OctalNotUnderstood;

    public bool HasError => !_octalUnderstood;

    public ICommand AcceptCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The mode chosen, or nothing when the dialog was dismissed.</summary>
    internal int? Answer { get; private set; }

    /// <summary>Raised once there is an answer and the window should close.</summary>
    internal event EventHandler? Closed;

    public static string OwnerLabel => Ui.Inspector.Owner;

    public static string GroupLabel => Ui.Inspector.Group;

    public static string OthersLabel => Ui.Inspector.Others;

    public static string ReadLabel => Ui.Inspector.Read;

    public static string WriteLabel => Ui.Inspector.Write;

    public static string ExecuteLabel => Ui.Inspector.Execute;

    public static string OctalLabel => Ui.Inspector.Octal;

    public static string SpecialBitsNote => Ui.Inspector.SpecialBitsAreTheFirstDigit;

    public static string ApplyLabel => Ui.Inspector.Apply;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;

    public static string CheckboxesAccessibleName => Ui.Inspector.PermissionCheckboxes;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Has(int bit, int shift) => (_mode & (bit << shift)) != 0;

    /// <summary>
    /// Ticks or clears one box, and writes the octal from the result.
    /// </summary>
    /// <remarks>
    /// From the mode the boxes show, which is the last octal that was understood: a box ticked
    /// while the octal box holds something unreadable replaces what was typed with what is ticked,
    /// which is the one reading of that click that cannot surprise.
    /// </remarks>
    private void Put(int bit, int shift, bool value)
    {
        var mode = value ? _mode | (bit << shift) : _mode & ~(bit << shift);
        if (mode == _mode && _octalUnderstood) return;
        _mode = mode;
        _octal = UnixPermissionText.ToOctal(mode);
        _octalUnderstood = true;
        Raise(nameof(Octal));
        RaiseMode();
    }

    private void RaiseMode()
    {
        foreach (var name in (string[])
                 [
                     nameof(OwnerRead), nameof(OwnerWrite), nameof(OwnerExecute),
                     nameof(GroupRead), nameof(GroupWrite), nameof(GroupExecute),
                     nameof(OthersRead), nameof(OthersWrite), nameof(OthersExecute),
                     nameof(Symbolic), nameof(Error), nameof(HasError)
                 ])
        {
            Raise(name);
        }

        (AcceptCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Finish(int? answer)
    {
        Answer = answer;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
