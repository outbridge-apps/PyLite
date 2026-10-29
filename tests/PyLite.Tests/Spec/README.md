# Spec golden files (T-TST-02)

A golden `.py` is self-contained: the script plus expectations in `#` comment directives.

Directives: `# in.x: v`, `# json_in.x: {...}`, `# out.x: v`, `# json_out.x: {...}`,
`# print: line` (repeat, order matters), `# error: PyType[: message-substring]`,
`# error_kind: Syntax|Runtime|Budget|Host|Fault`, `# error_line: N`,
`# limits.<snake_name>: N`, `# seed: N`, `# skip: reason`, `# tags: csv`.

Escapes in values: `\n`, `\t`, `\r`, `\`. Rules: one space after `:` is REQUIRED
(`# out.x:5` fails the parse); duplicate `out.x` fails; comparisons are Ordinal byte-for-byte.

Process: create `Spec/<topic>/<name>.py`, write the script + expectations (capture hard values
from reference CPython 3.14), run the Spec tests, commit the `.py` — no separate .expected file.
