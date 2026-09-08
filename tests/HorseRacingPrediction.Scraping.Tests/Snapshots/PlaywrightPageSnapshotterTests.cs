using System.Text.Json;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Snapshots;

[TestClass]
public sealed class PlaywrightPageSnapshotterTests
{
    private IPlaywright _playwright = null!;
    private IBrowser? _browser;
    private IPage _page = null!;
    private readonly PlaywrightPageSnapshotter _snapshotter = new();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }
        _playwright?.Dispose();
    }

    [TestMethod]
    public async Task CaptureAsync_BuildsSemanticTreeWithoutWrapperOrParentTextDuplication()
    {
        await SetContentAsync("""
            <main><article><h1>Title</h1><p>Hello <strong>World</strong> again</p></article></main>
            <div><span><span>Wrapper text</span></span></div>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var nodes = snapshot.Root.Descendants().ToArray();

        Assert.AreEqual(PageContentKind.Document, snapshot.Root.Kind);
        Assert.AreEqual("Title", snapshot.FindHeadings().Single().Text);
        Assert.AreEqual(1, nodes.Count(node => node.Text == "Hello"));
        Assert.AreEqual(1, nodes.Count(node => node.Text == "World"));
        CollectionAssert.AreEqual(
            new[] { "Hello", "World", "again" },
            nodes.Where(node => node.Text is "Hello" or "World" or "again").Select(node => node.Text).ToArray());
        Assert.AreEqual(1, nodes.Count(node => node.Text == "Wrapper text"));
        Assert.IsFalse(nodes.Any(node => node.Text?.Contains("Title Hello World", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public async Task CaptureAsync_SafeVisibilityRulesAvoidGeometryAndOpacityFalsePositives()
    {
        await SetContentAsync("""
            <main>
              <div hidden>Hidden attribute</div>
              <div style="display:none"><a href="/hidden">Display hidden</a></div>
              <div style="visibility:hidden">Visibility hidden</div>
              <div style="opacity:0">Opacity retained</div>
              <div style="width:0;height:0;overflow:hidden">Zero retained</div>
              <div style="position:absolute;left:-10000px">Offscreen retained</div>
            </main>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var text = SnapshotText(snapshot);

        Assert.DoesNotContain("Hidden attribute", text);
        Assert.DoesNotContain("Display hidden", text);
        Assert.DoesNotContain("Visibility hidden", text);
        Assert.Contains("Opacity retained", text);
        Assert.Contains("Zero retained", text);
        Assert.Contains("Offscreen retained", text);
        Assert.IsEmpty(snapshot.Links);

        var includingHidden = await _snapshotter.CaptureAsync(_page, new PageSnapshotOptions { IncludeHiddenContent = true });
        Assert.Contains("Hidden attribute", SnapshotText(includingHidden));
    }

    [TestMethod]
    public async Task CaptureAsync_ExtractsTablesDefinitionListsAndResolvedLinks()
    {
        await SetContentAsync("""
            <base href="https://example.test/catalog/">
            <dl><dt>Price</dt><dd>1,200円</dd></dl>
            <table id="prices"><caption>Prices</caption>
              <tr><th rowspan="2">Name</th><th colspan="2">Values</th></tr>
              <tr><td><a class="name race-link" href="race/1"><span class="label">A</span></a></td><td>B</td></tr>
            </table>
            <a id="next" href="next?page=2" rel="next" title="Continue"><span>Next</span></a>
            <a href="#top" aria-label="Top"></a>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var table = snapshot.FindTableByCaption("Prices")!;

        Assert.AreEqual(new PageKeyValueSnapshot("Price", "1,200円", snapshot.KeyValues[0].Source), snapshot.KeyValues.Single());
        Assert.HasCount(2, table.Rows);
        Assert.IsTrue(table.Rows[0].Cells[0].IsHeader);
        Assert.AreEqual(2, table.Rows[0].Cells[0].RowSpan);
        Assert.AreEqual(2, table.Rows[0].Cells[1].ColumnSpan);
        var fragment = table.Rows[1].Cells[0].Fragments.First(item => item.TagName == "a");
        CollectionAssert.AreEqual(new[] { "name", "race-link" }, fragment.ClassTokens.ToArray());
        Assert.AreEqual("A", fragment.Text);
        Assert.AreEqual("https://example.test/catalog/race/1", fragment.Url!.AbsoluteUri);
        Assert.AreEqual("race/1", fragment.RawUrl);
        Assert.IsNotNull(table.Rows[1].Cells[0].Source);
        var nextLink = snapshot.Links.Single(link => link.Source?.ElementId == "next");
        Assert.AreEqual("https://example.test/catalog/next?page=2", nextLink.Url!.AbsoluteUri);
        Assert.AreEqual("Next", nextLink.Text);
        Assert.AreEqual("Top", snapshot.Links.Single(link => link.RawHref == "#top").AccessibleName);
        Assert.AreEqual("#next", nextLink.Source!.LocatorHint);
    }

    [TestMethod]
    public async Task CaptureAsync_ExtractsMetadataAndSkipsInvalidJsonLdWithDiagnostic()
    {
        await SetContentAsync("""
            <html lang="ja"><head><title>Example</title>
              <meta name="description" content="A page">
              <meta property="og:title" content="OG title">
              <meta property="OG:TITLE" content="Conflicting title">
              <meta name="twitter:card" content="summary">
              <link rel="canonical" href="https://example.test/canonical">
              <script type="application/ld+json">{"@type":"Thing","name":"Valid"}</script>
              <script type="application/ld+json">{broken</script>
            </head><body><p>Body</p></body></html>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page);

        Assert.AreEqual("Example", snapshot.Title);
        Assert.AreEqual("ja", snapshot.Metadata.Language);
        Assert.AreEqual("A page", snapshot.Metadata.Description);
        Assert.AreEqual("OG title", snapshot.Metadata.Meta["og:title"]);
        Assert.AreEqual("summary", snapshot.Metadata.Meta["twitter:card"]);
        Assert.AreEqual("https://example.test/canonical", snapshot.Metadata.CanonicalUrl!.AbsoluteUri);
        Assert.AreEqual("Valid", snapshot.Metadata.JsonLd.Single().Value.GetProperty("name").GetString());
        Assert.IsTrue(snapshot.Diagnostics.Any(item => item.Code == "invalid-json-ld"));
        Assert.IsTrue(snapshot.Diagnostics.Any(item => item.Code == "duplicate-meta"));
    }

    [TestMethod]
    public async Task CaptureAsync_ExtractsFormsAndImagesWithoutSensitiveValues()
    {
        await SetContentAsync("""
            <base href="https://example.test/root/">
            <form id="profile" name="profile" action="save" method="post">
              <label for="user">User name</label><input id="user" name="user" value="alice" placeholder="Name" required>
              <label>Password <input name="password" type="password" value="secret"></label>
              <input name="upload" type="file">
              <select name="color"><option>Red</option><option selected>Blue</option></select>
              <button name="submit" value="yes">Save</button>
            </form>
            <img id="logo" src="images/logo.png" alt="Logo" title="Site logo">
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page, new PageSnapshotOptions { IncludeElementLocations = true });
        var form = snapshot.Forms.Single();

        Assert.AreEqual("POST", form.Method);
        Assert.AreEqual("https://example.test/root/save", form.Action!.AbsoluteUri);
        var user = form.Controls.Single(control => control.Name == "user");
        Assert.AreEqual("User name", user.Label);
        Assert.AreEqual("alice", user.Value);
        Assert.AreEqual("Name", user.Placeholder);
        Assert.IsTrue(user.Required);
        Assert.IsNull(form.Controls.Single(control => control.Type == "password").Value);
        Assert.IsNull(form.Controls.Single(control => control.Type == "file").Value);
        CollectionAssert.AreEqual(new[] { "Blue" }, form.Controls.Single(control => control.Name == "color").SelectedOptions.ToArray());
        Assert.AreEqual("https://example.test/root/images/logo.png", snapshot.Images.Single().Source!.AbsoluteUri);
        Assert.AreEqual("Logo", snapshot.Images.Single().AltText);
        Assert.IsNotNull(snapshot.Root.Descendants().First(node => node.Source?.ElementId == "logo").Location);
    }

    [TestMethod]
    public async Task CaptureAsync_UsesImageAltAsSemanticAndImageOnlyLinkText()
    {
        await SetContentAsync("""
            <main>
              <img id="meaningful" src="finish.png" alt="  Finish   photo  " title="Photo title">
              <img id="decorative" src="decoration.png" alt="" title="Decoration title">
              <img id="untitled" src="horse.png" title="Horse title">
              <a id="image-link" href="/result"><img src="result.png" alt="Race result"></a>
              <a id="mixed-link" href="/details">Visible details<img src="detail.png" alt="Detail icon"></a>
            </main>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var meaningful = snapshot.Root.Descendants().Single(node => node.Source?.ElementId == "meaningful");
        var decorative = snapshot.Root.Descendants().Single(node => node.Source?.ElementId == "decorative");
        var untitled = snapshot.Root.Descendants().Single(node => node.Source?.ElementId == "untitled");
        var imageLink = snapshot.Root.Descendants().Single(node => node.Source?.ElementId == "image-link");

        Assert.AreEqual("Finish photo", meaningful.Text);
        Assert.AreEqual("Finish photo", snapshot.Images.Single(image => image.SourceReference?.ElementId == "meaningful").AltText);
        Assert.IsNull(decorative.Text);
        Assert.IsNull(untitled.Text);
        Assert.AreEqual("Decoration title", decorative.AccessibleName);
        Assert.AreEqual("Race result", snapshot.Links.Single(link => link.Source?.ElementId == "image-link").Text);
        Assert.AreEqual("Visible details", snapshot.Links.Single(link => link.Source?.ElementId == "mixed-link").Text);
        Assert.IsNull(imageLink.Text);
        Assert.AreEqual("Race result", imageLink.Children.Single().Text);
        Assert.AreEqual("Race result", imageLink.GetEffectiveText());
    }

    [TestMethod]
    public void QueryHelpers_ProvideEffectiveTextTraversalKindsAndTablePredicates()
    {
        var duplicateText = new PageContentNode { Kind = PageContentKind.Text, Text = "Repeated" };
        var heading = new PageContentNode
        {
            Kind = PageContentKind.Heading,
            AccessibleName = "Fallback",
            Children =
            [
                duplicateText,
                duplicateText with { Text = " Repeated " },
                new PageContentNode { Kind = PageContentKind.Image, Text = "Photo" },
            ],
        };
        var snapshot = new PageSnapshot
        {
            Url = new Uri("https://example.test"),
            Root = new PageContentNode { Kind = PageContentKind.Document, Children = [heading] },
            Metadata = new PageMetadataSnapshot { Meta = new Dictionary<string, string>(), JsonLd = [] },
            KeyValues = [],
            Tables =
            [
                new PageTableSnapshot
                {
                    Caption = null,
                    Rows = [new PageTableRowSnapshot { Cells = [new PageTableCellSnapshot { Text = "Winner" }] }],
                },
            ],
            Links = [],
            Images = [],
            Forms = [],
            Diagnostics = [],
        };

        CollectionAssert.AreEqual(
            new[] { PageContentKind.Document, PageContentKind.Heading, PageContentKind.Text, PageContentKind.Text, PageContentKind.Image },
            snapshot.Root.SelfAndDescendants().Select(node => node.Kind).ToArray());
        Assert.AreSame(heading, snapshot.FindByKind(PageContentKind.Heading).Single());
        Assert.AreEqual("Repeated Photo", heading.GetEffectiveText());
        Assert.AreEqual("Fallback", (heading with { Children = [] }).GetEffectiveText());
        Assert.AreEqual("Winner", snapshot.FindTables(table => table.Rows.SelectMany(row => row.Cells).Any(cell => cell.Text == "Winner"))
            .Single().Rows[0].Cells[0].Text);
    }

    [TestMethod]
    public async Task CaptureAsync_HonorsCollectionWhitespaceAndAggressiveOptions()
    {
        await SetContentAsync("""
            <nav><p>Site navigation</p></nav><main><p>Hello   world</p></main><footer>Footer</footer>
            <a href="/next">Next</a><form><input name="q"></form><img src="x.png" alt="X">
            <script type="application/ld+json">{"ok":true}</script>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page, new PageSnapshotOptions
        {
            Pruning = PageSnapshotPruningLevel.Aggressive,
            NormalizeWhitespace = false,
            IncludeMetadata = false,
            IncludeStructuredData = false,
            IncludeLinks = false,
            IncludeForms = false,
            IncludeImages = false,
        });

        Assert.DoesNotContain("Site navigation", SnapshotText(snapshot));
        Assert.DoesNotContain("Footer", SnapshotText(snapshot));
        Assert.Contains("Hello   world", SnapshotText(snapshot));
        Assert.IsEmpty(snapshot.Links);
        Assert.IsEmpty(snapshot.Forms);
        Assert.IsEmpty(snapshot.Images);
        Assert.IsEmpty(snapshot.Tables);
        Assert.IsEmpty(snapshot.Metadata.Meta);
        Assert.IsEmpty(snapshot.Metadata.JsonLd);
        Assert.IsFalse(snapshot.Root.Descendants().Any(node => node.Text is not null && string.IsNullOrWhiteSpace(node.Text)));
    }

    [TestMethod]
    public async Task CaptureAsync_NonePruningRetainsWrapperGrouping()
    {
        await SetContentAsync("<main><div><span>Grouped</span></div></main>");

        var safe = await _snapshotter.CaptureAsync(_page);
        var unpruned = await _snapshotter.CaptureAsync(
            _page,
            new PageSnapshotOptions { Pruning = PageSnapshotPruningLevel.None });

        Assert.IsGreaterThan(
            safe.Root.Descendants().Count(node => node.Kind == PageContentKind.Section),
            unpruned.Root.Descendants().Count(node => node.Kind == PageContentKind.Section));
        Assert.Contains("Grouped", SnapshotText(unpruned));
    }

    [TestMethod]
    public async Task CaptureAsync_PruningAlsoAppliesToStructuredCollections()
    {
        await SetContentAsync("""
            <main><a href="/kept">Kept</a><dl><dt>Shown</dt><dd>Value</dd></dl></main>
            <nav><a href="/pruned">Pruned</a></nav>
            <footer><table><tr><td>Footer table</td></tr></table></footer>
            <div hidden><dl><dt>Hidden</dt><dd>Value</dd></dl><form><input name="secret"></form></div>
            """);

        var snapshot = await _snapshotter.CaptureAsync(
            _page,
            new PageSnapshotOptions { Pruning = PageSnapshotPruningLevel.Aggressive });

        Assert.AreEqual("Kept", snapshot.Links.Single().Text);
        Assert.AreEqual("Shown", snapshot.KeyValues.Single().Key);
        Assert.IsEmpty(snapshot.Tables);
        Assert.IsEmpty(snapshot.Forms);
    }

    [TestMethod]
    public async Task CaptureAsync_BoundsTableCellFragmentsAndReportsTruncation()
    {
        var fragments = string.Concat(Enumerable.Range(0, 50).Select(index =>
            $"<span class='token-{index}'>Value {index}</span>"));
        await SetContentAsync($"<table><tr><td>{fragments}</td></tr></table>");

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var cell = snapshot.Tables.Single().Rows.Single().Cells.Single();

        Assert.HasCount(48, cell.Fragments);
        Assert.AreEqual("token-0", cell.Fragments[0].ClassTokens.Single());
        Assert.AreEqual("token-47", cell.Fragments[^1].ClassTokens.Single());
        Assert.IsTrue(snapshot.Diagnostics.Any(item => item.Code == "table-cell-fragments-truncated"));
    }

    [TestMethod]
    public async Task CaptureAsync_ObservesDynamicDomAtCaptureTime()
    {
        await SetContentAsync("<main></main>");
        await _page.EvaluateAsync("() => document.querySelector('main').append(Object.assign(document.createElement('p'), { textContent: 'Dynamic' }))");

        var snapshot = await _snapshotter.CaptureAsync(_page);

        Assert.Contains("Dynamic", SnapshotText(snapshot));
    }

    [TestMethod]
    public async Task CaptureAsync_PreCanceledTokenThrows()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _snapshotter.CaptureAsync(_page, cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public async Task CaptureAsync_CancellationStopsAwaitingLargeCapture()
    {
        await SetContentAsync("<main id=content></main>");
        await _page.EvaluateAsync("""
            () => {
                const fragment = document.createDocumentFragment();
                for (let index = 0; index < 20000; index++) {
                    const paragraph = document.createElement('p');
                    paragraph.textContent = `Item ${index}`;
                    fragment.append(paragraph);
                }
                document.querySelector('#content').append(fragment);
            }
            """);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _snapshotter.CaptureAsync(_page, cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public async Task CaptureAsync_LargeNestedDomIsSemanticallyReduced()
    {
        var wrappers = string.Concat(Enumerable.Repeat("<div><span>", 250));
        var closers = string.Concat(Enumerable.Repeat("</span></div>", 250));
        var html = $"<main>{wrappers}Useful content{closers}</main>";
        await SetContentAsync(html);

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var serialized = JsonSerializer.Serialize(snapshot);

        Assert.IsLessThan(10, snapshot.Root.Descendants().Count());
        Assert.IsLessThan(html.Length, serialized.Length, $"Raw={html.Length}, Snapshot={serialized.Length}");
    }

    private async Task SetContentAsync(string html)
        => await _page.SetContentAsync(html);

    private static string SnapshotText(PageSnapshot snapshot)
        => string.Join(" | ", snapshot.Root.Descendants().Select(node => node.Text).Where(text => text is not null));
}
