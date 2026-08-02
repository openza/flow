# Support

For bugs and feature requests, use GitHub Issues:

https://github.com/openza/flow/issues

For privacy or security-sensitive questions, email Deependra Solanky at deependra@solanky.dev instead of posting publicly.

## Before reporting an Agent Sessions problem

Include the Flow version, Windows version, affected environment type (Windows or WSL), WSL distribution name when relevant, Codex version, and the visible error category.

Do not post:

- GitHub access tokens or OAuth codes
- Codex session identifiers
- Prompt or conversation content
- Personal working-directory paths
- Private repository, organization, branch, pull request, release, or workflow names
- Unreviewed diagnostic logs

You can replace private values with short placeholders while keeping the structure of an error or command understandable.

## Common Agent Sessions checks

- Confirm that `codex --version` works in the same Windows or WSL environment.
- Confirm that Windows Terminal is installed before using **Resume in terminal**.
- Refresh Agent Sessions after starting a WSL distribution that has been stopped for a long time.
- If Resume is unavailable, confirm that the original working directory still exists in its owning environment.

Flow does not install, update, authenticate, or repair Codex, WSL, Windows Terminal, or GitHub.
