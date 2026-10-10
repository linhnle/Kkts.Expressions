# CodeMirror expression autocomplete sample

A local, minimal CodeMirror 6 editor with a .NET 10 completion/diagnostics bridge.
The host references only the core library. Frontend dependencies and the lockfile
are confined to [client](client/). It neither executes queries nor resolves
variables and is not a production API or authorization mechanism.

## Prerequisites and startup

Use the .NET 10 SDK and Node.js 24 (verified with 24.14.0 and npm 11.9.0).
Run from the repository root in two terminals.

Terminal 1:

```sh
dotnet run --project examples/Kkts.Examples/Kkts.Examples.Autocomplete/Kkts.Examples.Autocomplete.csproj -- --urls http://127.0.0.1:5178
```

Terminal 2:

```sh
cd examples/Kkts.Examples/Kkts.Examples.Autocomplete/client
npm ci
npm run dev
```

Open <http://127.0.0.1:5179>. Vite proxies `/api` to the loopback-only host.
Ctrl-Space opens completions; select an item to apply its exact replacement.
Stop each process with Ctrl-C when finished.

## Server-owned fixtures

[SampleMetadata.cs](SampleMetadata.cs) configures Decimal Price, String Status,
DateTime CreatedAt, and the permitted **legacy** path Customer.Name:

| Source | Expected suggestions |
|---|---|
| `Price ` | `=`, `!=`, `>`, `>=`, `<`, `<=`, `in` |
| `Status = ` | Declared `'Active'`, `'Closed'`, `'Pending'`, and applicable null |
| `CreatedAt > $` | Declared `$startOfMonth` and `$utcnow` only |
| `Customer.` | `Customer.Name`, replacing the complete path token |

Policy limits are 4,096 source units, 16 parentheses, 32 conditions, 16 membership
items, one navigation level, and no collection access. Hints are not constraints.
Typed computed scalar registrations are illustrated separately in the
[guide](../../../docs/autocomplete.md#public-fields-legacy-paths-and-variables);
dotted typed public-field registrations are not implied by this legacy example.

The single POST operation in [completion.http](completion.http) accepts exact
text, offset, and client snapshot identity. Null/missing fields and invalid or
surrogate-splitting offsets return 400 Problem Details. Oversized editing input
returns `LimitExceeded` without deep analysis. DTOs expose only safe public
metadata, kind/status names, and same-snapshot diagnostic ranges.

## Snapshot and range handling

[client/adapter.js](client/adapter.js) debounces by 150 ms, aborts superseded
fetches, and checks document/cursor/request identity at publication and
acceptance. Cursor-only moves also invalidate results. Each item's custom apply
callback uses its own `Start..Start+Length`, and labels/descriptions are text.
CodeMirror offsets and JavaScript string indices are UTF-16; one-based line and
zero-based column coordinates map to `doc.line(line).from + column`.

The editor's document snapshot is authoritative: send `doc.toString()` exactly,
without subsequent newline normalization. Diagnostics map on that same source,
including empty EOF spans. Completion status is not semantic validity. Requests
that fail are shown visibly instead of masquerading as successful empty results.

## Validation

From the repository root:

```sh
dotnet build examples/Kkts.Examples/Kkts.Examples.Autocomplete/Kkts.Examples.Autocomplete.csproj --configuration Release
```

From [client](client/) with the host running:

```sh
npm test
npm run test:backend
npm run build
```

The adapter tests cover debounce, cancellation, deliberately out-of-order
responses, cursor-only moves, stale acceptance, HTTP errors, exact edits,
supplementary characters, CRLF serialization, and EOF diagnostic positions.
Backend smoke tests verify all four configured contexts, middle replacements,
input errors, and same-snapshot diagnostics.

Manual browser smoke checks:

1. Replace the entire editor text with `StaTus = 'Active'`, position the caret
   after `Sta`, choose Status, and verify `Status = 'Active'`.
2. In `Status = 'AcZZ' and Price > 1`, position after `Ac` and choose Active.
   The complete quoted token changes; the later clause is untouched.
3. Repeat with `CreatedAt > $uZZ` after `$u`, and `Customer.NaZZ = 'A'` after
   `Customer.Na`. Verify full `$utcnow` and `Customer.Name` spellings once.
4. Type rapidly or move only the cursor. Old suggestions/diagnostics must not
   overwrite the new snapshot. Use adapter tests for controlled response races.

## Reproducible performance command

From the repository root:

```sh
dotnet run --project examples/Kkts.Examples/Kkts.Examples.Autocomplete/Kkts.Examples.Autocomplete.csproj --configuration Release -- --performance
```

[Performance.cs](Performance.cs) uses 100 warmups and 1,000 measured requests:
12 snapshots, at most 2,803 UTF-16 units, and 256 metadata descriptors. It
reports p50/p95 latency and mean allocated bytes per request, requiring
**p95 <=20 ms** and **mean allocation <=256 KiB**. It prints separate adversarial
source/token/scope/large-metadata checks and exits nonzero on failed thresholds
or checks. Debug runs are rejected.

On the 2026-10-10 reference machine (Apple M4 Pro, macOS 27.0.1, Arm64, 12 logical CPUs,
.NET 10.0.12 / SDK 10.0.401), the representative run measured p50 0.026 ms,
p95 1.410 ms, and 159,740 bytes/request. Boundary checks measured approximately
0.003-9.641 ms per request; near-ceiling unary inputs allocated approximately 7.5 MiB.
The 5,000-field workload returned 200 proven-prefix items, measured approximately
5.857 ms and 2.8 MiB per request across two repeats, and retained `IsIncomplete`.
Those adversarial cases are separate from the representative acceptance mean,
not a weakening of its thresholds. Results depend on hardware, JIT, and load;
rerun the command rather than treat these measurements as universal guarantees.
