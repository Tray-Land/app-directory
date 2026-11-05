import assert from 'node:assert/strict';
import test from 'node:test';
import { overlayCatalog } from './catalog-overlay.mjs';

const seasonal = {
  year: 2026,
  apps: [
    { day: 2, date: '2026-10-02', name: 'Old name', idea: 'Seasonal story', releaseTag: 'v1.0',
      repoUrl: 'https://github.com/Tray-Land/tray-converter', storeUrl: 'https://example.com/unverified' },
    { day: 3, name: 'Unmatched', repoUrl: 'https://github.com/Tray-Land/other' }
  ]
};
const listing = {
  id: 'tray-converter', displayName: 'Tray Converter', description: 'Shared description',
  repositoryUrl: 'https://github.com/Tray-Land/tray-converter',
  status: 'active', channels: [], license: { fileUrl: 'https://github.com/Tray-Land/tray-converter/blob/main/LICENSE' },
  icon: { path: 'assets/tray-converter/icon.png', description: 'Two arrows' }
};

test('preserves seasonal history but replaces shared facts without inventing install links', () => {
  const merged = overlayCatalog(seasonal, { schemaVersion: 1, apps: [listing] });
  assert.equal(merged.apps[0].name, 'Tray Converter');
  assert.equal(merged.apps[0].idea, 'Seasonal story');
  assert.equal(merged.apps[0].releaseTag, 'v1.0');
  assert.equal(merged.apps[0].storeUrl, '');
  assert.equal(merged.apps[0].verifiedInstallUrl, null);
  assert.equal(merged.apps[0].catalogIconAlt, 'Two arrows');
  assert.equal(merged.apps[1], seasonal.apps[1]);
});

test('withdrawn or changed identity never retains a prior install destination', () => {
  const feed = { id: 'stable', kind: 'gitHub', identityVerified: true,
    appInstallerUrl: 'https://github.com/Tray-Land/tray-converter/releases/latest/download/App.appinstaller' };
  const active = overlayCatalog(seasonal, { schemaVersion: 1, apps: [{ ...listing, channels: [feed] }] });
  assert.equal(active.apps[0].verifiedInstallUrl, feed.appInstallerUrl);
  const withdrawn = overlayCatalog(seasonal, { schemaVersion: 1, apps: [
    { ...listing, status: 'withdrawn', channels: [feed] }
  ] });
  assert.equal(withdrawn.apps[0].verifiedInstallUrl, null);
  assert.equal(withdrawn.apps[0].releaseTag, 'v1.0');
});

test('rejects unsupported version and ambiguous repository matches', () => {
  assert.throws(() => overlayCatalog(seasonal, { schemaVersion: 2, apps: [] }), /supported v1/);
  assert.throws(() => overlayCatalog(seasonal, { schemaVersion: 1, apps: [
    listing, { ...listing, id: 'another-id' }
  ] }), /Ambiguous/);
});

test('never turns a directory traversal reference into a website image URL', () => {
  const overlay = overlayCatalog(seasonal, { schemaVersion: 1, apps: [{
    ...listing, icon: { path: 'assets/tray-converter/../../evil.png', description: 'Untrusted' }
  }] });
  assert.equal(overlay.apps[0].catalogIconUrl, null);
});
