/*
 * Cloudflare Worker - Yorii Launcher Yggdrasil Server
 * Reads skins from GitHub repos, no database needed
 */

const GITHUB_INDEX_URL = "https://raw.githubusercontent.com/yoriichi111012/yorii-skins-index/main/index.json";
const PUBLIC_KEY = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgK9w0BAQEFAAOCAQ8AMIIBCgK9w0BAQEFAAOCAQ8AMIIBCgK\n-----END PUBLIC KEY-----\n";

let skinCache = null;
let cacheTime = 0;
const CACHE_TTL = 5 * 60 * 1000;

async function getSkinIndex() {
	const now = Date.now();
	if (skinCache && (now - cacheTime) < CACHE_TTL) {
		return skinCache;
	}
	try {
		const resp = await fetch(GITHUB_INDEX_URL);
		if (!resp.ok) throw new Error("Failed to fetch index: " + resp.status);
		const data = await resp.json();
		skinCache = data.players || {};
		cacheTime = now;
		return skinCache;
	} catch (e) {
		console.error("Error fetching skin index:", e);
		return skinCache || {};
	}
}

function uuidWithDashes(uuid) {
	if (uuid.includes("-")) return uuid;
	return uuid.replace(/^(.{8})(.{4})(.{4})(.{4})(.{12})$/, "$1-$2-$3-$4-$5");
}

function uuidWithoutDashes(uuid) {
	return uuid.replace(/-/g, "");
}

function buildProfile(username, entry) {
	const uuid = entry.uuid || "00000000-0000-0000-0000-000000000000";
	const skinUrl = entry.skinUrl || "";
	const timestamp = Date.now();

	const texturesPayload = {
		timestamp: timestamp,
		profileId: uuidWithoutDashes(uuid),
		profileName: username,
		textures: {}
	};

	if (skinUrl) {
		texturesPayload.textures = {
			SKIN: {
				url: skinUrl
			}
		};
	}

	const texturesValue = btoa(JSON.stringify(texturesPayload));

	return {
		id: uuidWithoutDashes(uuid),
		name: username,
		properties: [
			{
				name: "textures",
				value: texturesValue
			}
		]
	};
}

async function handleRequest(request) {
	const url = new URL(request.url);
	const path = url.pathname;

	if (request.method === "GET" && path === "/") {
		return new Response(JSON.stringify({
			signaturePublickey: PUBLIC_KEY,
			skinDomains: [".minecraft.net", ".mojang.com", "raw.githubusercontent.com"],
			meta: {
				feature.legacy_skin_api: false,
				feature.enable_mojang_anti_features: false,
				feature.enable_profile_key: false,
				serverName: "Yorii Launcher"
			}
		}), {
			headers: { "Content-Type": "application/json" }
		});
	}

	if (request.method === "POST" && path === "/api/profiles/minecraft") {
		let usernames = [];
		try {
			usernames = await request.json();
		} catch (e) {
			return new Response("[]", {
				headers: { "Content-Type": "application/json" }
			});
		}
		const players = await getSkinIndex();
		const results = [];
		for (const name of usernames) {
			const entry = players[name];
			if (entry) {
				const uuid = entry.uuid || "00000000-0000-0000-0000-000000000000";
				results.push({
					id: uuidWithoutDashes(uuid),
					name: name
				});
			}
		}
		return new Response(JSON.stringify(results), {
			headers: { "Content-Type": "application/json" }
		});
	}

	const profileMatch = path.match(/^\/sessionserver\/session\/minecraft\/profile\/([0-9a-f]{32})$/);
	if (request.method === "GET" && profileMatch) {
		const uuid = profileMatch[1];
		const players = await getSkinIndex();
		let found = null;
		let foundName = null;
		for (const [name, entry] of Object.entries(players)) {
			if (uuidWithoutDashes(entry.uuid) === uuid) {
				found = entry;
				foundName = name;
				break;
			}
		}
		if (found) {
			const profile = buildProfile(foundName, found);
			const unsigned = url.searchParams.get("unsigned");
			if (unsigned === "false") {
				profile.properties[0].signature = "signature_placeholder";
			}
			return new Response(JSON.stringify(profile), {
				headers: { "Content-Type": "application/json" }
			});
		}
		return new Response("", { status: 204 });
	}

	if (request.method === "GET" && path.startsWith("/MinecraftSkins/")) {
		const match = path.match(/^\/MinecraftSkins\/([^/]+)\.png$/);
		if (match) {
			const username = match[1];
			const players = await getSkinIndex();
			const entry = players[username] || players[username.toLowerCase()];
			if (entry && entry.skinUrl) {
				const skinResp = await fetch(entry.skinUrl);
				if (skinResp.ok) {
					const skinData = await skinResp.arrayBuffer();
					return new Response(skinData, {
						headers: { "Content-Type": "image/png" }
					});
				}
			}
		}
		return new Response("", { status: 404 });
	}

	return new Response("Not Found", { status: 404 });
}

export default {
	async fetch(request) {
		return handleRequest(request);
	}
};
