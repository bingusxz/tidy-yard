using System.IO;
using TidyYard.Models;
namespace TidyYard.Services;

public static class FenceSorting
{
    public static IOrderedEnumerable<FileSystemInfo> Sort(IEnumerable<FileSystemInfo> entries, FenceSort sort, bool descending)
    {
        var foldersFirst = entries.OrderBy(e => e is FileInfo);
        IOrderedEnumerable<FileSystemInfo> By<TKey>(Func<FileSystemInfo, TKey> key, IComparer<TKey>? comparer = null) =>
            descending ? foldersFirst.ThenByDescending(key, comparer) : foldersFirst.ThenBy(key, comparer);
        var result = sort switch
        {
            FenceSort.DateModified => By(e => e.LastWriteTimeUtc),
            FenceSort.Type => By(e => e.Extension, StringComparer.CurrentCultureIgnoreCase),
            FenceSort.Size => By(e => e is FileInfo file ? file.Length : 0L),
            _ => By(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        return result.ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
    }
}
