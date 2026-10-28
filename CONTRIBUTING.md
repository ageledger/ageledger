# Contributing

- Work on a branch and open a pull request into `main`; CI must pass.
- Keep the decision log to its five fields. Never add a birthdate, install ID, account ID, age-source tier or device identifier to the model, the log or the tests.
- Test data is made up.

## Building

`global.json` pins the .NET SDK feature band 10.0.300 (latest patch), so contributors need a 10.0.3xx SDK.

Local commands:

```bash
dotnet test tests/Ageledger.Tests -p:CoreOnly=true   # quick, no mobile workloads
dotnet build                                         # all targets (needs the MAUI workloads and Xcode)
```

The public API baseline (`PublicAPI.Unshipped.txt`) is per target: a `CoreOnly` build checks only the `net10.0` baseline, and the Android and iOS baselines (`src/Ageledger/PublicAPI/`) are checked by the full build and by CI.

## Releasing

Maintainers only. Publishing happens only from the public repository `ageledger/ageledger`; the workflow does nothing anywhere else. Pushing a tag `vX.Y.Z` (or `vX.Y.Z-pre` for a prerelease) on a commit that is on `main` runs `.github/workflows/release.yml`: it repeats the CI checks, packs both packages with the version taken from the tag, re-checks them (`scripts/ci/check-packages.sh`), and then a separate job pushes them to nuget.org after approval of the `release` environment. Pushes use NuGet trusted publishing, so no API key is stored.

Before the first tag, complete this checklist in this order:

1. [ ] In the repository settings, create the `release` environment with the maintainers as required reviewers, and limit its deployment branches and tags to the tag pattern `v*`. Leave "Prevent self-review" off while there is one maintainer (it would block every release); turn it on once there are at least two. Until then the approval step is a deliberate pause before publishing, not a second person's check.
2. [ ] Store `NUGET_USER` (the nuget.org profile name that owns the packages) as a secret of the `release` environment.
3. [ ] Add a tag ruleset on `v*` so only maintainers can create, update or delete tags.
4. [ ] Only then, after the repository exists, create the trusted publishing policy on nuget.org for repository `ageledger/ageledger`, workflow `release.yml`, the package owner and Environment = `release`. The environment field is required: without it a run from any other environment of this workflow could ask for a key.

Create release tags as annotated or lightweight tags (either works) and never move or recreate a `v*` tag: the publish job checks out the tagged commit by its SHA.

### If a release stops half way

The workflow has no automatic recovery. A version is refused when nuget.org already lists it; the lookup can lag validation by minutes, so nuget.org's own 409 answer is the final guard. If a run fails after the push started (`Ageledger` pushed and `Ageledger.Testing` not, a `.snupkg` missing, or a failure while nuget.org was still validating), work through this:

0. Check nuget.org before anything else: the package pages, or the flat-container indexes `https://api.nuget.org/v3-flatcontainer/ageledger/index.json` and `.../ageledger.testing/index.json`, plus the symbol package status. Wait for validation (it can take several minutes), then write down which of the four files (`Ageledger.X.nupkg`, `Ageledger.X.snupkg`, `Ageledger.Testing.X.nupkg`, `Ageledger.Testing.X.snupkg`) are missing.
   - If NEITHER package is listed after waiting, **Re-run failed jobs** is the right action: it re-runs only the publish job, with the same hash-verified artifact and a fresh approval.
   - Never use **Re-run all jobs**. It builds again and fails at the artifact upload by design.
   - In every other case, finish by hand as below. Always push `Ageledger.X.nupkg` before `Ageledger.Testing.X.nupkg`; a 409 on a file that is already there is harmless.
1. Do not move the tag.
2. Download the `packages` artifact of the first attempt and unzip it.
3. Verify the files against the hashes printed by that attempt's build job: copy the four lines from the log in the web view (or with the timestamp column stripped) into a file and run `shasum -a 256 -c <file>` in the folder with the packages.
4. On nuget.org create an API key with an expiry of 1 day, scoped to only the missing package id. Use the scope "Push only new package versions" if that id has been published before. If it has never been published (the first release), use "Push new packages and package versions" with the glob set to exactly that id.
5. Read the key into an environment variable without echoing it, never typing it literally on a command line: `read -rs NUGET_API_KEY; export NUGET_API_KEY`. While the push runs the key is visible in the process list (`ps`) to other users of the machine, so do not do this on a shared machine with other users active.
6. Push the missing package, for example: `dotnet nuget push Ageledger.Testing.X.Y.Z.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"`.
7. If only a `.snupkg` is missing, push that file the same way.
8. Run `unset NUGET_API_KEY` and delete the key on nuget.org.
9. If the artifact has expired (it is kept for its own retention period), the version cannot be finished: deprecate or unlist the half-published version on nuget.org and release the next patch version.

## Contributions

Contributions are licensed under the Apache License 2.0, the same licence as the project (inbound = outbound, as in section 5 of the licence). Copyright stays with each contributor; the project's copyright line is "Copyright 2026 The Ageledger Authors".

Every commit needs a [Developer Certificate of Origin](https://developercertificate.org/) sign-off (`git commit -s`, which adds a `Signed-off-by:` line). By signing off you agree to the DCO 1.1: in short, that you wrote the change or otherwise have the right to submit it under the project's licence, and that the contribution and the sign-off (including your name or handle) are a public, permanent record. A real name is not required.

Security problems go through the private process in [SECURITY.md](SECURITY.md), not public issues.
