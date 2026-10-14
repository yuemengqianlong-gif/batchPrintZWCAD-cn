using System;
using System.Collections.Generic;
#if AUTOCAD
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#else
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#endif

namespace ZwcadBatchPlot;

/// <summary>
/// 扫描候选对象的公共类型过滤。
/// 「扫描当前图」范围枚举与「框选扫描」GetSelection 共用同一套规则，先筛合适实体再识别。
/// </summary>
internal static class ScanCandidateFilter
{
    /// <summary>图框块允许的 DXF 类型名（仅 INSERT）。</summary>
    public static string[] TitleBlockDxfNames() => new[] { "INSERT" };

    /// <summary>
    /// 矩形图框允许的 DXF 类型名：块参照与各类多段线；开启四线识别时含 LINE。
    /// </summary>
    /// <param name="includeLines">是否把直线纳入候选类型。</param>
    public static string[] RectangleFrameDxfNames(bool includeLines)
    {
        return includeLines
            ? new[] { "INSERT", "LWPOLYLINE", "POLYLINE", "3DPOLYLINE", "LINE" }
            : new[] { "INSERT", "LWPOLYLINE", "POLYLINE", "3DPOLYLINE" };
    }

    /// <summary>图框块 GetSelection 过滤器。</summary>
    public static SelectionFilter TitleBlockSelectionFilter()
        => CreateStartTypeFilter(TitleBlockDxfNames());

    /// <summary>矩形图框 GetSelection 过滤器。</summary>
    /// <param name="includeLines">是否把直线纳入可选类型。</param>
    public static SelectionFilter RectangleFrameSelectionFilter(bool includeLines)
        => CreateStartTypeFilter(RectangleFrameDxfNames(includeLines));

    /// <summary>判断对象是否为图框块扫描候选（块参照）。</summary>
    /// <param name="obj">事务内打开的数据库对象。</param>
    public static bool IsTitleBlockCandidate(DBObject? obj) => obj is BlockReference blockRef && HasValidDefinition(blockRef);

    /// <summary>
    /// 块参照是否指向有效块定义。损坏图纸里可能有 BlockTableRecord 为空的 INSERT，
    /// 打开其定义会抛 eNullObjectId；这类对象一律不当作候选，避免拖垮整次扫描。
    /// </summary>
    public static bool HasValidDefinition(BlockReference? blockRef)
    {
        if (blockRef == null)
        {
            return false;
        }

        try
        {
            return !blockRef.BlockTableRecord.IsNull;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断对象是否为矩形图框扫描候选（块参照 / 多段线；可选直线）。
    /// </summary>
    /// <param name="obj">事务内打开的数据库对象。</param>
    /// <param name="includeLines">是否把直线视为候选。</param>
    public static bool IsRectangleFrameCandidate(DBObject? obj, bool includeLines)
    {
        if (obj is BlockReference blockRef)
        {
            return HasValidDefinition(blockRef);
        }

        if (obj is Polyline or Polyline2d or Polyline3d)
        {
            return true;
        }

        return includeLines && obj is Line;
    }

    /// <summary>
    /// 在单个布局空间内收集符合类型过滤的顶层 ObjectId。
    /// </summary>
    /// <param name="tr">已打开事务。</param>
    /// <param name="owner">布局对应的块表记录。</param>
    /// <param name="isCandidate">类型判定；返回 false 的对象跳过。</param>
    /// <param name="results">输出收集结果。</param>
    public static void CollectFromSpace(
        Transaction tr,
        BlockTableRecord owner,
        Func<DBObject, bool> isCandidate,
        ICollection<ObjectId> results)
    {
        foreach (ObjectId id in owner)
        {
            DBObject? obj;
            try
            {
                obj = tr.GetObject(id, OpenMode.ForRead, false);
            }
            catch
            {
                continue;
            }

            if (obj == null || !isCandidate(obj))
            {
                continue;
            }

            results.Add(id);
        }
    }

    /// <summary>
    /// 按布局判定回调遍历数据库，收集符合类型过滤的顶层 ObjectId。
    /// </summary>
    /// <param name="db">目标图纸数据库。</param>
    /// <param name="shouldIncludeLayout">是否纳入该布局。</param>
    /// <param name="isCandidate">实体类型判定。</param>
    public static List<ObjectId> CollectInLayouts(
        Database db,
        Func<Layout, BlockTableRecord, bool> shouldIncludeLayout,
        Func<DBObject, bool> isCandidate)
    {
        var results = new List<ObjectId>();
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId recordId in blockTable)
        {
            var owner = (BlockTableRecord)tr.GetObject(recordId, OpenMode.ForRead);
            if (!owner.IsLayout || owner.LayoutId.IsNull)
            {
                continue;
            }

            Layout? layout;
            try
            {
                layout = tr.GetObject(owner.LayoutId, OpenMode.ForRead, false) as Layout;
            }
            catch
            {
                continue;
            }

            if (layout == null || !shouldIncludeLayout(layout, owner))
            {
                continue;
            }

            CollectFromSpace(tr, owner, isCandidate, results);
        }

        tr.Commit();
        return results;
    }

    /// <summary>由图框块 DXF 类型名构建 SelectionFilter。</summary>
    private static SelectionFilter CreateStartTypeFilter(IReadOnlyList<string> dxfNames)
    {
        return new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Start, string.Join(",", dxfNames))
        });
    }
}
