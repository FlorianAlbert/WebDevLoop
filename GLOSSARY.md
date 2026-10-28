# WebDevLoop

WebDevLoop runs AI agents in roles against a repository's specs and tickets. This glossary covers the vocabulary of choosing which AI models those agents use and which issues they may work on.

## Language

**Model**:
An AI model an agent role can run on, identified by a stable ID and shown under a display name.
_Avoid_: Engine, LLM

**Model Catalog**:
The set of models currently available to the signed-in GitHub account, as reported by Copilot.
_Avoid_: Model list, model registry

**Reasoning Effort**:
How much thinking a model is asked to spend on a step; each model supports its own subset of efforts.
_Avoid_: Thinking level

**WebDevLoop Opt-in**:
Explicit permission for WebDevLoop to work on a particular issue, independent of its readiness or dependencies.
Represented by the fixed GitHub label `webdevloop`; a parent spec and each sub-issue opt in independently.
Label matching is case-insensitive. Work selection additionally requires the issue to be open.
