# ADR 0002 — Windows Desktop technology: WPF on .NET 10

- Status: Accepted for baseline
- Date: 2026-09-10
- Phase: P00

## Spike criteria

The P00 desktop-framework spike compared the two permitted directions against the product constraints: .NET 10 alignment, x64 institutional deployment, capture-card/vendor SDK interoperability, high-DPI resizable desktop workflows, Arabic/English composition, long-running ingest reliability, testability, installer ownership and CI/build reproducibility.

## Decision

Select WPF on .NET 10 for `MAM.Desktop`.

WPF provides a mature Windows desktop runtime and interop surface without adding a Windows App SDK deployment dependency to the baseline. It is suitable for the dense tape-capture and operational workspaces required here, while custom design tokens/templates can deliver the locked Navy + Gold premium visual system.

WinUI 3 remains a future architecture-change option only if a later validated requirement materially outweighs deployment/interop simplicity. Such a change requires a new ADR; P01 may not silently switch frameworks.

## Guardrails

- No default/legacy visual styling is accepted as final UI quality.
- P01 owns the premium shell, responsive composition, RTL/LTR behavior and accessibility evidence.
- Hardware capture stays behind provider interfaces; UI code must not own vendor SDK business rules.
