# Code quality

- Make the smallest complete change. Keep unrelated refactors and formatting out of the diff.
- Use clear names and straightforward control flow. Prefer early returns for exceptional cases. Keep functions focused.
- Reuse existing code that fits. Extract a helper when it clarifies the caller or gives a shared rule one home.
- Preserve existing behavior and saved data unless the task calls for a change. Handle missing inputs and optional dependencies where they enter the code.
- Verify API names and return values before relying on them. Prefer local variables and keep mutable state close to its owner.
- Keep frequent update paths cheap. Reuse UI objects and update them only when needed.

## Comments

- Default to no comment. Improve the name or simplify the code first.
- Comment only when the reason would be easy to miss, such as an API quirk, a required operation order, or an invariant. Usually one sentence is enough.
- Skip line-by-line narration, boilerplate docstrings, and decorative section headers. Remove comments that become stale.
