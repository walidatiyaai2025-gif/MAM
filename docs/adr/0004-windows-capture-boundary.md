# ADR 0004 — Windows capture provider and central-ingest boundary

- Status: Accepted
- Date: 2026-09-10
- Phase: P00

## Context

Professional tape capture is a Windows-only capability that depends on certified capture cards/decks, vendor drivers/SDKs, live preview, audio meters, timecode and dropped-frame reporting. The MAM product is nevertheless a centralized multi-user system: capture hardware must not turn the workstation into the authoritative catalog or permanent archive, and vendor SDK types must not spread into shared business contracts.

## Decision

`MAM.Desktop` owns hardware-facing orchestration on approved Windows capture stations through a provider abstraction. Vendor-specific adapters are isolated behind that abstraction and may use native interop only inside the Windows capture implementation layer.

The capture boundary exposes normalized MAM concepts such as device identity/capabilities, selected input/profile, tape identity, session state, timecode, audio levels, dropped-frame/error evidence and the path/identity of a temporary local capture artifact. Domain/Application/API contracts must not expose Blackmagic/AJA/other vendor SDK objects.

A capture session writes first to the configured **temporary Windows ingest cache** because sustained hardware recording cannot depend on uninterrupted network throughput. The local artifact is not authoritative media and does not make an asset complete or protected.

After stop/finalize:

1. the capture adapter closes and validates the local artifact;
2. technical capture evidence is recorded;
3. the Desktop transfers the artifact through the same durable Central API/upload-session boundary used for governed ingest;
4. the server verifies size/SHA-256 and commits the original to Primary Storage;
5. Backup is a separate durable server workflow;
6. `Protected` remains impossible until Primary + Backup originals independently verify with matching SHA-256;
7. local cache cleanup follows configured retention/handoff policy and cannot delete the only recoverable copy prematurely.

## Failure and recovery rules

- Device/driver loss, disk-capacity failure, dropped frames, invalid profile, finalize failure and upload interruption are explicit session states; they are never silently converted to success.
- Network loss during recording must not force corruption of an otherwise recoverable local capture; upload can resume after recording/finalization when policy permits.
- Application restart must not silently abandon discoverable temporary capture artifacts; P07 owns the durable recovery implementation and physical-device evidence.
- Capture-device certification is per exact hardware/driver/profile combination and cannot be satisfied by mocks.

## Security and administration

- Only authorized users/stations may invoke capture operations.
- Device/profile configuration is server-governed; client-supplied arbitrary native paths or vendor configuration cannot bypass policy.
- Secrets or storage credentials are not passed to capture providers.
- Capture actions and material failures become auditable central events once the runtime is implemented.

## Consequences

- WPF remains the native Windows shell selected by ADR 0002, but the provider contract is independent of a specific hardware vendor.
- Web never performs professional tape capture.
- P01 may build a real navigable Tape Capture workspace shell without fabricating hardware readiness.
- P07 implements and certifies the first real provider, sustained capture, preview/meters/timecode, temporary-cache behavior and recovery evidence.
- Any change that makes a workstation the permanent media authority or permits `Protected` before independently verified Backup requires a new architecture decision and is incompatible with the current product contract.
