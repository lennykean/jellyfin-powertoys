using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed class WrapYear
{
    private const int AdultRating = 18;

    private WrapYear(
        WrapContext? server,
        IReadOnlyList<WrapContext> users,
        ItemIndex itemIndex,
        IReadOnlyList<Play> plays,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>?> visibleByUser)
    {
        Server = server;
        Users = users;
        ItemIndex = itemIndex;
        Plays = plays;
        VisibleByUser = visibleByUser;
    }

    public WrapContext? Server { get; }
    public IReadOnlyList<WrapContext> Users { get; }

    public ItemIndex ItemIndex { get; }
    public IReadOnlyList<Play> Plays { get; }
    public IReadOnlyDictionary<Guid, IReadOnlySet<Guid>?> VisibleByUser { get; }
    public static WrapYear Build(WrapSource source)
    {
        var itemIndex = new ItemIndex(source.Items);
        var knownUsers = source.Users.Select(u => u.Id).ToHashSet();
        var now = source.GeneratedAt.UtcDateTime;
        var plays = PlayPairer.Pair(
            [.. source.Activity.Where(r => knownUsers.Contains(r.UserId))],
            itemIndex,
            source.TimeZone,
            now);
        var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(source.Year, 1, 1), source.TimeZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(new DateTime(source.Year + 1, 1, 1), source.TimeZone);
        var window = plays.Where(p => p.Start >= start && p.Start < end && p.Start <= now).ToList();
        var included = source.Users.Where(u => window.Any(p => p.UserId == u.Id)).ToList();
        if (included.Count == 0)
        {
            return new WrapYear(null, [], itemIndex, [], new Dictionary<Guid, IReadOnlySet<Guid>?>());
        }

        var byUser = window.ToLookup(p => p.UserId);
        var priorByUser = plays.Where(p => p.Start < start).ToLookup(p => p.UserId);
        var firstRow = source.Activity.Min(r => r.Time);
        var dataStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(firstRow > start ? firstRow : start, source.TimeZone));

        (IReadOnlyList<UserStats> Users, GroupStats Groups, ServerStats Stats) ServerFor(Func<MediaItem, bool> isVisible)
        {
            var calculator = new StatsCalculator(itemIndex, source.Year, isVisible);
            var inServerOrder = included
                .Select(u => calculator.User(u, IsKid(u), [.. byUser[u.Id]], [.. priorByUser[u.Id]]))
                .ToList();
            var users = inServerOrder.OrderByDescending(u => u.Totals.Hours).ToList();
            var server = calculator.Server(source.ServerName, users, inServerOrder, window, source.Growth);
            return (users, calculator.Groups(users, window), server);
        }

        WrapContext Context((IReadOnlyList<UserStats> Users, GroupStats Groups, ServerStats Stats) server, Guid? viewer)
        {
            var me = server.Users.FirstOrDefault(u => u.Id == viewer);
            return new WrapContext(
                source.Year,
                source.GeneratedAt,
                dataStart,
                firstRow > start,
                server.Stats,
                server.Users,
                server.Groups,
                me,
                me is null ? null : Compare(me, server.Users, server.Groups, server.Stats));
        }

        var unrestricted = ServerFor(_ => true);
        var contexts = unrestricted.Users
            .Join(included, u => u.Id, u => u.Id, (_, u) => u)
            .Select(u => Context(u.Visible is not null ? ServerFor(item => u.Visible.Contains(item.Id)) : unrestricted, u.Id))
            .ToList();
        return new WrapYear(Context(unrestricted, null), contexts, itemIndex, window, included.ToDictionary(u => u.Id, u => u.Visible));
    }

    private static bool IsKid(SourceUser user)
    {
        return user.IsKidOverride ?? user.MaxParentalRating is < AdultRating;
    }

    private static Comparisons Compare(UserStats me, IReadOnlyList<UserStats> users, GroupStats groups, ServerStats server)
    {
        var rank = users.Select(u => u.Id).ToList().IndexOf(me.Id) + 1;
        var group = me.IsKid ? groups.Kids : groups.Grownups;
        return new Comparisons(
            rank,
            users.Count,
            me.Totals.Hours / server.Totals.Hours,
            groups.Grownups.Count > 0 ? me.Totals.Hours / groups.Grownups.HoursPerPerson : 0,
            me.Totals.Hours / group.HoursPerPerson,
            rank > 1 ? users[rank - 2].ToRef() : null,
            rank < users.Count ? users[rank].ToRef() : null);
    }
}
