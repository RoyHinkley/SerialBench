# SerialBench project notes

> Temporary development handoff. Keep unresolved questions, deferred work, and easy-to-lose constraints here. Settled behavior belongs in code, tests, XML documentation, and README.md; completed history belongs in Git.

## Current checkpoint

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is now an independent repository and reusable serial-device communications library intended for SerialBench and automated consumers such as AeonHacs.

The repository split was completed in October 2026. The former `RoyHinkley/SerialPortTerminal` repository was detached from its upstream fork network and renamed `RoyHinkley/SerialBench`, preserving the application's development history. `RoyHinkley/SerialCommunications` was created as an independent repository from the reviewed library source at the split checkpoint.

For source development the repositories are currently expected to be sibling directories. `SerialBench.csproj` references `../SerialCommunications/SerialCommunications.csproj`, and `SerialBench.sln` includes that sibling project.

Core boundary: the library supplies faithful communications mechanisms, state, diagnostics, telemetry, and reusable conversion/checking tools. SerialBench owns human presentation, persistence/profiles, logging policy, and interactive behavior. Instrument consumers own command/response meaning.

Keep connection intent, transport availability, and communications health independent.

## Settled boundary decisions

- Live protocol changes apply immediately when the receive decoder has no incomplete message. If bytes are already buffered, the old decoder completes that message and the staged decoder takes over exactly at its boundary, including when following bytes occur in the same transport chunk.
- `SerialChannelTelemetry.MessagesReceived` counts decoded/framed messages, including invalid messages suppressed from consumer delivery. `CheckFailures` records the independent failed-check fact. Recovery-period attempt state remains distinct from lifetime telemetry.
- A normal logical `Transmit` trace is emitted only after the complete message has been written successfully. Failed writes are errors but are neither counted nor narrated as completed transmissions.
- Exceptions thrown by `SerialChannel` event subscribers are consumer faults, not communications errors; they are isolated and reported only as detailed diagnostics.
- `TraceFlags.Detail` and `TraceFlags.Noisy` are independent inclusion categories in SerialBench. A trace carrying both is shown when either corresponding option is enabled.
- Consumer dispatch remains asynchronous and deliberately unbounded. The library must not stall serial reception or silently discard decoded messages because application code is slow. Consumers with a genuine bounded-admission requirement should impose that policy above the faithful communications layer. Disposal remains non-blocking with respect to arbitrary consumer code.
- The obsolete generic `SerialChannel.Reset()` API is removed. A consumer that deliberately wants to cycle a transport can express that policy explicitly with `Disconnect()` / `Connect()`; communications-health failures do not implicitly mean transport loss.
- Survey of AeonHacs consumers confirms that request/response orchestration belongs above `SerialCommunications`. Existing consumers vary in polling, command queues, expected-response counts, retry/timeout policy, response validation, ACK/NAK interpretation, addressing/shared-device management, and protocol-specific bypass/check representation. Do not add a generic conversation/protocol DSL without concrete common behavior.
- Configuration ownership is split by role: `SerialChannelConfiguration` / `SerialProtocolSettings` are reusable library snapshots; SerialBench owns live editing, persistence, profiles, and UI defaults.
- `ReceivedData` remains byte-oriented evidence. Text decoding and presentation are consumer responsibilities; consumers do not construct receive records themselves.
- Receive decoder/framer/pump/dispatch types are implementation details rather than public library API. Message-check calculations remain reusable public mechanisms.
- Generic XOR and additive checksum algorithms are fixed at the demonstrated zero-seeded calculations. Do not expose speculative initial-accumulator configuration without a concrete protocol requirement.
- XML documentation is maintained deliberately where it adds API meaning. Do not enable CS1591 merely to require comments on every public identifier; omitted comments may be intentional.

## Post-split cleanup

- Move tests that exercise SerialCommunications internals and library behavior from `SerialBench.Tests` into a library-owned `SerialCommunications.Tests` project. Keep application/parser/UI-facing tests in SerialBench. Once complete, change the library's `InternalsVisibleTo` target from `SerialBench.Tests` to `SerialCommunications.Tests`.
- Keep both repositories independently buildable in CI. SerialBench CI must check out SerialCommunications as a sibling while the source-level project reference remains in use.
- After the test move, each repository should own the tests and CI for its own behavior; SerialBench may retain integration tests that intentionally cross the boundary.

## SerialBench deferred work

- Normalized data recording remains application responsibility; authoritative library data stays faithful.
- Revisit command history/Escape behavior and received-data presentation (CR/LF, tab, BEL, message separation) without altering authoritative data.
- Reduce UI density and add useful concise tooltips/help.
- Reconsider diagnostic presentation terminology so live diagnostic view and authoritative disk record are not confused.
- Replace `TextBox.Text +=` live views with append-friendly bounded presentation; current behavior is O(N²).
- Revisit configuration UI (flyout/drawer remains favored over a permanent dense pane).
- Add named profiles/connection names.
- Validate self-contained Windows deployment on a clean machine and writable settings/log locations.
- Replace obsolete Avalonia `TextBox.Watermark` with `PlaceholderText`.

## SerialCommunications deferred work

Track library-specific work primarily in the SerialCommunications repository. Current known items are:

- Continue concise XML documentation where public API semantics benefit from it: non-obvious behavior, units, defaults, special values, lifetime/threading behavior, and exact check algorithms. Avoid tautological boilerplate and do not use CS1591 as a compliance requirement.
- Verify RTS Toggle behavior with current .NET/Windows and representative adapters only when a target setup requires it; do not restore reflection into private `SerialPort` internals.
- Add genuinely unframed streaming only for a demonstrated use case.
- Clean stale `SerialDevice` test filenames/names as part of moving library tests to their own repository.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
