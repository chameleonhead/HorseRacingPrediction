namespace HorseRacingPrediction.Scraping.Browser.Snapshots;

public static class PageSnapshotQueryExtensions
{
    public static IEnumerable<PageContentNode> Descendants(this PageContentNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    public static IEnumerable<PageContentNode> FindHeadings(this PageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Root.Descendants().Where(node => node.Kind == PageContentKind.Heading);
    }

    public static PageTableSnapshot? FindTableByCaption(this PageSnapshot snapshot, string caption)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(caption);
        return snapshot.Tables.FirstOrDefault(table => string.Equals(table.Caption, caption, StringComparison.Ordinal));
    }
}
