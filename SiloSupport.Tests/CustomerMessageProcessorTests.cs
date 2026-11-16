using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;
using SiloSupport.Service.CustomerSupport.Options;
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
            new FakeCustomerRepository(_store),
            new FakeSupportPersonRepository(_store),
            new FakeThreadRepository(_store),
            new FakeGroupedMessageRepository(_store),
            _bale,
            _classifier,
            Microsoft.Extensions.Options.Options.Create(new BaleBotOptions()),
            Microsoft.Extensions.Options.Options.Create(new ThreadingOptions()),
            _clock,
            _logger);
    }

    private void AddGroups(int customerId, int firstId, long firstBaleId)
    {
        _store.Groups.Add(new CustomerGroup { Id = firstId, CustomerId = customerId, Priority = GroupPriority.Low, BaleId = firstBaleId, Name = $"C{customerId} Low" });
        _store.Groups.Add(new CustomerGroup { Id = firstId + 1, CustomerId = customerId, Priority = GroupPriority.Medium, BaleId = firstBaleId + 1, Name = $"C{customerId} Medium" });
        _store.Groups.Add(new CustomerGroup { Id = firstId + 2, CustomerId = customerId, Priority = GroupPriority.Critical, BaleId = firstBaleId + 2, Name = $"C{customerId} Critical" });
    }

    private static IncomingBaleMessage Text(long sender, string text, long chat = SupportGroup, bool isBot = false, string? username = null) =>
        new(MessageId: 1, ChatId: chat, SenderId: sender, SenderIsBot: isBot, Text: text, HasNonTextContent: false, SenderUsername: username);

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

    [Theory]
    [InlineData("@ali", "ali")]
    [InlineData("ali", "ali")]
    [InlineData("@Ali", "ALI")]
    [InlineData("ali", "@ali")]
    public async Task ProcessAsync_MemberRegisteredByUserName_IsMatchedByTheSenderUserName(string stored, string incoming)
    {
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 2, BaleId = stored, Name = "Ali", PhoneNumber = "0912" });
        ClassifyAs(GroupPriority.Critical);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "urgent", username: incoming), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(3003, _bale.Sent.Single().ChatId); // Beta's Critical group
    }

    [Fact]
    public async Task ProcessAsync_NumericIdMatchWinsOverUserNameOfAnotherCustomer()
    {
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 2, BaleId = "@ali", Name = "Ali", PhoneNumber = "0" });
        ClassifyAs(GroupPriority.Low);

        // Sender id belongs to Acme's Sara, but the user name is registered for Beta's Ali.
        var outcome = await _processor.ProcessAsync(Text(AcmeMember, "hello", username: "ali"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(2001, _bale.Sent.Single().ChatId); // Acme's Low group
    }

    [Fact]
    public async Task ProcessAsync_UserNameRegisteredForTwoCustomers_IsIgnoredWithWarning()
    {
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 1, BaleId = "@ali", Name = "Ali A", PhoneNumber = "0" });
        _store.Members.Add(new CustomerMember { Id = 4, CustomerId = 2, BaleId = "ali", Name = "Ali B", PhoneNumber = "0" });
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "hello", username: "ali"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnknownMember, outcome);
        Assert.Empty(_bale.Sent);
        Assert.True(_logger.Has(LogLevel.Warning, "several customers"));
    }

    [Fact]
    public async Task ProcessAsync_UnknownUserName_IsIgnoredAndLogMentionsHowToRegister()
    {
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "hello", username: "stranger"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnknownMember, outcome);
        Assert.True(_logger.Has(LogLevel.Information, "@stranger"));
    }

    [Fact]
    public async Task ProcessAsync_NoSenderIdButKnownUserName_IsForwarded()
    {
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 1, BaleId = "@sara", Name = "Sara", PhoneNumber = "0" });
        ClassifyAs(GroupPriority.Medium);
        var message = new IncomingBaleMessage(1, SupportGroup, null, false, "hello", false, "sara");

        Assert.Equal(MessageProcessingOutcome.Forwarded, await _processor.ProcessAsync(message, CancellationToken.None));
    }

    private const long AcmeChat = 8888;

    private void RegisterAcmeChat() => _store.Customers.Single(c => c.Id == 1).BaleChatId = AcmeChat;

    [Theory]
    [InlineData("@Support1", "support1")]
    [InlineData("support1", "SUPPORT1")]
    public async Task ProcessAsync_MessageFromASupportPerson_IsNotACustomerMessage(string stored, string incoming)
    {
        _store.SupportPersons.Add(new SupportPerson { Id = 1, Name = "Ali", PhoneNumber = "09121234567", BaleUsername = SiloSupport.Application.Helpers.BaleIdentity.Normalize(stored) });
        RegisterAcmeChat();
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "I will check", chat: AcmeChat, username: incoming), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredSupportPerson, outcome);
        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
        Assert.Empty(_store.Messages);
    }

    [Fact]
    public async Task ProcessAsync_SupportPersonWhoIsAlsoListedAsMember_IsStillIgnored()
    {
        _store.SupportPersons.Add(new SupportPerson { Id = 1, Name = "Ali", PhoneNumber = "09121234567", BaleUsername = "sara_support" });
        _store.Members.Add(new CustomerMember { Id = 3, CustomerId = 1, BaleId = "@sara_support", Name = "Sara", PhoneNumber = "0" });
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "hello", username: "sara_support"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredSupportPerson, outcome);
        Assert.Empty(_bale.Sent);
    }

    [Fact]
    public async Task ProcessAsync_UnregisteredSenderWithoutUserName_InACustomerChat_IsProcessedAsThatCustomersMessage()
    {
        RegisterAcmeChat();
        ClassifyAs(GroupPriority.Critical);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "System is down", chat: AcmeChat), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(new[] { "System is down" }, _classifier.Calls);
        var sent = Assert.Single(_bale.Sent);
        Assert.Equal(2003, sent.ChatId); // Acme's Critical group
        Assert.Contains("Acme", sent.Text);
        Assert.Contains(UnknownUser.ToString(), sent.Text);
        Assert.Contains(sent.Text, _store.Messages.Select(m => m.Message));
    }

    [Fact]
    public async Task ProcessAsync_UnregisteredSenderWithAUserName_ThatIsNotInTheCustomerTable_IsStillACustomerMessage()
    {
        RegisterAcmeChat();
        ClassifyAs(GroupPriority.Medium);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "Slow reports", chat: AcmeChat, username: "newcomer"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Contains("@newcomer", Assert.Single(_bale.Sent).Text);
        Assert.Single(_store.Messages);
    }

    [Fact]
    public async Task ProcessAsync_RegisteredMemberWinsOverTheChatOfAnotherCustomer()
    {
        RegisterAcmeChat();
        ClassifyAs(GroupPriority.Low);

        // Omid is a member of Beta but writes in Acme's chat: his own customer is used.
        var outcome = await _processor.ProcessAsync(Text(BetaMember, "hello", chat: AcmeChat), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Equal(3001, _bale.Sent.Single().ChatId); // Beta's Low group
    }

    [Fact]
    public async Task ProcessAsync_UnregisteredSenderInAChatOfNoCustomer_IsIgnoredWithAHint()
    {
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(Text(UnknownUser, "hello", chat: 12345, username: "stranger"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnknownMember, outcome);
        Assert.Empty(_bale.Sent);
        Assert.True(_logger.Has(LogLevel.Information, "Bale chat id"));
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
        Assert.True(_logger.Has(LogLevel.Information, "not the Bale chat of any customer"));
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
    public async Task ProcessAsync_UnsupportedContentWithoutAFile_IsIgnoredAndLogged()
    {
        // e.g. a sticker, contact or location: Bale gives no text and no supported file.
        var sticker = new IncomingBaleMessage(1, SupportGroup, AcmeMember, false, null, true);
        var stickerWithText = new IncomingBaleMessage(2, SupportGroup, AcmeMember, false, "caption-like text", HasNonTextContent: true);

        Assert.Equal(MessageProcessingOutcome.IgnoredUnsupportedMessage, await _processor.ProcessAsync(sticker, CancellationToken.None));
        Assert.Equal(MessageProcessingOutcome.IgnoredUnsupportedMessage, await _processor.ProcessAsync(stickerWithText, CancellationToken.None));

        Assert.Empty(_classifier.Calls);
        Assert.Empty(_bale.Sent);
        Assert.Empty(_bale.SentAttachments);
        Assert.True(_logger.Has(LogLevel.Information, "neither text nor a supported file"));
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

    // ---- Files: photo, video, voice, audio, document, animation ----

    private static IncomingBaleMessage File(AttachmentKind kind, string? caption = null, string fileId = "FILE-1", long? size = 1234, string? fileName = null, string? mime = null, long sender = AcmeMember) =>
        new(1, SupportGroup, sender, false, caption, true,
            Attachment: new IncomingAttachment(kind, fileId, "UNIQ-1", fileName, mime, size));

    [Theory]
    [InlineData(AttachmentKind.Photo)]
    [InlineData(AttachmentKind.Video)]
    [InlineData(AttachmentKind.Voice)]
    [InlineData(AttachmentKind.Audio)]
    [InlineData(AttachmentKind.Document)]
    [InlineData(AttachmentKind.Animation)]
    public async Task ProcessAsync_File_IsForwardedByFileIdWithTheTextAsCaption_AndStored(AttachmentKind kind)
    {
        ClassifyAs(GroupPriority.Critical);

        var outcome = await _processor.ProcessAsync(File(kind, "the server is down"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        var sent = Assert.Single(_bale.SentAttachments);
        Assert.Equal(2003, sent.ChatId); // Acme's Critical group, never the source chat
        Assert.Equal(kind, sent.Kind);
        Assert.Equal("FILE-1", sent.FileId);
        Assert.Contains("the server is down", sent.Caption);
        Assert.Contains("تحلیل هوش مصنوعی", sent.Caption);
        Assert.Empty(_bale.Sent);

        var message = Assert.Single(_store.Messages);
        Assert.Equal(sent.Caption, message.Message);
        Assert.Equal("the server is down", message.OriginalText);

        var stored = Assert.Single(_store.Attachments);
        Assert.Equal(message.Id, stored.GroupedMessageId);
        Assert.Equal(kind, stored.Kind);
        Assert.Equal("FILE-1", stored.BaleFileId);
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.Content);
        Assert.False(string.IsNullOrEmpty(stored.MimeType));
    }

    [Fact]
    public async Task ProcessAsync_FileWithoutText_IsDescribedToTheAi_AndKeepsThePriorityTheAiChose()
    {
        // support-instructions.md section 9: a message that is only a file is Low ("no readable problem description").
        ClassifyAs(GroupPriority.Low);

        var outcome = await _processor.ProcessAsync(File(AttachmentKind.Voice), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Contains("voice message", _classifier.Calls.Single());
        Assert.Equal(2001, _bale.SentAttachments.Single().ChatId); // Low group: no silent raise
        Assert.Null(_store.Messages.Single().OriginalText);
        Assert.Single(_store.Attachments);
    }

    [Fact]
    public async Task ProcessAsync_FileWithoutText_KeepsAHigherPriorityTheAiChose()
    {
        ClassifyAs(GroupPriority.Critical);

        await _processor.ProcessAsync(File(AttachmentKind.Photo), CancellationToken.None);

        Assert.Equal(2003, _bale.SentAttachments.Single().ChatId);
    }

    [Fact]
    public async Task ProcessAsync_FileTooLargeToDownload_IsStillForwardedButStoredWithoutBytes()
    {
        ClassifyAs(GroupPriority.Medium);

        var outcome = await _processor.ProcessAsync(File(AttachmentKind.Video, "big", size: 25L * 1024 * 1024), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Single(_bale.SentAttachments);
        Assert.Empty(_bale.Downloads); // not even attempted
        var stored = Assert.Single(_store.Attachments);
        Assert.Null(stored.Content);
        Assert.Equal(25L * 1024 * 1024, stored.SizeBytes);
    }

    [Fact]
    public async Task ProcessAsync_DownloadFailure_DoesNotLoseTheMessage()
    {
        ClassifyAs(GroupPriority.Medium);
        _bale.Download = null;

        var outcome = await _processor.ProcessAsync(File(AttachmentKind.Photo, "see"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Single(_bale.SentAttachments);
        Assert.Null(Assert.Single(_store.Attachments).Content);
    }

    [Fact]
    public async Task ProcessAsync_WhenBaleRefusesTheFile_TheTextIsStillSentWithANote()
    {
        ClassifyAs(GroupPriority.Medium);
        _bale.SendAttachmentResult = false;

        var outcome = await _processor.ProcessAsync(File(AttachmentKind.Document, "invoice", fileName: "a.pdf"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Empty(_bale.SentAttachments);
        var text = Assert.Single(_bale.Sent);
        Assert.Equal(2002, text.ChatId);
        Assert.Contains("invoice", text.Text);
        Assert.Contains("ارسال پیوست", text.Text);
    }

    [Fact]
    public async Task ProcessAsync_WhenEverythingFailsToSend_NothingIsRecorded()
    {
        ClassifyAs(GroupPriority.Medium);
        _bale.SendAttachmentResult = false;
        _bale.SendResult = false;

        var outcome = await _processor.ProcessAsync(File(AttachmentKind.Photo, "x"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.SendFailed, outcome);
        Assert.Empty(_store.Messages);
        Assert.Empty(_store.Attachments);
        Assert.Empty(_store.Threads);
        Assert.Empty(_bale.Downloads);
    }

    [Fact]
    public async Task ProcessAsync_FileFromAPriorityGroupOrSupportPerson_IsIgnored()
    {
        _store.SupportPersons.Add(new SupportPerson { Id = 1, BaleUsername = "agent", Name = "Agent" });
        var fromSupport = new IncomingBaleMessage(1, SupportGroup, 42, false, null, true, SenderUsername: "agent",
            Attachment: new IncomingAttachment(AttachmentKind.Photo, "F", null, null, null, 1));
        var inGroup = File(AttachmentKind.Photo) with { ChatId = 2001 };

        Assert.Equal(MessageProcessingOutcome.IgnoredSupportPerson, await _processor.ProcessAsync(fromSupport, CancellationToken.None));
        Assert.Equal(MessageProcessingOutcome.IgnoredPriorityGroup, await _processor.ProcessAsync(inGroup, CancellationToken.None));
        Assert.Empty(_bale.SentAttachments);
        Assert.Empty(_bale.Downloads);
    }

    // ---- Issue threads ----

    [Fact]
    public async Task ProcessAsync_FirstMessage_StartsANewIssueWithTheAiTitle()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", threadId: null, threadTitle: "Login problem");

        await _processor.ProcessAsync(Text(AcmeMember, "I cannot log in"), CancellationToken.None);

        var thread = Assert.Single(_store.Threads);
        Assert.Equal(1, thread.CustomerId);
        Assert.Equal("Login problem", thread.Title);
        Assert.Equal(thread.Id, _store.Messages.Single().ThreadId);
        Assert.Empty(_classifier.ThreadContexts.Single());
        Assert.Contains("موضوع جدید: Login problem", _bale.Sent.Single().Text);
    }

    [Fact]
    public async Task ProcessAsync_WithoutAnAiTitle_TheIssueIsTitledWithAnExcerptOfTheMessage()
    {
        ClassifyAs(GroupPriority.Low);

        await _processor.ProcessAsync(Text(AcmeMember, "please   help\nme"), CancellationToken.None);

        Assert.Equal("please help me", Assert.Single(_store.Threads).Title);
    }

    [Fact]
    public async Task ProcessAsync_FollowUp_IsAttachedToTheOpenIssueTheAiNames_AndTheAiSawTheHistory()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Login problem");
        await _processor.ProcessAsync(Text(AcmeMember, "I cannot log in"), CancellationToken.None);
        var threadId = _store.Threads.Single().Id;

        _clock.Advance(TimeSpan.FromMinutes(3));
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Critical, "a", threadId, "ignored for follow ups");
        await _processor.ProcessAsync(Text(AcmeMember, "it says wrong password even with the reset one"), CancellationToken.None);

        Assert.Single(_store.Threads);
        Assert.All(_store.Messages, m => Assert.Equal(threadId, m.ThreadId));
        Assert.Equal(2, _store.Messages.Count);

        // the AI saw the earlier message of the open issue
        var context = Assert.Single(_classifier.ThreadContexts[1]);
        Assert.Equal(threadId, context.ThreadId);
        Assert.Equal("Login problem", context.Title);
        Assert.Equal("I cannot log in", Assert.Single(context.Messages).Text);

        // follow-up is routed by its own priority and labelled as a continuation
        Assert.Equal(2003, _bale.Sent[1].ChatId);
        Assert.Contains("ادامه موضوع: Login problem", _bale.Sent[1].Text);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, _store.Threads.Single().LastActivityUtc);
    }

    [Fact]
    public async Task ProcessAsync_UnrelatedMessage_StartsItsOwnIssue()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Login problem");
        await _processor.ProcessAsync(Text(AcmeMember, "I cannot log in"), CancellationToken.None);

        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", null, "Invoice question");
        await _processor.ProcessAsync(Text(AcmeMember, "when is my invoice due?"), CancellationToken.None);

        Assert.Equal(2, _store.Threads.Count);
        Assert.Equal(new[] { "Login problem", "Invoice question" }, _store.Threads.Select(t => t.Title));
        Assert.NotEqual(_store.Messages[0].ThreadId, _store.Messages[1].ThreadId);
        // both open issues were offered to the AI for the second decision
        Assert.Single(_classifier.ThreadContexts[1]);
    }

    [Fact]
    public async Task ProcessAsync_AnIssueIdTheAiWasNeverOffered_IsNotTrusted()
    {
        // Beta has an open issue; the AI for Acme names it anyway.
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Beta thing");
        await _processor.ProcessAsync(Text(BetaMember, "beta problem"), CancellationToken.None);
        var betaThread = _store.Threads.Single().Id;

        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", betaThread, "Acme thing");
        await _processor.ProcessAsync(Text(AcmeMember, "acme question"), CancellationToken.None);

        Assert.Equal(2, _store.Threads.Count);
        var acme = _store.Threads.Single(t => t.CustomerId == 1);
        Assert.Equal("Acme thing", acme.Title);
        Assert.Equal(acme.Id, _store.Messages[1].ThreadId);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("was not offered"));
    }

    [Fact]
    public async Task ProcessAsync_ResolvedIssues_AreNotOffered_ButOpenOnesAreOfferedHoweverOldTheyAre()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Old", "old summary");
        await _processor.ProcessAsync(Text(AcmeMember, "first"), CancellationToken.None);
        _store.Messages.Single().IsResolved = true;

        await _processor.ProcessAsync(Text(AcmeMember, "second"), CancellationToken.None);
        Assert.Empty(_classifier.ThreadContexts[1]); // the resolved issue is not offered

        // Two weeks later the unresolved issue ("second") is still offered: there is no age limit.
        _clock.Advance(TimeSpan.FromDays(14));
        await _processor.ProcessAsync(Text(AcmeMember, "third"), CancellationToken.None);
        Assert.Single(_classifier.ThreadContexts[2]);
    }

    [Fact]
    public async Task ProcessAsync_AnOldOpenIssue_IsRejoinedWeeksLater()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Printer offset", "Labels print off-center.");
        await _processor.ProcessAsync(Text(AcmeMember, "labels print off center"), CancellationToken.None);
        var threadId = _store.Threads.Single().Id;

        _clock.Advance(TimeSpan.FromDays(40));
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", threadId, null, "Labels print off-center again after the driver update.");
        await _processor.ProcessAsync(Text(AcmeMember, "the printer problem is back"), CancellationToken.None);

        Assert.Single(_store.Threads);
        Assert.All(_store.Messages, m => Assert.Equal(threadId, m.ThreadId));
        var offered = Assert.Single(_classifier.ThreadContexts[1]);
        Assert.Equal("Labels print off-center.", offered.Summary);
    }

    [Fact]
    public async Task ProcessAsync_NoCountLimit_EveryOpenIssueIsOfferedToTheAi()
    {
        for (var i = 1; i <= 12; i++)
        {
            _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", null, $"Topic {i}");
            await _processor.ProcessAsync(Text(AcmeMember, $"subject number {i}"), CancellationToken.None);
        }

        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", null, "Topic 13");
        await _processor.ProcessAsync(Text(AcmeMember, "another"), CancellationToken.None);

        Assert.Equal(12, _classifier.ThreadContexts.Last().Count);
    }

    [Fact]
    public async Task ProcessAsync_NewIssueKeepsTheAiSummary_AndAFollowUpReplacesIt()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Login problem", "User cannot log in.");
        await _processor.ProcessAsync(Text(AcmeMember, "I cannot log in"), CancellationToken.None);
        var thread = _store.Threads.Single();
        Assert.Equal("User cannot log in.", thread.Summary);

        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", thread.Id, null, "User cannot log in even after a password reset.");
        await _processor.ProcessAsync(Text(AcmeMember, "reset did not help"), CancellationToken.None);

        Assert.Equal("User cannot log in even after a password reset.", thread.Summary);
        Assert.Equal("Login problem", thread.Title); // the title of a continued issue never changes
    }

    [Fact]
    public async Task ProcessAsync_AFollowUpWithoutASummary_KeepsTheOldSummary()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "T", "Old summary.");
        await _processor.ProcessAsync(Text(AcmeMember, "one"), CancellationToken.None);
        var thread = _store.Threads.Single();

        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", thread.Id, null, null);
        await _processor.ProcessAsync(Text(AcmeMember, "two"), CancellationToken.None);

        Assert.Equal("Old summary.", thread.Summary);
    }

    [Fact]
    public async Task ProcessAsync_TheAiSeesMessageCountAndHighestPriorityOfEachIssue()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Critical, "a", null, "Outage");
        await _processor.ProcessAsync(Text(AcmeMember, "system down"), CancellationToken.None);
        var threadId = _store.Threads.Single().Id;
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", threadId, null);
        await _processor.ProcessAsync(Text(AcmeMember, "any news?"), CancellationToken.None);

        await _processor.ProcessAsync(Text(AcmeMember, "hello again"), CancellationToken.None);

        var offered = Assert.Single(_classifier.ThreadContexts[2]);
        Assert.Equal(2, offered.MessageCount);
        Assert.Equal(GroupPriority.Critical, offered.HighestPriority);
    }

    [Fact]
    public async Task ProcessAsync_OnlyTheNewestFewMessagesOfAnIssueAreWrittenOut_TheSummaryCoversTheRest()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", null, "Long talk", "summary");
        await _processor.ProcessAsync(Text(AcmeMember, "m1"), CancellationToken.None);
        var threadId = _store.Threads.Single().Id;
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Low, "a", threadId, null, "summary");
        for (var i = 2; i <= 6; i++)
            await _processor.ProcessAsync(Text(AcmeMember, $"m{i}"), CancellationToken.None);

        await _processor.ProcessAsync(Text(AcmeMember, "m7"), CancellationToken.None);

        var offered = Assert.Single(_classifier.ThreadContexts.Last());
        Assert.Equal(6, offered.MessageCount);
        Assert.Equal(new[] { "m4", "m5", "m6" }, offered.Messages.Select(m => m.Text)); // Threads:MessagesPerThread = 3
    }

    [Fact]
    public async Task ProcessAsync_AFileCanContinueAnIssue()
    {
        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", null, "Broken screen");
        await _processor.ProcessAsync(Text(AcmeMember, "the screen looks broken"), CancellationToken.None);
        var threadId = _store.Threads.Single().Id;

        _classifier.Result = PriorityClassificationResult.Success(GroupPriority.Medium, "a", threadId, null);
        await _processor.ProcessAsync(File(AttachmentKind.Photo), CancellationToken.None);

        Assert.Single(_store.Threads);
        Assert.Equal(threadId, _store.Messages[1].ThreadId);
        Assert.Contains("ادامه موضوع: Broken screen", _bale.SentAttachments.Single().Caption);
    }

    [Fact]
    public async Task ProcessAsync_WhenTheOpenIssuesCannotBeLoaded_TheMessageIsStillClassifiedAsANewIssue()
    {
        var failing = new CustomerMessageProcessor(
            new FakeGroupRepository(_store), new FakeMemberRepository(_store), new FakeCustomerRepository(_store),
            new FakeSupportPersonRepository(_store), new ThrowingThreadRepository(), new FakeGroupedMessageRepository(_store),
            _bale, _classifier,
            Microsoft.Extensions.Options.Options.Create(new BaleBotOptions()),
            Microsoft.Extensions.Options.Options.Create(new ThreadingOptions()), _clock, _logger);
        ClassifyAs(GroupPriority.Low);

        var outcome = await failing.ProcessAsync(Text(AcmeMember, "hello"), CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Forwarded, outcome);
        Assert.Single(_store.Threads);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("without history"));
    }

    private sealed class ThrowingThreadRepository : SiloSupport.Application.Interfaces.ISupportThreadRepository
    {
        public Task<IReadOnlyList<ThreadContextDto>> GetOpenContextAsync(int customerId, int recentMessagesPerThread, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("db down");

        public Task UpdateSummaryAsync(int threadId, string summary, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<SupportThread>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
