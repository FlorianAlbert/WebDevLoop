# Domain Docs

This repo uses a single-context domain documentation layout.

## Before exploring

Read root `GLOSSARY.md` and the ADRs in `docs/adr/` relevant to the area being explored.

If these files do not exist, proceed silently. The `/domain-modeling` skill creates them lazily when terms or decisions get resolved; it is also reached through `/grill-with-docs` and `/improve-codebase-architecture`.

## File structure

- `GLOSSARY.md`: shared domain vocabulary at the repository root.
- `docs/adr/`: architecture decision records.

## Use the glossary's vocabulary

When naming a domain concept in an issue, proposal, hypothesis, or test, use the glossary's term rather than synonyms it explicitly avoids.

If a needed concept is absent, reconsider whether it belongs to the domain or note the gap for `/domain-modeling`.

## Flag ADR conflicts

Surface any contradiction with an existing ADR explicitly, identifying the ADR and explaining why the decision may need reopening.
