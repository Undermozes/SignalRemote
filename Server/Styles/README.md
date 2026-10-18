# Styles

The web UI uses [Tailwind CSS v4](https://tailwindcss.com). Bootstrap is no longer used.

- Input: `Server/Styles/app.css` (design tokens, base styles) and `Server/Styles/components.css` (shared `sr-*` classes)
- Output: `Server/wwwroot/css/app.css` (committed, so builds without the CLI still work)

Rebuild the CSS after changing classes in `.razor`, `.cshtml`, `.cs` or viewer scripts. Use the
standalone CLI, which needs no Node project:

```bash
tailwindcss -i Server/Styles/app.css -o Server/wwwroot/css/app.css --minify
```

Add `--watch` while developing. Download the CLI for your platform from
https://github.com/tailwindlabs/tailwindcss/releases (v4.1.11). `Server/Dockerfile.source`
regenerates the file during the image build.

## Conventions

- Mobile first: base classes target a phone, add `sm:` / `md:` / `lg:` for larger screens.
  Touch targets are at least 44px (`min-h-11`).
- Use the semantic colors (`bg-surface`, `bg-surface-raised`, `bg-surface-sunken`, `text-fg`,
  `text-muted`, `border-border`, `bg-brand`, `text-danger`) so light and dark themes both work.
  Dark mode is the `dark` class on `<html>`, set from the user's theme.
- Use the `sr-*` classes for buttons, form fields, cards, badges, alerts, tables and dialogs.
