using SemanticSnapshot = HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot;
using SemanticTable = HorseRacingPrediction.Scraping.Browser.Snapshots.PageTableSnapshot;
using SemanticCell = HorseRacingPrediction.Scraping.Browser.Snapshots.PageTableCellSnapshot;
using HorseRacingPrediction.Scraping.Browser.Snapshots;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

internal sealed class JraSnapshotView
{
    private JraSnapshotView(SemanticSnapshot source)
    {
        Source = source;
        Url = source.Url.ToString();
        Title = source.Title ?? string.Empty;
        MainText = source.Root.GetEffectiveText() ?? string.Empty;
        Headings = source.FindHeadings().Select(node => node.GetEffectiveText())
            .Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text!).Distinct(StringComparer.Ordinal).ToArray();
        Links = source.Links.Select(link => new JraLinkView(
            link.Url?.ToString() ?? link.RawHref ?? string.Empty,
            new[] { link.Text, link.AccessibleName, link.Title }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty)).ToArray();
        Actions = source.FindByKind(PageContentKind.Button).Concat(source.FindByKind(PageContentKind.Link))
            .Select(node => node.GetEffectiveText()).Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => new JraActionView(text!)).Distinct().ToArray();
        var tables = new List<JraTableView>();
        var diagnostics = new List<JraTableProjectionDiagnostic>();
        for (var tableIndex = 0; tableIndex < source.Tables.Count; tableIndex++)
        {
            var result = JraTableView.Project(source.Tables[tableIndex], tableIndex);
            if (result.Table is not null)
            {
                tables.Add(result.Table);
            }
            else if (result.Diagnostic is not null)
            {
                diagnostics.Add(result.Diagnostic);
            }
        }

        Tables = tables;
        Diagnostics = diagnostics;
    }

    public SemanticSnapshot Source { get; }
    public string Url { get; }
    public string Title { get; }
    public string MainText { get; }
    public IReadOnlyList<string> Headings { get; }
    public IReadOnlyList<JraLinkView> Links { get; }
    public IReadOnlyList<JraActionView> Actions { get; }
    public IReadOnlyList<JraTableView> Tables { get; }
    public IReadOnlyList<JraTableProjectionDiagnostic> Diagnostics { get; }

    public static JraSnapshotView Create(SemanticSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new JraSnapshotView(source);
    }
}

internal sealed record JraLinkView(string Url, string Title);
internal sealed record JraActionView(string Text);

internal sealed record JraTableProjectionDiagnostic(
    int TableIndex,
    string Code,
    string Message,
    PageSourceReference? Source);

internal sealed record JraTableProjectionResult(
    JraTableView? Table,
    JraTableProjectionDiagnostic? Diagnostic);

public sealed class JraTableView
{
    private JraTableView(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<IReadOnlyList<JraCellView?>> cells)
    {
        Headers = headers;
        Rows = rows;
        Cells = cells;
    }

    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    public IReadOnlyList<IReadOnlyList<JraCellView?>> Cells { get; }

    public JraCellView? GetCell(int rowIndex, int columnIndex)
        => rowIndex >= 0 && rowIndex < Cells.Count && columnIndex >= 0 && columnIndex < Cells[rowIndex].Count
            ? Cells[rowIndex][columnIndex]
            : null;

    public static JraTableView? Create(SemanticTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Project(table, -1).Table;
    }

    internal static JraTableProjectionResult Project(SemanticTable table, int tableIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        var projection = BuildGrid(table);
        if (projection.Grid is null)
        {
            return Failure(table, tableIndex, projection.ErrorCode!, projection.ErrorMessage!);
        }

        var grid = projection.Grid;
        if (grid.Count == 0)
        {
            return Failure(table, tableIndex, "empty-table", "The table has no projectable rows.");
        }

        var headerRowCount = 0;
        while (headerRowCount < table.Rows.Count && table.Rows[headerRowCount].Cells.Any(cell => cell.IsHeader))
        {
            headerRowCount++;
        }

        var width = grid.Max(row => row.Count);
        var headers = new string[width];
        for (var column = 0; column < width; column++)
        {
            headers[column] = string.Join(' ', RemoveAdjacentDuplicates(grid.Take(headerRowCount)
                .Select(row => column < row.Count ? row[column]?.Text : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)));
        }

        var body = grid.Skip(headerRowCount).Select(row => Enumerable.Range(0, width)
            .Select(column => column < row.Count ? row[column]?.Text ?? string.Empty : string.Empty).ToArray())
            .Cast<IReadOnlyList<string>>().ToArray();
        var bodyCells = grid.Skip(headerRowCount).Select(row => Enumerable.Range(0, width)
            .Select(column => column < row.Count ? row[column] : null).ToArray())
            .Cast<IReadOnlyList<JraCellView?>>().ToArray();
        return new JraTableProjectionResult(new JraTableView(headers, body, bodyCells), null);
    }

