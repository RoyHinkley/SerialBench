# SerialBench project notes

> Development handoff for unresolved work and easy-to-lose constraints. Settled user-facing behavior belongs in code, tests, XML documentation, and README.md; completed history belongs in Git.

## Project boundary and working layout

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is the independent reusable communications library used by SerialBench and intended for automated consumers such as AeonHacs.

For source development the repositories are expected to be sibling directories. `SerialBench.csproj` references `../SerialCommunications/SerialCommunications.csproj`, and `SerialBench.sln` includes that sibling project. SerialBench CI must likewise check out SerialCommunications as a sibling while this source-level reference remains in use.

The library supplies faithful communications mechanisms, state, diagnostics, telemetry, and reusable conversion/checking tools. SerialBench owns human presentation, persistence/profiles, logging policy, and interactive behavior. Instrument consumers own command/response meaning.

Keep connection intent, transport availability, and communications health independent.

## Agreed design rules

- Live protocol changes apply immediately when the receive decoder has no incomplete message. If bytes are already buffered, the old decoder completes that message and the staged decoder takes over exactly at its boundary, including when following bytes occur in the same transport chunk.
- `SerialChannelTelemetry.MessagesReceived` counts decoded/framed messages, including invalid messages suppressed from consumer delivery. `CheckFailures` records the independent failed-check fact. Recovery-period attempt state remains distinct from lifetime telemetry.
- A normal logical `Transmit` trace is emitted only after the complete message has been written successfully. Failed writes are errors but are neither counted nor narrated as completed transmissions.
- Exceptions thrown by `SerialChannel` event subscribers are consumer faults, not communications errors; isolate them and report only as detailed diagnostics.
- `TraceFlags.Detail` and `TraceFlags.Noisy` are independent inclusion categories in SerialBench. A trace carrying both is shown when either corresponding option is enabled.
- Consumer dispatch remains asynchronous and deliberately unbounded. The library must not stall serial reception or silently discard decoded messages because application code is slow. Consumers with a genuine bounded-admission requirement impose that policy above the faithful communications layer. Disposal remains non-blocking with respect to arbitrary consumer code.
- Deliberate transport cycling is expressed explicitly with `Disconnect()` / `Connect()`. Communications-health failures do not implicitly mean transport loss.
- Request/response orchestration stays above `SerialCommunications`. Existing consumers vary in polling, command queues, expected-response counts, retry/timeout policy, response validation, ACK/NAK interpretation, addressing/shared-device management, and protocol-specific bypass/check representation. Do not add a generic conversation/protocol DSL without concrete common behavior.
- Configuration ownership is split by role: `SerialChannelConfiguration` / `SerialProtocolSettings` are reusable library snapshots; SerialBench owns live editing, persistence, profiles, and UI defaults.
- `ReceivedData` remains byte-oriented evidence. Text decoding and presentation are consumer responsibilities; consumers do not construct receive records themselves.
- Receive decoder/framer/pump/dispatch types are implementation details rather than public library API. Message-check calculations remain reusable public mechanisms.
- Generic XOR and additive checksum algorithms are fixed at the demonstrated zero-seeded calculations. Do not expose speculative initial-accumulator configuration without a concrete protocol requirement.
- Maintain XML documentation where it adds API meaning. Do not enable CS1591 merely to require comments on every public identifier.
- Normalized data recording, if added, remains an application responsibility; authoritative library data stays faithful to received bytes.

## Deferred SerialBench work

- Revisit command history/Escape behavior and received-data presentation (CR/LF, tab, BEL, message separation) without altering authoritative data.
- Reduce UI density and add useful concise tooltips/help.
- Reconsider diagnostic presentation terminology so the live diagnostic view and authoritative disk record are not confused.
- Replace `TextBox.Text +=` live views with append-friendly bounded presentation; current behavior is O(N²).
- Revisit configuration UI; a flyout/drawer is favored over a permanent dense pane.
- Add named profiles/connection names.
- Validate self-contained Windows deployment on a clean machine and writable settings/log locations.
- Change the main window title from the historical `Serial Port Terminal` to `SerialBench` when product-facing naming cleanup is addressed.

## Related SerialCommunications work

Track library-specific work primarily in the SerialCommunications repository. Current items that can affect SerialBench are:

- Continue concise XML documentation where public API semantics benefit from it: non-obvious behavior, units, defaults, special values, lifetime/threading behavior, and exact check algorithms.
- Verify RTS Toggle behavior with current .NET/Windows and representative adapters only when a target setup requires it; do not restore reflection into private `SerialPort` internals.
- Add genuinely unframed streaming only for a demonstrated use case.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
