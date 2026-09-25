/*
 * Copyright (C) 2020  Haowei Wen <yushijinhun@gmail.com> and contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */
package moe.yushi.authlibinjector.httpd;

import static java.nio.charset.StandardCharsets.ISO_8859_1;
import static java.util.Optional.empty;
import static java.util.Optional.of;
import static java.util.Optional.ofNullable;
import static moe.yushi.authlibinjector.util.IOUtils.asString;
import static moe.yushi.authlibinjector.util.IOUtils.http;
import static moe.yushi.authlibinjector.util.IOUtils.newUncheckedIOException;
import static moe.yushi.authlibinjector.util.JsonUtils.asJsonObject;
import static moe.yushi.authlibinjector.util.JsonUtils.parseJson;
import static moe.yushi.authlibinjector.util.Logging.log;
import static moe.yushi.authlibinjector.util.Logging.Level.DEBUG;
import static moe.yushi.authlibinjector.util.Logging.Level.INFO;
import static moe.yushi.authlibinjector.util.Logging.Level.WARNING;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.util.Base64;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Optional;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import moe.yushi.authlibinjector.internal.fi.iki.elonen.IHTTPSession;
import moe.yushi.authlibinjector.internal.fi.iki.elonen.Response;
import moe.yushi.authlibinjector.internal.fi.iki.elonen.Status;
import moe.yushi.authlibinjector.internal.org.json.simple.JSONObject;
import moe.yushi.authlibinjector.util.JsonUtils;
import moe.yushi.authlibinjector.yggdrasil.YggdrasilClient;

public class LegacySkinAPIFilter implements URLFilter {

	private static final Pattern PATH_SKINS = Pattern.compile("^/MinecraftSkins/(?<username>[^/]+)\\.png$");

	private static class SkinEntry {
		String uuid;
		String skinUrl;
	}

	private YggdrasilClient upstream;
	private /* nullable */ String githubSkinIndexUrl;
	private volatile Map<String, SkinEntry> skinIndexCache;

	public LegacySkinAPIFilter(YggdrasilClient upstream, String githubSkinIndexUrl) {
		this.upstream = upstream;
		this.githubSkinIndexUrl = githubSkinIndexUrl;
	}

	@Override
	public boolean canHandle(String domain) {
		return domain.equals("skins.minecraft.net");
	}

	@Override
	public Optional<Response> handle(URLProcessor urlProcessor, String domain, String path, IHTTPSession session) {
		if (!domain.equals("skins.minecraft.net"))
			return empty();
		Matcher matcher = PATH_SKINS.matcher(path);
		if (!matcher.find())
			return empty();
		String username = matcher.group("username");

		username = correctEncoding(username);

		Optional<String> skinUrl;
		try {
			skinUrl = resolveSkinUrl(username);
		} catch (UncheckedIOException e) {
			throw newUncheckedIOException("Failed to fetch skin metadata for " + username, e);
		}

		if (skinUrl.isPresent()) {
			String url = skinUrl.get();
			log(DEBUG, "Retrieving skin for " + username + " from " + url);
			byte[] data;
			try {
				data = http("GET", url);
			} catch (IOException e) {
				throw newUncheckedIOException("Failed to retrieve skin from " + url, e);
			}
			log(INFO, "Retrieved skin for " + username + " from " + url + ", " + data.length + " bytes");
			return of(Response.newFixedLength(Status.OK, "image/png", new ByteArrayInputStream(data), data.length));

		} else {
			log(INFO, "No skin is found for " + username);
			return of(Response.newFixedLength(Status.NOT_FOUND, null, null));
		}
	}

	private Optional<String> resolveSkinUrl(String username) {
		if (githubSkinIndexUrl != null) {
			SkinEntry entry = getSkinIndex().get(username.toLowerCase());
			if (entry != null && entry.skinUrl != null) {
				log(DEBUG, "Resolved skin for " + username + " from GitHub index");
				return Optional.of(entry.skinUrl);
			}
		}

		if (upstream == null) {
			return Optional.empty();
		}

		return upstream.queryUUID(username)
				.flatMap(uuid -> upstream.queryProfile(uuid, false))
				.flatMap(profile -> Optional.ofNullable(profile.properties.get("textures")))
				.map(property -> asString(Base64.getDecoder().decode(property.value)))
				.flatMap(texturesPayload -> obtainTextureUrl(texturesPayload, "SKIN"));
	}

	private Map<String, SkinEntry> getSkinIndex() {
		if (skinIndexCache != null) {
			return skinIndexCache;
		}
		synchronized (this) {
			if (skinIndexCache != null) {
				return skinIndexCache;
			}
			log(INFO, "Loading GitHub skin index from " + githubSkinIndexUrl);
			Map<String, SkinEntry> index = loadSkinIndex();
			skinIndexCache = index;
			log(INFO, "Loaded " + index.size() + " skin entries from GitHub index");
			return skinIndexCache;
		}
	}

	private Map<String, SkinEntry> loadSkinIndex() {
		try {
			String jsonText = asString(http("GET", githubSkinIndexUrl));
			JSONObject root = asJsonObject(parseJson(jsonText));
			Map<String, SkinEntry> index = new LinkedHashMap<>();

			JSONObject players = asJsonObject(root.get("players"));
			if (players != null) {
				for (Object key : players.keySet()) {
					String username = (String) key;
					JSONObject data = asJsonObject(players.get(username));
					if (data != null) {
						// only public profiles are served through the legacy
						// skins.minecraft.net API; private ones resolve via the
						// upstream Yggdrasil path (which proxies them)
						Object kind = data.get("kind");
						if (kind != null && !"public".equals(kind)) {
							continue;
						}
						SkinEntry entry = new SkinEntry();
						entry.uuid = (String) data.get("uuid");
						Object skinUrl = data.get("skinUrl");
						if (skinUrl != null) {
							entry.skinUrl = (String) skinUrl;
						}
						index.put(username.toLowerCase(), entry);
					}
				}
			}
			return index;
		} catch (IOException e) {
			log(WARNING, "Failed to load GitHub skin index: " + e);
			return Collections.emptyMap();
		}
	}

	public void invalidateCache() {
		skinIndexCache = null;
	}

	private Optional<String> obtainTextureUrl(String texturesPayload, String textureType) throws UncheckedIOException {
		JSONObject payload = asJsonObject(parseJson(texturesPayload));
		JSONObject textures = asJsonObject(payload.get("textures"));

		return ofNullable(textures.get(textureType))
				.map(JsonUtils::asJsonObject)
				.map(it -> ofNullable(it.get("url"))
						.map(JsonUtils::asJsonString)
						.orElseThrow(() -> newUncheckedIOException("Invalid JSON: Missing texture url")));
	}

	private static String correctEncoding(String grable) {
		return new String(grable.getBytes(ISO_8859_1));
	}
}
