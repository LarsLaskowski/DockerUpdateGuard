---
name: review-pr
description: Use when the user asks to review a pull request of this repository on GitHub. Checks out the PR, runs the build and tests, reviews it with the squad-reviewer subagent against this project's stack, analyzer, security and unit-test conventions, and posts the findings with an explicit verdict.
---

# Review PR

Use this skill to review a pull request on GitHub — someone else's, or your
own when you deliberately want a second opinion after it is open.

For a change that has not been pushed yet, do not use this skill: the
internal review loop in `create-pr` reviews the local branch before the pull
request exists, which is cheaper and does not fill the PR with comment
threads.

Write everything in **English** — the summary to the user, the findings, and
anything posted to GitHub — regardless of the language the user wrote in.

## Steps

1. Fetch and check out the PR (or read the diff directly if a checkout isn't
   necessary). Read the PR title and body to understand the intent, and read
   any issue it references so you can judge whether the change actually
   solves the stated problem. If the PR is already merged or closed, say so
   and ask whether the user still wants a review.
2. Delegate the review itself to the `squad-reviewer` subagent
   (subagent_type `squad-reviewer`, model `opus`). Give it the
   base ref, the head SHA, and the round number — round 1 for a first review,
   and for a re-review the previous round's findings plus the commits that
   were meant to fix them. The review checklist, the integration-surface
   sweep, the severity model and the round semantics all live in that agent's
   definition (`.claude/agents/squad-reviewer.md`), so they stay
   identical whether the review runs before or after the push.
3. Post the result:
   - Inline comments for findings anchored to a line, otherwise one review
     comment.
   - **Only genuine findings.** No positive remarks, no confirmation that
     checklist items pass, no "looks good" filler, no formatting
     the formatter already fixes.
   - Lead the review body with the verdict line the subagent produced
     (`APPROVE`, or the blocking/non-blocking counts), so the author can see
     whether anything is required of them without reading every thread.
   - Mark each finding `blocking` or `non-blocking` explicitly.
4. If the review produces no findings, post nothing beyond a short approving
   verdict — and if the previous round already said the same, post nothing at
   all.

## Every posted finding gets worked

A finding that has been posted as a review comment is work, not a note. This
holds for **every** posted finding — blocking and non-blocking alike, whether
it came from this skill, from a human reviewer, or from an automated code
review on the pull request.

- Resolve it in the pull request it was posted on, while the session that can
  act on it is still running.
- Do not defer a posted finding to "the next change that touches this code"
  or "the next substantive commit". No such change is scheduled, and the
  session holding the context needed to act on the comment will not exist
  later — the deferral is a way of dropping the finding, not of carrying it
  forward.
- If a posted finding genuinely should not be acted on in this PR, it gets
  one of two concrete outcomes, never an implied one: a reply explaining why
  the code stays as it is, or a GitHub issue opened **now** and linked from
  the reply. Either way the thread is answered and resolved before the PR is
  considered done.
- Non-blocking is about whether a finding gates the merge, not about whether
  anyone will ever deal with it.

## Keeping the loop finite

A pull request review can always produce one more finding. These rules make
it converge, without leaving posted findings unhandled:

- **Round 1 reviews the whole diff. Every later round reviews only the
  delta**: does each fix resolve its finding, and did the fix commits break
  something — including in prose they wrote to fix a documentation finding?
  Never re-review untouched code; that is what turns three findings into four
  rounds.
- **Only blocking findings justify another review round.** A non-blocking
  finding is still worked per the section above, but working it does not earn
  a new round of review.
- **Two consecutive rounds without a blocking finding means done.** Say so
  plainly instead of leaving the review open-ended.
- **At most two rounds on GitHub.** If blocking findings survive that, the
  change needs a decision from the author, not another review pass — say what
  is still blocking and stop.
- The number of rounds is capped; the number of posted findings that get
  handled is not. Every open thread is answered before the PR is done, even
  when no further round runs.

## Answering findings on your own PR

When acting as the author of a PR under review:

- Fix the finding, push, then keep the reply to one line:
  `Fixed in <sha>: <what changed>`. The reasoning belongs in the commit
  message, where it stays with the code; the reviewer verifies the commit,
  not the reply.
- Re-run *Format*, *Build*, the *Analyzer gate* (no diagnostic in a changed
  file), *Test with coverage* and the *Coverage gate* from `.squad/stack.md`
  before each push — a fix that turns CI red costs more
  than the finding did.
- Resolve the thread once it is answered. One summary comment per round beats
  one essay per thread.
- Work through every open thread before calling the PR done, including the
  non-blocking ones, as described above.

## Notes

- This skill reviews; it does not silently rewrite the PR. Fixing findings on
  your own PR is the author's step above, and it is explicit — never edit
  someone else's branch without being asked.
- Prefer non-interactive commands only.
- Base the verdict on evidence from the diff and the code; if something is
  uncertain, say so instead of guessing.