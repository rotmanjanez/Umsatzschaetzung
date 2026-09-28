import type { APIRoute } from 'astro';

const paths = Object.keys(import.meta.glob('./**/*.astro'))
  .map(file => file.slice(1).replace(/(index)?\.astro$/, '').replace(/([^/])$/, '$1/'))
  .filter(path => path !== '/404/');

export const GET: APIRoute = ({ site }) => new Response(
  `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${paths.map(path => `  <url><loc>${new URL(path, site)}</loc></url>`).join('\n')}
</urlset>
`,
  { headers: { 'Content-Type': 'application/xml' } },
);
