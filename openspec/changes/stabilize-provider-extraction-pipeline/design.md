## Context

The current extraction system has organically grown to include Direct, List, Families, Shopify, and legacy Playwright pathways. However, these pathways mutate global state, short-circuit execution prematurely, and persist data at different stages of the process. This leads to unpredictable behavior where adding a new scraping capability can inadvertently bypass an existing one. The Wizard UI also suffers from this by receiving partial diagnostic information and relying on staging records rather than a unified extraction report.

## Goals / Non-Goals

**Goals:**
- Separate the control plane (Wizard) from the execution core.
- Ensure all extraction pathways (contributors) return structured observations rather than making global decisions.
- Implement a deterministic Execution Planner that resolves how contributors run (e.g. `fallback`, `augment`, `ensemble`).
- Create an immutable `ExtractionRunReport` that acts as the single source of truth for both production and the Wizard UI.
- Defer persistence of business data (Providers, Products) until the entire pipeline is reconciled and passes a quality gate.

**Non-Goals:**
- A complete rewrite of every scraping strategy immediately. Legacy code will be wrapped in a contributor adapter.
- Changing the underlying web-automation tools (Playwright will still be used).
- Altering the user's ultimate goal (saving products); this just alters *how reliably* that goal is achieved.

## Decisions

1. **DAG Planner and Contributors**: We will replace the linear and short-circuiting runner with an Execution Planner. Every mechanism (e.g., Direct, Shopify API, Families) will implement a `ContributorDescriptor` and return a `ContributorResult`. The planner decides which to run based on a given policy.
2. **Execution Run Report**: Instead of staging products directly, the system will yield an `ExtractionRunReport`. This report contains `ProductObservation` items (raw data with provenance) and `ReconciledProduct` items (the final agreed-upon values).
3. **Demo Session vs Production Run**: The Wizard will run a Demo session (`persistencePolicy=none`, max 10 products) that yields the full report. Production will run the exact same logic with `persistencePolicy=commit` and the configured product limit.
4. **Late Persistence**: Persistence to `Products` or `Providers` will only happen *after* reconciliation and validation in production. Demo sessions will only write to an ephemeral session store if necessary for the preview UI.

## Risks / Trade-offs

- **Risk: Increased memory usage during execution due to holding all observations.**
  → Mitigation: We are targeting a limit of 120 products normally. A full report of observations for 120 products is small enough to fit in memory easily.
- **Risk: Legacy code doesn't fit the contributor model.**
  → Mitigation: Wrap legacy Playwright scraping in a `LegacyAdapterContributor` that mimics the new interface but internally runs the old logic, acting as a fallback until it can be fully replaced.

## Migration Plan

1. Correct the foundational OpenSpec definitions and get the new contracts in place.
2. Build the new DAG planner alongside the old runner (Shadow mode).
3. Connect the Wizard's Demo function to the new planner using a legacy adapter.
4. Migrate individual pathways (Direct, List, Shopify) into native contributors.
5. Decommission the old runner and staging reliance.
