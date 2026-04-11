Audit and fix accessibility issues in Svelte components.

## Priority Components (user-facing, interactive)
1. `apps/web/src/lib/components/message/` — Message display, composer, reactions
2. `apps/web/src/lib/components/channel-sidebar/` — Channel list, voice bar
3. `apps/web/src/lib/components/server-settings/` — Settings modals and forms
4. `apps/web/src/lib/components/auth/` — Login, registration forms
5. `apps/web/src/lib/components/modals/` — All modal dialogs

## Check Each Component For
1. **Keyboard navigation** — All interactive elements reachable via Tab, activated via Enter/Space
2. **ARIA labels** — Icon-only buttons must have `aria-label`
3. **Focus management** — Modals trap focus, return focus on close
4. **Screen reader text** — Dynamic content updates use `aria-live` regions
5. **Color contrast** — Check `tokens.css` values against WCAG AA (4.5:1 text, 3:1 large text)
6. **Form labels** — Every input has a visible or aria label
7. **Alt text** — Images have descriptive alt text (or `alt=""` for decorative)
8. **Heading hierarchy** — h1 > h2 > h3, no skipped levels

## Fix Patterns
```svelte
<!-- Icon button: add aria-label -->
<button onclick={handleClick} aria-label="Send message">
  <Icon name="send" />
</button>

<!-- Modal: add role and aria attributes -->
<div role="dialog" aria-modal="true" aria-labelledby="modal-title">
  <h2 id="modal-title">Settings</h2>
</div>

<!-- Dynamic content: add live region -->
<div aria-live="polite">{statusMessage}</div>
```

## Quality Gate
```bash
cd apps/web && npm run check && npm test
```
