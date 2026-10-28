using System.ComponentModel.DataAnnotations;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Enums;
using SiloSupport.Tests.Support;

namespace SiloSupport.Tests;

/// <summary>Dashboard statistics on the real repositories (SQLite in-memory). The fake clock is 2026-10-27 09:00 UTC = 12:30 in Iran.</summary>
public class DashboardServiceTests : TestBase
{
    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dashboard_WithoutMessages_ReturnsZerosAndAllPriorities()
    {
        await AddCustomerAsync("A");

        var dashboard = await Get<DashboardService>().GetAsync();

        Assert.Equal(1, dashboard.CustomerCount);
        Assert.Equal(0, dashboard.TotalMessages);
        Assert.Equal(0, dashboard.UnresolvedMessages);
        Assert.Equal(0, dashboard.ResolvedMessages);
        Assert.Equal(
            new[] { GroupPriority.Critical, GroupPriority.Medium, GroupPriority.Low },
            dashboard.ByPriority.Select(p => p.Priority).ToArray());
        Assert.All(dashboard.ByPriority, p => Assert.Equal(0, p.Total));
        Assert.Equal(DashboardService.DefaultDays, dashboard.Daily.Count);
        Assert.All(dashboard.Daily, d => Assert.Equal(0, d.Total));
    }

    [Fact]
    public async Task Dashboard_CountsMessagesPerPriorityAndPerCustomer_AndOnlyThoseOfEachCustomer()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        var c = await AddCustomerAsync("C"); // no groups, no messages
        var aLow = await AddGroupAsync(a.Id, GroupPriority.Low, 10);
        var aCritical = await AddGroupAsync(a.Id, GroupPriority.Critical, 11);
        var bMedium = await AddGroupAsync(b.Id, GroupPriority.Medium, 12);

        var resolved = await AddMessageAsync(aLow.Id, "a1");
        await AddMessageAsync(aLow.Id, "a2");
        await AddMessageAsync(aCritical.Id, "a3");
        for (var i = 0; i < 4; i++)
            await AddMessageAsync(bMedium.Id, $"b{i}");
        Assert.True((await Get<ConversationService>().SetResolvedAsync(a.Id, resolved.Id, true)).Succeeded);

        var dashboard = await Get<DashboardService>().GetAsync();

        Assert.Equal(3, dashboard.CustomerCount);
        Assert.Equal(7, dashboard.TotalMessages);
        Assert.Equal(6, dashboard.UnresolvedMessages);
        Assert.Equal(1, dashboard.ResolvedMessages);

        var critical = dashboard.ByPriority.Single(p => p.Priority == GroupPriority.Critical);
        var medium = dashboard.ByPriority.Single(p => p.Priority == GroupPriority.Medium);
        var low = dashboard.ByPriority.Single(p => p.Priority == GroupPriority.Low);
        Assert.Equal((1, 1), (critical.Total, critical.Unresolved));
        Assert.Equal((4, 4), (medium.Total, medium.Unresolved));
        Assert.Equal((2, 1), (low.Total, low.Unresolved));

