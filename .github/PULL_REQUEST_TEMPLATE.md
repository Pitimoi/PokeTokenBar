<!-- Write the PR title and this description in English (see CLAUDE.md). -->

## Summary

<!-- What does this PR do, and why? Keep it focused. -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / cleanup
- [ ] Documentation
- [ ] Other:

## UI changes

<!--
When this PR changes the status bar item or the companion webview, describe the before/after.
Images are welcome but optional — a clear text description is fine. Remove this section if
there are no UI changes.
-->

| Before | After |
| ------ | ----- |
|        |       |

## Checklist

- [ ] `npm run test:all` passes from `extension/` (dotnet tests plus extension tests)
- [ ] PR title and description are written in English
- [ ] New guards were verified by breaking them on purpose, not only by passing
- [ ] No protocol parameter names a path, URL, endpoint or executable
- [ ] No credential is logged, sent over the protocol, or stored outside the sidecar
- [ ] Anything newly rendered in the webview is escaped at the point of use
