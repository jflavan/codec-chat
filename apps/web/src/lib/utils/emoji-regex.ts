/** Matches a single custom emoji shortcode like `:pepe:` (exact match, no global flag). */
export const CUSTOM_EMOJI_EXACT_REGEX = /^:([a-zA-Z0-9_]{2,32}):$/;

/** Matches custom emoji shortcodes within a larger string (global flag for matchAll). */
export const CUSTOM_EMOJI_GLOBAL_REGEX = /:([a-zA-Z0-9_]{2,32}):/g;
