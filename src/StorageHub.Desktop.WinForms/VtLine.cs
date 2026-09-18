using System.Text;

namespace StorageHub.Desktop;

/// <summary>
/// One row of cells. Resizable, because reflow has to split a long line into several and join
/// several back into one, and <see cref="WrappedToNext"/> is what records which of those joins the
/// terminal itself made so reflow can undo them.
/// </summary>
internal sealed class VtLine
{
    private VtCell[] _cells;

    internal VtLine(int width)
    {
        _cells = new VtCell[Math.Max(1, width)];
        Array.Fill(_cells, VtCell.Blank);
        Length = _cells.Length;
    }

    private VtLine(VtCell[] cells)
    {
        _cells = cells;
        Length = cells.Length;
    }

    internal int Length { get; private set; }

    /// <summary>
    /// True when this row ran off the right edge and continues on the next one, rather than the
    /// program having deliberately ended the line. Reflow joins on this and nothing else.
    /// </summary>
    internal bool WrappedToNext { get; set; }

    internal Span<VtCell> Cells => _cells.AsSpan(0, Length);

    internal ReadOnlySpan<VtCell> ReadOnlyCells => _cells.AsSpan(0, Length);

    internal VtCell this[int column]
    {
        get => (uint)column < (uint)Length ? _cells[column] : VtCell.Blank;
        set
        {
            if ((uint)column < (uint)Length)
            {
                _cells[column] = value;
            }
        }
    }

    internal static VtLine FromCells(VtCell[] cells) => new(cells);

    internal void Fill(VtCell cell) => Cells.Fill(cell);

    /// <summary>Resizes in place, padding with <paramref name="padding"/> when it grows.</summary>
    internal void SetWidth(int width, VtCell padding)
    {
        width = Math.Max(1, width);
        if (width == Length)
        {
            return;
        }

        if (width > _cells.Length)
        {
            Array.Resize(ref _cells, width);
        }

        if (width > Length)
        {
            _cells.AsSpan(Length, width - Length).Fill(padding);
        }

        Length = width;
    }

    /// <summary>
    /// The column after the last cell worth keeping. Cells that carry a background colour or an
    /// attribute count as content even when the character is a space.
    /// </summary>
    internal int TrimmedLength()
    {
        var end = Length;
        while (end > 0 && _cells[end - 1].IsTrimmableBlank)
        {
            end--;
        }

        return end;
    }

    internal VtLine Clone()
    {
        var copy = new VtCell[Length];
        _cells.AsSpan(0, Length).CopyTo(copy);
        return new VtLine(copy) { WrappedToNext = WrappedToNext };
    }

    internal void AppendTextTo(StringBuilder builder, int from, int count)
    {
        var end = Math.Min(Length, from + count);
        for (var column = Math.Max(0, from); column < end; column++)
        {
            var cell = _cells[column];
            if (cell.Flags.HasFlag(VtCellFlags.WideTrailing))
            {
                continue;
            }

            builder.Append(char.ConvertFromUtf32(cell.Codepoint));
        }
    }
}
