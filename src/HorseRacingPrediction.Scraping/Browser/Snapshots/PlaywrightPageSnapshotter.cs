using System.Text.Json;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Browser.Snapshots;

/// <summary>
/// Captures an information-preserving semantic snapshot of the current rendered DOM.
/// It does not navigate, interact with the page, or wait for page readiness.
/// </summary>
public sealed class PlaywrightPageSnapshotter : IPageSnapshotter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PageSnapshot> CaptureAsync(
        IPage page,
        PageSnapshotOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new PageSnapshotOptions();

        var browserOptions = new Dictionary<string, object>
        {
            ["includeHiddenContent"] = options.IncludeHiddenContent,
            ["includeMetadata"] = options.IncludeMetadata,
            ["includeStructuredData"] = options.IncludeStructuredData,
            ["includeLinks"] = options.IncludeLinks,
            ["includeForms"] = options.IncludeForms,
            ["includeImages"] = options.IncludeImages,
            ["includeElementLocations"] = options.IncludeElementLocations,
            ["normalizeWhitespace"] = options.NormalizeWhitespace,
            ["pruning"] = options.Pruning.ToString().ToLowerInvariant(),
        };
        var json = await page.EvaluateAsync<string>(CaptureScript, browserOptions)
            .WaitAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var dto = JsonSerializer.Deserialize<BrowserSnapshot>(json, SerializerOptions)
            ?? throw new InvalidOperationException("The browser returned an empty page snapshot.");
        return Convert(dto);
    }

    private static PageSnapshot Convert(BrowserSnapshot dto)
    {
        var diagnostics = dto.Diagnostics
            .Select(ConvertDiagnostic)
            .ToList();

        var pageUrl = ParseRequiredUri(dto.Url, "page-url", diagnostics);
        var metadata = ConvertMetadata(dto.Metadata, diagnostics);

        return new PageSnapshot
        {
            Url = pageUrl,
            Title = EmptyToNull(dto.Title),
            Root = ConvertNode(dto.Root),
            Metadata = metadata,
            KeyValues = dto.KeyValues.Select(value => new PageKeyValueSnapshot(
                value.Key,
                value.Value,
                ConvertSource(value.Source))).ToArray(),
            Tables = dto.Tables.Select(table => ConvertTable(table, pageUrl, diagnostics)).ToArray(),
            Links = dto.Links.Select(link => ConvertLink(link, pageUrl, diagnostics)).ToArray(),
            Images = dto.Images.Select(image => ConvertImage(image, pageUrl, diagnostics)).ToArray(),
            Forms = dto.Forms.Select(form => ConvertForm(form, pageUrl, diagnostics)).ToArray(),
            Diagnostics = diagnostics.ToArray(),
        };
    }

    private static PageMetadataSnapshot ConvertMetadata(
        BrowserMetadata? dto,
        List<PageSnapshotDiagnostic> diagnostics)
    {
        if (dto is null)
        {
            return new PageMetadataSnapshot
            {
                Meta = new Dictionary<string, string>(),
                JsonLd = [],
            };
        }

        var jsonLd = new List<JsonLdSnapshot>();
        foreach (var value in dto.JsonLd)
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                jsonLd.Add(new JsonLdSnapshot { Value = document.RootElement.Clone() });
            }
            catch (JsonException exception)
            {
                diagnostics.Add(new PageSnapshotDiagnostic(
                    PageSnapshotDiagnosticSeverity.Warning,
                    "invalid-json-ld",
                    $"JSON-LD was ignored because it is invalid: {exception.Message}"));
            }
        }

        return new PageMetadataSnapshot
        {
            Description = EmptyToNull(dto.Description),
            CanonicalUrl = ParseOptionalUri(dto.CanonicalUrl, null, "invalid-canonical-url", diagnostics),
            Language = EmptyToNull(dto.Language),
            Meta = new Dictionary<string, string>(dto.Meta, StringComparer.OrdinalIgnoreCase),
            JsonLd = jsonLd.ToArray(),
        };
    }

    private static PageContentNode ConvertNode(BrowserNode dto)
        => new()
        {
            Kind = Enum.TryParse<PageContentKind>(dto.Kind, true, out var kind) ? kind : PageContentKind.Text,
            Text = EmptyToNull(dto.Text),
            HeadingLevel = dto.HeadingLevel,
            Role = EmptyToNull(dto.Role),
            AccessibleName = EmptyToNull(dto.AccessibleName),
            Location = dto.Location is null
                ? null
                : new PageElementLocation(dto.Location.X, dto.Location.Y, dto.Location.Width, dto.Location.Height),
            Source = ConvertSource(dto.Source),
            Children = dto.Children.Select(ConvertNode).ToArray(),
        };

    private static PageTableSnapshot ConvertTable(
        BrowserTable dto,
        Uri pageUrl,
        List<PageSnapshotDiagnostic> diagnostics)
        => new()
        {
            Caption = EmptyToNull(dto.Caption),
            Source = ConvertSource(dto.Source),
            Rows = dto.Rows.Select(row => new PageTableRowSnapshot
            {
                Cells = row.Cells.Select(cell => new PageTableCellSnapshot
                {
                    Text = cell.Text,
                    IsHeader = cell.IsHeader,
                    RowSpan = Math.Max(1, cell.RowSpan),
                    ColumnSpan = Math.Max(1, cell.ColumnSpan),
                    Source = ConvertSource(cell.Source),
                    Fragments = cell.Fragments.Select(fragment => new PageElementFragmentSnapshot
                    {
                        TagName = fragment.TagName,
                        Text = EmptyToNull(fragment.Text),
                        ClassTokens = fragment.ClassTokens,
                        Url = ParseOptionalUri(
                            fragment.ResolvedUrl ?? fragment.RawUrl,
                            pageUrl,
                            "invalid-table-fragment-url",
                            diagnostics,
                            fragment.Source),
                        RawUrl = EmptyToNull(fragment.RawUrl),
                        Role = EmptyToNull(fragment.Role),
                        AccessibleName = EmptyToNull(fragment.AccessibleName),
                        Source = ConvertSource(fragment.Source),
                    }).ToArray(),
                }).ToArray(),
            }).ToArray(),
        };

    private static PageLinkSnapshot ConvertLink(
        BrowserLink dto,
        Uri pageUrl,
        List<PageSnapshotDiagnostic> diagnostics)
        => new()
        {
            Text = dto.Text,
            RawHref = dto.RawHref,
            Url = ParseOptionalUri(dto.ResolvedHref ?? dto.RawHref, pageUrl, "invalid-link-url", diagnostics, dto.Source),
            Relation = EmptyToNull(dto.Relation),
            Title = EmptyToNull(dto.Title),
            AccessibleName = EmptyToNull(dto.AccessibleName),
            Source = ConvertSource(dto.Source),
        };

    private static PageImageSnapshot ConvertImage(
        BrowserImage dto,
        Uri pageUrl,
        List<PageSnapshotDiagnostic> diagnostics)
        => new()
        {
            RawSource = dto.RawSource,
            Source = ParseOptionalUri(dto.ResolvedSource ?? dto.RawSource, pageUrl, "invalid-image-url", diagnostics, dto.Source),
            AltText = EmptyToNull(dto.AltText),
            Title = EmptyToNull(dto.Title),
            AccessibleName = EmptyToNull(dto.AccessibleName),
            SourceReference = ConvertSource(dto.Source),
        };

    private static PageFormSnapshot ConvertForm(
        BrowserForm dto,
        Uri pageUrl,
        List<PageSnapshotDiagnostic> diagnostics)
        => new()
        {
            Name = EmptyToNull(dto.Name),
            RawAction = dto.RawAction,
            Action = ParseOptionalUri(dto.ResolvedAction ?? dto.RawAction, pageUrl, "invalid-form-action", diagnostics, dto.Source),
            Method = EmptyToNull(dto.Method),
            Source = ConvertSource(dto.Source),
            Controls = dto.Controls.Select(control => new PageFormControlSnapshot
            {
                Name = EmptyToNull(control.Name),
                Type = EmptyToNull(control.Type),
                Label = EmptyToNull(control.Label),
                Value = control.Value,
                Placeholder = EmptyToNull(control.Placeholder),
                AccessibleName = EmptyToNull(control.AccessibleName),
                SelectedOptions = control.SelectedOptions.ToArray(),
                Required = control.Required,
                Disabled = control.Disabled,
                Source = ConvertSource(control.Source),
            }).ToArray(),
        };

    private static PageSnapshotDiagnostic ConvertDiagnostic(BrowserDiagnostic dto)
        => new(PageSnapshotDiagnosticSeverity.Warning, dto.Code, dto.Message, ConvertSource(dto.Source));

    private static PageSourceReference? ConvertSource(BrowserSource? dto)
        => dto is null ? null : new PageSourceReference(dto.TagName, EmptyToNull(dto.ElementId), EmptyToNull(dto.LocatorHint));

    private static Uri ParseRequiredUri(
        string value,
        string code,
        List<PageSnapshotDiagnostic> diagnostics)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        diagnostics.Add(new PageSnapshotDiagnostic(
            PageSnapshotDiagnosticSeverity.Warning,
            code,
            $"The page URL '{value}' is not an absolute URI; about:blank was used."));
        return new Uri("about:blank");
    }

    private static Uri? ParseOptionalUri(
        string? value,
        Uri? baseUri,
        string code,
        List<PageSnapshotDiagnostic> diagnostics,
        BrowserSource? source = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            || (baseUri is not null && Uri.TryCreate(baseUri, value, out absolute)))
        {
            return absolute;
        }

        diagnostics.Add(new PageSnapshotDiagnostic(
            PageSnapshotDiagnosticSeverity.Warning,
            code,
            $"The URI '{value}' could not be parsed.",
            ConvertSource(source)));
        return null;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record BrowserSnapshot(
        string Url,
        string? Title,
        BrowserNode Root,
        BrowserMetadata? Metadata,
        BrowserKeyValue[] KeyValues,
        BrowserTable[] Tables,
        BrowserLink[] Links,
        BrowserImage[] Images,
        BrowserForm[] Forms,
        BrowserDiagnostic[] Diagnostics);

    private sealed record BrowserNode(
        string Kind,
        string? Text,
        int? HeadingLevel,
        string? Role,
        string? AccessibleName,
        BrowserLocation? Location,
        BrowserSource? Source,
        BrowserNode[] Children);

    private sealed record BrowserLocation(double X, double Y, double Width, double Height);
    private sealed record BrowserSource(string? TagName, string? ElementId, string? LocatorHint);
    private sealed record BrowserMetadata(
        string? Description,
        string? CanonicalUrl,
        string? Language,
        Dictionary<string, string> Meta,
        string[] JsonLd);
    private sealed record BrowserKeyValue(string Key, string Value, BrowserSource? Source);
    private sealed record BrowserTable(string? Caption, BrowserTableRow[] Rows, BrowserSource? Source);
    private sealed record BrowserTableRow(BrowserTableCell[] Cells);
    private sealed record BrowserTableCell(
        string Text,
        bool IsHeader,
        int RowSpan,
        int ColumnSpan,
        BrowserSource? Source,
        BrowserElementFragment[] Fragments);
    private sealed record BrowserElementFragment(
        string TagName,
        string? Text,
        string[] ClassTokens,
        string? RawUrl,
        string? ResolvedUrl,
        string? Role,
        string? AccessibleName,
        BrowserSource? Source);
    private sealed record BrowserLink(
        string Text,
        string? RawHref,
        string? ResolvedHref,
        string? Relation,
        string? Title,
        string? AccessibleName,
        BrowserSource? Source);
    private sealed record BrowserImage(
        string? RawSource,
        string? ResolvedSource,
        string? AltText,
        string? Title,
        string? AccessibleName,
        BrowserSource? Source);
    private sealed record BrowserForm(
        string? Name,
        string? RawAction,
        string? ResolvedAction,
        string? Method,
        BrowserFormControl[] Controls,
        BrowserSource? Source);
    private sealed record BrowserFormControl(
        string? Name,
        string? Type,
        string? Label,
        string? Value,
        string? Placeholder,
        string? AccessibleName,
        string[] SelectedOptions,
        bool Required,
        bool Disabled,
        BrowserSource? Source);
    private sealed record BrowserDiagnostic(string Code, string Message, BrowserSource? Source);

    private const string CaptureScript = """
        options => {
            const normalize = value => {
                const text = value ?? '';
                return options.normalizeWhitespace ? text.replace(/\s+/gu, ' ').trim() : text;
            };
            const nullable = value => {
                const text = normalize(value);
                return text.trim().length === 0 ? null : text;
            };
            const sourceOf = element => {
                const tagName = element.tagName.toLowerCase();
                const elementId = element.id || null;
                let locatorHint = tagName;
                if (elementId && typeof CSS !== 'undefined' && CSS.escape) {
                    locatorHint = `#${CSS.escape(elementId)}`;
                } else {
                    const name = element.getAttribute('name');
                    const type = element.getAttribute('type');
                    if (name) locatorHint = `${tagName}[name=${JSON.stringify(name)}]`;
                    else if (type) locatorHint = `${tagName}[type=${JSON.stringify(type)}]`;
                }
                return { tagName, elementId, locatorHint };
            };
            const ownText = element => normalize(Array.from(element.childNodes)
                .filter(node => node.nodeType === Node.TEXT_NODE)
                .map(node => node.textContent ?? '')
                .join(' '));
            const renderedText = element => normalize(element.innerText || element.textContent || '');
            const descendantImageAltText = element => normalize(Array.from(element.querySelectorAll('img[alt]'))
                .map(image => image.getAttribute('alt') || '')
                .filter(value => value.trim().length > 0)
                .join(' '));
            const labelledByText = element => normalize((element.getAttribute('aria-labelledby') || '')
                .split(/\s+/u)
                .filter(Boolean)
                .map(id => document.getElementById(id)?.innerText || '')
                .join(' '));
            const associatedLabel = element => {
                if (element.labels?.length) return normalize(Array.from(element.labels).map(label => label.innerText).join(' '));
                const parent = element.closest('label');
                return parent ? renderedText(parent) : '';
            };
            const accessibleName = element => nullable(
                element.getAttribute('aria-label')
                || labelledByText(element)
                || associatedLabel(element)
                || (element instanceof HTMLImageElement ? element.alt : '')
                || element.getAttribute('title')
                || renderedText(element));
            const isHidden = element => {
                if (options.includeHiddenContent) return false;
                for (let current = element; current instanceof Element; current = current.parentElement) {
                    if (current.hidden) return true;
                    const style = getComputedStyle(current);
                    if (style.display === 'none' || style.visibility === 'hidden') return true;
                }
                return false;
            };
            const isAggressiveNoise = element => {
                if (options.pruning !== 'aggressive') return false;
                const tag = element.tagName.toLowerCase();
                const role = (element.getAttribute('role') || '').toLowerCase();
                if (tag === 'nav' || tag === 'footer' || role === 'navigation' || role === 'contentinfo') return true;
                const hint = `${element.id} ${element.className || ''} ${element.getAttribute('aria-label') || ''}`.toLowerCase();
                return /(^|[\s_-])(cookie|advert|ads|social|related)([\s_-]|$)/u.test(hint);
            };
            const isPruned = element => {
                if (isHidden(element)) return true;
                for (let current = element; current instanceof Element; current = current.parentElement) {
                    if (isAggressiveNoise(current)) return true;
                }
                return false;
            };
            const omittedTags = new Set(['script', 'style', 'noscript', 'template', 'canvas']);
            const roleKind = new Map([
                ['navigation', 'Navigation'], ['article', 'Article'], ['complementary', 'Aside'],
                ['heading', 'Heading'], ['paragraph', 'Paragraph'], ['list', 'List'],
                ['listitem', 'ListItem'], ['link', 'Link'], ['img', 'Image'], ['button', 'Button']
            ]);
            const tagKind = new Map([
                ['main', 'Section'], ['section', 'Section'], ['header', 'Section'], ['footer', 'Section'],
                ['nav', 'Navigation'], ['article', 'Article'], ['aside', 'Aside'],
                ['h1', 'Heading'], ['h2', 'Heading'], ['h3', 'Heading'], ['h4', 'Heading'], ['h5', 'Heading'], ['h6', 'Heading'],
                ['p', 'Paragraph'], ['ul', 'List'], ['ol', 'List'], ['li', 'ListItem'], ['dl', 'DefinitionList'],
                ['a', 'Link'], ['img', 'Image'], ['blockquote', 'Quote'], ['pre', 'Code'], ['code', 'Code'], ['button', 'Button']
            ]);
            const locationOf = element => {
                if (!options.includeElementLocations) return null;
                const rect = element.getBoundingClientRect();
                return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
            };
            const walk = element => {
                const tag = element.tagName.toLowerCase();
                if (omittedTags.has(tag) || isPruned(element)) return [];
                if (tag === 'svg') {
                    const name = accessibleName(element);
                    return name ? [{ kind: 'Image', text: null, headingLevel: null, role: nullable(element.getAttribute('role')), accessibleName: name, location: locationOf(element), source: sourceOf(element), children: [] }] : [];
                }

                const text = ownText(element);
                const hasElementChildren = element.childElementCount > 0;
                const children = hasElementChildren ? Array.from(element.childNodes).flatMap(child => {
                    if (child.nodeType === Node.ELEMENT_NODE) return walk(child);
                    if (child.nodeType !== Node.TEXT_NODE) return [];
                    const childText = normalize(child.textContent);
                    return childText.trim() ? [{ kind: 'Text', text: childText, headingLevel: null, role: null, accessibleName: null,
                        location: locationOf(element), source: sourceOf(element), children: [] }] : [];
                }) : [];
                const explicitRole = nullable(element.getAttribute('role'));
                let kind = explicitRole ? roleKind.get(explicitRole.toLowerCase()) : null;
                kind ||= tagKind.get(tag) || null;

                if (!kind && options.pruning === 'none' && (tag === 'div' || tag === 'span')) kind = 'Section';
                if (!kind) {
                    if (hasElementChildren) return children;
                    return text.trim() ? [{ kind: 'Text', text, headingLevel: null, role: explicitRole, accessibleName: null,
                        location: locationOf(element), source: sourceOf(element), children: [] }] : [];
                }

                const headingLevel = kind === 'Heading'
                    ? (/^h[1-6]$/u.test(tag) ? Number(tag.substring(1)) : Number(element.getAttribute('aria-level')) || null)
                    : null;
                const nodeText = tag === 'img'
                    ? nullable(element.getAttribute('alt'))
                    : (hasElementChildren ? null : (text || null));
                const name = ['Link', 'Image', 'Button'].includes(kind) || explicitRole ? accessibleName(element) : null;
                if ((!nodeText || !nodeText.trim()) && children.length === 0 && !name) return [];
                return [{ kind, text: nodeText, headingLevel, role: explicitRole, accessibleName: name, location: locationOf(element), source: sourceOf(element), children }];
            };

            const diagnostics = [];
            const meta = {};
            const metaNames = new Map();
            const jsonLd = [];
            let metadata = null;
            if (options.includeMetadata || options.includeStructuredData) {
                if (options.includeMetadata) {
                    for (const element of document.querySelectorAll('meta[name], meta[property]')) {
                        const key = nullable(element.getAttribute('name') || element.getAttribute('property'));
                        const content = nullable(element.getAttribute('content'));
                        if (!key || !content) continue;
                        const identity = key.toLowerCase();
                        const existingKey = metaNames.get(identity);
                        if (!existingKey) {
                            metaNames.set(identity, key);
                            meta[key] = content;
                        } else if (meta[existingKey] !== content) {
                            diagnostics.push({ code: 'duplicate-meta', message: `Conflicting metadata for '${key}' was ignored.`, source: sourceOf(element) });
                        }
                    }
                }
                if (options.includeStructuredData) {
                    for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
                        const value = script.textContent?.trim();
                        if (value) jsonLd.push(value);
                    }
                }
                metadata = {
                    description: options.includeMetadata ? document.querySelector('meta[name="description"]')?.getAttribute('content') || null : null,
                    canonicalUrl: options.includeMetadata ? document.querySelector('link[rel~="canonical"]')?.href || null : null,
                    language: options.includeMetadata ? document.documentElement.lang || null : null,
                    meta,
                    jsonLd
                };
            }

            const keyValues = options.includeStructuredData ? Array.from(document.querySelectorAll('dl')).filter(dl => !isPruned(dl)).flatMap(dl => {
                const result = [];
                let key = null;
                let values = [];
                const flush = () => {
                    if (key && values.length) result.push({ key, value: normalize(values.join(' ')), source: sourceOf(dl) });
                    values = [];
                };
                for (const child of Array.from(dl.children)) {
                    if (isPruned(child)) continue;
                    if (child.tagName === 'DT') { flush(); key = renderedText(child); }
                    else if (child.tagName === 'DD' && key) values.push(renderedText(child));
                }
                flush();
                return result;
            }) : [];

            const tables = options.includeStructuredData ? Array.from(document.querySelectorAll('table'))
                .filter(table => !isPruned(table))
                .map((table, tableIndex) => ({
                    caption: nullable(table.caption?.innerText),
                    source: sourceOf(table),
                    rows: Array.from(table.rows).filter(row => !isPruned(row)).map((row, rowIndex) => ({
                        cells: Array.from(row.cells).filter(cell => !isPruned(cell)).map((cell, cellIndex) => {
                            const candidates = Array.from(cell.querySelectorAll('*')).filter(element => {
                                if (isPruned(element)) return false;
                                const tag = element.tagName.toLowerCase();
                                return element.classList.length > 0 || element.hasAttribute('href')
                                    || element.hasAttribute('role') || element.hasAttribute('aria-label')
                                    || element.hasAttribute('aria-labelledby')
                                    || ['a', 'button', 'img', 'time', 'data', 'abbr'].includes(tag);
                            });
                            if (candidates.length > 48) {
                                diagnostics.push({ code: 'table-cell-fragments-truncated',
                                    message: `Table ${tableIndex}, row ${rowIndex}, cell ${cellIndex} fragments were limited to 48.`,
                                    source: sourceOf(cell) });
                            }
                            const fragments = candidates.slice(0, 48).map(element => {
                                const rawUrl = element.hasAttribute('href') ? element.getAttribute('href') : null;
                                const resolvedUrl = element instanceof HTMLAnchorElement ? element.href || null : null;
                                return { tagName: element.tagName.toLowerCase(), text: nullable(
                                        element instanceof HTMLImageElement ? element.alt : renderedText(element)),
                                    classTokens: Array.from(element.classList).filter(Boolean).slice(0, 16).map(token => token.substring(0, 128)),
                                    rawUrl, resolvedUrl, role: nullable(element.getAttribute('role')),
                                    accessibleName: accessibleName(element), source: sourceOf(element) };
                            }).filter((fragment, index, all) => index === 0
                                || JSON.stringify(fragment) !== JSON.stringify(all[index - 1]));
                            return { text: renderedText(cell), isHeader: cell.tagName === 'TH', rowSpan: cell.rowSpan || 1,
                                columnSpan: cell.colSpan || 1, source: sourceOf(cell), fragments };
                        })
                    }))
                })) : [];

            const links = options.includeLinks ? Array.from(document.querySelectorAll('a[href]'))
                .filter(link => !isPruned(link))
                .map(link => {
                    const name = accessibleName(link);
                    return { text: nullable(renderedText(link)) || descendantImageAltText(link) || name || '', rawHref: link.getAttribute('href'),
                        resolvedHref: link.href || null, relation: nullable(link.getAttribute('rel')), title: nullable(link.getAttribute('title')),
                        accessibleName: name, source: sourceOf(link) };
                }) : [];

            const images = options.includeImages ? Array.from(document.querySelectorAll('img'))
                .filter(image => !isPruned(image))
                .map(image => ({ rawSource: image.getAttribute('src'), resolvedSource: image.src || null,
                    altText: nullable(image.alt), title: nullable(image.title), accessibleName: accessibleName(image), source: sourceOf(image) })) : [];

            const forms = options.includeForms ? Array.from(document.forms)
                .filter(form => !isPruned(form))
                .map(form => ({ name: nullable(form.getAttribute('name')), rawAction: form.getAttribute('action'), resolvedAction: form.action || null,
                    method: (form.getAttribute('method') || 'get').toUpperCase(), source: sourceOf(form),
                    controls: Array.from(form.querySelectorAll('input, select, textarea, button')).filter(control => !isPruned(control)).map(control => {
                        const type = (control.getAttribute('type') || control.tagName.toLowerCase()).toLowerCase();
                        const sensitive = type === 'password' || type === 'file';
                        const selectedOptions = control instanceof HTMLSelectElement
                            ? Array.from(control.selectedOptions).map(option => normalize(option.textContent)) : [];
                        return { name: nullable(control.getAttribute('name')), type, label: nullable(associatedLabel(control)),
                            value: sensitive ? null : ('value' in control ? control.value : null), placeholder: nullable(control.getAttribute('placeholder')),
                            accessibleName: accessibleName(control), selectedOptions, required: !!control.required || control.getAttribute('aria-required') === 'true',
                            disabled: !!control.disabled || control.getAttribute('aria-disabled') === 'true', source: sourceOf(control) };
                    })
                })) : [];

            const rootChildren = document.body ? walk(document.body) : [];
            return JSON.stringify({
                url: document.URL,
                title: document.title || null,
                root: { kind: 'Document', text: null, headingLevel: null, role: null, accessibleName: null, location: null,
                    source: document.documentElement ? sourceOf(document.documentElement) : null, children: rootChildren },
                metadata, keyValues, tables, links, images, forms, diagnostics
            });
        }
        """;
}
