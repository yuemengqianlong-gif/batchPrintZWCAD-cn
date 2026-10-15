# LA批量打印插件 v1.15.10 发布说明

本版本相对 **v1.15.9**，合并多文件统一入口（PR #3）加固、图框录入重入修复（#27）以及一批 Lxl 移植能力。适用于 ZWCAD、AutoCAD 2015–2024 与 AutoCAD 2025–2027。

## 相对 v1.15.9 的更新

### 多文件批打统一入口（PR #3）

- 「多文件批打」与「多文件批打（文件夹）」合并为统一入口。
- 新增 `DwgBatchFilePicker`：Vista `IFileOpenDialog` + 自定义「选择文件夹」按钮，同一对话框支持选择文件 / 选择文件夹 / 取消。
- 针对 Win10 下自定义按钮 `WM_COMMAND` 回调不触发的缺陷做双通道捕获，并以 `PostMessage` 异步关闭避免死锁；按 `IntPtr.Size` 选用 `SetWindowLong` / `SetWindowLongPtr`，在 Detach / NCDESTROY / 点文件夹时恢复原 WndProc。
- 选择文件夹时递归收集其中全部 DWG（含子文件夹）。

### 图框录入重入（Issue #27）

- 修复图框型录入相关重入问题：模态对话框与 `StartUserInteraction` 交互方案（scheme A），覆盖添加/编辑图框、字段框选、图框库管理等入口，避免二次进入卡死或交互失效。

### 打印预览与稳定性

- 预览支持 ESC 取消与自动重试（AutoCAD / ZWCAD Pipeline `maxAttempts=2`，空引擎重建，用户取消识别）。
- 临时序号 Overlay 增加 try/catch 与长度合理性检查；UCS 退化时坐标变换回退，降低异常图导致的崩溃。

### 设置与识别

- 比例设置支持删除/排除内置比例（`RemovedBuiltInScales`），纸张识别与匹配会尊重排除列表。
- 新增属性框/图框块名限定配置（设置页）：可按块名过滤扫描；提供图块属性名对应配置与拾取器。
- 图框块名过滤已接入扫描器；比例设置页签索引修正。

### 界面与功能区

- 留白下拉显示去掉「mm」后缀（数值语义仍为毫米）。
- 新增品牌化 LA 功能区（Tab「LA打印」/`ZBP_LA_PRINT_TAB` / `ZBP_LA_DISABLE_RIBBON`）；`MENUBAR=0` 时安装功能区不强行打开菜单栏；支持 `ZBP_INSTALL_RIBBON`。
- 通用型批打支持图号重排（复用图框型 `DrawingNumberReorderDialog`）。

## 安装说明

与以往版本相同：按平台解压对应 ZIP，按说明 NETLOAD 或使用自动加载；升级时请先关闭已加载旧 DLL 的 CAD 进程后再替换文件。

## 完整变更

详见仓库 `main` 自 `v1.15.9` 至本 tag 的提交记录。
