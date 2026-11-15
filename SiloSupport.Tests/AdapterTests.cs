using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SiloSupport.Application;
using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Enums;

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
          {"update_id":10,"message":{"message_id":1,"from":{"id":4503599627370495,"is_bot":false,"username":"ali"},"chat":{"id":-77,"type":"group"},"text":"hi"}},
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
        Assert.Equal("ali", text.SenderUsername);
        Assert.Equal("hi", text.Text);
        Assert.False(text.HasNonTextContent);

        Assert.Null(updates[1].Message!.SenderUsername);
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

public class BaleBotApiClientMediaTests
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

    private static (BaleBotApiClient Client, StubHandler Handler, ListLogger Logger) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://tapi.test/bot123456:TOKEN/") };
        var logger = new ListLogger();
        var options = new BaleBotOptions { BotToken = "123456:TOKEN", ApiBaseUrl = "https://tapi.test" };
        var client = new BaleBotApiClient(new StubFactory(http), Microsoft.Extensions.Options.Options.Create(options), logger);
        return (client, handler, logger);
    }

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger<BaleBotApiClient>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }

    [Theory]
    [InlineData(AttachmentKind.Photo, "sendPhoto", "photo")]
    [InlineData(AttachmentKind.Video, "sendVideo", "video")]
    [InlineData(AttachmentKind.Voice, "sendVoice", "voice")]
    [InlineData(AttachmentKind.Audio, "sendAudio", "audio")]
    [InlineData(AttachmentKind.Animation, "sendAnimation", "animation")]
    [InlineData(AttachmentKind.Document, "sendDocument", "document")]
    public async Task SendAttachmentAsync_UsesTheMethodOfTheKind_AndSendsTheFileIdWithTheCaption(AttachmentKind kind, string method, string field)
    {
        var (client, handler, _) = Create(_ => Json("{\"ok\":true,\"result\":{}}"));

        var sent = await client.SendAttachmentAsync(-100, kind, "FILE-ID", "caption سلام", CancellationToken.None);

        Assert.True(sent);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/" + method, request.Url);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(-100, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("FILE-ID", body.RootElement.GetProperty(field).GetString());
        Assert.Equal("caption سلام", body.RootElement.GetProperty("caption").GetString());
    }

    [Fact]
    public async Task SendAttachmentAsync_Failures_ReturnFalse()
    {
        Assert.False(await Create(_ => Json("{\"ok\":false}", HttpStatusCode.BadRequest)).Client.SendAttachmentAsync(1, AttachmentKind.Photo, "f", null, CancellationToken.None));
        Assert.False(await Create(_ => Json("{\"ok\":false,\"error_code\":400}")).Client.SendAttachmentAsync(1, AttachmentKind.Photo, "f", null, CancellationToken.None));
        Assert.False(await Create(_ => throw new HttpRequestException("down")).Client.SendAttachmentAsync(1, AttachmentKind.Photo, "f", null, CancellationToken.None));
    }

    [Fact]
    public async Task GetUpdatesAsync_MapsEveryKindOfFile_AndTheCaptionBecomesTheText()
    {
        const string json = """
        {"ok":true,"result":[
          {"update_id":1,"message":{"message_id":1,"from":{"id":5},"chat":{"id":-7},"caption":"look","photo":[
              {"file_id":"small","file_unique_id":"u1","width":90,"height":90,"file_size":1000},
              {"file_id":"big","file_unique_id":"u2","width":1280,"height":960,"file_size":90000}]}},
          {"update_id":2,"message":{"message_id":2,"from":{"id":5},"chat":{"id":-7},"video":{"file_id":"v","file_unique_id":"uv","mime_type":"video/mp4","file_size":5000}}},
          {"update_id":3,"message":{"message_id":3,"from":{"id":5},"chat":{"id":-7},"voice":{"file_id":"vo","mime_type":"audio/ogg"}}},
          {"update_id":4,"message":{"message_id":4,"from":{"id":5},"chat":{"id":-7},"audio":{"file_id":"au","file_name":"song.mp3"}}},
          {"update_id":5,"message":{"message_id":5,"from":{"id":5},"chat":{"id":-7},"document":{"file_id":"d","file_name":"a.pdf","mime_type":"application/pdf"}}},
          {"update_id":6,"message":{"message_id":6,"from":{"id":5},"chat":{"id":-7},"animation":{"file_id":"an"},"document":{"file_id":"an"}}},
          {"update_id":7,"message":{"message_id":7,"from":{"id":5},"chat":{"id":-7},"sticker":{"file_id":"st"}}}
        ]}
        """;
        var (client, _, _) = Create(_ => Json(json));

        var m = (await client.GetUpdatesAsync(0, 1, CancellationToken.None)).Select(u => u.Message!).ToList();

        Assert.Equal("look", m[0].Text);
        Assert.Equal(AttachmentKind.Photo, m[0].Attachment!.Kind);
        Assert.Equal("big", m[0].Attachment!.FileId); // the largest size
        Assert.Equal(AttachmentKind.Video, m[1].Attachment!.Kind);
        Assert.Equal("video/mp4", m[1].Attachment!.MimeType);
        Assert.Equal(AttachmentKind.Voice, m[2].Attachment!.Kind);
        Assert.Equal(AttachmentKind.Audio, m[3].Attachment!.Kind);
        Assert.Equal("song.mp3", m[3].Attachment!.FileName);
        Assert.Equal(AttachmentKind.Document, m[4].Attachment!.Kind);
        Assert.Equal("a.pdf", m[4].Attachment!.FileName);
        Assert.Equal(AttachmentKind.Animation, m[5].Attachment!.Kind);
        Assert.Null(m[6].Attachment); // a sticker is not a supported file
        Assert.True(m[6].HasNonTextContent);
    }

    [Fact]
    public async Task DownloadFileAsync_CallsGetFileThenFetchesTheFile()
    {
        var (client, handler, logger) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/getFile")
                ? Json("{\"ok\":true,\"result\":{\"file_id\":\"F\",\"file_path\":\"photos/x.jpg\",\"file_size\":3}}")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9, 8, 7 }) });

        var file = await client.DownloadFileAsync("F", 1024, CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(new byte[] { 9, 8, 7 }, file!.Content);
        Assert.Equal("photos/x.jpg", file.FilePath);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("\"file_id\":\"F\"", handler.Requests[0].Body);
        Assert.Equal("https://tapi.test/file/bot123456:TOKEN/photos/x.jpg", handler.Requests[1].Url);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("TOKEN"));
    }

    [Fact]
    public async Task DownloadFileAsync_OverTheLimit_IsNotDownloaded()
    {
        var (reported, handler1, _) = Create(_ => Json("{\"ok\":true,\"result\":{\"file_path\":\"a\",\"file_size\":5000}}"));
        Assert.Null(await reported.DownloadFileAsync("F", 1000, CancellationToken.None));
        Assert.Single(handler1.Requests); // stopped after getFile

        // size unknown up front, but the real content is bigger than the limit
        var (streamed, _, _) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/getFile")
                ? Json("{\"ok\":true,\"result\":{\"file_path\":\"a\"}}")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5000]) });
        Assert.Null(await streamed.DownloadFileAsync("F", 1000, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadFileAsync_Failures_ReturnNullAndNeverThrow()
    {
        Assert.Null(await Create(_ => Json("{\"ok\":false}", HttpStatusCode.BadRequest)).Client.DownloadFileAsync("F", 10, CancellationToken.None));
        Assert.Null(await Create(_ => Json("{\"ok\":true,\"result\":{}}")).Client.DownloadFileAsync("F", 10, CancellationToken.None));
        Assert.Null(await Create(_ => throw new HttpRequestException("down")).Client.DownloadFileAsync("F", 10, CancellationToken.None));

        var (notFound, _, _) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/getFile")
                ? Json("{\"ok\":true,\"result\":{\"file_path\":\"a\"}}")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await notFound.DownloadFileAsync("F", 10, CancellationToken.None));
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

        var result = await Create().ClassifyAsync("customer text", Array.Empty<ThreadContextDto>(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Priority);
        Assert.Equal("a", result.Analysis);
    }

    [Fact]
    public async Task ClassifyAsync_OffersOpenIssuesToTheAi_AndReturnsTheIssueItChose()
    {
        _ai.Response = new RagChatResponse { ResponseText = "{\"priority\":\"Critical\",\"analysis\":\"a\",\"threadId\":7,\"title\":\"Server down\"}" };
        var open = new[]
        {
            new ThreadContextDto(7, "Server down", new DateTime(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc),
                new[] { new ThreadContextMessageDto("the server is down", GroupPriority.Critical, new DateTime(2026, 11, 15, 7, 0, 0, DateTimeKind.Utc)) })
        };

        var result = await Create().ClassifyAsync("still down", open, CancellationToken.None);

        Assert.Contains("[issue 7] Server down", Assert.Single(_ai.Calls).Message);
        Assert.Equal(7, result.ThreadId);
        Assert.Equal("Server down", result.ThreadTitle);
        Assert.Equal(GroupPriority.Critical, result.Priority);
    }

    [Fact]
    public async Task ClassifyAsync_SendsTheCustomerTextWithoutAnExistingConversation()
    {
        _ai.Response = new RagChatResponse { ResponseText = "{\"priority\":\"Low\"}" };

        await Create().ClassifyAsync("my special text", Array.Empty<ThreadContextDto>(), CancellationToken.None);

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

        var result = await Create().ClassifyAsync("x", Array.Empty<ThreadContextDto>(), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ClassifyAsync_HttpErrorStatus_Fails()
    {
        _ai.Response = new RagChatResponse { StatusCode = HttpStatusCode.InternalServerError };

        Assert.False((await Create().ClassifyAsync("x", Array.Empty<ThreadContextDto>(), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ClassifyAsync_NullResponse_Fails()
    {
        _ai.Response = null;

        Assert.False((await Create().ClassifyAsync("x", Array.Empty<ThreadContextDto>(), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ClassifyAsync_ApiThrows_FailsWithoutThrowing()
    {
        _ai.Throw = new HttpRequestException("down");

        Assert.False((await Create().ClassifyAsync("x", Array.Empty<ThreadContextDto>(), CancellationToken.None)).Succeeded);
    }
}
