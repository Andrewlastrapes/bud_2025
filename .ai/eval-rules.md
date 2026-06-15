# AI Evaluation Rules

## Deterministic Tests First

For this first pass, prefer ordinary deterministic tests over model-graded evals.

## When to Use Model-Graded Evals

- Natural language understanding (e.g., merchant name normalization)
- Ambiguous classification (e.g., "is this a subscription?")
- User-facing text quality (e.g., notification messages)

## When NOT to Use Model-Graded Evals

- Pure logic (e.g., date calculations, frequency detection)
- Numeric calculations (e.g., budget formulas, confidence scoring)
- Data transformations (e.g., DTO mapping)
- Filtering/grouping operations

## Eval Design Principles (Future)

1. **Ground truth dataset** - manually curated, version-controlled
2. **Clear rubric** - what makes a "correct" answer?
3. **Regression tracking** - compare against baseline
4. **Fast feedback** - evals should run in < 10 seconds
5. **Actionable failures** - show exactly what went wrong

## Current State

No model-graded evals exist yet. This is intentional. Build deterministic test coverage first, then add evals where they provide unique value.