    private static (List<List<JraCellView?>>? Grid, string? ErrorCode, string? ErrorMessage) BuildGrid(
        SemanticTable table)
    {
        const int maxColumns = 256;
        const int maxRows = 10_000;
        var grid = new List<List<JraCellView?>>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            while (grid.Count <= rowIndex) grid.Add([]);
            var column = 0;
            foreach (var cell in table.Rows[rowIndex].Cells)
            {
                while (column < grid[rowIndex].Count && grid[rowIndex][column] is not null) column++;
                if (cell.RowSpan < 1 || cell.ColumnSpan < 1)
                {
                    return (null, "invalid-span", $"Row {rowIndex} contains a non-positive row or column span.");
                }

                if (column + cell.ColumnSpan > maxColumns)
                {
                    return (null, "column-limit-exceeded", $"Row {rowIndex} exceeds the {maxColumns}-column safety limit.");
                }

                if (rowIndex + cell.RowSpan > maxRows)
                {
                    return (null, "row-limit-exceeded", $"Row {rowIndex} exceeds the {maxRows}-row safety limit.");
                }
                var view = JraCellView.Create(cell);
                for (var rowOffset = 0; rowOffset < cell.RowSpan; rowOffset++)
                {
                    var targetRow = rowIndex + rowOffset;
                    while (grid.Count <= targetRow) grid.Add([]);
                    while (grid[targetRow].Count < column + cell.ColumnSpan) grid[targetRow].Add(null);
                    for (var columnOffset = 0; columnOffset < cell.ColumnSpan; columnOffset++)
                    {
                        if (grid[targetRow][column + columnOffset] is not null)
                        {
                            return (null, "overlapping-span", $"Row {rowIndex}, column {column} overlaps a reserved span.");
                        }
                        grid[targetRow][column + columnOffset] = view;
                    }
                }
                column += cell.ColumnSpan;
            }
        }
        if (grid.Count == 0)
        {
            return (grid, null, null);
        }

        var width = grid.Max(row => row.Count);
        if (grid.Any(row => row.Count != width))
        {
            return (null, "inconsistent-width", "Projected rows have inconsistent widths.");
        }

        if (grid.Any(row => row.Any(cell => cell is null)))
        {
            return (null, "sparse-grid", "The projected table contains an unoccupied logical cell.");
        }

        return (grid, null, null);
    }

    private static IEnumerable<string> RemoveAdjacentDuplicates(IEnumerable<string> values)
    {
        string? previous = null;
        foreach (var value in values)
        {
            if (value is null || string.Equals(previous, value, StringComparison.Ordinal))
            {
                continue;
            }

            previous = value;
            yield return value;
        }
    }

    private static JraTableProjectionResult Failure(
        SemanticTable table,
        int tableIndex,
        string code,
        string message)
        => new(null, new JraTableProjectionDiagnostic(tableIndex, code, message, table.Source));
}

public sealed class JraCellView
{
    private JraCellView(string text, IReadOnlyList<PageElementFragmentSnapshot> fragments)
    {
        Text = text;
        Fragments = fragments;
    }

    public string Text { get; }
    public IReadOnlyList<PageElementFragmentSnapshot> Fragments { get; }
    public PageElementFragmentSnapshot? FindByClass(string classToken)
        => Fragments.FirstOrDefault(fragment => fragment.ClassTokens.Contains(classToken, StringComparer.Ordinal));
    public static JraCellView Create(SemanticCell cell) => new(cell.Text, cell.Fragments);
}
