using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal static class SharedKeyGrouping
{
    // Two items that share a key are in one group, directly or through other items.
    public static IReadOnlyList<IReadOnlyList<T>> GroupBySharedKey<T, TKey>(this IEnumerable<T> items, Func<T, IEnumerable<TKey>> keysOf)
        where TKey : notnull
    {
        var list = items.ToList();
        var parents = Enumerable.Range(0, list.Count).ToArray();
        int Root(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }

            return index;
        }

        var firstWithKey = new Dictionary<TKey, int>();
        for (var index = 0; index < list.Count; index++)
        {
            foreach (var key in keysOf(list[index]))
            {
                if (!firstWithKey.TryAdd(key, index))
                {
                    parents[Root(index)] = Root(firstWithKey[key]);
                }
            }
        }

        return [.. Enumerable.Range(0, list.Count).GroupBy(Root, index => list[index]).Select(group => (IReadOnlyList<T>)[.. group])];
    }
}
