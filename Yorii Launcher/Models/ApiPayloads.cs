using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yorii_Launcher.Models;

// Source-generation DTOs for JSON payloads that were previously anonymous
// types or Dictionary<string, object?>. Both shapes require runtime
// reflection, which breaks trimming (IL2026) and NativeAOT (IL3050).
// Wire names are locked with [JsonPropertyName] so the on-the-wire format
// is byte-identical to what the anonymous types produced.

public sealed record GitHubPutContentPayload(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("sha")] string? Sha);

public sealed record GitHubDeleteContentPayload(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("sha")] string? Sha,
    [property: JsonPropertyName("branch")] string Branch);

public sealed record OAuthCodeRequest(
    [property: JsonPropertyName("code")] string Code);

public sealed record SkinUploadPayload(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("skinBase64")] string SkinBase64,
    [property: JsonPropertyName("kind")] string Kind);

public sealed record CapeUploadPayload(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("capeBase64")] string CapeBase64,
    [property: JsonPropertyName("kind")] string Kind);

public sealed record HeartbeatPayload(
    [property: JsonPropertyName("username")] string Username);

public sealed record SkinProfileSnapshot(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("uuid")] string Uuid,
    [property: JsonPropertyName("skinUrl")] string SkinUrl,
    [property: JsonPropertyName("capeUrl")] string CapeUrl,
    [property: JsonPropertyName("capeVersion")] string CapeVersion,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("lastSeenAt")] long LastSeenAt);

public sealed record SkinIndexSnapshot(
    [property: JsonPropertyName("players")] Dictionary<string, SkinProfileSnapshot> Players);

public sealed record CslProfilePayload(
    [property: JsonPropertyName("skinUrl")] string SkinUrl,
    [property: JsonPropertyName("model")] string Model = "default",
    [property: JsonPropertyName("capeUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CapeUrl = null);
