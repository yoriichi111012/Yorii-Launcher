# YoriiSkins CustomSkinLoader Patch

This patch modifies the CustomSkinLoader mod to prioritize preloaded YoriiSkins
skins for instant loading while maintaining full compatibility with all other
skin/cape providers as fallbacks.

## Problem

The stock CustomSkinLoader architecture has a binary loading mode:
- `forceLoadAllTextures: true` → waits for ALL sources (slow, dead sources block everything)
- `forceLoadAllTextures: false` → breaks after first source (fast but incomplete)

There's no way to get "instant skin from YoriiSkins + capes from other providers".

## Solution

Add a **preload fast-path** that checks for preloaded YoriiSkins skins BEFORE
the loadlist loop. If a preloaded skin exists in `LocalSkin/`, return immediately.
If not, fall through to the normal loadlist (all providers still work).

## Changes Made

### 1. `Common/src/main/java/customskinloader/CustomSkinLoader.java`

#### Added import (line 3):
```java
import java.io.File;
```

#### Added preload fast-path in `loadProfile0()` (after line 97, before loadlist check):
```java
// --- YORII SKINLOADER EDIT: fast path for preloaded local skin ---
// If the launcher has pre-fetched the skin into LocalSkin before the
// game started, return it immediately — zero delay, no placeholder
// flash.  This bypasses the entire loadlist when YoriiSkins has a
// ready-to-use skin on disk.
UserProfile preload = getPreloadProfile(gameProfile);
if (preload != null) {
    logger.info("Preloaded YoriiSkins skin found, returning immediately.");
    if (!config.enableCape) {
        preload.capeUrl = null;
    }
    profileCache.updateCache(credential, preload);
    profileCache.setLoading(credential, false);
    logger.info(preload.toString(profileCache.getExpiry(credential)));
    return preload;
}
// --- END YORII SKINLOADER EDIT ---
```

#### Added `getPreloadProfile()` helper method (after `loadProfile0()`):
```java
// --- YORII SKINLOADER EDIT: check for preloaded YoriiSkins skin ---
// If the launcher has pre-fetched the skin into LocalSkin/skins/<user>.png
// before the game started, build a UserProfile from it and return it
// immediately — zero delay, no placeholder flash.
private static UserProfile getPreloadProfile(GameProfile gameProfile) {
    String username = TextureUtil.AuthlibField.GAME_PROFILE_NAME.get(gameProfile);
    if (username == null || username.isEmpty()) return null;

    // Check for preloaded skin in LocalSkin
    String skinPath = "LocalSkin/skins/" + username + ".png";
    File skinFile = new File(DATA_DIR, skinPath);
    if (!skinFile.exists() || !skinFile.isFile()) return null;

    // Also check for preloaded cape in LocalSkin
    String capePath = "LocalSkin/capes/" + username + ".png";
    File capeFile = new File(DATA_DIR, capePath);

    UserProfile profile = new UserProfile();
    // Use the same URL format as LegacyLoader for local skins
    String skinHash = HttpTextureUtil.getHash(skinPath, skinFile.length(), skinFile.lastModified());
    profile.skinUrl = HttpTextureUtil.getLocalLegacyFakeUrl(skinPath, skinHash);
    profile.model = "auto";
    if (capeFile.exists() && capeFile.isFile()) {
        String capeHash = HttpTextureUtil.getHash(capePath, capeFile.length(), capeFile.lastModified());
        profile.capeUrl = HttpTextureUtil.getLocalLegacyFakeUrl(capePath, capeHash);
    }
    return profile;
}
// --- END YORII SKINLOADER EDIT ---
```

## How to Re-apply This Patch to a New CustomSkinLoader Version

1. Download the new CustomSkinLoader source
2. Open `Common/src/main/java/customskinloader/CustomSkinLoader.java`
3. Add `import java.io.File;` to the imports
4. In `loadProfile0()`, insert the preload check AFTER `logger.info("Loading " + username + "'s profile.");` and BEFORE the loadlist null check
5. Add the `getPreloadProfile()` method after `loadProfile0()` ends
6. Build the mod

## Launcher-Side Requirements

The launcher must:
1. Pre-fetch the YoriiSkins skin PNG → write to `<gameDir>/CustomSkinLoader/LocalSkin/skins/<username>.png`
2. Pre-fetch the YoriiSkins cape PNG (optional) → write to `<gameDir>/CustomSkinLoader/LocalSkin/capes/<username>.png`
3. Set `forceLoadAllTextures: false` in the CSL config (so the loop breaks early if preload fails)
4. Keep LocalSkin first in the loadlist (so preload takes priority)
5. Keep YoriiSkins second in the loadlist (fallback if no preload)
6. All other providers remain in the loadlist as additional fallbacks

## Config Changes

```json
{
  "forceLoadAllTextures": false,
  "enableLocalProfileCache": true,
  "enableCape": true,
  "loadlist": [
    { "name": "LocalSkin", "type": "Legacy", ... },
    { "name": "YoriiSkins", "type": "CustomSkinAPI", "root": "https://yorii-worker.yoriiskin.workers.dev/csl/" },
    "... other providers ..."
  ]
}
```

## Result

- **YoriiSkins user**: Skin loads instantly from preloaded file (0ms delay, no placeholder flash)
- **Non-YoriiSkins players**: Falls through to normal loadlist (YoriiSkins API → Mojang → BlessingSkin → Cosmetica → etc.)
- **All providers still work**: The loadlist is only bypassed when a preloaded skin exists
- **Capes still work**: Preloaded capes in `LocalSkin/capes/` are included in the preload profile
