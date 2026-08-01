# Triage Labels

**This repository's own labels are authoritative.** The five canonical roles the skills speak in are
secondary: they belong to the skill, not to this tracker, and appear here only so a skill's
instruction can be translated into a label that actually exists.

Do not create the canonical names as labels, and do not apply a label that is not in the table below.
`gh label list` is the source of truth; this file is a reading of it.

## The labels this tracker uses

| Label | Used for |
| --- | --- |
| `Bug｜程序错误` | A defect in shipped behaviour |
| `Feature｜功能请求` | New capability, or planned surface not yet built |
| `Test-Required｜需要测试` | Cannot be closed until covered by a test |
| `Waiting-Needed｜需等待` | Deferred on purpose; the reason and the reopening trigger belong in a comment |
| `Question｜提问` | Waiting on the reporter, or a question rather than a report |
| `Help-Wanted｜需要帮助` | Wants a human; not suitable to hand to an agent unattended |
| `Not-Planned｜未规划` | Will not be actioned |
| `Invalid｜不合规` | Out of scope, malformed, or not reproducible as reported |
| `Ignore｜忽略` | Noise |
| `Finished｜已解决` | Resolved |
| `Perfect｜优秀建议` | Notably good proposal |

## Translating a skill's canonical role

| Canonical role | Use here |
| --- | --- |
| `needs-triage` | *no equivalent* — leave unlabelled until someone judges it |
| `needs-info` | `Question｜提问` |
| `ready-for-agent` | *no equivalent* — say so in a comment instead |
| `ready-for-human` | `Help-Wanted｜需要帮助` |
| `wontfix` | `Not-Planned｜未规划` |

Two roles have no counterpart, deliberately. Inventing `needs-triage` and `ready-for-agent` would add
vocabulary this project does not use, to satisfy a skill that does not own the tracker. An unlabelled
issue already reads as untriaged, and whether an issue is ready for an agent is a judgement whose
reasoning matters more than its label — put that in a comment.

So when a skill says "apply the AFK-ready triage label", the correct action here is to write down
what makes the issue ready, not to reach for a label that does not exist.
