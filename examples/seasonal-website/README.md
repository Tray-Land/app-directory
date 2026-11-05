# Build-time bridge for the existing Tray-tober site

The public `Tray-Land/tray-tober.github.io` site currently uses
`node build.mjs` to read `data/apps.json`. Keep that file for seasonal
`day`, `date`, `idea`, `blogUrl`, `hours`, and historical `releaseTag` fields;
do not create a second maintained list of shared app facts.

After an **approved** directory endpoint exists, its build workflow can fetch
the published v1 `catalog.json` to a local build artifact and add the
[`catalog-overlay.mjs`](catalog-overlay.mjs) module to the website. In
`build.mjs`, replace the existing `const apps = JSON.parse(readFileSync(dataFile,
'utf8')).apps.filter(...)` with this equivalent:

```js
import { overlayCatalog } from './catalog-overlay.mjs';

const catalogFile = process.env.TRAYLAND_CATALOG_FILE;
if (!catalogFile) throw new Error('Set TRAYLAND_CATALOG_FILE to a downloaded catalog.json');
const seasonal = JSON.parse(readFileSync(dataFile, 'utf8'));
const directory = JSON.parse(readFileSync(catalogFile, 'utf8'));
const apps = overlayCatalog(seasonal, directory).apps.filter((a) => a.date <= today);
```

`TRAYLAND_CATALOG_FILE` is the build's explicit path to that
already-downloaded index.
The adapter joins exact `https://github.com/<owner>/<repo>` URLs, never
display names. For a Store-only app without a repository, add its permanent
`catalogId` to the seasonal record. Unmatched days stay as they were.

The current site template also turns **any** `releaseTag` into a "Download"
link to loose GitHub assets. Preserve the tag in historical facts but replace
that one template branch with `verifiedInstallUrl`:

```js
if (a.verifiedInstallUrl)
  links.push(`<a class="btn primary" href="${esc(a.verifiedInstallUrl)}">Get ${esc(a.name)}</a>`);
```

The adapter clears unverified legacy Store links and never returns an install
destination for withdrawn listings. It also exposes `catalogDescription`,
`catalogStatus`, `catalogLicenseUrl`, and a safely constructed
`catalogIconUrl`/`catalogIconAlt`; the existing `idea` and local seasonal icons
need not change. Use the existing site's HTML escaping for any new text/URLs.
The published directory itself is not a replacement for package signature
checks or the manager's native-deployment qualification.

Run the standalone example checks with
`node --test examples\seasonal-website\catalog-overlay.test.mjs` from the
directory root. No website checkout was edited or pushed by this work.
