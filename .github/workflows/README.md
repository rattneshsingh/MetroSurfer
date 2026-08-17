# CI

`tests.yml` runs the EditMode and PlayMode suites on every push and pull request.

## One-time setup required before it will work

The workflow needs a Unity licence, which cannot be committed. Add these as
repository secrets under **Settings → Secrets and variables → Actions**:

| Secret | What it is |
|---|---|
| `UNITY_LICENSE` | Contents of your `.ulf` licence file |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |

To obtain `UNITY_LICENSE` for a Personal licence, follow the activation steps at
https://game.ci/docs/github/activation — it produces a `.ulf` file whose entire
contents become the secret's value.

Until those secrets exist the workflow will fail at the licence step. That is
expected, and nothing else about the repository depends on it.

## Note

This repository currently has no remote. The workflow does nothing until the
project is pushed to GitHub.
