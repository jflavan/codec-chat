// apps/web/src/lib/utils/attachments.ts
//
// Shared file-attachment validation used by both MessageStore and DmStore.

export const ALLOWED_IMAGE_TYPES = new Set([
	'image/jpeg',
	'image/png',
	'image/webp',
	'image/gif'
]);

export const ALLOWED_FILE_EXTENSIONS = new Set([
	'.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
	'.txt', '.csv', '.md', '.rtf',
	'.zip', '.tar', '.gz', '.7z', '.rar',
	'.json', '.xml', '.html', '.css', '.js', '.ts',
	'.mp3', '.ogg', '.wav', '.webm', '.mp4'
]);

const MAX_IMAGE_SIZE = 10 * 1024 * 1024; // 10 MB
const MAX_FILE_SIZE = 25 * 1024 * 1024; // 25 MB

/** Validate an image file. Returns an error message or null if valid. */
export function validateImage(file: File): string | null {
	if (!ALLOWED_IMAGE_TYPES.has(file.type)) {
		return 'Unsupported image type. Allowed: JPG, PNG, WebP, GIF.';
	}
	if (file.size > MAX_IMAGE_SIZE) {
		return 'Image must be under 10 MB.';
	}
	return null;
}

/** Validate a non-image file. Returns an error message or null if valid. */
export function validateFile(file: File): string | null {
	const ext = '.' + file.name.split('.').pop()?.toLowerCase();
	if (!ext || !ALLOWED_FILE_EXTENSIONS.has(ext)) {
		return 'Unsupported file type.';
	}
	if (file.size > MAX_FILE_SIZE) {
		return 'File must be under 25 MB.';
	}
	return null;
}
