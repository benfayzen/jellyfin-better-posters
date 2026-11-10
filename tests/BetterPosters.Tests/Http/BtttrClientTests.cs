using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Jellyfin.Plugin.BetterPosters.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Http;

public sealed class BtttrClientTests
{
    [Fact]
    public async Task FetchValidatedPosterAsync_ValidWebp200_ReturnsSuccess()
    {
        var handler = new StubHttpMessageHandler(_ => CreateImageResponse(CreateSyntheticWebp(64), "image/webp"));
        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/webp", result.MimeType);
        Assert.Equal("ok", result.ReasonCode);
        Assert.Equal(64, result.ImageBytes.Length);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_404NotFound_DoesNotRetryAndReturnsNotFound()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler, maxRetries: 2);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt9999999.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.ReasonCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_429Then200_RetriesRespectingRetryAfterAndSucceeds()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                return rateLimited;
            }

            return CreateImageResponse(CreateSyntheticWebp(64), "image/webp");
        });

        var client = CreateClient(handler, maxRetries: 2);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_Persistent429_ReturnsRateLimitedAfterMaxRetries()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = CreateClient(handler, maxRetries: 2);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("rate_limited", result.ReasonCode);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_503Then200_RetriesAndSucceeds()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : CreateImageResponse(CreateSyntheticWebp(64), "image/webp");
        });

        var client = CreateClient(handler, maxRetries: 2);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_Persistent500_ReturnsUpstreamError()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler, maxRetries: 1);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("upstream_error", result.ReasonCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_RequestTimeout_ReturnsTimeoutWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return CreateImageResponse(CreateSyntheticWebp(64), "image/webp");
        });

        var client = CreateClient(handler, maxRetries: 0, requestTimeout: TimeSpan.FromMilliseconds(50));

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("timeout", result.ReasonCode);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_CallerCancellation_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new StubHttpMessageHandler(_ => CreateImageResponse(CreateSyntheticWebp(64), "image/webp"));
        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.FetchValidatedPosterAsync(
                new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
                cts.Token));
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_UntrustedInitialHost_RejectsWithoutCallingNetwork()
    {
        var handler = new StubHttpMessageHandler(_ => CreateImageResponse(CreateSyntheticWebp(64), "image/webp"));
        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://evil.example.com/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("untrusted_host", result.ReasonCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_RedirectToExternalHost_RejectsRedirect()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example.com/poster.webp");
            return redirect;
        });

        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("redirect_rejected", result.ReasonCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_RedirectWithNoArtFallbackHeader_ReturnsNotFound()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://images.metahub.space/poster/medium/tt999999999/img");
            redirect.Headers.TryAddWithoutValidation("x-bp-fallback", "noart");
            return redirect;
        });

        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt999999999.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.ReasonCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_RedirectToHttpDowngrade_RejectsRedirect()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            redirect.Headers.Location = new Uri("http://btttr.cc/poster/imdb/poster-default/tt1375666.jpg");
            return redirect;
        });

        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("redirect_rejected", result.ReasonCode);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_SameHostHttpsRedirect_FollowsAndSucceeds()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith(".jpg", StringComparison.Ordinal))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirect.Headers.Location = new Uri("/poster/movie/tt1375666/auto~gr.png", UriKind.Relative);
                return redirect;
            }

            return CreateImageResponse(CreateSyntheticWebp(64), "image/webp");
        });

        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_OversizedStreamedPayload_RejectsWithPayloadTooLarge()
    {
        var handler = new StubHttpMessageHandler(_ => CreateImageResponse(CreateSyntheticWebp(2048), "image/webp"));
        var client = CreateClient(handler, maxImageBytes: 512);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("payload_too_large", result.ReasonCode);
    }

    [Fact]
    public async Task FetchValidatedPosterAsync_NonImageHtmlContent_RejectsWithInvalidMagicBytes()
    {
        var html = Encoding.UTF8.GetBytes("<html><body>Upstream error page disguised as 200 OK with sufficient length</body></html>");
        var handler = new StubHttpMessageHandler(_ => CreateImageResponse(html, "image/webp"));
        var client = CreateClient(handler);

        var result = await client.FetchValidatedPosterAsync(
            new Uri("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_magic_bytes", result.ReasonCode);
    }

    [Fact]
    public async Task ResolveImdbIdFromTmdbAsync_ValidMetaResponse_ExtractsImdbIdAndCachesResult()
    {
        var json = """{"meta":{"id":"tmdb:27205","type":"movie","name":"Inception","poster":"https://btttr.cc/poster/movie/tt1375666/Oscar%20Nominee.png"}}""";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var client = CreateClient(handler);

        var resolved1 = await client.ResolveImdbIdFromTmdbAsync(SupportedMediaType.Movie, "27205", CancellationToken.None);
        var resolved2 = await client.ResolveImdbIdFromTmdbAsync(SupportedMediaType.Movie, "27205", CancellationToken.None);

        Assert.Equal("tt1375666", resolved1);
        Assert.Equal("tt1375666", resolved2);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveImdbIdFromTmdbAsync_NullMetaResponse_ReturnsNull()
    {
        var json = """{"meta":null}""";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var client = CreateClient(handler);

        var resolved = await client.ResolveImdbIdFromTmdbAsync(SupportedMediaType.Movie, "99999999", CancellationToken.None);

        Assert.Null(resolved);
    }

    private static BtttrClient CreateClient(
        StubHttpMessageHandler handler,
        int maxRetries = 0,
        TimeSpan? requestTimeout = null,
        int maxImageBytes = ImageContentValidator.DefaultMaxImageBytes)
    {
        var factory = new StubHttpClientFactory(handler);
        var config = new PluginConfiguration
        {
            MaxRetryAttempts = maxRetries,
            RequestTimeoutSeconds = 15
        };

        return new BtttrClient(
            factory,
            () => config,
            NullLogger<BtttrClient>.Instance,
            requestTimeoutOverride: requestTimeout,
            retryBaseDelayOverride: TimeSpan.FromMilliseconds(5),
            maxImageBytesOverride: maxImageBytes);
    }

    private static HttpResponseMessage CreateImageResponse(byte[] bytes, string mediaType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        };
    }

    private static byte[] CreateSyntheticWebp(int length)
    {
        var bytes = new byte[Math.Max(length, 32)];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        return bytes;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        private int _callCount;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncResponder)
        {
            _responder = (req, _) => Task.FromResult(syncResponder(req));
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncResponder)
        {
            _responder = asyncResponder;
        }

        public int CallCount => _callCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var response = await _responder(request, cancellationToken).ConfigureAwait(false);
            response.RequestMessage ??= request;
            return response;
        }
    }
}
