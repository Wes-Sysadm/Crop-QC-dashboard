# Adding or changing rules

1. Read the catalog, decision register and existing detailed contract. Reuse an ID for the same rule; never renumber/reuse retired IDs.
2. Classify the change: clarification, evidence/test addition, implementation correction, or foundational policy change. Correct code to policy; do not weaken policy to fit code or a failing test.
3. Provide a compliance assessment. For a foundational change explicitly document old rule, proposed rule, reason, all affected workflows, tests, migrations, historical-data consequences and rollback.
4. Obtain explicit human policy approval with a durable reference; record who/when and add a decision. A prompt does not silently supersede policy. The initial constitution records the owner's supplied rules; later changes need their own decision.
5. Update the specification version, traceability and focused contracts together. A pending gap is explicit, scoped and owned by subsequent work; it is not an exemption to release safety.
6. Keep existing architecture writer guards enabled. Changing classifications/hashes is a review event, not automatic rebaselining to silence a failure.
7. Protect governance, test infrastructure, writer registries and mapped test files with CODEOWNERS. A PR changing them must include a Governance change section stating old/new behavior, impact, test/historical implications and approval requirement/reference.
8. The workflow validates that disclosure, but cannot authenticate a prose approval. Human/code-owner review and required checks must be enforced in repository settings. An unrelated fix cannot quietly delete invariant tests.

Use [the PR template](../../.github/pull_request_template.md). Pure evidence additions do not require invented new business approval; say why policy is unchanged. Never weaken an invariant/check within an unrelated PR.

Before merging outstanding inventory work, follow [integration requirements](OUTSTANDING_PRS.md). No authorization to repair the six lineage findings is contained here.
