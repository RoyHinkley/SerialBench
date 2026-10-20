# SerialBench project notes

> Temporary development handoff. Keep unresolved questions, deferred work, and easy-to-lose constraints here. Settled behavior belongs in code, tests, XML documentation, and eventually README.md; completed history belongs in Git.

## Current checkpoint

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is becoming a reusable serial-device communications library intended for both SerialBench and automated consumers such as AeonHacs.

The lower-level transport/lifecycle/recovery architecture and the principal app/library boundary are substantially settled. Current finishing work is completing generic message-check configuration, public API documentation/tests, and the final open-item classification before repository extraction.

Core boundary: the library supplies faithful communications mechanisms, state, diagnostics, telemetry, and reusable conversion/checking tools. SerialBench owns human presentation, persistence/profiles, logging policy, and interactive behavior. Instrument consumers own command/response meaning.

Keep connection intent, transport availability, and communications health independent.

## Settled pre-split boundary decisions

- Live protocol changes apply immediately when the receive decoder has no incomplete message. If bytes are already buffered, the old decoder completes that message and the staged decoder takes over exactly at its boundary, including when following bytes occur in the same transport chunk.
- `SerialChannelTelemetry.MessagesReceived` counts decoded/framed messages, including invalid messages suppressed from consumer delivery. `CheckFailures` records the independent failed-check fact. Recovery-period attempt state remains distinct from lifetime telemetry.
- A normal logical `Transmit` trace is emitted only after the complete message has been written successfully. Failed writes are errors but are neither counted nor narrated as completed transmissions.
- Exceptions thrown by `SerialChannel` event subscribers are consumer faults, not communications errors; they are isolated and reported only as detailed diagnostics.
- `TraceFlags.Detail` and `TraceFlags.Noisy` are independent inclusion categories in SerialBench. A trace carrying both is shown when either corresponding option is enabled; this lets `LogSignals` show signal diagnostics without requiring all detailed transport logging.
- Consumer dispatch remains asynchronous and deliberately unbounded. The library must not stall serial reception or silently discard decoded messages because application code is slow. Consumers with a genuine bounded-admission requirement should impose that policy above the faithful communications layer. Disposal remains non-blocking with respect to arbitrary consumer code.
- The obsolete generic `SerialChannel.Reset()` API is removed. A consumer that deliberately wants to cycle a transport can express that policy explicitly with `Disconnect()` / `Connect()`; communications-health failures do not implicitly mean transport loss.
- Survey of AeonHacs consumers confirms that request/response orchestration belongs above `SerialCommunications`. Existing consumers vary in polling, command queues, expected-response counts, retry/timeout policy, response validation, ACK/NAK interpretation, addressing/shared-device management, and protocol-specific bypass/check representation. Do not add a generic conversation/protocol DSL to the library now. A future reusable conversation helper should be justified by concrete common behavior after AeonHacs migration, not anticipated in advance.
- Configuration ownership is split by role: `SerialChannelConfiguration` / `SerialProtocolSettings` are reusable library snapshots; SerialBench owns live editing, persistence, profiles, and UI defaults.

## Immediate work

- Complete generic message-check support. CRC is configurable; XOR defaults to zero-seeded byte XOR (the mechanism behind the surveyed Eurotherm BCC); Checksum defaults to the low eight bits of the additive byte sum. Protocol-specific field names, byte selection, bypass rules, and textual representations remain above the generic calculation.
- Complete concise XML documentation for every public `SerialCommunications` type/member, including every enum value. Document non-obvious semantics, units, defaults, special values, lifetime/threading behavior, and exact check algorithms; avoid tautological boilerplate. Enable CS1591 enforcement after the backlog is clean.
- Finish deterministic coverage for reusable configuration, telemetry reset/lifetime behavior, diagnostics, transmission accounting/failure, and all message-check variants.
- Review every remaining item below and classify it as SerialCommunications or SerialBench. Do not split until each repository has an independently coherent objective and remaining-work list.

## SerialBench deferred work

- Write the project-consumer-facing README once the near-final app/library boundary is clear. It should describe what SerialBench and SerialCommunications are, capabilities, configuration concepts, build/run instructions, and a small library usage example; it is not a development diary.
- Normalized data recording remains application responsibility; authoritative library data stays faithful.
- Revisit command history/Escape behavior and received-data presentation (CR/LF, tab, BEL, message separation) without altering authoritative data.
- Reduce UI density and add useful concise tooltips/help.
- Reconsider diagnostic presentation terminology so live diagnostic view and authoritative disk record are not confused.
- Replace `TextBox.Text +=` live views with append-friendly bounded presentation; current behavior is O(N²).
- Revisit configuration UI (flyout/drawer remains favored over a permanent dense pane).
- Add named profiles/connection names after reusable configuration settles.
- Validate self-contained Windows deployment on a clean machine and writable settings/log locations.
- Replace obsolete Avalonia `TextBox.Watermark` with `PlaceholderText`.

## SerialCommunications deferred work

- Verify RTS Toggle behavior with current .NET/Windows and representative adapters only when a target setup requires it; do not restore reflection into private `SerialPort` internals.
- Add genuinely unframed streaming only for a demonstrated use case.
- Clean stale `SerialDevice` filenames/names after functional API cleanup.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
