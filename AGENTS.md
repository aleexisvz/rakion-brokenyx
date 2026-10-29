# AGENTS.md — rakion-brokenyx

Rules for any agent or contributor working in this repository. Read this file
before making changes. These rules override generic defaults.

## 1. Project goal

`rakion-brokenyx` is a **clean, from-scratch C# server emulator** for the
Rakion: Chaos Force client **v258 (RIS_Ver1026 / XfsVer258)**.

It replaces the legacy community stack (patched `RakionWorldServ.exe`,
`BrokenServer.exe`, `RankUpdate.exe`, PHP web scripts) with an organic,
maintainable implementation that speaks the client's protocol natively.

Reference documents (source of truth for scope and order of work):

- [`docs/ECOSISTEMA_ARQUITECTURA.md`](docs/ECOSISTEMA_ARQUITECTURA.md) — whole-ecosystem architecture and protocol notes.
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — phased implementation plan (Phase 0–7).
- [`docs/DESGLOSE.md`](docs/DESGLOSE.md) — original reverse-engineering report of the legacy server.

Work follows the roadmap phase order unless the maintainer says otherwise.
Work is split into two lanes with path ownership and shared Core contracts
(see "Organización del Trabajo" in the roadmap). Roadmap checkboxes are
marked only by the integrator when a PR is merged, never in a feature branch.

## 2. Clean-room policy (non-negotiable)

- **No binary patches, memory injection, DLL hooking or loaders.** The server
  must work by implementing the protocol, never by modifying the client or
  re-hosting legacy executables.
- **Do not copy decompiled code** from `RakionWorldServ.exe`, `BrokenServer.exe`,
  `RankUpdate.exe` or the client. Use the binaries only to *observe behavior*
  (opcodes, field layouts, SQL semantics) and re-implement it independently.
- **No legacy runtime dependencies**: no `ggauth*.dll`, no GameGuard,
  no `libmysql.dll`, no 32-bit-only components, no PHP.
- **Never commit** anything from `audit/`, client assets (`*.xfs`, `*.bin`),
  proprietary DLLs/EXEs, database dumps with real data, or credentials.
  `audit/` is git-ignored and stays local.

## 3. Evidence discipline

Reverse-engineered facts have different confidence levels. In code comments,
docs and PRs, label them:

- **Observed** — seen directly (packet capture, string/symbol, IL, SQL text).
- **Derived** — computed from observed data (e.g. offsets from a struct dump).
- **Inferred** — plausible but unverified (e.g. purpose of a field).
- **Unknown** — explicitly unresolved.

Parts of the existing docs are *inferred* (e.g. some opcode meanings, game-mode
IDs, grade tables). Before implementing a packet, verify its layout against the
real v258 client (capture or symbol evidence) and record the evidence in the
PR. Never present an inference as fact; keep unknowns explicit in code
(`// UNKNOWN: field purpose not yet verified`).

## 4. Architecture rules

Target: **.NET 10 (LTS, `net10.0`)**, 64-bit, cross-platform (Windows + Linux/Docker).

Solution layout (see roadmap):

```text
src/
  Rakion.Core/       opcodes, enums, packet reader/writer, crypto — no I/O
  Rakion.Network/    TCP (System.IO.Pipelines) / UDP transport, sessions
  Rakion.Database/   MySQL/MariaDB persistence (repositories, migrations)
  Rakion.Broker/     login + server list (TCP 40706)
  Rakion.World/      lobby, characters, inventory, rooms (TCP 40708)
  Rakion.Relay/      UDP relay routing logic — no sockets; the 40708/40709
                     listeners live in World
  Rakion.Maintenance/ rankings, anti-abuse, scheduled jobs (phase 7)
  Rakion.Buddy/      Messenger (Buddy2.dll) server, TCP/UDP 8500
  Rakion.App/        host/composition root, configuration, logging
tests/
```

- Dependencies flow inward: `Core` ← `Network`/`Database`/`Relay` ← `Broker`/`World`/`Maintenance` ← `App`.
  `Core` must not reference sockets, SQL or hosting.
- One owner per concept: a packet layout is defined once (in `Core`) and every
  consumer uses it. No ad-hoc byte parsing in handlers.
- Handlers are bound to opcodes through an explicit, typed registry — never by
  reflection over ordering or naming conventions.
- Parse untrusted input at the boundary; reject malformed packets with a
  logged reason instead of throwing deep inside game logic. Every length field
  from the client is bounds-checked before allocation.
- SQL is always parameterized. Money/item changes (gold, cash, items) run in a
  transaction and are logged.
- Configuration lives in `appsettings.json` (+ env vars). No hardcoded IPs,
  ports or credentials in code.
- Async all the way; no blocking calls on network threads. Prefer
  `Span<byte>`/`Memory<byte>` and pooled buffers on hot paths.
