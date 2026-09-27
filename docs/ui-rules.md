# UI rules

How StorageHub's screens are laid out, and what holds each rule in place. A rule without a test is
a wish; each one below names the test that fails when it is broken. The target is still 1.4
(`docs/ui-reference/`): these rules keep 2.0 there, they do not redesign it.

## Spacing and sizes

- **Only tokens.** Every margin, padding, radius, height and font size comes from
  `src/StorageHub.Desktop/Themes/DesignTokens.axaml`. The spacing steps are `SpaceXs` 2, `SpaceSm` 4,
  `SpaceMd` 8, `SpaceLg` 12, `SpaceXl` 16 and `Space2Xl` 24; the paddings are `PaddingSm`, `PaddingMd`,
  `PaddingLg` and `PagePadding`. A number written into a view is a token that has not been made
  yet. Make it one, beside the others, with a line saying what it is for.
- **Dense chrome, one height.** Toolbar buttons, the pane's fields, drop-downs in a toolbar
  (`ComboBox.dense`) and the pane's text boxes (`TextBox.dense`) share one height, so a row of them
  lines up. Fluent's own sizes are for dialogs; in a toolbar they are too tall.
  _Test:_ the shell photographs (`ShellScalingTests`) are compared by eye with `docs/ui-reference/`.

## Tables

- **Rows are dense.** `TableViewRowPadding` is 6,3. That gives about 22 px rows, close to 1.4's.
- **The heading band and its dividers move together.** The column dividers are resize grips,
  stretched to the band's height by `TableViewColumnResizerMargin`. That margin must match the band's
  padding (`TableViewRowPadding`): the top and bottom values are the padding, negated. Change one
  and change the other, or the dividers stick out above the headings and into the first row.
  _Test:_ `TableHeaderTests.TheColumnDividersStayInsideTheHeadingBand`, over every table in the shell.
- **No column collapses.** No column is ever narrower than `TableColumnRules.MinimumWidth` (60 px).
  A column can only be dragged as wide as leaves every other column that much, and fixed-width
  columns give way, the widest first, before anything goes below it. When a table is narrower than
  its columns at their minimum, they stay at the minimum and the table scrolls sideways; `*` columns
  get their own widths back once there is room. `*` columns share room by weight, so the lightest
  one decides how much room they need, not their count. `TableColumnRules` applies this
  to every TableView in the application by itself; a new table needs nothing to get it.
  _Test:_ `TableColumnRulesTests`, which drags every column of every table on Welcome, Sync tasks and
  a workspace, and shrinks the window.
- **An empty table says so, and the message is not a row anybody can use.** "No workspaces yet" is a
  row of its own, as 1.4's was, so the table keeps its columns and height. Its model says
  `IsPlaceholder` (`IPlaceholderRow`), and `PlaceholderRows` does the rest for every TableView and
  ListBox: no hover, no focus, a click falls through, and Select all or a selection from code is
  undone. A command that acts on the selected row never gets the message. One that can run with
  nothing selected, such as a context menu on the whole table or a double-click handler, still has
  to cancel when `SelectedItem` is null, as 1.4's workspace menu did in its `Opening`.
  _Test:_ `OverviewTests.AnEmptyTablesMessageCannotBeSelected`.
- **Column widths are `*` or a number**, never `Auto`. An auto column follows its content, so an
  empty table has columns of nothing.
- **Alignment is the column's.** A number column is right-aligned with the column's own
  `HorizontalContentAlignment="Right"`. Aligning the text inside a cell template does nothing,
  because the cell hugs its content.
- **Headings are plain**: regular weight, muted, on the surface colour. The band's colour is
  Fluent's `SystemControlBackgroundChromeMediumBrush`, redefined in DesignTokens so it follows the
  scheme.

## Colour

- Colours come from the scheme's tokens (`CanvasBrush`, `SurfaceBrush`, `TextMutedBrush` and so on),
  never a literal. A Fluent resource that draws something visible gets redefined from a token, as
  `SystemAccentColor*` and the table band are, so all 22 schemes cover it.
  _Test:_ `ColorSchemeApplierTests`, `DesignTokenTests`.

## Markup

- **No `--` inside an XML comment.** The build refuses the file, and the error names the parser,
  not the comment. Write "a, b" or "(a) b" instead of "a -- b" in `.axaml` comments.
