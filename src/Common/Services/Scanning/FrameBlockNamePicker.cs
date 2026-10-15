using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#else
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#endif

namespace ZwcadBatchPlot;

/// <summary>
/// 「属性框配置 → 图框块名限定设置」的图中拾取：
/// 用户点选一个图框块，读取其块名并追加（去重）到图框名限定列表，英文逗号分隔。
/// </summary>
public static class FrameBlockNamePicker
{
    public static bool PromptFrameBlockName(Document document, AppSettings settings, out AppSettings updated, out string message)
    {
        updated = settings;
        var editor = document.Editor;
        var result = editor.GetEntity(new PromptEntityOptions("\n选择图框块（读取其块名作为图框名限定）: ")
        {
            AllowNone = false
        });
        if (result.Status == PromptStatus.Cancel || result.Status == PromptStatus.None)
        {
            message = "已取消拾取。";
            return false;
        }

        if (result.Status != PromptStatus.OK)
        {
            message = "拾取失败，请重试。";
            return false;
        }

        string blockName;
        try
        {
            using var tr = document.Database.TransactionManager.StartTransaction();
            if (tr.GetObject(result.ObjectId, OpenMode.ForRead) is not BlockReference blockRef)
            {
                message = "所选对象不是图块。";
                return false;
            }

            blockName = (blockRef.Name ?? "").Trim();
            tr.Commit();
        }
        catch (Exception ex)
        {
            message = "读取块名失败：" + ex.Message;
            return false;
        }

        if (string.IsNullOrEmpty(blockName))
        {
            message = "该图块没有块名，无法用于限定。";
            return false;
        }

        // 匿名块/外部参照临时名没有限定价值，直接提示用户换普通图框块。
        if (blockName.StartsWith("*", StringComparison.Ordinal) || blockName.Contains('|'))
        {
            message = $"块名“{blockName}”是匿名块或外部参照临时名，不能作为图框名限定；请选择普通图框块。";
            return false;
        }

        var existing = (settings.FrameBlockNameFilter ?? "")
            .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (!existing.Any(x => string.Equals(x, blockName, StringComparison.OrdinalIgnoreCase)))
        {
            existing.Add(blockName);
        }

        settings.FrameBlockNameFilter = string.Join(",", existing);
        var saveError = (Exception?)null;
        AppSettingsStore.TrySave(settings, out saveError);
        if (saveError != null)
        {
            message = "图框名已读取为 " + blockName + "，但保存设置失败：" + saveError.Message;
            return false;
        }

        updated = AppSettingsStore.Load();
        var builder = new StringBuilder();
        builder.Append("已把块名“").Append(blockName).Append("”加入图框名限定：").Append(updated.FrameBlockNameFilter);
        message = builder.ToString();
        return true;
    }
}
