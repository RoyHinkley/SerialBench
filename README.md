# SerialBench

SerialBench is an interactive .NET desktop workbench for configuring, testing, diagnosing, and investigating serial devices and protocols.

It provides a human-facing environment for working with serial links while keeping the reusable communications machinery in the separate [SerialCommunications](https://github.com/RoyHinkley/SerialCommunications) library.

## Capabilities

SerialBench currently provides:

- configurable serial-port settings, handshake, and RTS behavior;
- message pacing and inter-byte pacing;
- silence- and terminator-based message framing;
- configurable CRC-16, XOR, and additive checksum handling;
- optional suppression of messages that fail their configured check;
- escaped-text and hexadecimal byte entry/display;
- communications diagnostics and trace filtering;
- signal-state reporting;
- automatic recovery from unexpected transport loss through SerialCommunications;
- persistent application/configuration settings.

SerialBench deliberately does not assign instrument-specific meaning to commands and responses. Protocol interpretation, polling, retries, expected-response counts, ACK/NAK semantics, and instrument state machines belong in instrument-specific consumers rather than the general communications layer.

## SerialCommunications dependency

SerialBench and SerialCommunications are independent repositories. For source development they are expected to be cloned as siblings:

```text
<source-root>/
    SerialBench/
    SerialCommunications/
```

`SerialBench.csproj` references `../SerialCommunications/SerialCommunications.csproj`, and the solution includes that sibling project. This arrangement preserves source-level debugging and coordinated development without duplicating the library or using a Git submodule.

## Build and test

The projects currently target .NET 10. From the `SerialBench` directory:

```text
dotnet restore SerialBench.sln
dotnet build SerialBench.sln
dotnet test SerialBench.sln
```

The sibling `SerialCommunications` repository must be present at the path shown above.

## Data model

The communications boundary is byte-oriented. SerialCommunications delivers exact wire and payload bytes plus check evidence; SerialBench is responsible for converting those bytes into human-readable escaped text, hexadecimal display, logs, and other presentation forms.

Connection intent, transport availability, and communications health are intentionally separate concepts. A malformed response, failed message check, timeout, or isolated I/O problem is diagnostic evidence and does not by itself mean that the physical transport has disappeared.

## Provenance

SerialBench is the continuation of the project originally maintained as `RoyHinkley/SerialPortTerminal`. It began as a fork of the GPL-licensed SerialPortTerminal project and was substantially refactored and extended into a serial communications investigation and diagnostic application.

In October 2026, its reusable communications subsystem was extracted into the independent [SerialCommunications](https://github.com/RoyHinkley/SerialCommunications) repository. The SerialBench repository retains the earlier development history, including development of that library before extraction.

## License

SerialBench is distributed under the GNU General Public License v3.0. See `LICENSE` for the complete license text.
