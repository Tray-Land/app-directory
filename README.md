# TrayLand app directory

This is the source for the public [`Tray-Land/app-directory`](https://github.com/Tray-Land/app-directory)
repository. The publisher, schema, assets, examples, and workflows do **not**
depend on Maker Market, a private project, or a sibling checkout. The live
catalog is available at
[`https://tray-land.github.io/app-directory/v1/catalog.json`](https://tray-land.github.io/app-directory/v1/catalog.json),
and its canonical schema is available at
[`https://tray-land.github.io/app-directory/v1/catalog.schema.json`](https://tray-land.github.io/app-directory/v1/catalog.schema.json).

## Contribute and build

Submit one `apps/<permanent-id>.json` per app in a pull request, with evidence
for every actionable field. The canonical v1 JSON Schema is
[`schemas/catalog.schema.json`](schemas/catalog.schema.json). App source records
have the schema's `$defs/app` shape; the publisher wraps them in a versioned
index and runs that exact schema against the completed index. It additionally
rejects duplicate JSON properties/IDs, unreviewed files, mismatched channels,
unsupported recipe claims, missing images, unsafe paths, and executable artwork.

Requires .NET SDK 10.0.403. From **this directory** in PowerShell:

```powershell
dotnet run --file scripts\BuildCatalog.cs -- --output _site\v1 --revision local-seed-2026-10-27 --generated-at 2026-10-27T14:17:27Z
```

`_site\v1\catalog.json`, `catalog.schema.json`, and the complete referenced
`assets\` tree are generated in one replacement pass. The explicit UTC stamp
and revision make identical source/input builds byte-for-byte identical. The
main-branch workflow supplies its commit SHA and commit time. A local preview
label is **not** a published Git revision.

Run the standalone publisher rejection fixtures with
`.\scripts\TestCatalog.ps1`. They use and clean an ignored directory under
`_verify\`; no machine temp folder or remote repository is involved.

The publisher uses a pinned `JsonSchema.Net` library directive in its single
C# file; normal `dotnet run` restores this library on first use. It performs
no network health requests and needs no GitHub credentials. A failed
validation must not publish. CI validation on pull requests has read-only
permissions and no publication/signing secrets.

### Review checklist

- IDs are permanent even if display names, organization ownership, or slugs
  change. Check source, Windows compatibility, x64/ARM64 claims, and
  maintenance status. Withdrawn/deprecated records need a reason; withdrawal
  prevents new installs/remixes but never removes an installed package.
- License, license-file URL, and attribution are real; setting
  `redistributionVerified`/`remixVerified` confirms curator-reviewed **rights**,
  not that an automated remix recipe or signed installer exists. No recipe
  means no Remix action. A recipe requires a separate exact approval in
  `recipes/approved.json` tied to an immutable tray-app-kit release, supported
  source revision, and project path. Source alone is not permission to build.
- The converter pilot recipe exists in the local kit source, but the required
  immutable public kit release has not been published or verified. Accordingly,
  `recipes/approved.json` remains empty and the bundled listing does not expose
  a Remix action. Do not substitute a proposed release URL for that evidence.
- A GitHub channel requires an actual stable `.appinstaller`, package name
  and publisher independently checked against the feed/package, and feed and
  immutable assets in the **same repository** as the listing. Store channels
  have a Store product ID/URL and an independently verified identity if
  available. Never infer a feed or identity from a release tag, display name,
  GitHub redirect, or a renamed repository. Installation additionally
  requires source/identity revalidation and the separate native-deployment
  qualification gate; a directory PR cannot bypass Windows signatures or
  user consent.
- Images must be reviewable, licensed raster files (`.png`, `.jpg`, `.jpeg`,
  `.webp`, `.ico`), at most 1 MiB each and 16 MiB total, under
  `assets/<app-id>/`. Add accessible `description` text and preserve
  copyright/permission notices in `assets/LICENSE` and
  `assets/ATTRIBUTION.md`. No SVG, script, HTML, external image URLs,
  symlinks, executable content, or images with uncertain third-party rights.
  The two seed icons are MIT-licensed; screenshots with map/wallpaper
  content were intentionally omitted.
- Protect changes to publishers, feed destinations, identities, recipe
  approvals, and workflows with maintainer review. Network health checks
  should report issues, never rewrite metadata or withdraw apps automatically.

## Publication gate

The public repository deploys from `main` with GitHub Pages configured to use
GitHub Actions. `main` requires pull-request review and the
`Validate app directory` check; the `github-pages` environment requires a
reviewer and accepts deployments only from `main`. A push to `main` runs
validation and the provided `Publish approved app directory` workflow, which
builds the full versioned index/assets without secrets, uploads only `_site`,
and deploys through that protected environment. Publication is considered
complete only after the catalog, schema, and every referenced asset are
retrieved successfully from the unauthenticated HTTPS URLs.

The current v1 client is strict: unknown fields and channel values are rejected.
Changes confined to existing presentation fields are compatible; adding fields,
changing meanings, or removing values requires a coordinated new major endpoint
and client support. Unsupported or invalid data retains the last-known-good
local catalog with an explanatory status.

## Website consumer

The existing seasonal website keeps day, date, blog, idea, and release-history
information. A small optional build-time adapter at
[`examples/seasonal-website/catalog-overlay.mjs`](examples/seasonal-website/catalog-overlay.mjs)
joins verified shared facts by exact repository URL without maintaining a
second catalog. Its [integration example](examples/seasonal-website/README.md)
shows the two changes needed in the existing `node build.mjs` approach. The
website is not local to this checkout and **has not been modified remotely**.

Directory tooling and schema are MIT licensed under [LICENSE](LICENSE).
Redistributed icons retain their own copyright/permission notice under
[`assets/LICENSE`](assets/LICENSE).
