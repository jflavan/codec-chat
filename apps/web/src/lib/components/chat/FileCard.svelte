<script lang="ts">
	let {
		fileUrl,
		fileName,
		fileSize,
		fileContentType
	}: {
		fileUrl: string;
		fileName: string;
		fileSize?: number | null;
		fileContentType?: string | null;
	} = $props();

	function formatSize(bytes: number): string {
		if (bytes < 1024) return `${bytes} B`;
		if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
		return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
	}

	const iconType = $derived.by(() => {
		const ct = fileContentType?.toLowerCase() ?? '';
		const ext = fileName.split('.').pop()?.toLowerCase() ?? '';
		if (ct.startsWith('audio/') || ['mp3', 'wav', 'ogg', 'webm'].includes(ext)) return 'audio';
		if (ct.startsWith('video/') || ['mp4'].includes(ext)) return 'video';
		if (ct === 'application/pdf' || ext === 'pdf') return 'pdf';
		if (ct.includes('zip') || ct.includes('tar') || ct.includes('rar') || ct.includes('7z') || ['zip', 'tar', 'gz', '7z', 'rar'].includes(ext)) return 'archive';
		if (ct.includes('spreadsheet') || ct.includes('excel') || ['xls', 'xlsx', 'csv'].includes(ext)) return 'spreadsheet';
		if (ct.includes('presentation') || ct.includes('powerpoint') || ['ppt', 'pptx'].includes(ext)) return 'presentation';
		if (ct.includes('word') || ct.includes('document') || ['doc', 'docx', 'rtf'].includes(ext)) return 'document';
		if (ct.startsWith('text/') || ['txt', 'md', 'json', 'xml', 'html', 'css', 'js', 'ts'].includes(ext)) return 'text';
		return 'file';
	});
</script>

<a href={fileUrl} class="file-card" target="_blank" rel="noopener noreferrer" download={fileName} aria-label="Download {fileName}">
	<div class="file-icon" data-type={iconType} aria-hidden="true">
		{#if iconType === 'pdf'}
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm-1 2l5 5h-5V4zM9.5 15.5c0 .28-.22.5-.5.5h-1v1.5H7V13h2a1 1 0 0 1 1 1v1.5zm4-.5c0 1.1-.9 2-2 2h-1v-4h1c1.1 0 2 .9 2 2zm4.5-1.5h-2V15h1.5v1H16v1.5h-1V13h3v1z"/></svg>
		{:else if iconType === 'archive'}
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 6h-8l-2-2H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-2 6h-2v2h2v2h-2v2h-2v-2h2v-2h-2v-2h2v-2h-2V8h2v2h2v2z"/></svg>
		{:else if iconType === 'audio'}
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6z"/></svg>
		{:else if iconType === 'video'}
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z"/></svg>
		{:else if iconType === 'spreadsheet'}
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm-1 2l5 5h-5V4zM8 17h3v-1H8v1zm0-2h8v-1H8v1zm0-2h8v-1H8v1z"/></svg>
		{:else}
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm-1 2l5 5h-5V4zM6 20V4h7v5h5v11H6z"/></svg>
		{/if}
	</div>
	<div class="file-info">
		<span class="file-name">{fileName}</span>
		{#if fileSize}
			<span class="file-size">{formatSize(fileSize)}</span>
		{/if}
	</div>
	<div class="file-download" aria-hidden="true">
		<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>
	</div>
</a>

<style>
	.file-card {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 10px 12px;
		margin-top: 4px;
		max-width: 400px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		text-decoration: none;
		color: inherit;
		transition: background-color 150ms ease;
	}

	.file-card:hover {
		background: var(--bg-tertiary);
	}

	.file-icon {
		flex-shrink: 0;
		width: 40px;
		height: 40px;
		border-radius: 8px;
		display: grid;
		place-items: center;
		color: #fff;
	}

	.file-icon[data-type='pdf'] { background: #e53935; }
	.file-icon[data-type='archive'] { background: #8e6e00; }
	.file-icon[data-type='audio'] { background: #7b1fa2; }
	.file-icon[data-type='video'] { background: #1565c0; }
	.file-icon[data-type='spreadsheet'] { background: #2e7d32; }
	.file-icon[data-type='presentation'] { background: #d84315; }
	.file-icon[data-type='document'] { background: #1565c0; }
	.file-icon[data-type='text'] { background: #546e7a; }
	.file-icon[data-type='file'] { background: #546e7a; }

	.file-info {
		flex: 1;
		min-width: 0;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.file-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--accent);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.file-size {
		font-size: 12px;
		color: var(--text-muted);
	}

	.file-download {
		flex-shrink: 0;
		color: var(--text-muted);
		transition: color 150ms ease;
	}

	.file-card:hover .file-download {
		color: var(--accent);
	}
</style>
