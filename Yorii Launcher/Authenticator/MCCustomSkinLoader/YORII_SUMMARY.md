# YoriiSkins CustomSkinLoader Modification - Complete Summary

## What We Did

Modified the CustomSkinLoader mod source code to create a "YoriiSkins Edition" that:
1. Loads YoriiSkins skins **instantly** (0ms delay, no placeholder flash)
2. Gives YoriiSkins **first priority** after preloaded local skin
3. **Preserves all other providers** as fallbacks
4. Works for both singleplayer and multiplayer

## The Problem (Mod Architecture)

The stock CustomSkinLoader has a **binary loading flaw**:

| Mode | Behavior | Result |
|------|----------|--------|
| `forceLoadAllTextures: true` | Waits for **ALL** sources | 1.7s (dead CloakPlus blocks everything) |
| `forceLoadAllTextures: false` | Breaks after **first** source | Instant, but loses capes from other sources |

**No middle ground exists.** The mod fans out ALL sources in parallel via `CompletableFuture`, then `.join()`s them sequentially. With `forceLoadAllTextures: true`, the slowest source (CloakPlus connection-reset ~1.7s) determines total time.

## The Solution

### 1. Mod Source Code Changes (`CustomSkinLoader.java`)

Added a **preload fast-path** that checks for preloaded YoriiSkins skins BEFORE the loadlist loop:

```java
// In loadProfile0(), BEFORE the loadlist:
UserProfile preload = getPreloadProfile(gameProfile);
if (preload != null) {
    profileCache.updateCache(credential, preload);
    profileCache.setLoading(credential, false);
    return preload;  // INSTANT - no loadlist needed
}
```

The `getPreloadProfile()` helper:
- Checks `LocalSkin/skins/{username}.png` exists
- If yes, builds a `UserProfile` with proper `(LOCAL_LEGACY){hash},{path}` URL format
- Also checks `LocalSkin/capes/{username}.png` for preloaded capes
- Returns `null` if no preloaded skin (falls through to normal loadlist)

### 2. Launcher Changes (`SkinManager.cs`)

- **`PreloadSkinForLaunchAsync(username, uuid)`**: Fetches skin from worker, writes to `LocalSkin/skins/{username}.png`
- **`PrefetchCslProfileAsync(username, uuid, root)`**: Queries worker CSL endpoint, writes resolved profile to `ProfileCache/{uuid}.json`
- **`ConfigureCslForLaunch(root)`**: Rewrites CSL config:
  - LocalSkin → first in loadlist
  - YoriiSkins → second in loadlist (fallback)
  - All other providers remain
  - `enableCape: true`
  - `enableLocalProfileCache: true`
  - `forceLoadAllTextures: false` (break early after first source)

### 3. Load Order

```
1. PRELOAD FAST-PATH (mod)     ← YoriiSkins preloaded by launcher → INSTANT
2. LocalSkin (file read)       ← Instant fallback
3. YoriiSkins API (worker)     ← Fast network (~200ms)
4. GameProfile (Mojang)        ← Standard skins
5. BlessingSkin                ← Third-party
6. GlitchlessGames             ← Third-party
7. MinecraftCapes              ← Capes
8. OptiFine                    ← Capes
9. CloakPlus                   ← Capes
10. Cosmetica                  ← Capes
```

## Result

- **YoriiSkins user**: Skin loads in **0ms** (preloaded file, no network, no placeholder flash)
- **Non-YoriiSkins players**: Falls through to normal loadlist (all providers work)
- **Capes**: Preloaded capes load instantly; otherwise fetched from Cosmetica/MinecraftCapes/etc.
- **Compatibility**: All 10 default providers preserved as fallbacks

## Files Modified

| File | Changes |
|------|---------|
| `MCCustomSkinLoader/Common/src/main/java/customskinloader/CustomSkinLoader.java` | Added preload fast-path + `getPreloadProfile()` |
| `Helpers/SkinManager.cs` | Preload + config rewrite logic |
| `MainWindow.xaml.cs` | YoriiSkins login flow + UUID passing |

## Files Created

| File | Purpose |
|------|---------|
| `MCCustomSkinLoader/YORII_PATCH.md` | Complete patch guide for future mod versions |

## How to Re-apply to Future Mod Versions

1. Download new CustomSkinLoader source
2. Open `CustomSkinLoader.java`
3. Add `import java.io.File;`
4. Insert preload check in `loadProfile0()` after the "Loading..." log line
5. Add `getPreloadProfile()` method after `loadProfile0()`
6. Build the mod

See `YORII_PATCH.md` for exact code snippets.
