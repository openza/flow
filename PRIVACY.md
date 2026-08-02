# Privacy Policy

Last updated: July 26, 2026

Openza Flow is a local-first Windows developer companion maintained by Deependra Solanky. This policy explains what information Flow accesses, where it is processed and stored, and the controls available to you.

Openza Flow does not send telemetry, analytics, advertising identifiers, crash reports, agent-session content, or GitHub data to Openza. Openza does not operate a server that receives this information.

## Agent Sessions

When Agent Sessions is enabled, Flow performs local environment discovery:

- It searches the current Windows user's executable path for a working Codex CLI.
- It asks Windows Subsystem for Linux (WSL) for the installed distribution names.
- For each detected distribution, it checks whether a working Codex CLI is available and reads its version.
- It starts the locally installed Codex `app-server` process for each enabled environment to request interactive, non-archived session summaries.

Session information displayed by Flow can include session identifiers, titles, source application, timestamps, working directories, repository roots, repository names, branches, and a short preview of user and assistant messages. Flow requests a preview only after you select a session.

Agent-session summaries and previews are held in memory while the relevant parts of Flow are active. Flow does not copy them into its GitHub cache, upload them to Openza, or directly read, edit, move, or delete Codex JSONL or SQLite state files.

For Agent Sessions, Flow stores the identifiers of agent environments you disable and your preferred terminal launch mode in Windows application settings. It does not persist a catalog of sessions, session identifiers, prompts, previews, working directories, or repositories.

## Resuming a Session

When you choose **Resume in terminal**, Flow validates the original working directory and starts Windows Terminal with structured process arguments:

- Windows sessions use the detected native Codex executable and original Windows directory.
- WSL sessions use the original WSL distribution, Linux working directory, and Linux Codex executable.

Flow does not translate a WSL path into a Windows path and does not add model, profile, sandbox, approval, or bypass options.

Codex may independently connect to OpenAI when you resume or otherwise use it. That communication is performed by your installed Codex CLI under its own configuration and applicable OpenAI terms and privacy policy; it is not sent through Flow or an Openza service.

Copy actions place the selected session identifier, path, or resume command on the Windows clipboard only after you request that action.

## GitHub Credentials

GitHub sign-in is optional. If you connect GitHub, Flow stores the credential you choose to use:

- A GitHub OAuth device-flow access token, or
- A GitHub Personal Access Token.

The token is stored locally in Windows Credential Locker and is used only to authenticate requests made directly from Flow to GitHub APIs. Flow also stores the signed-in GitHub username in Windows application settings.

Signing out removes the saved token and username and clears Flow's GitHub cache.

## GitHub Data

Flow reads pull request, repository, organization, release, and workflow-run metadata from GitHub so it can display review requests, pull requests you created, recently reviewed and recently created history, releases, Actions workflow runs, organization filters, and optional notifications.

Release and Actions pages are read-only. Draft releases appear only when GitHub returns them under the signed-in account's permissions.

Some default GitHub views are cached as JSON in Flow's local application-data folder to improve startup and navigation performance. Cached data can include pull request titles, repository names, authors, organization information, URLs, timestamps, releases, and workflow-run metadata.

Flow does not send GitHub credentials or GitHub data to Openza servers.

## Network Requests and External Applications

Flow connects directly to GitHub API and authentication endpoints only when you use GitHub features. It may open the following destinations through Windows using your default browser:

- GitHub pull request, release, workflow-run, source, support, and authentication pages.
- The Openza Flow user guide, privacy policy, and release notes on `solanky.dev` or GitHub.

Agent-session enumeration uses local processes and local inter-process communication. Flow does not upload agent-session summaries or previews.

## Background Mode and Notifications

If you enable **Run in background**, Flow may periodically refresh GitHub review requests while the app remains active in the notification area. If notifications are also enabled, Flow may show Windows notifications for newly detected review requests. These settings are optional and can be disabled in Flow or through Windows notification settings.

Flow remains usable when background mode and notifications are disabled.

## Application Settings and Diagnostic Logs

Flow stores local preferences such as theme, selected GitHub organization, background and notification choices, disabled agent environments, and terminal launch mode in Windows application settings.

Flow may write a rotating diagnostic log in its packaged local application-data folder to help diagnose startup and integration failures. The log records operational events and sanitized error categories rather than exception messages or stack traces. It is not uploaded automatically.

## Sharing and Sale

Openza does not sell, rent, license, or share information accessed by Flow with data brokers, advertising services, or other third parties. Flow contains no advertising or behavioral tracking.

GitHub and a user-installed Codex CLI process information under their own terms when you choose to use those integrations.

## Your Controls and Data Removal

You can:

- Disable an agent environment in Settings.
- Avoid loading a session preview by not selecting that session.
- Sign out of GitHub to remove the saved GitHub credential and clear the GitHub cache.
- Disable background operation and notifications.
- Reset or uninstall Flow through Windows Settings to remove its packaged application settings, cache, and diagnostic log.

Deleting or archiving Codex sessions is outside Flow. Use the controls provided by Codex for its own data.

## Changes to This Policy

This policy will be updated when Flow adds features that materially change how information is accessed, stored, transmitted, or shared. The revision date at the top identifies the latest version.

## Contact

For privacy questions or requests, contact Deependra Solanky at deependra@solanky.dev.
