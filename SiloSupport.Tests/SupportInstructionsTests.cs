using System.Text.RegularExpressions;
using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Tests;

/// <summary>
/// Keeps <c>Docs/support-instructions.md</c> (the knowledge-base document the AI follows) in step with the code that
/// builds the request and parses the answer: the activation sentence, the markers, the answer shape, and every example
/// answer in the document must be accepted by the real parser.
/// </summary>
public class SupportInstructionsTests
{
    private static readonly string Document = File.ReadAllText(Path.Combine(FindRoot(), "Docs", "support-instructions.md"));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SiloSupport.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SiloSupport.slnx was not found");
    }

    private static List<string> ExampleAnswers() =>
        Regex.Matches(Document, @"(?m)^(\{""priority"":""(?:Low|Medium|Critical)"".*\})$").Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public void Document_UsesTheSameActivationSentenceAndMarkersAsThePrompt()
    {
        var prompt = PriorityClassificationPrompt.Build("hello", new[]
        {
            new ThreadContextDto(1, "t", "s", DateTime.UtcNow, 1, GroupPriority.Low, Array.Empty<ThreadContextMessageDto>())
        });

        Assert.Contains("You are a support-ticket triage assistant. Classify the priority of the customer support message below.", prompt);
        Assert.Contains("You are a support-ticket triage assistant. Classify the priority of the customer support message below.", Document);
        foreach (var marker in new[] { "<<<CUSTOMER_MESSAGE", "CUSTOMER_MESSAGE>>>", "<<<OPEN_ISSUES", "OPEN_ISSUES>>>" })
        {
            Assert.Contains(marker, prompt);
            Assert.Contains(marker, Document);
        }
    }

    [Fact]
    public void Document_DescribesTheFiveKeyAnswerTheParserReads()
    {
        Assert.Contains("Exactly **five keys**", Document);
        Assert.Contains("`\"priority\"`, `\"analysis\"`, `\"threadId\"`, `\"title\"`, `\"summary\"`", Document);
        foreach (var key in new[] { "threadId", "title", "summary" })
            Assert.Contains($"\"{key}\"", Document);

        // the keys the prompt asks for are the keys the document defines
        var prompt = PriorityClassificationPrompt.Build("hello");
        foreach (var key in new[] { "priority", "analysis", "threadId", "title", "summary" })
            Assert.Contains($"\"{key}\":", prompt);
    }

    [Fact]
    public void EveryExampleAnswerInTheDocument_IsValidForTheParser()
    {
        var answers = ExampleAnswers();

        Assert.True(answers.Count >= 33, $"expected at least 33 example answers, found {answers.Count}");
        foreach (var answer in answers)
        {
            Assert.True(AiPriorityResultParser.TryParse(answer, out var result, out var error), $"Not accepted: {answer} ({error})");
            Assert.False(string.IsNullOrWhiteSpace(result!.Analysis));
            Assert.False(string.IsNullOrWhiteSpace(result.ThreadTitle));
            Assert.False(string.IsNullOrWhiteSpace(result.ThreadSummary));
            Assert.True(result.ThreadTitle!.Length <= 80, "title too long: " + answer);
            Assert.True(result.ThreadSummary!.Length <= 400, "summary too long: " + answer);
        }
    }

    [Fact]
    public void EveryExampleAnswer_HasExactlyTheFiveKeysInOrder()
    {
        foreach (var answer in ExampleAnswers())
        {
            using var json = System.Text.Json.JsonDocument.Parse(answer);
            Assert.Equal(new[] { "priority", "analysis", "threadId", "title", "summary" }, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        }
    }

    [Fact]
    public void ExamplesWithOpenIssues_OnlyLinkToIssuesListedInTheirOwnBlock()
    {
        var section = Document[Document.IndexOf("## 12. Examples with open issues", StringComparison.Ordinal)..];
        var examples = Regex.Split(section, @"(?m)^\*\*[A-Z] – ").Skip(1).ToList();
        Assert.True(examples.Count >= 8);

        foreach (var example in examples)
        {
            var answer = Regex.Match(example, @"(?m)^(\{""priority"".*\})$").Groups[1].Value;
            Assert.True(AiPriorityResultParser.TryParse(answer, out var result, out _));
            if (result!.ThreadId is { } id)
                Assert.Contains($"[issue {id}]", example);
        }
    }

    [Fact]
    public void Document_ExplainsFilesWithoutTextAndTheUnlimitedOpenIssues()
    {
        Assert.Contains("[The customer sent a photo without any text.]", Document);
        Assert.Contains("however old", Document);
        Assert.Contains("When in doubt, start a new issue", Document);
    }
}
