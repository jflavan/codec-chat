import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';

import { fileURLToPath } from 'node:url';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const srcRoot = path.join(projectRoot, 'src');
const eventDirectivePattern = /\bon:[a-zA-Z][\w-]*/g;

async function walk(dir) {
	const entries = await readdir(dir, { withFileTypes: true });
	const files = [];
	for (const entry of entries) {
		const fullPath = path.join(dir, entry.name);
		if (entry.isDirectory()) {
			files.push(...(await walk(fullPath)));
		} else if (entry.isFile()) {
			files.push(fullPath);
		}
	}
	return files;
}

function findEventDirectives(contents) {
	const matches = [];
	const lines = contents.split(/\r?\n/);
	for (let index = 0; index < lines.length; index += 1) {
		const line = lines[index];
		if (eventDirectivePattern.test(line)) {
			const trimmed = line.trim();
			matches.push({ line: index + 1, snippet: trimmed });
		}
		eventDirectivePattern.lastIndex = 0;
	}
	return matches;
}

async function run() {
	const files = await walk(srcRoot);
	const svelteFiles = files.filter((file) => file.endsWith('.svelte'));
	const findings = [];

	for (const file of svelteFiles) {
		const contents = await readFile(file, 'utf-8');
		const matches = findEventDirectives(contents);
		for (const match of matches) {
			const relative = path.relative(projectRoot, file);
			findings.push(`${relative}:${match.line} ${match.snippet}`);
		}
	}

	if (findings.length > 0) {
		console.error('Deprecated Svelte event directives found:');
		for (const finding of findings) {
			console.error(`- ${finding}`);
		}
		process.exit(1);
	}

	console.log('No deprecated Svelte event directives found.');
}

run().catch((error) => {
	console.error('Failed to scan for deprecated event directives.');
	console.error(error);
	process.exit(1);
});
