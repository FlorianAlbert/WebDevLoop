# Issue tracker: GitHub

Issues and specs live in GitHub Issues for `FlorianAlbert/WebDevLoop`. Use the `gh` CLI from this clone; it infers the repository from the remote. Outside the clone, specify `--repo FlorianAlbert/WebDevLoop` on issue commands.

## Conventions

- **Create**: `gh issue create --title "..." --body "..."`. For multi-line bodies, use `--body-file <path>`.
- **Read**: `gh issue view <number> --json number,title,body,labels,comments`.
- **List**: `gh issue list --state open --json number,title,body,labels,comments` with appropriate `--label` and `--state` filters.
- **Sub-issues**: `gh issue create --parent <parent> ...` or `gh issue edit <parent> --add-sub-issue <child>` (`gh` 2.94+). Older `gh`: `gh api --method POST repos/FlorianAlbert/WebDevLoop/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>`. Without sub-issues, put `Part of #<parent>` at the top of the child body.
- **Comment**: `gh issue comment <number> --body "..."`.
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`.
- **Close**: `gh issue close <number> --comment "..."`.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Skill operations

When a skill says "publish to the issue tracker", create a GitHub issue. When it says "fetch the relevant ticket", read the issue using the command above.

## Wayfinding operations

Used by `/wayfinder`. The map is one issue with child issues as tickets.

- **Map**: an issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body.
- **Child ticket**: link to the map as a sub-issue. Where unavailable, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`, `prototype`, `grilling`, or `task`).
- **Blocking**: use native issue dependencies: `gh api --method POST repos/FlorianAlbert/WebDevLoop/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`. Obtain the numeric database id with `gh api repos/FlorianAlbert/WebDevLoop/issues/<number> --jq .id`; it is not the issue number or node id. Where dependencies are unavailable, use `Blocked by: #<number>` at the top of the child body.
- **Frontier**: list the map's open children, excluding assigned tickets and tickets with open blockers (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the fallback line). First in map order wins.
- **Claim**: `gh issue edit <number> --add-assignee @me`, the session's first write.
- **Resolve**: comment with the answer, close the ticket, and append a context pointer (gist + link) to the map's Decisions-so-far.
