# Triage Labels

The skills use five canonical triage roles. This table maps them to this repo's GitHub labels.

| Label in mattpocock/skills | Label in our tracker | Meaning |
| --- | --- | --- |
| `needs-triage` | `needs-triage` | Maintainer needs to evaluate this issue |
| `needs-info` | `needs-info` | Waiting on reporter for more information |
| `ready-for-agent` | `ready-for-agent` | Fully specified, ready for an AFK agent |
| `ready-for-human` | `ready-for-human` | Requires human implementation |
| `wontfix` | `wontfix` | Will not be actioned |

When a skill mentions a role, use the corresponding label string from this table.

## Spec Type during triage

Before applying `ready-for-agent` or `ready-for-human` to a parentless issue, set its **Spec Type** in the repository's linked **WebDevLoop Roadmap** project to `Bug`, `Feature`, or `Chore` according to the issue's intent. Readiness labels do not determine the type. Sub-issues do not require a Spec Type.

Find the project's owner and number in its GitHub URL. The owner is the project's account or organization, not necessarily the signed-in user. If the issue is not yet in the project, add it with `gh project item-add <project-number> --owner <project-owner> --url <issue-url>`. Set the type with `gh project item-edit <project-number> --owner <project-owner> --url <issue-url> --field "Spec Type" --value <spec-type>`.

Contributors without project write access can still work on issues and pull requests. Tell a maintainer the proposed Spec Type and ask them to set it before applying a ready label.

Edit the "Label in our tracker" column to match the vocabulary used in the tracker.
