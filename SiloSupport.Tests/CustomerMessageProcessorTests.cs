using Microsoft.Extensions.Logging;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;
using SiloSupport.Tests.Support;

namespace SiloSupport.Tests;

/// <summary>Service tests of the Bale message routing flow using fakes for repositories, Bale and the AI API.</summary>
public class CustomerMessageProcessorTests
{
    private const long BotId = 999;

    // Customer 1 (Acme): member 1001, groups 2001/2002/2003. Customer 2 (Beta): member 1002, groups 3001/3002/3003.
    private const long AcmeMember = 1001, BetaMember = 1002, UnknownUser = 5555, SupportGroup = 7777;

    private readonly FakeStore _store = new();
    private readonly FakeBaleClient _bale = new() { BotUserId = BotId };
    private readonly FakeClassifier _classifier = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.Zero));
    private readonly ListLogger<CustomerMessageProcessor> _logger = new();
    private readonly CustomerMessageProcessor _processor;

    public CustomerMessageProcessorTests()
    {
        _store.Customers.Add(new Customer { Id = 1, Name = "Acme" });
        _store.Customers.Add(new Customer { Id = 2, Name = "Beta" });
        _store.Members.Add(new CustomerMember { Id = 1, CustomerId = 1, BaleId = AcmeMember.ToString(), Name = "Sara", PhoneNumber = "0912" });
        _store.Members.Add(new CustomerMember { Id = 2, CustomerId = 2, BaleId = BetaMember.ToString(), Name = "Omid", PhoneNumber = "0935" });

        AddGroups(1, 1, 2001);
        AddGroups(2, 4, 3001);

        _processor = new CustomerMessageProcessor(
            new FakeGroupRepository(_store),
            new FakeMemberRepository(_store),
            new FakeGroupedMessageRepository(_store),
            _bale,
            _classifier,
            _clock,
            _logger);
    }

    private void AddGroups(int customerId, int firstId, long firstBaleId)
    {
        _store.Groups.Add(new CustomerGroup { Id = firstId, CustomerId = customerId, Priority = GroupPriority.Low, BaleId = firstBaleId, Name = $"C{customerId} Low" });
        _store.Groups.Add(new CustomerGroup { Id = firstId + 1, CustomerId = customerId, Priority = GroupPriority.Medium, BaleId = firstBaleId + 1, Name = $"C{customerId} Medium" });
        _store.Groups.Add(new CustomerGroup { Id = firstId + 2, CustomerId = customerId, Priority = GroupPriority.Critical, BaleId = firstBaleId + 2, Name = $"C{customerId} Critical" });
    }

    private static IncomingBaleMessage Text(long sender, string text, long chat = SupportGroup, bool isBot = false) =>
        new(MessageId: 1, ChatId: chat, SenderId: sender, SenderIsBot: isBot, Text: text, HasNonTextContent: false);

    private void ClassifyAs(GroupPriority priority, string? analysis = "AI analysis text") =>
        _classifier.Result = PriorityClassificationResult.Success(priority, analysis);

    [Fact]
    public async Task ProcessAsync_MemberRegisteredWithANonNumericTextId_IsNotMatchedByANumericSender()
    {
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 1, BaleId = "user_9", Name = "Text id", PhoneNumber = "0" });
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(Text(9, "hello"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnknownMember, outcome);
        Assert.Empty(_bale.Sent);
    }

    [Fact]
    public async Task ProcessAsync_SenderIdIsMatchedAgainstTheStoredTextId()
    {
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 2, BaleId = "4503599627370495", Name = "Big id", PhoneNumber = "0" });
        ClassifyAs(GroupPriority.Critical);

        var outcome = await _processor.ProcessAsync(Text(4503599627370495, "urgent"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(3003, _bale.Sent.Single().ChatId); // Beta's Critical group
    }

    // 1
    [Fact]
    public async Task ProcessAsync_RegisteredMemberText_IsClassifiedAndForwarded()
    {
        ClassifyAs(GroupPriority.Medium);

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "Reports are slow"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(new[] { "Reports are slow" }, _classifier.Calls);
        var sent = Assert.Single(_bale.Sent);
        Assert.Contains("Reports are slow", sent.Text);
        Assert.Contains("AI analysis text", sent.Text);
        Assert.Contains("Medium", sent.Text);
        Assert.Contains("Acme", sent.Text);
    }

    // 2
    [Theory]
    [InlineData(2001)]
    [InlineData(2002)]
    [InlineData(2003)]
    [InlineData(3001)]
    [InlineData(3003)]
    public async Task ProcessAsync_MessageFromAnyConfiguredPriorityGroup_IsIgnored(long priorityGroupChat)
    {
        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "forwarded text", chat: priorityGroupChat), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredPriorityGroup, outcome);
        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
        Assert.Empty(_store.Messages);
    }

    // 3
    [Fact]
    public async Task ProcessAsync_SenderFlaggedAsBot_IsIgnored()
    {
        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "hello", isBot: true), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredBotSender, outcome);
        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
    }

    [Fact]
    public async Task ProcessAsync_SenderIsOwnBotId_IsIgnoredEvenWhenNotFlagged()
    {
        var outcome = await _processor.ProcessAsync(Text(BotId, "hello"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredBotSender, outcome);
        Assert.Empty(_bale.Sent);
    }

    // 4
    [Fact]
    public async Task ProcessAsync_UnregisteredSender_IsIgnoredAndLogged()
    {
        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "hello"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnknownMember, outcome);
        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
        Assert.True(_logger.Has(LogLevel.Information, "not a registered customer member"));
    }

    [Fact]
    public async Task ProcessAsync_NoSenderId_IsIgnored()
    {
        var message = new IncomingBaleMessage(1, SupportGroup, null, false, "hello", false);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnknownMember, await _processor.ProcessAsync(message, CancellationToken.None));
        Assert.Empty(_bale.Sent);
    }

    // 5
    [Fact]
    public async Task ProcessAsync_NonTextMessage_IsIgnoredAndLogged()
    {
        // e.g. a voice message, photo or document: Bale gives no "text", only the media object.
        var voice = new IncomingBaleMessage(1, SupportGroup, AcmeMember, false, null, true);
        var photoWithCaptionOnly = new IncomingBaleMessage(2, SupportGroup, AcmeMember, false, null, true);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnsupportedMessage, await _processor.ProcessAsync(voice, CancellationToken.None));
        Assert.Equal(MessageProcessingOutcome.IgnoredUnsupportedMessage, await _processor.ProcessAsync(photoWithCaptionOnly, CancellationToken.None));

        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
        Assert.True(_logger.Has(LogLevel.Information, "not an ordinary text message"));
    }

    [Fact]
    public async Task ProcessAsync_MediaMessageThatAlsoCarriesText_IsIgnored()
    {
        var message = new IncomingBaleMessage(1, SupportGroup, AcmeMember, false, "caption-like text", HasNonTextContent: true);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnsupportedMessage, await _processor.ProcessAsync(message, CancellationToken.None));
        Assert.Empty(_bale.Sent);
    }

    // 6
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public async Task ProcessAsync_EmptyOrWhitespaceText_IsIgnored(string text)
    {
        var outcome = await _processor.ProcessAsync(Text(AcmeMember, text), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredEmptyText, outcome);
        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
    }

    // 7, 8, 9
    [Theory]
    [InlineData(GroupPriority.Low, 2001)]
    [InlineData(GroupPriority.Medium, 2002)]
    [InlineData(GroupPriority.Critical, 2003)]
    public async Task ProcessAsync_RoutesToTheCustomersGroupOfTheReturnedPriority(GroupPriority priority, long expectedChat)
    {
        ClassifyAs(priority);

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(expectedChat, Assert.Single(_bale.Sent).ChatId);
    }

    // 10
    [Theory]
    [InlineData(GroupPriority.Low)]
    [InlineData(GroupPriority.Medium)]
    [InlineData(GroupPriority.Critical)]
    public async Task ProcessAsync_NeverRoutesToAnotherCustomersGroup(GroupPriority priority)
    {
        ClassifyAs(priority);

        await _processor.ProcessAsync(Text(BetaMember, "help"), CancellationToken.None);

        var sent = Assert.Single(_bale.Sent);
        Assert.InRange(sent.ChatId, 3001, 3003);
        var recorded = Assert.Single(_store.Messages);
        Assert.Equal(2, _store.Groups.Single(g => g.Id == recorded.GroupId).CustomerId);
    }

    [Fact]
    public async Task ProcessAsync_CustomerWithoutGroupForPriority_DoesNotBorrowAnotherCustomersGroup()
    {
        // Beta loses its Critical group; Acme still has one.
        _store.Groups.RemoveAll(g => g.CustomerId == 2 && g.Priority == GroupPriority.Critical);
        ClassifyAs(GroupPriority.Critical);

        var outcome = await _processor.ProcessAsync(Text(BetaMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.DestinationGroupMissing, outcome);
        Assert.Empty(_bale.Sent);
    }

    // 11
    [Fact]
    public async Task ProcessAsync_InvalidAiResult_DoesNotFallBackToAnyGroup()
    {
        _classifier.Result = PriorityClassificationResult.Failure("unsupported priority value");

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.ClassificationFailed, outcome);
        Assert.Empty(_bale.Sent);
        Assert.Empty(_store.Messages);
        Assert.True(_logger.Has(LogLevel.Warning, "unsupported priority value"));
    }

    [Fact]
    public async Task ProcessAsync_AiApiThrows_IsLoggedAndNothingIsSent()
    {
        _classifier.Throw = true;

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.ClassificationFailed, outcome);
        Assert.Empty(_bale.Sent);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("AI API failure"));
    }

    // 12
    [Fact]
    public async Task ProcessAsync_MissingDestinationGroup_PreventsSendingAndIsLogged()
    {
        _store.Groups.RemoveAll(g => g.CustomerId == 1 && g.Priority == GroupPriority.Medium);
        ClassifyAs(GroupPriority.Medium);

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.DestinationGroupMissing, outcome);
        Assert.Empty(_bale.Sent);
        Assert.Empty(_store.Messages);
        Assert.True(_logger.Has(LogLevel.Warning, "no destination group configured"));
    }

    // 13
    [Fact]
    public async Task ProcessAsync_BaleSendReturnsFalse_NoRecordIsCreated()
    {
        ClassifyAs(GroupPriority.Low);
        _bale.SendResult = false;

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.SendFailed, outcome);
        Assert.Empty(_store.Messages);
        Assert.True(_logger.Has(LogLevel.Error, "Bale send failed"));
    }

    [Fact]
    public async Task ProcessAsync_BaleSendThrows_NoRecordIsCreated()
    {
        ClassifyAs(GroupPriority.Low);
        _bale.ThrowOnSend = true;

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.SendFailed, outcome);
        Assert.Empty(_store.Messages);
    }

    // 14
    [Fact]
    public async Task ProcessAsync_SuccessfulSend_PersistsTheSentTextAsUnresolved()
    {
        ClassifyAs(GroupPriority.Critical);

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "Server is down"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        var sent = Assert.Single(_bale.Sent);
        var record = Assert.Single(_store.Messages);
        Assert.Equal(sent.Text, record.Message);
        Assert.Equal(3, record.GroupId); // Acme Critical group (internal id, not the Bale id)
        Assert.False(record.IsResolved);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, record.CreatedAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_DatabaseFailureAfterSend_IsLoggedAndReported()
    {
        ClassifyAs(GroupPriority.Low);
        _store.FailMessageInsert = true;

        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "help"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.PersistenceFailed, outcome);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("persistence failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessAsync_NeverRepliesToTheSourceChat()
    {
        ClassifyAs(GroupPriority.Low);

        await _processor.ProcessAsync(Text(AcmeMember, "help", chat: SupportGroup), CancellationToken.None);

        Assert.DoesNotContain(_bale.Sent, s => s.ChatId == SupportGroup);
    }
}
