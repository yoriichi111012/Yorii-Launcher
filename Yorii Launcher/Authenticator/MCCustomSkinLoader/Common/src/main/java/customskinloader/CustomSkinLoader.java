package customskinloader;

import java.io.File;
import java.util.LinkedList;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;

import com.google.common.collect.ImmutableMap;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.mojang.authlib.GameProfile;
import com.mojang.authlib.minecraft.MinecraftProfileTexture;
import customskinloader.config.Config;
import customskinloader.config.SkinSiteProfile;
import customskinloader.loader.GameProfileLoader;
import customskinloader.loader.ProfileLoader;
import customskinloader.log.LogManager;
import customskinloader.log.Logger;
import customskinloader.profile.ModelManager0;
import customskinloader.profile.ProfileCache;
import customskinloader.profile.UserProfile;
import customskinloader.utils.MinecraftUtil;
import customskinloader.utils.TextureUtil;
import customskinloader.utils.ThreadPoolFactory;
import customskinloader.utils.HttpTextureUtil;

/**
 * Custom skin loader mod for Minecraft.
 *
 * @author Jeremy Lam [JLChnToZ] 2013-2014 & Alexander Xia [xfl03] 2014-2023
 * @version @MOD_FULL_VERSION@
 */
public class CustomSkinLoader {
    public static final String CustomSkinLoader_VERSION = "@MOD_VERSION@";
    public static final String CustomSkinLoader_FULL_VERSION = "@MOD_FULL_VERSION@";
    public static final int CustomSkinLoader_BUILD_NUMBER = Integer.parseInt("@MOD_BUILD_NUMBER@");

    public static final File
            DATA_DIR = new File(MinecraftUtil.getMinecraftDataDir(), "CustomSkinLoader"),
            LOG_FILE = new File(DATA_DIR, "CustomSkinLoader.log"),
            CONFIG_FILE = new File(DATA_DIR, "CustomSkinLoader.json");

    public static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    public static final Logger logger = initLogger();
    public static final Config config = initConfig();

    private static final ProfileCache profileCache = new ProfileCache();

    public static final ExecutorService THREAD_POOL = ThreadPoolFactory.create(config.threadPoolSize, false);
    public static final ExecutorService PROFILE_THREAD_POOL = ThreadPoolFactory.create(config.loadlist.size() * config.threadPoolSize, true);

    public static void loadProfileTextures(Runnable runnable) {
        THREAD_POOL.execute(runnable);
    }

    //For User Skin
    public static UserProfile loadProfile(GameProfile gameProfile) {
        String username = TextureUtil.AuthlibField.GAME_PROFILE_NAME.get(gameProfile);
        String credential = MinecraftUtil.getCredential(gameProfile);

        String tempName = Thread.currentThread().getName();
        Thread.currentThread().setName(username + " <" + TextureUtil.AuthlibField.GAME_PROFILE_ID.get(gameProfile) + ">"); // Change Thread Name

        // Fix: http://hopper.minecraft.net/crashes/minecraft/MCX-2773713
        if (username == null || username.isEmpty() || username.equals(" ")) {
            return ModelManager0.toUserProfile(GameProfileLoader.getTextures(TextureUtil.AuthlibField.GAME_PROFILE_PROPERTIES.get(gameProfile)));
        }

        UserProfile profile;
        if (profileCache.isReady(credential)) {
            logger.info("Cached profile will be used.");
            profile = profileCache.getProfile(credential);
            if (profile == null) {
                logger.warning("(Cached Profile is empty!) Expiry: " + profileCache.getExpiry(credential));
                if (profileCache.isExpired(credential)) { // force load
                    profile = loadProfile0(gameProfile, false);
                }
            } else {
                logger.info(profile.toString(profileCache.getExpiry(credential)));
            }
        } else {
            profileCache.setLoading(credential, true);
            profile = loadProfile0(gameProfile, false);
        }
        Thread.currentThread().setName(tempName);
        return profile == null ? new UserProfile() : profile;
    }

