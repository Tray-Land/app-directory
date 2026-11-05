const catalogBase = 'https://tray-land.github.io/app-directory/v1/';

function repositoryKey(value) {
  if (!value) return null;
  try {
    const url = new URL(value);
    const path = url.pathname.replace(/\/$/, '');
    if (url.protocol !== 'https:' || url.hostname !== 'github.com' || url.port ||
        url.username || url.password || url.search || url.hash ||
        !/^\/[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+$/.test(path)) return null;
    return path.toLowerCase();
  } catch {
    return null;
  }
}

function iconUrl(icon, id) {
  const path = icon?.path;
  if (!/^[A-Za-z0-9.-]{1,80}$/.test(id) || typeof path !== 'string' ||
      !path.startsWith(`assets/${id}/`) ||
      !/^assets\/[A-Za-z0-9._/-]+\.(png|jpe?g|webp|ico)$/.test(path) ||
      path.split('/').some((part) => !part || part === '.' || part === '..' || part.endsWith('.')))
    return null;
  return new URL(path, catalogBase).href;
}

export function overlayCatalog(seasonal, catalog) {
  if (catalog?.schemaVersion !== 1 || !Array.isArray(catalog.apps) ||
      !Array.isArray(seasonal?.apps))
    throw new Error('Expected the supported v1 catalog and existing seasonal apps.');

  const byId = new Map();
  const byRepository = new Map();
  for (const app of catalog.apps) {
    if (byId.has(app.id)) throw new Error(`Duplicate directory ID: ${app.id}`);
    byId.set(app.id, app);
    const key = repositoryKey(app.repositoryUrl);
    if (key) {
      if (byRepository.has(key)) throw new Error('Ambiguous directory repository.');
      byRepository.set(key, app);
    }
  }

  return {
    ...seasonal,
    apps: seasonal.apps.map((day) => {
      const listing = day.catalogId ? byId.get(day.catalogId) : byRepository.get(repositoryKey(day.repoUrl));
      if (!listing) return day;
      const store = listing.status === 'active' ?
        listing.channels?.find((channel) => channel.kind === 'store') : null;
      const github = listing.status === 'active' ?
        listing.channels?.find((channel) => channel.kind === 'gitHub' && channel.identityVerified) : null;
      return {
        ...day,
        catalogId: listing.id,
        name: listing.displayName,
        repoUrl: listing.repositoryUrl ?? '',
        storeUrl: store?.storeUrl ?? '',
        catalogDescription: listing.description,
        catalogStatus: listing.status,
        catalogLicenseUrl: listing.license?.fileUrl ?? null,
        catalogIconUrl: iconUrl(listing.icon, listing.id),
        catalogIconAlt: listing.icon?.description ?? '',
        verifiedInstallUrl: store?.storeUrl ?? github?.appInstallerUrl ?? null
      };
    })
  };
}
