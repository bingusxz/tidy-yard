# Proposed PC-to-PC sync

Status: design recommendation only. No cloud backend, account login or sync client has been created. Existing calendar subscriptions are read-only and are separate from this feature.

## Recommended setup

1. Create a Supabase project owned by the product owner. Enable email/password authentication and configure email confirmation and password recovery for production.
2. Create user-owned tables for events, recurrence exceptions, checklist/list items and sticky notes. Use stable UUID record IDs across devices. Existing local integer event IDs need a UUID mapping migration; do not treat PC1's local row number as PC2's identity.
3. Enable row level security on every exposed table. Authorize reads, inserts and updates only when `owner_id = auth.uid()`; check ownership on both existing and incoming records. Keep service credentials out of the desktop app. Test isolation using two separate accounts.
4. Add the project URL and publishable client key to app configuration. Add a Sync settings page for sign-in, sign-out, Sync now, last successful sync and pending changes. Store refresh credentials protected for the Windows user; never log them.
5. Implement a local outbox and per-account sync state in SQLite. Commit each local edit and its outgoing change together. Send idempotent operations, retry with backoff, and only clear them after acknowledgement. Notes currently use JSON; migrate their syncable content into transactional local storage before enabling note sync.
6. Implement server-side version checks and a reliable change feed. A stale edit creates a conflict rather than overwriting the latest version. Keep both versions and allow resolution. Use server-issued change positions with a tested commit-order guarantee (or an overlapping, deduplicated feed); a naive timestamp or incrementing-ID cursor can miss concurrent commits.
7. Enable Realtime as a prompt to fetch durable changes, not as the only delivery mechanism. Fetch again on launch, reconnection and periodically. PC2 updates its local database and refreshes the calendar when PC1 changes an event. Missing a WebSocket notification must not lose an edit.
8. Track deletions as versioned tombstones so removed events do not reappear from an offline device. Set a retention and full-resync policy for devices offline longer than the tombstone history. Undo creates a new restoring change. Isolate local data by account and prevent pending changes from one account uploading under another.
9. Install the sync-enabled build on both PCs and sign into the same account. Verify PC1 create/edit/delete/undo appears on PC2, then reverse direction. Test offline edits, simultaneous edits, reconnects, retries, expiry, sign-out and switching accounts before release.

## What to sync

Start with events, recurrence, reminders and checklists; add sticky notes next. Keep window positions, monitor geometry, local folder paths and startup preferences per device. Let users choose where reminders fire to avoid unwanted duplicate notifications. Syncing plot definitions later needs per-device folder mappings.

Actual files require a separate file-sync provider such as OneDrive; syncing a plot definition does not transfer its files. Keep SQLite databases local and sync records through the API rather than putting a live database in a shared cloud folder. Calendar subscription URLs need an explicit cross-device secret-handling design; Windows-account encryption is not portable.

## Sources

- [Supabase Auth](https://supabase.com/docs/guides/auth)
- [Row level security](https://supabase.com/docs/guides/database/postgres/row-level-security)
- [Realtime Postgres changes](https://supabase.com/docs/guides/realtime/postgres-changes)