    //Core
    public static UserProfile loadProfile0(GameProfile gameProfile, boolean isSkull) {
        String username = TextureUtil.AuthlibField.GAME_PROFILE_NAME.get(gameProfile);
        String credential = MinecraftUtil.getCredential(gameProfile);

        profileCache.setLoading(credential, true);
        long time = System.currentTimeMillis();
        logger.info("Loading " + username + "'s profile.");

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

        if (config.loadlist == null || config.loadlist.isEmpty()) {
            logger.info("LoadList is Empty.");
            return null;
        }

        int size = config.loadlist.size();
        LinkedList<CompletableFuture<UserProfile>> profileGetters = new LinkedList<>();
        UserProfile profile0 = new UserProfile();
        for (int i = 0; i < size; i++) {
            SkinSiteProfile ssp = config.loadlist.get(i);
            logger.info((i + 1) + "/" + size + " Try to load profile from '" + ssp.name + "'.");
            if (ssp.type == null) {
                logger.info("The type of '" + ssp.name + "' is null.");
                continue;
            }
            ProfileLoader.IProfileLoader loader = ProfileLoader.LOADERS.get(ssp.type.toLowerCase());
            if (loader == null) {
                logger.info("Type '" + ssp.type + "' is not defined.");
                continue;
            }
            profileGetters.add(CompletableFuture.supplyAsync(() -> {
                String tempName = Thread.currentThread().getName();
                Thread.currentThread().setName(username + " <" + TextureUtil.AuthlibField.GAME_PROFILE_ID.get(gameProfile) + "> (" + ssp.name + ")"); // Change Thread Name
                UserProfile profile = null;
                try {
                    profile = loader.loadProfile(ssp, gameProfile);
                } catch (Exception e) {
                    logger.warning("Exception occurs while loading.");
                    logger.warning(e);
                    if (e.getCause() != null) {
                        logger.warning("Caused By:");
                        logger.warning(e.getCause());
                    }
                }
                Thread.currentThread().setName(tempName);
                return profile;
            }, PROFILE_THREAD_POOL));
        }

        for (CompletableFuture<UserProfile> profileGetter : profileGetters) {
            UserProfile profile = profileGetter.join();
            if (profile == null) {
                continue;
            }
            profile0.mix(profile);
            if (isSkull && !profile0.hasSkinUrl()) {
                continue;
            }
            if (!config.forceLoadAllTextures) {
                break;
            }
            if (profile0.isFull()) {
                break;
            }
        }

        if (!profile0.isEmpty()) {
            logger.info(username + "'s profile loaded. (" + (System.currentTimeMillis() - time) + "ms)");
            if (!config.enableCape) {
                profile0.capeUrl = null;
            }
            profileCache.updateCache(credential, profile0);
            profileCache.setLoading(credential, false);
            logger.info(profile0.toString(profileCache.getExpiry(credential)));
            return profile0;
        }
        logger.info(username + "'s profile not found in load list. (" + (System.currentTimeMillis() - time) + "ms)");

        if (config.enableLocalProfileCache) {
            UserProfile profile = profileCache.getLocalProfile(credential);
            if (profile == null || profile.equals(UserProfile.NULL)) {
                logger.info(username + "'s LocalProfile not found.");
            } else {
                profileCache.updateCache(credential, profile, false);
                profileCache.setLoading(credential, false);
                logger.info(username + "'s LocalProfile will be used.");
                logger.info(profile.toString(profileCache.getExpiry(credential)));
                return profile;
            }
        }
        profileCache.setLoading(credential, false);
        return null;
    }

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

    public final static Map<MinecraftProfileTexture.Type, MinecraftProfileTexture> INCOMPLETED = ImmutableMap.of();
    //For Skull
    public static Map<MinecraftProfileTexture.Type, MinecraftProfileTexture> loadProfileFromCache(final GameProfile gameProfile) {
        String username = TextureUtil.AuthlibField.GAME_PROFILE_NAME.get(gameProfile);
        String credential = MinecraftUtil.getCredential(gameProfile);

        //CustomSkinLoader needs username to load standard skin, if username not exist, only textures in NBT can be used
        //Authlib 3.11.50 makes empty username to " "
        if (username == null || username.isEmpty() || username.equals(" ") || credential == null) {
            return GameProfileLoader.getTextures(TextureUtil.AuthlibField.GAME_PROFILE_PROPERTIES.get(gameProfile));
        }
        if (config.forceUpdateSkull ? profileCache.isReady(credential) : profileCache.isExist(credential)) {
            UserProfile profile = profileCache.getProfile(credential);
            return ModelManager0.fromUserProfile(profile);
        }
        if (!profileCache.isLoading(credential)) {
            profileCache.setLoading(credential, true);
            Runnable loadThread = () -> {
                String tempName = Thread.currentThread().getName();
                Thread.currentThread().setName(username + "'s skull");
                loadProfile0(gameProfile, true);//Load in thread
                Thread.currentThread().setName(tempName);
            };
            if (config.forceUpdateSkull) {
                new Thread(loadThread).start();
            } else {
                THREAD_POOL.execute(loadThread);
            }
        }
        return INCOMPLETED;
    }

    private static Logger initLogger() {
        LogManager.setLogFile(LOG_FILE.toPath());
        Logger logger = LogManager.getLogger("Core");
        logger.info("CustomSkinLoader " + CustomSkinLoader_FULL_VERSION);
        logger.info("DataDir: " + DATA_DIR.getAbsolutePath());
        logger.info("Operating System: " + System.getProperty("os.name") + " (" + System.getProperty("os.arch") + ") version " + System.getProperty("os.version"));
        logger.info("Java Version: " + System.getProperty("java.version") + ", " + System.getProperty("java.vendor"));
        logger.info("Java VM Version: " + System.getProperty("java.vm.name") + " (" + System.getProperty("java.vm.info") + "), " + System.getProperty("java.vm.vendor"));
        logger.info("Minecraft: " + MinecraftUtil.getMinecraftMainVersion());
        return logger;
    }

    /**
     * Create <code>Config</code> object.
     * To use <code>logger</code>, please make sure call this function after <code>initLogger</code>.
     *
     * @return <code>Config</code>
     */
    private static Config initConfig() {
        Config config = Config.loadConfig0();
        logger.enableLogStdOut = config.enableLogStdOut;
        return config;
    }
}
