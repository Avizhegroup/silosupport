using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SiloSupport.Application;
using SiloSupport.Domain.Enums;
using SiloSupport.Infrastructure.Ai;
using SiloSupport.Infrastructure.Bale;
using SiloSupport.Infrastructure.Options;

namespace SiloSupport.Tests;

/// <summary>Adapter tests with stubbed HTTP handlers / fakes: no network access.</summary>
public class BaleBotApiClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            return _respond(request);
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (BaleBotApiClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://tapi.test/bot123456:TOKEN/") };
        var client = new BaleBotApiClient(
            new StubFactory(http),
            Microsoft.Extensions.Options.Options.Create(new BaleBotOptions()),
            NullLogger<BaleBotApiClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task SendMessageAsync_PostsChatIdAndText_AndReturnsTrueOnOk()
    {
        var (client, handler) = Create(_ => Json("{\"ok\":true,\"result\":{\"message_id\":5}}"));

        var sent = await client.SendMessageAsync(-1001234, "hello سلام", CancellationToken.None);

        Assert.True(sent);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/sendMessage", request.Url);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(-1001234, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("hello سلام", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_HttpError_ReturnsFalse()
    {
        var (client, _) = Create(_ => Json("{\"ok\":false,\"error_code\":403}", HttpStatusCode.Forbidden));

        Assert.False(await client.SendMessageAsync(1, "x", CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageAsync_OkFalseInBody_ReturnsFalse()
    {
        var (client, _) = Create(_ => Json("{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request\"}"));

        Assert.False(await client.SendMessageAsync(1, "x", CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageAsync_NetworkException_ReturnsFalse()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("down"));

        Assert.False(await client.SendMessageAsync(1, "x", CancellationToken.None));
    }

    [Fact]
    public async Task GetUpdatesAsync_MapsTextVoiceAndPhotoMessages()
    {
        const string json = """
        {"ok":true,"result":[
          {"update_id":10,"message":{"message_id":1,"from":{"id":4503599627370495,"is_bot":false},"chat":{"id":-77,"type":"group"},"text":"hi"}},
          {"update_id":11,"message":{"message_id":2,"from":{"id":5,"is_bot":false},"chat":{"id":-77,"type":"group"},"voice":{"file_id":"f","file_unique_id":"u"}}},
          {"update_id":12,"message":{"message_id":3,"from":{"id":6,"is_bot":true},"chat":{"id":-77,"type":"group"},"photo":[{"file_id":"p"}]}},
          {"update_id":13}
        ]}
        """;
        var (client, handler) = Create(_ => Json(json));

        var updates = await client.GetUpdatesAsync(10, 30, CancellationToken.None);

        Assert.Contains("getUpdates?offset=10&timeout=30", handler.Requests.Single().Url);
        Assert.Equal(4, updates.Count);

        var text = updates[0].Message!;
        Assert.Equal(-77, text.ChatId);
        Assert.Equal(4503599627370495, text.SenderId);
        Assert.Equal("hi", text.Text);
        Assert.False(text.HasNonTextContent);

        Assert.True(updates[1].Message!.HasNonTextContent);
        Assert.Null(updates[1].Message!.Text);

        Assert.True(updates[2].Message!.HasNonTextContent);
        Assert.True(updates[2].Message!.SenderIsBot);

        Assert.Null(updates[3].Message);
        Assert.Equal(13, updates[3].UpdateId);
    }

    [Fact]
    public async Task GetUpdatesAsync_OkFalse_Throws()
    {
        var (client, _) = Create(_ => Json("{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized\"}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetUpdatesAsync(0, 1, CancellationToken.None));
    }

    [Fact]
    public async Task GetBotUserIdAsync_ReadsGetMeOnceAndCachesIt()
    {
        var (client, handler) = Create(_ => Json("{\"ok\":true,\"result\":{\"id\":424242,\"is_bot\":true}}"));

        Assert.Equal(424242, await client.GetBotUserIdAsync(CancellationToken.None));
        Assert.Equal(424242, await client.GetBotUserIdAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.EndsWith("/getMe", handler.Requests[0].Url);
    }

    [Fact]
    public async Task GetBotUserIdAsync_Failure_ReturnsNull()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("down"));

        Assert.Null(await client.GetBotUserIdAsync(CancellationToken.None));
    }
}

public class SiloAiPriorityClassifierTests
{
    private sealed class FakeAi : ISiloAiClient
    {
        public RagChatResponse? Response { get; set; }

        public Exception? Throw { get; set; }

        public List<(Guid? ConversationId, string Message)> Calls { get; } = new();

        public Task<Guid?> StartNewSessionAsync(RagDocType docType, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<RagChatResponse?> SendAsync(Guid? conversationId, string message, RagDocType docType, CancellationToken cancellationToken)
        {
            Calls.Add((conversationId, message));
            if (Throw is not null)
                throw Throw;

            return Task.FromResult(Response);
        }

        public Task<RagVoiceResponse?> SendVoiceAsync(Guid? conversationId, byte[] audio, string fileName, string contentType, RagDocType docType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private readonly FakeAi _ai = new();

    private SiloAiPriorityClassifier Create() => new(_ai, NullLogger<SiloAiPriorityClassifier>.Instance);

    [Theory]
    [InlineData("{\"priority\":\"Low\",\"analysis\":\"a\"}", GroupPriority.Low)]
    [InlineData("{\"priority\":\"Medium\",\"analysis\":\"a\"}", GroupPriority.Medium)]
    [InlineData("{\"priority\":\"Critical\",\"analysis\":\"a\"}", GroupPriority.Critical)]
    public async Task ClassifyAsync_ValidAnswer_ReturnsThePriorityAndAnalysisReturnedByTheApi(string answer, GroupPriority expected)
    {
        _ai.Response = new RagChatResponse { ResponseText = answer, ConversationId = Guid.NewGuid() };

        var result = await Create().ClassifyAsync("customer text", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Priority);
        Assert.Equal("a", result.Analysis);
    }

    [Fact]
    public async Task ClassifyAsync_SendsTheCustomerTextWithoutAnExistingConversation()
    {
        _ai.Response = new RagChatResponse { ResponseText = "{\"priority\":\"Low\"}" };

        await Create().ClassifyAsync("my special text", CancellationToken.None);

        var call = Assert.Single(_ai.Calls);
        Assert.Null(call.ConversationId);
        Assert.Contains("my special text", call.Message);
    }

    [Theory]
    [InlineData("I think this is high priority")]
    [InlineData("{\"priority\":\"High\"}")]
    [InlineData("")]
    public async Task ClassifyAsync_UnusableAnswer_Fails(string answer)
    {
        _ai.Response = new RagChatResponse { ResponseText = answer };

        var result = await Create().ClassifyAsync("x", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ClassifyAsync_HttpErrorStatus_Fails()
    {
        _ai.Response = new RagChatResponse { StatusCode = HttpStatusCode.InternalServerError };

        Assert.False((await Create().ClassifyAsync("x", CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ClassifyAsync_NullResponse_Fails()
    {
        _ai.Response = null;

        Assert.False((await Create().ClassifyAsync("x", CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ClassifyAsync_ApiThrows_FailsWithoutThrowing()
    {
        _ai.Throw = new HttpRequestException("down");

        Assert.False((await Create().ClassifyAsync("x", CancellationToken.None)).Succeeded);
    }
}
