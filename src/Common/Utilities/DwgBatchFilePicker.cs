using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace ZwcadBatchPlot;

/// <summary>
/// 「多文件批打」统一入口对话框：Vista 文件对话框 + 自定义「选择文件夹」按钮。
/// 点「选择文件」返回多选的 DWG 列表；点「选择文件夹」返回文件列表里选中的文件夹，
/// 未选中文件夹时取当前所在文件夹；取消返回 null。
/// </summary>
public static class DwgBatchFilePicker
{
    internal const int FolderButtonId = 1001;

    /// <summary>打开选择框。<paramref name="seedFolder"/> 存在时作为初始目录。</summary>
    public static DwgBatchPickResult? Pick(IntPtr ownerHandle, string? seedFolder, string title)
    {
        IFileOpenDialog? dialog;
        try
        {
            dialog = CreateConfiguredDialog(seedFolder, title);
        }
        catch (Exception ex)
        {
            LogFailure("CreateConfiguredDialog", ex);
            dialog = null;
        }

        if (dialog == null)
        {
            return PickWithSystemDialog(title);
        }

        var events = new DwgBatchFilePickerEvents(dialog, title);
        try
        {
            int adviseHr = dialog.Advise(events, out uint cookie);
            if (adviseHr < 0)
            {
                LogFailure($"Advise failed HRESULT=0x{adviseHr:X8}");
                return PickWithSystemDialog(title);
            }

            int hr;
            try
            {
                hr = dialog.Show(ownerHandle);
            }
            finally
            {
                try
                {
                    dialog.Unadvise(cookie);
                }
                catch (Exception ex)
                {
                    LogFailure("Unadvise", ex);
                }

                events.Detach();
            }

            // 选择文件夹时对话框按「取消」关闭（hr 非 0），这里以标志位优先判定。
            if (events.FolderChosen)
            {
                return DwgBatchPickResult.ForFolder(events.FolderPath!);
            }

            if (hr != 0)
            {
                return null;
            }

            var files = CollectSelectedFiles(dialog);
            return files.Count == 0 ? null : DwgBatchPickResult.ForFiles(files);
        }
        catch (Exception ex)
        {
            LogFailure("Pick", ex);
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    internal static void LogFailure(string where, Exception? ex = null)
    {
        var msg = ex == null
            ? $"[DwgBatchFilePicker] {where}"
            : $"[DwgBatchFilePicker] {where}: {ex}";
        Trace.WriteLine(msg);
        Debug.WriteLine(msg);
    }

    private static IFileOpenDialog CreateConfiguredDialog(string? seedFolder, string title)
    {
        var clsid = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        var type = Type.GetTypeFromCLSID(clsid);
        var dialog = (IFileOpenDialog)Activator.CreateInstance(type!)!;

        var filters = new[]
        {
            new COMDLG_FILTERSPEC { pszName = "DWG 文件 (*.dwg)", pszSpec = "*.dwg" }
        };
        dialog.SetFileTypes((uint)filters.Length, filters);
        dialog.SetFileTypeIndex(1);
        dialog.SetOptions((uint)(Fos.NoChangeDir | Fos.ForceFileSystem | Fos.PathMustExist
            | Fos.FileMustExist | Fos.AllowMultiSelect));
        dialog.SetTitle(title);
        dialog.SetOkButtonLabel("选择文件");

        if (!string.IsNullOrWhiteSpace(seedFolder) && Directory.Exists(seedFolder))
        {
            TrySetSeedFolder(dialog, seedFolder!);
        }

        ((IFileDialogCustomize)dialog).AddPushButton(FolderButtonId, "选择文件夹");
        return dialog;
    }

    private static void TrySetSeedFolder(IFileOpenDialog dialog, string folder)
    {
        IShellItem? item = null;
        try
        {
            // shell 解析对正斜杠路径会失败（E_INVALIDARG），统一成 Windows 分隔符。
            var normalized = folder.Replace('/', '\\');
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(normalized, IntPtr.Zero, ref iid, out item);
            if (item != null)
            {
                dialog.SetFolder(item);
            }
        }
        catch (Exception ex)
        {
            LogFailure("TrySetSeedFolder", ex);
        }
        finally
        {
            if (item != null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    private static List<string> CollectSelectedFiles(IFileOpenDialog dialog)
    {
        var files = new List<string>();
        IShellItemArray? results = null;
        try
        {
            results = dialog.GetResults();
            results.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                IShellItem? item = null;
                try
                {
                    results.GetItemAt(i, out item);
                    var path = TryGetFileSystemPath(item);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        files.Add(Path.GetFullPath(path!));
                    }
                }
                catch (Exception ex)
                {
                    LogFailure($"CollectSelectedFiles[{i}]", ex);
                }
                finally
                {
                    if (item != null)
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogFailure("CollectSelectedFiles", ex);
        }
        finally
        {
            if (results != null)
            {
                Marshal.ReleaseComObject(results);
            }
        }

        return files;
    }

    internal static string? TryGetFileSystemPath(IShellItem? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            item.GetDisplayName(Sigdn.FileSystemPath, out var path);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex)
        {
            LogFailure("TryGetFileSystemPath", ex);
            return null;
        }
    }

    /// <summary>自定义对话框创建失败时退回普通文件多选框，保证多文件批打始终可用。</summary>
    private static DwgBatchPickResult? PickWithSystemDialog(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "DWG 文件 (*.dwg)|*.dwg",
            Multiselect = true,
            Title = title
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var files = dialog.FileNames
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return files.Count == 0 ? null : DwgBatchPickResult.ForFiles(files);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}

/// <summary>多文件批打选择结果：<see cref="IsFolder"/> 为 true 时用 <see cref="FolderPath"/>，否则用 <see cref="Files"/>。</summary>
public sealed class DwgBatchPickResult
{
    private DwgBatchPickResult(bool isFolder, string? folderPath, IReadOnlyList<string> files)
    {
        IsFolder = isFolder;
        FolderPath = folderPath;
        Files = files;
    }

    public bool IsFolder { get; }

    public string? FolderPath { get; }

    public IReadOnlyList<string> Files { get; }

    public static DwgBatchPickResult ForFolder(string folderPath) => new(true, folderPath, Array.Empty<string>());

    public static DwgBatchPickResult ForFiles(IReadOnlyList<string> files) => new(false, null, files);
}

/// <summary>
/// 文件对话框事件接收器（CCW），负责「选择文件夹」按钮点击。
/// 双通道：① 标准 IFileDialogControlEvents 回调；② 兜底子类化对话框窗口捕获按钮的
/// WM_COMMAND（部分 Windows 上系统不会回调 OnButtonClicked，按钮的 HWND 控件号恒为 0，
/// 只以 WM_COMMAND 到达对话框窗口，故需自行接管）。两条通道用 _folderPicked 互斥。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DwgBatchFilePickerEvents : IFileDialogEvents, IFileDialogControlEvents
{
    private const int GwlWndProc = -4;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmFolderClickAsync = 0x8000 + 0x7E;

    private readonly IFileOpenDialog _dialog;
    private readonly string _title;
    private bool _folderPicked;
    private bool _subclassInstalled;
    private IntPtr _dialogHwnd;
    private IntPtr _buttonHwnd;
    private IntPtr _origWndProc;
    private SubclassProc? _wndProcDelegate;

    public DwgBatchFilePickerEvents(IFileOpenDialog dialog, string title)
    {
        _dialog = dialog;
        _title = title;
    }

    public bool FolderChosen { get; private set; }

    public string? FolderPath { get; private set; }

    /// <summary>Show 结束后恢复原 WndProc，避免窗口被销毁后仍挂着托管委托。</summary>
    public void Detach()
    {
        UninstallSubclass();
    }

    public int OnFileOk(IFileDialog pfd) => 0;

    public int OnFolderChanging(IFileDialog pfd, IShellItem psiFolder)
    {
        TryInstallSubclass();
        return 0;
    }

    public int OnFolderChange(IFileDialog pfd)
    {
        TryInstallSubclass();
        return 0;
    }

    public int OnSelectionChange(IFileDialog pfd)
    {
        TryInstallSubclass();
        return 0;
    }

    public int OnShareViolation(IFileDialog pfd, IShellItem psi, out int pResponse)
    {
        pResponse = 0;
        return 0;
    }

    public int OnTypeChange(IFileDialog pfd)
    {
        TryInstallSubclass();
        return 0;
    }

    public int OnOverwrite(IFileDialog pfd, IShellItem psi, out int pResponse)
    {
        pResponse = 0;
        return 0;
    }

    public int OnItemSelected(IFileDialogCustomize pfdc, int dwIDCtl, int dwIDItem) => HandleControlEvent(dwIDCtl);

    public int OnButtonClicked(IFileDialogCustomize pfdc, int dwIDCtl) => HandleControlEvent(dwIDCtl);

    public int OnCheckButtonToggled(IFileDialogCustomize pfdc, int dwIDCtl, bool bChecked) => HandleControlEvent(dwIDCtl);

    public int OnControlActivating(IFileDialogCustomize pfdc, int dwIDCtl) => HandleControlEvent(dwIDCtl);

    private int HandleControlEvent(int dwIDCtl)
    {
        if (dwIDCtl != DwgBatchFilePicker.FolderButtonId)
        {
            return 0;
        }

        HandleFolderClick(_dialogHwnd);
        return 0;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>子类化对话框窗口（与 Show 同线程，于文件夹事件回调中安装，安全）。</summary>
    private void TryInstallSubclass()
    {
        if (_subclassInstalled)
        {
            return;
        }

        try
        {
            var hwnd = FindDialogWindow();
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var button = FindFolderButton(hwnd);
            if (button == IntPtr.Zero)
            {
                return;
            }

            _dialogHwnd = hwnd;
            _buttonHwnd = button;
            _wndProcDelegate = DialogWndProc;
            var newProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _origWndProc = SetWindowLongPtr(hwnd, GwlWndProc, newProc);
            if (_origWndProc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                // ERROR_SUCCESS with null prev is rare; treat zero as failure only if GetLastError != 0
                if (err != 0)
                {
                    DwgBatchFilePicker.LogFailure($"SetWindowLongPtr failed Win32={err}");
                    _wndProcDelegate = null;
                    return;
                }
            }

            _subclassInstalled = true;
        }
        catch (Exception ex)
        {
            DwgBatchFilePicker.LogFailure("TryInstallSubclass", ex);
        }
    }

    private void UninstallSubclass()
    {
        if (!_subclassInstalled || _dialogHwnd == IntPtr.Zero)
        {
            _subclassInstalled = false;
            return;
        }

        try
        {
            if (_origWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_dialogHwnd, GwlWndProc, _origWndProc);
            }
        }
        catch (Exception ex)
        {
            DwgBatchFilePicker.LogFailure("UninstallSubclass", ex);
        }
        finally
        {
            _subclassInstalled = false;
            _origWndProc = IntPtr.Zero;
            // 保留 _wndProcDelegate 至方法结束，避免恢复瞬间 GC 回收委托
            _wndProcDelegate = null;
            _dialogHwnd = IntPtr.Zero;
            _buttonHwnd = IntPtr.Zero;
        }
    }

    private IntPtr FindDialogWindow()
    {
        var found = IntPtr.Zero;
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, lp) =>
        {
            var className = GetClassNameOf(hwnd);
            var text = GetWindowTextOf(hwnd);
            // 优先：标题匹配 + 标准对话框类；其次仅标题 Contains（兼容旧行为）
            if (text.Length > 0 && text.Contains(_title))
            {
                if (className == "#32770" || FindFolderButton(hwnd) != IntPtr.Zero)
                {
                    found = hwnd;
                    return false;
                }

                if (found == IntPtr.Zero)
                {
                    found = hwnd;
                }
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// 找「选择文件夹」按钮：优先控件 ID（AddPushButton 的 FolderButtonId），
    /// 部分系统上 GetDlgCtrlID 恒为 0 时再按文案兜底。
    /// </summary>
    private static IntPtr FindFolderButton(IntPtr parent)
    {
        var byId = IntPtr.Zero;
        var byText = IntPtr.Zero;
        EnumChildWindows(parent, (hwnd, lp) =>
        {
            if (GetClassNameOf(hwnd) != "Button")
            {
                return true;
            }

            var id = GetDlgCtrlID(hwnd);
            if (id == DwgBatchFilePicker.FolderButtonId)
            {
                byId = hwnd;
                return false;
            }

            if (byText == IntPtr.Zero && GetWindowTextOf(hwnd) == "选择文件夹")
            {
                byText = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return byId != IntPtr.Zero ? byId : byText;
    }

    private static string GetWindowTextOf(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassNameOf(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(128);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private IntPtr DialogWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WmCommand && lParam == _buttonHwnd)
            {
                // 不能在此同步 Close（对话框消息分发未返回，同步关闭会卡死），转投自定义消息异步处理。
                PostMessageW(hWnd, WmFolderClickAsync, IntPtr.Zero, IntPtr.Zero);
            }
            else if (msg == WmFolderClickAsync)
            {
                if (!_folderPicked)
                {
                    HandleFolderClick(hWnd);
                }

                return IntPtr.Zero;
            }
            else if (msg == WmNcDestroy)
            {
                // 窗口销毁前恢复，避免悬挂托管 WndProc
                var orig = _origWndProc;
                _subclassInstalled = false;
                _origWndProc = IntPtr.Zero;
                _dialogHwnd = IntPtr.Zero;
                _buttonHwnd = IntPtr.Zero;
                if (orig != IntPtr.Zero)
                {
                    return CallWindowProcW(orig, hWnd, msg, wParam, lParam);
                }

                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            DwgBatchFilePicker.LogFailure("DialogWndProc", ex);
        }

        return CallWindowProcW(_origWndProc, hWnd, msg, wParam, lParam);
    }

    private void HandleFolderClick(IntPtr hwnd)
    {
        if (_folderPicked)
        {
            return;
        }

        try
        {
            var folder = ReadCurrentFolder();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show(
                    "当前位置不是文件夹，请进入（或选中）要批量打印的文件夹后再点击「选择文件夹」。",
                    "批量打印",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _folderPicked = true;
            FolderChosen = true;
            FolderPath = folder;
            UninstallSubclass();
            if (hwnd != IntPtr.Zero)
            {
                PostMessageW(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE：按取消关闭，结果由 FolderChosen 标志决定
            }
            else
            {
                _dialog.Close(0);
            }
        }
        catch (Exception ex)
        {
            DwgBatchFilePicker.LogFailure("HandleFolderClick", ex);
        }
    }

    private string? ReadCurrentFolder()
    {
        IShellItem? selection = null;
        try
        {
            selection = _dialog.GetCurrentSelection();
            var path = DwgBatchFilePicker.TryGetFileSystemPath(selection);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                return path;
            }
        }
        catch (Exception ex)
        {
            DwgBatchFilePicker.LogFailure("ReadCurrentFolder/selection", ex);
        }
        finally
        {
            if (selection != null)
            {
                Marshal.ReleaseComObject(selection);
            }
        }

        IShellItem? folder = null;
        try
        {
            folder = _dialog.GetFolder();
            var path = DwgBatchFilePicker.TryGetFileSystemPath(folder);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                return path;
            }
        }
        catch (Exception ex)
        {
            DwgBatchFilePicker.LogFailure("ReadCurrentFolder/folder", ex);
        }
        finally
        {
            if (folder != null)
            {
                Marshal.ReleaseComObject(folder);
            }
        }

        return null;
    }

    /// <summary>按进程位数选择 SetWindowLongPtrW / SetWindowLongW（x86 无 Ptr 入口）。</summary>
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            return SetWindowLongPtrW(hWnd, nIndex, dwNewLong);
        }

        return new IntPtr(SetWindowLongW(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private delegate bool EnumThreadWindowsProc(IntPtr hwnd, IntPtr lParam);

    private delegate bool EnumChildWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadWindowsProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildWindowsProc lpfn, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct COMDLG_FILTERSPEC
{
    [MarshalAs(UnmanagedType.LPWStr)] public string pszName;

    [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
}

internal enum Sigdn : uint
{
    FileSystemPath = 0x80058000
}

[Flags]
internal enum Fos : uint
{
    NoChangeDir = 0x8,
    ForceFileSystem = 0x40,
    AllowMultiSelect = 0x200,
    PathMustExist = 0x800,
    FileMustExist = 0x1000
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

    void GetParent(out IShellItem ppsi);

    void GetDisplayName(Sigdn sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    void Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

    void GetPropertyStore(int flags, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

    void GetPropertyDescriptionList(IntPtr keyType, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

    void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);

    void GetCount(out uint pdwNumItems);

    void GetItemAt(uint dwIndex, out IShellItem ppsi);

    void EnumItems(out IntPtr ppenumShellItems);
}

[ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileDialog
{
    [PreserveSig]
    int Show(IntPtr parent);

    void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] COMDLG_FILTERSPEC[] rgFilterSpec);

    void SetFileTypeIndex(uint iFileType);

    uint GetFileTypeIndex();

    [PreserveSig]
    int Advise(IFileDialogEvents pfde, out uint pdwCookie);

    void Unadvise(uint dwCookie);

    void SetOptions(uint fos);

    uint GetOptions();

    void SetDefaultFolder(IShellItem psi);

    void SetFolder(IShellItem psi);

    IShellItem GetFolder();

    IShellItem GetCurrentSelection();

    void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

    void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

    void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

    void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    IShellItem GetResult();

    void AddPlace(IShellItem psi, int fdap);

    void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

    void Close(int hr);

    void SetClientGuid(ref Guid guid);

    void ClearClientData();

    void SetFilter(IntPtr pFilter);
}

[ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOpenDialog
{
    [PreserveSig]
    int Show(IntPtr parent);

    void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] COMDLG_FILTERSPEC[] rgFilterSpec);

    void SetFileTypeIndex(uint iFileType);

    uint GetFileTypeIndex();

    [PreserveSig]
    int Advise(IFileDialogEvents pfde, out uint pdwCookie);

    void Unadvise(uint dwCookie);

    void SetOptions(uint fos);

    uint GetOptions();

    void SetDefaultFolder(IShellItem psi);

    void SetFolder(IShellItem psi);

    IShellItem GetFolder();

    IShellItem GetCurrentSelection();

    void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

    void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

    void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

    void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    IShellItem GetResult();

    void AddPlace(IShellItem psi, int fdap);

    void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

    void Close(int hr);

    void SetClientGuid(ref Guid guid);

    void ClearClientData();

    void SetFilter(IntPtr pFilter);

    IShellItemArray GetResults();

    IShellItemArray GetSelectedItems();
}

[ComImport, Guid("E6FDD21A-163F-4975-9C8C-A69F1BA37034"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileDialogCustomize
{
    void EnableOpenDropDown(int dwID);

    void AddMenu(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    void AddPushButton(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    void AddComboBox(int dwID);

    void AddRadioButtonList(int dwID);

    void AddCheckButton(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel, bool bChecked);

    void AddEditBox(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszText);

    void AddSeparator(int dwID);

    void AddText(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszText);

    void MakeProminent(int dwID);

    void SetControlLabel(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    void GetControlState(int dwID, out uint pdwState);

    void SetControlState(int dwID, uint dwState);

    void GetEditBoxText(int dwID, out IntPtr ppszText);

    void SetEditBoxText(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszText);

    void GetCheckButtonState(int dwID, out bool pbChecked);

    void SetCheckButtonState(int dwID, bool bChecked);

    void AddControlItem(int dwID, int dwItemID, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    void RemoveControlItem(int dwID, int dwItemID);

    void RemoveAllControlItems(int dwID);

    void GetControlItemState(int dwID, int dwItemID, out uint pdwState);

    void SetControlItemState(int dwID, int dwItemID, uint dwState);

    void GetSelectedControlItem(int dwID, out int pdwItemID);

    void SetSelectedControlItem(int dwID, int dwItemID);

    void StartVisualGroup(int dwID, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    void EndVisualGroup();
}

[ComImport, Guid("973510DB-7D7F-452B-8975-74A85828D354"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileDialogEvents
{
    [PreserveSig]
    int OnFileOk(IFileDialog pfd);

    [PreserveSig]
    int OnFolderChanging(IFileDialog pfd, IShellItem psiFolder);

    [PreserveSig]
    int OnFolderChange(IFileDialog pfd);

    [PreserveSig]
    int OnSelectionChange(IFileDialog pfd);

    [PreserveSig]
    int OnShareViolation(IFileDialog pfd, IShellItem psi, out int pResponse);

    [PreserveSig]
    int OnTypeChange(IFileDialog pfd);

    [PreserveSig]
    int OnOverwrite(IFileDialog pfd, IShellItem psi, out int pResponse);
}

[ComImport, Guid("36149969-0A8F-49C8-8B00-4AECB20222FB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileDialogControlEvents
{
    [PreserveSig]
    int OnItemSelected(IFileDialogCustomize pfdc, int dwIDCtl, int dwIDItem);

    [PreserveSig]
    int OnButtonClicked(IFileDialogCustomize pfdc, int dwIDCtl);

    [PreserveSig]
    int OnCheckButtonToggled(IFileDialogCustomize pfdc, int dwIDCtl, bool bChecked);

    [PreserveSig]
    int OnControlActivating(IFileDialogCustomize pfdc, int dwIDCtl);
}
