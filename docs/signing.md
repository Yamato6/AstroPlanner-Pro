# Code signing

v2.0.0 ships **unsigned**: Windows SmartScreen may warn on the first download ("Windows protected
your PC" → *More info* → *Run anyway*). The release workflow can sign the executable and the
installer through the [SignPath Foundation](https://signpath.org/), which signs open-source
projects for free. The signing steps in `.github/workflows/ci.yml` run only once it is set up.

## 1. Apply to SignPath Foundation

1. Read the [conditions](https://signpath.org/terms) and apply from <https://signpath.org/> with
   the repository URL. The project must be open source under an OSI license (MIT here), with a
   public repository and releases built by CI from the repository's source. **The repository must
   be public before applying.**
2. SignPath asks for a short description, the maintainers, and the code-signing policy that the
   README must show (who approves releases; that no binaries are signed outside CI).
3. Once accepted, SignPath creates an organization and a project. Note the **organization id**,
   the **project slug** and the **signing policy slug** (usually `release-signing`).

## 2. Configure the SignPath project

- Connect the GitHub repository as a *trusted build system* (GitHub.com).
- Create two **artifact configurations**:
  - slug `exe`: a single PE file, `AstroPlannerPro.exe`;
  - slug `installer`: a single PE file, `AstroPlannerPro_*_Setup.exe`.
- Create an API token for a CI user with *submitter* rights on the project.

## 3. Configure the GitHub repository

In *Settings → Secrets and variables → Actions*:

| Kind | Name | Value |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | the CI user's API token |
| Variable | `SIGNPATH_ORGANIZATION_ID` | the organization id |
| Variable | `SIGNPATH_PROJECT_SLUG` | the project slug |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | the signing policy slug |

With `SIGNPATH_ORGANIZATION_ID` set, the next `v*` tag runs the signing steps: the published exe
is signed before it goes into the zip and the installer, and then the installer is signed. Each
request waits for approval in SignPath (manual for the Foundation's release policy), so the job
stays running until a maintainer approves it.

## 4. Check

After the release: right-click the exe → *Properties* → *Digital Signatures*, or
`Get-AuthenticodeSignature .\AstroPlannerPro.exe` must show `Valid`. `SHA256SUMS.txt` needs no
change by hand: the checksums are computed after signing.
