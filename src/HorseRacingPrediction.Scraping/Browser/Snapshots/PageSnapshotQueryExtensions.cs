namespace HorseRacingPrediction.Scraping.Browser.Snapshots;

public static class PageSnapshotQueryExtensions
{
    /// <summary>Enumerates the current node and then its descendants in depth-first document order.</summary>
    public static IEnumerable<PageContentNode> SelfAndDescendants(this PageContentNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        yield return node;
        foreach (var descendant in node.Descendants())
        {
            yield return descendant;
        }
    }

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

    /// <summary>Finds semantic nodes of the specified kind in depth-first document order.</summary>
    public static IEnumerable<PageContentNode> FindByKind(this PageSnapshot snapshot, PageContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Root.SelfAndDescendants().Where(node => node.Kind == kind);
    }

    /// <summary>
    /// Returns the node's direct text, otherwise ordered descendant text, otherwise its accessible name.
    /// Adjacent equal descendant contributions are emitted once.
    /// </summary>
    public static string? GetEffectiveText(this PageContentNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!string.IsNullOrWhiteSpace(node.Text))
        {
            return node.Text;
        }

        var contributions = new List<string>();
        foreach (var child in node.Children)
        {
            var text = child.GetEffectiveText();
            if (string.IsNullOrWhiteSpace(text)
                || string.Equals(
                    NormalizeForComparison(contributions.LastOrDefault()),
                    NormalizeForComparison(text),
                    StringComparison.Ordinal))
            {
                continue;
            }

            contributions.Add(text);
        }

        if (contributions.Count > 0)
        {
            return string.Join(' ', contributions);
        }

        return string.IsNullOrWhiteSpace(node.AccessibleName) ? null : node.AccessibleName;
    }

    private static string? NormalizeForComparison(string? value)
        => value is null
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static PageTableSnapshot? FindTableByCaption(this PageSnapshot snapshot, string caption)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(caption);
        return snapshot.Tables.FirstOrDefault(table => string.Equals(table.Caption, caption, StringComparison.Ordinal));
    }

    /// <summary>Finds tables matching a caller-supplied generic predicate.</summary>
    public static IEnumerable<PageTableSnapshot> FindTables(
        this PageSnapshot snapshot,
        Func<PageTableSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(predicate);
        return snapshot.Tables.Where(predicate);
    }
}
