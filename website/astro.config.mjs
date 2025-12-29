import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import sitemap from '@astrojs/sitemap';

export default defineConfig({
  site: 'https://openza.github.io',
  base: '/flow',
  integrations: [
    sitemap(),
    starlight({
      title: 'Openza Flow',
      description: 'Documentation for Openza Flow - GitHub PR Review Inbox for Windows and Linux',
      favicon: '/favicon.png',
      logo: {
        src: './src/assets/logo.svg',
      },
      social: {
        github: 'https://github.com/openza/flow',
      },
      customCss: [
        './src/styles/custom.css',
      ],
      sidebar: [
        {
          label: 'Getting Started',
          items: [
            { label: 'Introduction', slug: 'getting-started/introduction' },
            { label: 'Installation', slug: 'getting-started/installation' },
            { label: 'GitHub Token Setup', slug: 'getting-started/github-token' },
          ],
        },
        {
          label: 'Guides',
          items: [
            { label: 'Reviewing PRs', slug: 'guides/reviewing-prs' },
            { label: 'Notifications', slug: 'guides/notifications' },
          ],
        },
        {
          label: 'Features',
          autogenerate: { directory: 'features' },
        },
        {
          label: 'Development',
          items: [
            { label: 'Building from Source', slug: 'development/building' },
            { label: 'Architecture', slug: 'development/architecture' },
            { label: 'Contributing', slug: 'development/contributing' },
          ],
        },
      ],
    }),
  ],
});
