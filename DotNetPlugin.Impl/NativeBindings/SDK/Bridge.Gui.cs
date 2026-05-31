using System;
using System.Runtime.InteropServices;

namespace DotNetPlugin.NativeBindings.SDK
{
    // https://github.com/x64dbg/x64dbg/blob/development/src/bridge/bridgemain.h
    partial class Bridge
    {
        public const int GUI_MAX_LINE_SIZE = 65536;

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        private static extern bool GuiGetLineWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, IntPtr text);

        public static unsafe bool GuiGetLineWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, out string text)
        {
            // alternatively we could implement a custom marshaler (ICustomMarshaler) but that wont't work for ref/out parameters for some reason...
            var textBuffer = Marshal.AllocHGlobal(GUI_MAX_LINE_SIZE);
            try
            {
                var success = GuiGetLineWindow(title, textBuffer);
                text = success ? textBuffer.MarshalToStringUTF8(GUI_MAX_LINE_SIZE) : default;
                return success;
            }
            finally { Marshal.FreeHGlobal(textBuffer); }
        }

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddStatusBarMessage([MarshalAs(UnmanagedType.LPUTF8Str)] string msg);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiLogClear();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddLogMessage([MarshalAs(UnmanagedType.LPUTF8Str)] string msg);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateDisassemblyView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        private static extern bool GuiGetDisassembly(nuint addr, IntPtr text);

        public static unsafe bool GuiGetDisassembly(nuint addr, out string text)
        {
            var textBuffer = Marshal.AllocHGlobal(GUI_MAX_LINE_SIZE);
            try
            {
                var success = GuiGetDisassembly(addr, textBuffer);
                text = success ? textBuffer.MarshalToStringUTF8(GUI_MAX_LINE_SIZE) : default;
                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(textBuffer);
            }
        }

        #region GUI Subsystem Functions

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr GuiTranslateText([MarshalAs(UnmanagedType.LPUTF8Str)] string source); // Returns const char*

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiDisasmAt(nuint addr, nuint cip);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSetDebugState(int state); // DBGSTATE enum integer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSetDebugStateFast(int state); // DBGSTATE enum integer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateAllViews();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateRegisterView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateBreakpointsView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateWindowTitle([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr GuiGetWindowHandle(); // Returns HWND

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiDumpAt(nuint va);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptAdd(int count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] lines);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptClear();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptSetIp(int line);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptError(int line, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptSetTitle([MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptSetInfoLine(int line, [MarshalAs(UnmanagedType.LPUTF8Str)] string info);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptMessage([MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int GuiScriptMsgyn([MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiScriptEnableHighlighting(bool enable);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSymbolLogAdd([MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSymbolLogClear();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSymbolSetProgress(int percent);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSymbolUpdateModuleList(int count, IntPtr modules); // SYMBOLMODULEINFO* pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSymbolRefreshCurrent();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceAddColumn(int width, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceSetRowCount(int count);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int GuiReferenceGetRowCount();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int GuiReferenceSearchGetRowCount();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceDeleteAllColumns();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceInitialize([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceSetCellContent(int row, int col, [MarshalAs(UnmanagedType.LPUTF8Str)] string str);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr GuiReferenceGetCellContent(int row, int col); // Returns char* string pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr GuiReferenceSearchGetCellContent(int row, int col); // Returns char* string pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceReloadData();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceSetSingleSelection(int index, bool scroll);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceSetProgress(int progress);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceSetCurrentTaskProgress(int progress, [MarshalAs(UnmanagedType.LPUTF8Str)] string taskTitle);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiReferenceSetSearchStartCol(int col);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiStackDumpAt(nuint addr, nuint csp);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateDumpView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateWatchView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateThreadView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateMemoryView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddRecentFile([MarshalAs(UnmanagedType.LPUTF8Str)] string file);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSetLastException(uint exception);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int GuiMenuAdd(int hMenu, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int GuiMenuAddEntry(int hMenu, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuAddSeparator(int hMenu);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuClear(int hMenu);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuRemove(int hEntryMenu);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool GuiSelectionGet(int hWindow, ref SELECTIONDATA selection);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool GuiSelectionSet(int hWindow, ref SELECTIONDATA selection);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAutoCompleteAddCmd([MarshalAs(UnmanagedType.LPUTF8Str)] string cmd);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAutoCompleteDelCmd([MarshalAs(UnmanagedType.LPUTF8Str)] string cmd);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAutoCompleteClearAll();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateSideBar();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiRepaintTableView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdatePatches();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateCallStack();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateSEHChain();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiLoadSourceFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int line);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetIcon(int hMenu, IntPtr icon); // const ICONDATA* layout pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetEntryIcon(int hEntry, IntPtr icon); // const ICONDATA* layout pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetEntryChecked(int hEntry, bool @checked);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetVisible(int hMenu, bool visible);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetEntryVisible(int hEntry, bool visible);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetName(int hMenu, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetEntryName(int hEntry, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiMenuSetEntryHotkey(int hEntry, [MarshalAs(UnmanagedType.LPUTF8Str)] string hack);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiShowCpu();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddQWidgetTab(IntPtr qWidget);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiShowQWidgetTab(IntPtr qWidget);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiCloseQWidgetTab(IntPtr qWidget);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiExecuteOnGuiThread(IntPtr cbGuiThread); // GUICALLBACK function pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateTimeWastedCounter();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSetGlobalNotes([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiGetGlobalNotes(out IntPtr text); // returns double-pointer char** text

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSetDebuggeeNotes([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiGetDebuggeeNotes(out IntPtr text); // returns double-pointer char** text

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiDumpAtN(nuint va, int index);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiDisplayWarning([MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiRegisterScriptLanguage(IntPtr info); // SCRIPTTYPEINFO* reference structure pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUnregisterScriptLanguage(int id);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateArgumentWidget();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiFocusView(int hWindow);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool GuiIsUpdateDisabled();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateEnable(bool updateNow);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateDisable();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool GuiLoadGraph(IntPtr graph, nuint addr); // BridgeCFGraphList*

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint GuiGraphAt(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateGraphView();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiDisableLog();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiEnableLog();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddFavouriteTool([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string description);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddFavouriteCommand([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string shortcut);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSetFavouriteToolShortcut([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string shortcut);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiFoldDisassembly(nuint startAddress, nuint length);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiSelectInMemoryMap(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiGetActiveView(IntPtr activeView); // ACTIVEVIEW* pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiAddInfoLine([MarshalAs(UnmanagedType.LPUTF8Str)] string infoLine);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiProcessEvents();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr GuiTypeAddNode(IntPtr parent, ref TYPEDESCRIPTOR type);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool GuiTypeClear();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiUpdateTypeWidget();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiCloseApplication();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void GuiFlushLog();

        #endregion
    }
}
