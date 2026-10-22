# SerialBench project notes

> Temporary development handoff. Keep unresolved questions, deferred work, and easy-to-lose constraints here. Settled behavior belongs in code, tests, XML documentation, and eventually README.md; completed history belongs in Git.

## Current checkpoint

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is the reusable serial-device communications library intended for both SerialBench and automated consumers such as AeonHacs.

The pre-split architecture/API review is complete. The two projects have independently coherent objectives and remaining-work lists. Empty destination repositories `RoyHinkley/SerialBench` and `RoyHinkley/SerialCommunications` exist; repository extraction is the next structural step after the current review commits have a clean test checkpoint.

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
- `ReceivedData` remains byte-oriented evidence. Text decoding and presentation are consumer responsibilities; consumers do not construct receive records themselves.
- Receive decoder/framer/pump/dispatch types are implementation details rather than public library API. Message-check calculations remain reusable public mechanisms.
- Generic XOR and additive checksum algorithms are fixed at the demonstrated zero-seeded calculations. Do not expose speculative initial-accumulator configuration without a concrete protocol requirement.
- XML documentation is maintained deliberately where it adds API meaning. Do not enable CS1591 merely to require comments on every public identifier; omitted comments may be intentional.

## Split readiness

Generic message-check support is complete for configurable CRC-16, zero-seeded byte XOR, and the low eight bits of the zero-seeded additive byte sum. Protocol-specific field names, byte selection, bypass rules, and textual representations remain above the generic calculation.

Deterministic coverage includes reusable configuration, receive framing transitions, lifecycle/recovery, diagnostics, transmission accounting/failure, message-check variants, generic receive check evidence, and telemetry reset behavior. The current review commits must pass the normal test checkpoint before extraction.

Remaining work below is classified by repository and is not a reason to keep the projects physically coupled.

## SerialBench deferred work

- Write the project-consumer-facing README. It should describe what SerialBench and SerialCommunications are, capabilities, configuration concepts, build/run instructions, and a small library usage example; it is not a development diary.
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

- Continue concise XML documentation where public API semantics benefit from it: non-obvious behavior, units, defaults, special values, lifetime/threading behavior, and exact check algorithms. Avoid tautological boilerplate and do not use CS1591 as a compliance requirement.
- Verify RTS Toggle behavior with current .NET/Windows and representative adapters only when a target setup requires it; do not restore reflection into private `SerialPort` internals.
- Add genuinely unframed streaming only for a demonstrated use case.
- Clean stale `SerialDevice` test filenames/names when convenient after repository extraction.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
