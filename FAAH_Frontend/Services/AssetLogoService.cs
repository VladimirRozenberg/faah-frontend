using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.Services;

// Service partagé par les pages : une image déjà reçue n'est pas téléchargée à nouveau.
// Les appels sont faits depuis le thread de l'interface, comme les autres ViewModels.
public sealed class AssetLogoService
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloads = new(3); // Au maximum 3 téléchargements simultanés.
    private readonly Dictionary<string, CachedLogo> _cache = new();
    private const int MaxImageBytes = 1024 * 1024; // Même limite de 1 Mo que le backend.

    private record CachedLogo(Task<Bitmap?> Image, DateTimeOffset RetryAfter);

    public AssetLogoService(HttpClient http) => _http = http;

    public async Task LoadAsync(Asset asset, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(asset.LogoUrl)) return;

        // Seule la route de notre backend est acceptée : aucun token envoyé à un autre site.
        string path = $"/api/assets/{Uri.EscapeDataString(asset.Symbol)}/logo";
        if (asset.LogoUrl != path || _http.BaseAddress is null) return;
        var address = new Uri(_http.BaseAddress, path);

        if (!_cache.TryGetValue(path, out var cached)
            || (cached.Image.IsCompletedSuccessfully && cached.Image.Result is null
                && DateTimeOffset.UtcNow >= cached.RetryAfter))
        {
            cached = new CachedLogo(DownloadAsync(address), DateTimeOffset.UtcNow.AddMinutes(2));
            _cache[path] = cached;
        }

        try
        {
            // Plusieurs lignes demandant le même logo partagent la même requête.
            var image = await cached.Image.WaitAsync(cancellationToken);
            if (!cancellationToken.IsCancellationRequested) asset.Logo = image;
        }
        catch (OperationCanceledException) { } // La page a été quittée.
    }

    private async Task<Bitmap?> DownloadAsync(Uri address)
    {
        await _downloads.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes) return null;
            string? mime = response.Content.Headers.ContentType?.MediaType;
            if (mime is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif")) return null;

            using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (data.Length + count > MaxImageBytes) return null;
                data.Write(buffer, 0, count);
            }
            data.Position = 0;
            // 64 pixels suffisent pour ces icônes et limitent la mémoire du cache.
            return Bitmap.DecodeToWidth(data, 64);
        }
        catch (Exception)
        {
            // Logo absent, réseau indisponible ou image illisible : conserver les initiales.
            // Un échec est mis en cache 2 minutes, puis une nouvelle tentative est permise.
            return null;
        }
        finally { _downloads.Release(); }
    }
}
