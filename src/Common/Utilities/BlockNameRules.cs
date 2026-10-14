using System;

namespace ZwcadBatchPlot;

/// <summary>
/// 与 CAD API 无关的纯规则：块名可读性判断、长循环进度节流。单独成文件便于单元测试直接链接编译。
/// </summary>
public static class BlockNameRules
{
    /// <summary>
    /// 所有者块名是否可作为“可读块名”参与文字候选：空名（含损坏的空定义引用）、
    /// 匿名块（*U/*D 等）、天正/动态块生成名（A$C…、含 $0$）都不算。
    /// </summary>
    public static bool IsReadableOwnerBlockName(string? blockName)
    {
        if (string.IsNullOrWhiteSpace(blockName))
        {
            return false;
        }

        var trimmed = blockName!.Trim();
        if (trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("A$C", StringComparison.OrdinalIgnoreCase)
            || trimmed.IndexOf("$0$", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 长循环中是否到了上报进度 / 检查取消的节点：第 0 个及之后每 <paramref name="every"/> 个一次。
    /// every 不大于 0 时视为每次都上报。
    /// </summary>
    public static bool ShouldReportProgress(int index, int every)
    {
        if (index < 0)
        {
            return false;
        }

        return every <= 0 || index % every == 0;
    }
}
