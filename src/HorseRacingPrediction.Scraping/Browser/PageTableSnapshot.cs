namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// ページ内テーブルの構造スナップショット。
/// </summary>
public sealed record PageTableSnapshot(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<IReadOnlyList<PageTableCellSnapshot>>? Cells = null)
{
    public PageTableCellSnapshot? GetCell(int rowIndex, int columnIndex)
    {
        if (Cells is null || rowIndex < 0 || rowIndex >= Cells.Count)
        {
            return null;
        }

        var row = Cells[rowIndex];
        return columnIndex >= 0 && columnIndex < row.Count ? row[columnIndex] : null;
    }
}
