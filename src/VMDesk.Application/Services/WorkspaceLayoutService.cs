using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>Workspace tiling layout engine (spec §13, §44). No RDP knowledge.</summary>
public sealed class WorkspaceLayoutService : IWorkspaceLayoutService
{
    private const double Gap = 6;

    public IReadOnlyList<LayoutRect> ComputeLayout(int sessionCount, int availableWidth, int availableHeight, WorkspaceTilingMode mode)
    {
        if (sessionCount <= 0)
        {
            return Array.Empty<LayoutRect>();
        }

        int columns;
        int rows;

        if (mode == WorkspaceTilingMode.Auto || mode == WorkspaceTilingMode.One)
        {
            (columns, rows) = sessionCount switch
            {
                1 => (1, 1),
                2 => (2, 1),
                3 or 4 => (2, 2),
                5 or 6 => (3, 2),
                7 or 8 => (4, 2),
                9 => (3, 3),
                _ => ((int)Math.Ceiling(Math.Sqrt(sessionCount)), (int)Math.Ceiling((double)sessionCount / Math.Ceiling(Math.Sqrt(sessionCount))))
            };
        }
        else
        {
            (columns, rows) = mode switch
            {
                WorkspaceTilingMode.TwoHorizontal => (2, 1),
                WorkspaceTilingMode.TwoVertical => (1, 2),
                WorkspaceTilingMode.Grid4 => (2, 2),
                WorkspaceTilingMode.Grid6 => (3, 2),
                _ => (1, 1)
            };
        }

        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        var rects = new List<LayoutRect>(sessionCount);
        var cellW = (availableWidth - Gap * (columns - 1)) / columns;
        var cellH = (availableHeight - Gap * (rows - 1)) / rows;

        for (var i = 0; i < sessionCount; i++)
        {
            var r = i / columns;
            var c = i % columns;
            var w = c == columns - 1 ? cellW : cellW;
            var h = r == rows - 1 ? cellH : cellH;
            rects.Add(new LayoutRect(c * (cellW + Gap), r * (cellH + Gap), w, h));
        }

        return rects;
    }
}
