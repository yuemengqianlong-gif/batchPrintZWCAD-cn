using System;

namespace ZwcadBatchPlot;

/// <summary>
/// 纸张下拉框 SelectionChanged 的过滤规则（纯逻辑，便于单元测试）。
/// 表格重建时下拉框重新绑定也会触发该事件，需要与用户真实改纸区分开。
/// </summary>
public static class PaperChoiceChangeRules
{
    private const double SizeToleranceMm = 0.001;

    /// <summary>
    /// 是否应当按“用户改纸”处理：必须有被替换掉的旧选项（初次绑定时为 0），
    /// 且用户正在操作该下拉框（下拉展开或键盘焦点在其中）。
    /// </summary>
    public static bool ShouldHandleSelectionChange(int removedItemCount, bool userInteracting)
        => removedItemCount > 0 && userInteracting;

    /// <summary>目标纸张与作业当前纸张是否相同（名称、比例、宽高、是否自定义纸）；相同则无需重算。</summary>
    public static bool IsSamePaper(
        string? currentName,
        string? currentScale,
        double currentWidthMm,
        double currentHeightMm,
        bool currentRequiresCustom,
        string? newName,
        string? newScale,
        double newWidthMm,
        double newHeightMm,
        bool newRequiresCustom)
        => string.Equals(currentName ?? "", newName ?? "", StringComparison.Ordinal)
           && string.Equals(currentScale ?? "", newScale ?? "", StringComparison.Ordinal)
           && Math.Abs(currentWidthMm - newWidthMm) <= SizeToleranceMm
           && Math.Abs(currentHeightMm - newHeightMm) <= SizeToleranceMm
           && currentRequiresCustom == newRequiresCustom;
}
