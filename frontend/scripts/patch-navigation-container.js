/**
 * Patches expo-router's forked NavigationContainer to provide LinkingContext
 * even before linking is fully resolved.
 *
 * Without this patch, NativeStackView calls useLinkBuilder() during the initial
 * render on web, but the NavigationContainer hasn't yet wrapped children in
 * LinkingContext.Provider (it returns a fallback instead). This causes:
 *   "Couldn't find a LinkingContext context."
 *
 * The fix wraps the early-return fallback in LinkingContext.Provider so the
 * context is always available.
 */
const fs = require("fs");
const path = require("path");

const file = path.join(
  __dirname,
  "..",
  "node_modules",
  "expo-router",
  "build",
  "fork",
  "NavigationContainer.js"
);

if (!fs.existsSync(file)) {
  console.log("[patch] NavigationContainer.js not found, skipping");
  process.exit(0);
}

let content = fs.readFileSync(file, "utf8");

const marker = "// PATCHED: LinkingContext fallback";

if (
  content.includes(marker) ||
  content.includes(
    "<native_1.LinkingContext.Provider value={linkingContext}><native_1.ThemeProvider value={theme}>"
  )
) {
  console.log("[patch] NavigationContainer.js already patched");
  process.exit(0);
}

const target =
  "return <native_1.ThemeProvider value={theme}>{fallback}</native_1.ThemeProvider>;";
const replacement =
  `${marker}\n        return <native_1.LinkingContext.Provider value={linkingContext}><native_1.ThemeProvider value={theme}>{fallback}</native_1.ThemeProvider></native_1.LinkingContext.Provider>;`;

if (!content.includes(target)) {
  console.log("[patch] Target string not found — expo-router may have updated");
  process.exit(0);
}

content = content.replace(target, replacement);
fs.writeFileSync(file, content);
console.log("[patch] NavigationContainer.js patched successfully");
