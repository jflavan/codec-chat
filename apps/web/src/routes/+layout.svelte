<script lang="ts">
	import '$lib/styles/global.css';
	import favicon from '$lib/assets/favicon.svg';
	import { page } from '$app/state';
	import { pwaInfo } from 'virtual:pwa-info';
	import ReloadPrompt from '$lib/components/ReloadPrompt.svelte';

	let { children } = $props();

	const description = 'Codec is an open-source, real-time chat app inspired by Discord — built with SvelteKit and ASP.NET Core. Create servers, channels, and DMs with friends.';
	const ogTitle = 'Codec — Real-Time Chat';
	const ogImagePath = '/og-image.png';

	const webManifestLink = $derived(pwaInfo ? pwaInfo.webManifest.linkTag : '');
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
	<link rel="preconnect" href="https://fonts.googleapis.com" />
	<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin="" />
	<link
		rel="preload"
		as="style"
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&display=swap"
	/>
	<link
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&display=swap"
		rel="stylesheet"
	/>

	<!-- SEO -->
	<meta name="description" content={description} />

	<!-- Open Graph -->
	<meta property="og:type" content="website" />
	<meta property="og:site_name" content="Codec" />
	<meta property="og:title" content={ogTitle} />
	<meta property="og:description" content={description} />
	<meta property="og:url" content={page.url.href} />
	<meta property="og:image" content="{page.url.origin}{ogImagePath}" />
	<meta property="og:image:width" content="1200" />
	<meta property="og:image:height" content="630" />
	<meta property="og:image:alt" content="Codec — Real-Time Chat application" />

	<!-- Twitter / X Card -->
	<meta name="twitter:card" content="summary_large_image" />
	<meta name="twitter:title" content={ogTitle} />
	<meta name="twitter:description" content={description} />
	<meta name="twitter:image" content="{page.url.origin}{ogImagePath}" />

	<!-- PWA Web Manifest -->
	{@html webManifestLink}
</svelte:head>

{@render children()}
<ReloadPrompt />
