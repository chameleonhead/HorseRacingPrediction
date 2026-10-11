# Read-only migration preflight mock

This is a hierarchy and interaction proposal for the 2026-10-12 preflight child. It is not a pixel-perfect mock, migration plan, or apply authorization.

## Desktop — 1280px, first page loaded

Settings
└─ Data integrity
   └─ Saved-data migration preflight                              Read-only

   This inspection reads bounded evidence from configured stores.
   Separate stores are read at different times. This is not an apply plan.

   [Start new inspection]

   Coverage
   Event store          Supported schema · observed 14:20:13 UTC · page 1
   Collection store     Supported schema · observed 14:20:15 UTC · page 1
   Prediction schedule  SchemaOnly · rows not inspected
   Local queue          Not configured · schema/rows not inspected
   External delivery    Not inspected by this page

   Scan accounting
   Scanned 128   Unverifiable 4   Conflict 1   Unknown shape 2
   Current page: horse identities / server-protected keyset continuation

   Evidence
   Horse       Initial / current     Source evidence    Related references
   horse-…     names differ          Unverifiable       2 races / 3 entries
   horse-…     same display name     Conflict           1 race / 1 entry
   horse-…     initial event unknown Unknown shape      0 observed

   No identity is automatically adopted. No calculated candidate ID is displayed.
   These pages are not one consistent snapshot and cannot apply changes.

   [Read next page]  [Restart inspection]

## Desktop — loading and unavailable page

Reading one bounded page…
Event store: page 3 · previous result remains visible · no background polling
[Read next page disabled] [Restart inspection disabled]

Could not read this page. Visible counts are from the earlier observation
at 14:20:15 UTC and may be stale.
The configured store is unavailable or the bounded read timed out.
[Retry current page] [Restart inspection]

## Narrow viewport — 390px

Saved-data migration preflight                              Read-only

Separate store reads are not one snapshot.
Not an apply plan.

[Start new inspection]

Coverage
Event store · Supported · observed 14:20:13 UTC
Collection store · Supported · observed 14:20:15 UTC
Prediction schedule · Not yet scanned
External delivery · Not inspected

Scanned 128
Unverifiable 4 · Conflict 1 · Unknown 2

Horse horse-…
Initial / current names: ...
Source: Unverifiable
Related: 2 races / 3 entries

[Read next page]
[Restart inspection]

## Expired/restarted cursor

This continuation expired or the server restarted.
No saved scan or migration plan exists. Previous pages are not current evidence.
[Start a fresh inspection]

The mock intentionally has no Apply, Resume, Reset, job retry, arbitrary query, or raw-payload control. Unknown, truncated, unsupported, external, and unavailable coverage must remain visible; they are not rendered as zero or success.
