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
            <main><article><h1>Title</h1><p>Hello <strong>World</strong></p></article></main>
            <div><span><span>Wrapper text</span></span></div>
            """);

        var snapshot = await _snapshotter.CaptureAsync(_page);
        var nodes = snapshot.Root.Descendants().ToArray();

        Assert.AreEqual(PageContentKind.Document, snapshot.Root.Kind);
        Assert.AreEqual("Title", snapshot.FindHeadings().Single().Text);
        Assert.AreEqual(1, nodes.Count(node => node.Text == "Hello"));
        Assert.AreEqual(1, nodes.Count(node => node.Text == "World"));
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
              <tr><td>A</td><td>B</td></tr>
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
        Assert.AreEqual("https://example.test/catalog/next?page=2", snapshot.Links[0].Url!.AbsoluteUri);
        Assert.AreEqual("Next", snapshot.Links[0].Text);
        Assert.AreEqual("Top", snapshot.Links[1].AccessibleName);
        Assert.AreEqual("#next", snapshot.Links[0].Source!.LocatorHint);
    }

    [TestMethod]
    public async Task CaptureAsync_ExtractsMetadataAndSkipsInvalidJsonLdWithDiagnostic()
    {
        await SetContentAsync("""
            <html lang="ja"><head><title>Example</title>
              <meta name="description" content="A page">
              <meta property="og:title" content="OG title">
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
