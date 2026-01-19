# Frontend Development (React/TypeScript)

## Tech Stack

- React 19+ with React Compiler (babel-plugin-react-compiler)
- TypeScript strict mode
- Vite with `@/` path alias mapping to `./src/`
- shadcn/ui + Radix UI primitives
- Tailwind CSS v4 with CSS variables
- Zustand for state management
- react-i18next for i18n
- Prettier (no semicolons) + ESLint

## File Organization

```
src/
├── components/ui/       # shadcn/ui primitives
├── components/icons/    # Custom SVG icons
├── components/modals/   # Dialog/modal components
├── pages/               # Route page components
├── stores/              # Zustand stores (use[Name]Store)
├── lib/hooks/           # Custom hooks (use[Feature])
├── lib/utils.ts         # Utilities (cn, etc.)
├── types/               # TypeScript definitions
```

## Component Patterns

- Functional components with hooks only
- Use `cn()` from `@/lib/utils` for conditional classes
- Use `cva` for component variants
- Accept `className` prop and merge with `cn()`
- Add `data-testid` attributes for Playwright
- Use `@tabler/icons-react` for icons

## State Management

```typescript
interface StoreState { ... }
interface StoreActions { ... }
export const useStore = create<StoreState & StoreActions>((set) => ({ ... }))
```

- Zustand for global state, `useState` for local only
- Create selector hooks for commonly accessed slices

## Styling

- Tailwind CSS v4, mobile-first responsive
- Use `cn()` with `tailwind-merge` for class conflicts
- Prettier auto-sorts Tailwind classes

## Data & Messaging

- `useAppMessage` hook for WebView message handling
- `publishOnMessageExchange()` to send to backend
- Types in `types/messages.d.ts` and `types/commands.d.ts`

## i18n

- Use `useTranslation()` hook, `t()` for all user strings
- Keys: `"Namespace.Section.Key"`
- Files: `public/locales/{lang}/translation.json`

## Code Style

- No semicolons
- Arrow functions: `const Component = () => { ... }`
- Destructure props in function signature
- React Compiler handles memoization - avoid manual `useMemo`/`useCallback`

## Playwright Testing

### Locators & Assertions
- Use role-based locators: `getByRole`, `getByLabel`, `getByText`
- Use `test.step()` for grouping interactions
- Use auto-retrying assertions: `await expect(locator).toHaveText()`
- Rely on Playwright's auto-waiting, avoid hard-coded waits

### Structure
```typescript
import { test, expect } from "./fixtures"

test.describe("Feature tests", () => {
  test("should do something", async ({ page }) => {
    // test implementation
  })
})
```

### Organization
- Tests in `tests/` directory
- Page objects in `tests/fixtures/`
- Test data in `tests/data/`
- Naming: `<feature>.spec.ts`

### Assertions
- `toMatchAriaSnapshot` - accessibility tree structure
- `toHaveCount` - element counts
- `toHaveText` / `toContainText` - text content
- `toHaveURL` - navigation verification