        // Most active customer first; customers without messages are still listed (last).
        Assert.Equal(new[] { "B", "A", "C" }, dashboard.ByCustomer.Select(s => s.CustomerName).ToArray());
        var statsB = dashboard.ByCustomer[0];
        Assert.Equal((0, 4, 0, 4, 4), (statsB.Low, statsB.Medium, statsB.Critical, statsB.Total, statsB.Unresolved));
        var statsA = dashboard.ByCustomer[1];
        Assert.Equal((2, 0, 1, 3, 2), (statsA.Low, statsA.Medium, statsA.Critical, statsA.Total, statsA.Unresolved));
        Assert.Equal(0, dashboard.ByCustomer[2].Total);
    }

    [Fact]
    public async Task Dashboard_DailySeries_UsesIranLocalDaysAndTheWindowBoundaries()
    {
        var customer = await AddCustomerAsync("A");
        var low = await AddGroupAsync(customer.Id, GroupPriority.Low, 10);
        var critical = await AddGroupAsync(customer.Id, GroupPriority.Critical, 11);

        await AddMessageAsync(low.Id, "today noon", Utc(10, 27, 9));              // 27th 12:30 local
        await AddMessageAsync(low.Id, "just after midnight local", Utc(10, 26, 21)); // 27th 00:30 local
        await AddMessageAsync(critical.Id, "late evening", Utc(10, 26, 20));      // 26th 23:30 local
        await AddMessageAsync(critical.Id, "first minute of the window", Utc(10, 24, 20, 30)); // 25th 00:00 local
        await AddMessageAsync(critical.Id, "one minute before the window", Utc(10, 24, 20, 29)); // 24th 23:59 local

        var dashboard = await Get<DashboardService>().GetAsync(days: 3);

        Assert.Equal(3, dashboard.Days);
        Assert.Equal(
            new[] { new DateTime(2026, 10, 25), new DateTime(2026, 10, 26), new DateTime(2026, 10, 27) },
            dashboard.Daily.Select(d => d.LocalDate).ToArray());
        Assert.Equal((0, 0, 1), (dashboard.Daily[0].Low, dashboard.Daily[0].Medium, dashboard.Daily[0].Critical));
        Assert.Equal((0, 0, 1), (dashboard.Daily[1].Low, dashboard.Daily[1].Medium, dashboard.Daily[1].Critical));
        Assert.Equal((2, 0, 0), (dashboard.Daily[2].Low, dashboard.Daily[2].Medium, dashboard.Daily[2].Critical));

        // The all-time totals are independent of the daily window.
        Assert.Equal(5, dashboard.TotalMessages);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    [InlineData(1000, 365)]
    public async Task Dashboard_ClampsTheNumberOfDays(int requested, int expected)
    {
        var dashboard = await Get<DashboardService>().GetAsync(requested);

        Assert.Equal(expected, dashboard.Days);
        Assert.Equal(expected, dashboard.Daily.Count);
    }

    [Fact]
    public async Task MessageRepository_CountsAndSince_AreAggregatedPerCustomer()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        var aGroup = await AddGroupAsync(a.Id, GroupPriority.Low, 10);
        var bGroup = await AddGroupAsync(b.Id, GroupPriority.Low, 11);
        await AddMessageAsync(aGroup.Id, "1", Utc(10, 20, 10));
        await AddMessageAsync(aGroup.Id, "2", Utc(10, 26, 10));
        await AddMessageAsync(bGroup.Id, "3", Utc(10, 26, 11));

        var repository = Get<ICustomerGroupedMessageRepository>();
        var counts = await repository.GetCountsAsync();
        var since = await repository.GetSinceAsync(Utc(10, 25, 0).UtcDateTime);

        Assert.Equal(2, counts.Single(c => c.CustomerId == a.Id).Count);
        Assert.Equal(1, counts.Single(c => c.CustomerId == b.Id).Count);
        Assert.Equal(2, since.Count);
        Assert.All(since, row => Assert.Equal(GroupPriority.Low, row.Priority));
        Assert.All(since, row => Assert.Equal(DateTimeKind.Utc, row.CreatedAtUtc.Kind));
    }
}

public class PersianPresentationTests
{
    [Fact]
    public void FormatUtc_ConvertsToIranTimeAndTheJalaliCalendar()
    {
        // 2026-10-27 09:00 UTC is 12:30 in Iran, 5 Aban 1405.
        Assert.Equal("1405/08/05 12:30", PersianDateFormatter.FormatUtc(new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void FormatUtc_RollsOverToTheNextJalaliDayAfterLocalMidnight()
    {
        // 21:00 UTC is 00:30 of the next day in Iran.
        Assert.Equal("1405/08/06 00:30", PersianDateFormatter.FormatUtc(new DateTime(2026, 10, 27, 21, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void FormatUtc_TreatsUnspecifiedKindAsUtc()
    {
        Assert.Equal("1405/08/05 12:30", PersianDateFormatter.FormatUtc(new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void FormatLocalDate_IsZeroPadded()
    {
        // 21 March 2026 is 1 Farvardin 1405 (Nowruz).
        Assert.Equal("1405/01/01", PersianDateFormatter.FormatLocalDate(new DateTime(2026, 3, 21)));
    }

    [Theory]
    [InlineData(GroupPriority.Critical, "بحرانی")]
    [InlineData(GroupPriority.Medium, "متوسط")]
    [InlineData(GroupPriority.Low, "کم")]
    public void PriorityDisplayName_IsPersian(GroupPriority priority, string expected)
    {
        Assert.Equal(expected, PriorityHelper.DisplayName(priority));
    }

    [Fact]
    public void PriorityDisplayName_DoesNotChangeTheStoredEnglishNames()
    {
        Assert.Equal("Critical", GroupPriority.Critical.ToString());
        Assert.True(PriorityHelper.TryParse("Critical", out _));
    }
}

public class MemberInputValidationTests
{
    private static IList<ValidationResult> Validate(MemberInput input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("4503599627370495")]
    [InlineData("user_42")]
    [InlineData("  777  ")]
    public void BaleId_AcceptsTextIds(string baleId)
    {
        Assert.Empty(Validate(new MemberInput { Name = "Sara", BaleId = baleId }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("12 34")]
    public void BaleId_RejectsEmptyZeroOrIdsContainingSpaces(string baleId)
    {
        var errors = Validate(new MemberInput { Name = "Sara", BaleId = baleId });

        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Contains("شناسه بله", e.ErrorMessage));
    }

    [Fact]
    public void BaleId_IsLimitedTo64Characters()
    {
        Assert.Empty(Validate(new MemberInput { Name = "Sara", BaleId = new string('1', 64) }));
        Assert.NotEmpty(Validate(new MemberInput { Name = "Sara", BaleId = new string('1', 65) }));
    }

    [Fact]
    public void ValidationMessages_ArePersian()
    {
        var errors = Validate(new MemberInput { Name = "", BaleId = "" });

        Assert.Contains(errors, e => e.ErrorMessage == "نام الزامی است.");
        Assert.Contains(errors, e => e.ErrorMessage == "شناسه بله الزامی است.");
    }
}
