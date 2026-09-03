using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MCServerLauncher.WPF.Features.Plugins;

/// <summary>A locally trusted signing root for signed .mpx packages.</summary>
public sealed record PluginTrustRoot(
    string Publisher,
    string KeyId,
    byte[] PublicKeySubjectPublicKeyInfo,
    DateTimeOffset AddedAt)
{
    public string PublicKeySha256 => Sha256Hex(PublicKeySubjectPublicKeyInfo);

    public static string Sha256Hex(byte[] bytes)
    {
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(digest);
    }
}

/// <summary>
/// Persists the locally trusted publisher keys. Mirrors the Web client's
/// trust-root store: signed packages are only admitted when their signing
/// key matches one of these roots.
/// </summary>
public sealed class PluginTrustRootStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<PluginTrustRoot> _roots;

    public PluginTrustRootStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultPath;
        _roots = Load();
    }

    public static string DefaultPath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data",
        "Plugins",
        "TrustRoots.json");

    public IReadOnlyList<PluginTrustRoot> List() => _roots;

    public IReadOnlyList<PluginMpxTrustedPublisher> ToValidatorOptions() =>
        _roots
            .Select(root => new PluginMpxTrustedPublisher(root.Publisher, root.KeyId, root.PublicKeySubjectPublicKeyInfo))
            .ToArray();

    public bool IsTrusted(string publisher, string publicKeySha256) =>
        _roots.Any(root =>
            StringComparer.Ordinal.Equals(root.Publisher, publisher) &&
            StringComparer.Ordinal.Equals(root.PublicKeySha256, publicKeySha256));

    public IReadOnlyList<PluginTrustRoot> Add(string publisher, string keyId, byte[] spki)
    {
        lock (_lock)
        {
            var next = _roots
                .Where(root =>
                    !StringComparer.Ordinal.Equals(root.Publisher, publisher) ||
                    !Convert.ToBase64String(root.PublicKeySubjectPublicKeyInfo).Equals(Convert.ToBase64String(spki), StringComparison.Ordinal))
                .Append(new PluginTrustRoot(publisher.Trim(), keyId.Trim(), spki, DateTimeOffset.UtcNow))
                .ToList();
            _roots = next;
            Save();
            return next;
        }
    }

    public IReadOnlyList<PluginTrustRoot> Remove(string publisher, string publicKeySha256)
    {
        lock (_lock)
        {
            _roots = _roots
                .Where(root =>
                    !StringComparer.Ordinal.Equals(root.Publisher, publisher) ||
                    !StringComparer.Ordinal.Equals(root.PublicKeySha256, publicKeySha256))
                .ToList();
            Save();
            return _roots;
        }
    }

    private List<PluginTrustRoot> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return [];
            using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            var roots = new List<PluginTrustRoot>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var publisher = element.TryGetProperty("publisher", out var publisherElement) ? publisherElement.GetString() : null;
                var keyId = element.TryGetProperty("keyId", out var keyIdElement) ? keyIdElement.GetString() : null;
                var spkiBase64 = element.TryGetProperty("publicKeySpkiBase64", out var spkiElement) ? spkiElement.GetString() : null;
                var addedAt = element.TryGetProperty("addedAt", out var addedAtElement) ? addedAtElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(publisher) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(spkiBase64))
                    continue;
                byte[] spki;
                try
                {
                    spki = Convert.FromBase64String(spkiBase64);
                }
                catch (FormatException)
                {
                    continue;
                }
                roots.Add(new PluginTrustRoot(
                    publisher,
                    keyId,
                    spki,
                    DateTimeOffset.TryParse(addedAt, out var parsed) ? parsed : DateTimeOffset.UtcNow));
            }
            return roots;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(_roots.Select(root => new
            {
                publisher = root.Publisher,
                keyId = root.KeyId,
                publicKeySpkiBase64 = Convert.ToBase64String(root.PublicKeySubjectPublicKeyInfo),
                addedAt = root.AddedAt.ToString("O"),
            }));
            File.WriteAllText(_filePath, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // non-fatal
        }
    }
}
