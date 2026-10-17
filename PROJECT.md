# SerialBench project notes

> Temporary development handoff. Keep unresolved questions, deferred work, and easy-to-lose constraints here. Settled behavior belongs in code, tests, XML documentation, and eventually README.md; completed history belongs in Git.

## Current checkpoint

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is becoming a reusable serial-device communications library intended for both SerialBench and automated consumers such as AeonHacs.

The lower-level transport/lifecycle/recovery architecture is substantially settled and tested. Current finishing work is removing legacy terminology/API remnants, completing message-check support and reusable configuration, documenting the public library API, and then reviewing the remaining cross-layer questions before considering a repository split.

Core boundary: the library supplies faithful communications mechanisms, state, diagnostics, telemetry, and reusable conversion/checking tools. SerialBench owns human presentation, persistence/profiles, logging policy, and interactive behavior. Instrument consumers own command/response meaning.

Keep connection intent, transport availability, and communications health independent.

## Immediate work

- Finish replacing `Integrity` terminology with `Check`; remove transitional compatibility types/properties rather than carrying them forward.
- Complete generic message-check support. CRC is configurable; XOR defaults to zero-seeded byte XOR (the mechanism behind the surveyed Eurotherm BCC); Checksum defaults to the low eight bits of the additive byte sum. Protocol-specific field names, byte selection, bypass rules, and textual representations remain above the generic calculation.
- Finish the `SerialChannelConfiguration` / `SerialProtocolSettings` migration and remove redundant legacy CRC mirrors (`CrcConfig`, `IgnoreCRCErrors`) once callers are migrated.
- Remove obsolete `SerialDataFormatter` after all callers use byte-oriented `BinaryDataFormatter`.
- Remove obsolete generic `SerialChannel.Reset()` if no real consumer remains.
- Complete concise XML documentation for every public `SerialCommunications` type/member, including every enum value. Document non-obvious semantics, units, defaults, special values, lifetime/threading behavior, and exact check algorithms; avoid tautological boilerplate. Enable CS1591 enforcement after the backlog is clean.
- Add/adjust deterministic tests for configuration application, telemetry semantics, diagnostics, transmission accounting/failure, and message checks.

## Cross-layer questions to resolve before repository split

Review every remaining item and classify it as SerialCommunications, SerialBench, or genuinely cross-layer. Resolve the cross-layer items while both projects are still easy to refactor together.

- **Conversation layer:** survey materially distinct AeonHacs serial consumers before deciding how much reusable request/response behavior belongs above decoded messages: echo, ACK/NAK, unsolicited/multiple responses, addressing/shared buses, prompts, turnaround timing, timeouts/retries, polling, and sequence IDs. Prefer a simple consumer-facing abstraction over a protocol DSL.
- **Live protocol changes:** verify staging semantics when settings change while idle and remove any obsolete reconnect-required application path.
- **Configuration boundary:** SerialCommunications must expose complete reusable channel/protocol configuration without depending on SerialBench; SerialBench owns live editing, persistence, profiles, and UI defaults. Preserve practical profile/file compatibility where worthwhile.
- **Diagnostics boundary:** preserve useful chronological evidence without duplicate narration. Revisit whether subscriber exceptions should be reported as SerialChannel communications errors and verify `Detail | Noisy` filtering semantics in SerialBench.
- **Telemetry semantics:** decide explicitly whether `MessagesReceived` means decoded/framed messages or messages actually delivered after invalid-message suppression. Keep current recovery-period attempt state distinct from lifetime telemetry.
- **Transmit diagnostics:** decide whether the normal logical `Transmit` trace should occur only after successful complete transmission rather than before the first write.
- **Dispatch/backpressure:** decide whether an unbounded consumer dispatch queue is acceptable for realistic blocked-consumer cases.

Do not split repositories until these boundaries leave two independently coherent project objectives. Then extract SerialCommunications and its tests, establish the application dependency, and give each repository its own consumer-facing README and remaining-work list.

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

## Library/transport deferred work

- Verify RTS Toggle behavior with current .NET/Windows and representative adapters only when a target setup requires it; do not restore reflection into private `SerialPort` internals.
- Add genuinely unframed streaming only for a demonstrated use case.
- Revisit `MessageDispatchQueue.Dispose()` signal/disposal race if concurrency review warrants it.
- Clean stale `SerialDevice` filenames/names after functional API cleanup.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
