using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Services;

/// <summary>Builds the statistics shown on the main (dashboard) page from the forwarded-message history.</summary>
public class DashboardService
{
    public const int DefaultDays = 30;
    public const int MaxDays = 365;

    private readonly ICustomerRepository _customers;
    private readonly ICustomerGroupedMessageRepository _messages;
    private readonly TimeProvider _time;

    public DashboardService(ICustomerRepository customers, ICustomerGroupedMessageRepository messages, TimeProvider time)
    {
        _customers = customers;
        _messages = messages;
        _time = time;
    }

    /// <param name="days">Length of the daily series (clamped to 1..365), ending today in Iran time.</param>
    public async Task<DashboardDto> GetAsync(int days = DefaultDays, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, MaxDays);

        var customers = await _customers.GetSummariesAsync(cancellationToken);
        var counts = await _messages.GetCountsAsync(cancellationToken);

        var today = PersianDateFormatter.ToIranTime(_time.GetUtcNow().UtcDateTime).Date;
        var firstDay = today.AddDays(-(days - 1));
        var sinceUtc = DateTime.SpecifyKind(firstDay - PersianDateFormatter.IranOffset, DateTimeKind.Utc);
        var recent = await _messages.GetSinceAsync(sinceUtc, cancellationToken);

        return new DashboardDto(
            customers.Count,
            counts.Sum(c => c.Count),
            counts.Where(c => !c.IsResolved).Sum(c => c.Count),
            BuildByPriority(counts),
            BuildByCustomer(customers, counts),
            BuildDaily(recent, firstDay, days),
            days);
    }

    private static IReadOnlyList<PriorityCountDto> BuildByPriority(IReadOnlyList<MessageCountRow> counts) =>
        PriorityHelper.Ordered
            .Select(p => new PriorityCountDto(
                p,
                counts.Where(c => c.Priority == p).Sum(c => c.Count),
                counts.Where(c => c.Priority == p && !c.IsResolved).Sum(c => c.Count)))
            .ToList();

    private static IReadOnlyList<CustomerMessageStatsDto> BuildByCustomer(
        IReadOnlyList<CustomerSummary> customers,
        IReadOnlyList<MessageCountRow> counts)
    {
        return customers
            .Select(c =>
            {
                var own = counts.Where(r => r.CustomerId == c.Id).ToList();
                return new CustomerMessageStatsDto(
                    c.Id,
                    c.Name,
                    own.Where(r => r.Priority == GroupPriority.Low).Sum(r => r.Count),
                    own.Where(r => r.Priority == GroupPriority.Medium).Sum(r => r.Count),
                    own.Where(r => r.Priority == GroupPriority.Critical).Sum(r => r.Count),
                    own.Where(r => !r.IsResolved).Sum(r => r.Count));
            })
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.CustomerName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static IReadOnlyList<DailyMessageCountDto> BuildDaily(IReadOnlyList<MessageTimeRow> rows, DateTime firstDay, int days)
    {
        var byDay = rows
            .GroupBy(r => PersianDateFormatter.ToIranTime(r.CreatedAtUtc).Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Enumerable.Range(0, days)
            .Select(offset =>
            {
                var day = firstDay.AddDays(offset);
                var list = byDay.TryGetValue(day, out var items) ? items : new List<MessageTimeRow>();
                return new DailyMessageCountDto(
                    day,
                    list.Count(r => r.Priority == GroupPriority.Low),
                    list.Count(r => r.Priority == GroupPriority.Medium),
                    list.Count(r => r.Priority == GroupPriority.Critical));
            })
            .ToList();
    }
}