- Logging via structured logging (`Microsoft.Extensions.Logging`, Serilog sink).
  Never log passwords or session tokens.

## 5. Code style

- C# latest, `Nullable` enabled, `TreatWarningsAsErrors` for `src/`.
- File-scoped namespaces, one public type per file, `PascalCase` types,
  `_camelCase` private fields.
- Keep comments for *why* and for evidence references; don't narrate the code.
- Every protocol packet gets a round-trip test (serialize → bytes → parse) and,
  when available, a golden test against captured client bytes.

Commands:

```bash
dotnet build                 # must pass with zero warnings
dotnet test                  # must pass before any push
dotnet format --verify-no-changes
```

## 6. Git & GitHub workflow (mandatory)

### 6.1 Repositories

- Local Git repository at the project root; remote on GitHub (`origin`).
- `main` is the protected integration branch. **Never commit directly to
  `main`** (the only exception was the initial bootstrap commit).

### 6.2 Branches

- Every change happens on a short-lived branch created from an up-to-date `main`:
  - `feat/<scope>-<short-desc>` — new functionality (`feat/broker-server-list`)
  - `fix/<scope>-<short-desc>` — bug fixes
  - `docs/<short-desc>`, `refactor/…`, `test/…`, `chore/…`, `ci/…`
- One branch = one coherent change (one roadmap item or one fix).

### 6.3 Commits — local only while the branch is open

- While working on a branch, **commit only to the local Git repository.
  Do NOT push to GitHub** during development.
- Commits must be **atomic**: one logical change per commit, small enough to
  review and revert on its own, and the build must pass at each commit.
- Messages follow **Conventional Commits** (<https://www.conventionalcommits.org>):

  ```text
  <type>(<scope>): <imperative summary, ≤72 chars>

  <body: what and why, wrapped at 72 cols; evidence for protocol facts>

  <footer: Refs: #issue / BREAKING CHANGE: ...>
  ```

  - Types: `feat`, `fix`, `docs`, `refactor`, `test`, `perf`, `build`, `ci`, `chore`, `style`, `revert`.
  - Scopes: `core`, `network`, `db`, `broker`, `world`, `relay`, `maintenance`, `app`, `docs`, `repo`.
  - Breaking changes: `feat(core)!: …` or a `BREAKING CHANGE:` footer.
  - Examples:
    - `feat(core): add little-endian PacketWriter`
    - `test(core): cover PacketReader bounds checks`
    - `feat(broker): answer 0xCDDF with server list packet 0x0101`
    - `docs(roadmap): mark phase 0.3 as done` (integrator only)

### 6.4 Finishing a branch — strictly gated by user approval

Under NO circumstances should the agent push to GitHub, open a PR, or merge a PR without the maintainer's explicit prior approval.

When the change or fix is **complete** (builds, tests pass, module docs updated):

1. Commit all changes locally to the Git repository using atomic Conventional Commits.
2. Run `dotnet build && dotnet test` to ensure zero errors and passing tests locally.
3. Update the module `README.md` files owned by your lane. Do not tick
   `docs/ROADMAP.md` checkboxes; the integrator does that on merge.
4. **STOP and ask the maintainer for approval.** Inform the user that local commits and tests are complete, and explicitly ask if they want to review or test the code locally before pushing to GitHub.
5. **Only upon receiving explicit approval from the maintainer**, execute:
   a. `git fetch origin && git rebase origin/main` (resolve conflicts locally).
   b. `dotnet build && dotnet test` one final time.
   c. `git push -u origin <branch>` — first and only push of the branch
      (a follow-up push is allowed only to address review or rebase).
   d. Open a **Pull Request into `main`** on GitHub with a documented description:
      - **Summary** — what changed and why.
      - **Changes** — bullet list matching the commits.
      - **Evidence** — protocol facts used and their confidence (observed/derived/inferred).
      - **Validation** — commands run, tests added, manual client test performed (or "not verified with real client").
      - **Roadmap** — item(s) completed.
      - **Remaining work / known limitations.**
   e. **Merge the PR** into `main` using a merge commit (`--merge`) to preserve
      the atomic commit history. PR title also follows Conventional Commits.
   f. Delete the branch (remote and local) and update local `main`.

Never force-push to `main`. Never rewrite history of a branch after its PR is merged.

### 6.5 Agent behavior

- Do not start implementing roadmap phases unless the maintainer asked for it.
- Do not install system software (gh, MySQL, Docker…) without asking first.
- **NEVER push to GitHub, open a PR, or merge a PR without explicit prior approval from the maintainer.** Always finish the work, commit locally to Git, and ask the maintainer if they want to test the code locally first before uploading.
- If a rule here conflicts with a request, state the conflict before acting.
