using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DotNetPlugin.NativeBindings.SDK
{
    // https://github.com/x64dbg/x64dbg/blob/development/src/bridge/bridgemain.h
    partial class Bridge
    {
        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgCmdExec([MarshalAs(UnmanagedType.LPUTF8Str)] string cmd);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgCmdExecDirect([MarshalAs(UnmanagedType.LPUTF8Str)] string cmd);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgDisasmFastAt(nuint addr, ref BASIC_INSTRUCTION_INFO basicinfo);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgGetBranchDestination(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        private static extern bool DbgGetCommentAt(nuint addr, IntPtr text);

        public static unsafe bool DbgGetCommentAt(nuint addr, out string text)
        {
            var textBufferPtr = stackalloc byte[MAX_COMMENT_SIZE];
            var success = DbgGetCommentAt(addr, new IntPtr(textBufferPtr));
            text = success ? new IntPtr(textBufferPtr).MarshalToStringUTF8(MAX_COMMENT_SIZE) : default;
            return success;
        }

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        private static extern bool DbgGetLabelAt(nuint addr, SEGMENTREG segment, IntPtr text);

        public static unsafe bool DbgGetLabelAt(nuint addr, SEGMENTREG segment, out string text)
        {
            var textBufferPtr = stackalloc byte[MAX_LABEL_SIZE];
            var success = DbgGetLabelAt(addr, segment, new IntPtr(textBufferPtr));
            text = success ? new IntPtr(textBufferPtr).MarshalToStringUTF8(MAX_LABEL_SIZE) : default;
            return success;
        }

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        private static extern bool DbgGetModuleAt(nuint addr, IntPtr text);

        public static unsafe bool DbgGetModuleAt(nuint addr, out string text)
        {
            var textBufferPtr = stackalloc byte[MAX_MODULE_SIZE];
            var success = DbgGetModuleAt(addr, new IntPtr(textBufferPtr));
            text = success ? new IntPtr(textBufferPtr).MarshalToStringUTF8(MAX_MODULE_SIZE) : default;
            return success;
        }

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgIsDebugging(); //Active debugging session with a binary.

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgIsRunning(); //This always seem to return true.

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgIsRunLocked(); // The debugger engine execution state machine is locked by an ongoing operation or script.

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgModBaseFromName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgValFromString([MarshalAs(UnmanagedType.LPUTF8Str)] string @string);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgValToString([MarshalAs(UnmanagedType.LPUTF8Str)] string @string, nuint value);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgDisasmAt(nuint addr, ref DISASM_INSTR instr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgMemFindBaseAddr(nuint addr, out nuint size);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetCommentAt(nuint addr, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetLabelAt(nuint addr, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetAutoCommentAt(nuint addr, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetAutoLabelAt(nuint addr, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgClearAutoCommentRange(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgClearAutoLabelRange(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgClearCommentRange(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgClearLabelRange(nuint start, nuint end);





        [Flags]
        public enum ADDRINFOFLAGS
        {
            flagmodule = 0x1,
            flaglabel = 0x2,
            flagcomment = 0x4,
            flagbookmark = 0x8,
            flagfunction = 0x10,
            flagloop = 0x20,
            flagargs = 0x40,
            flagNoFuncOffset = 0x80
        }

        // Represents FUNCTION, LOOP, ARG structures (simplified)
        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct FUNCTION_LOOP_INFO // Name adjusted for clarity
        {
            public nuint start;
            public nuint end;
            public nuint instrcount;
            // Note: The C++ FUNCTION_LOOP_INFO might have other fields like 'manual', 'depth'
            // which would need to be added here if flags indicate they are used/valid.
            // For basic symbol lookup, these might not be essential.
        }

        // Struct to receive data from _dbg_addrinfoget, using StringBuilder for output strings
        // In NativeMethods.cs

        // Struct to pass to _dbg_addrinfoget, using IntPtr for output string buffers
        [StructLayout(LayoutKind.Sequential, Pack = NativePacking, CharSet = CharSet.Ansi)]
        public struct BRIDGE_ADDRINFO_NATIVE // Renamed for clarity
        {
            public ADDRINFOFLAGS flags; // Input: Flags indicating what info to retrieve
            public IntPtr module;       // Output: Pointer to buffer for Module name (SizeConst=256)
            public IntPtr label;        // Output: Pointer to buffer for Label name (SizeConst=256)
            public IntPtr comment;      // Output: Pointer to buffer for Comment text (SizeConst=512)
            [MarshalAs(UnmanagedType.Bool)]
            public bool isbookmark;           // Output: Bookmark status
            public FUNCTION_LOOP_INFO function; // Output: Function info
            public FUNCTION_LOOP_INFO loop;     // Output: Loop info
            public FUNCTION_LOOP_INFO args;     // Output: Argument info
        }

        // Update the P/Invoke signature to use the new struct name
        [DllImport("x64dbg.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "_dbg_addrinfoget", ExactSpelling = true, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DbgAddrInfoGet(nuint addr, int segment, ref BRIDGE_ADDRINFO_NATIVE addrinfo); // Use new struct



        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        private static extern bool DbgMemRead(nuint va, IntPtr dest, nuint size);

        public static unsafe bool DbgMemRead<T>(nuint va, T[] buffer, nuint size) where T : unmanaged
        {
            if (buffer is null || size > (nuint)buffer.Length) return false;

            fixed (T* ptr = buffer)
            {
                return DbgMemRead(va, (IntPtr)ptr, size);
            }
        }

        public static unsafe bool DbgMemRead<T>(nuint va, ref T dest, nuint size) where T : struct
        {
            if (size > (nuint)Marshal.SizeOf(dest)) return false;

            var handle = GCHandle.Alloc(dest, GCHandleType.Pinned);
            try
            {
                var success = DbgMemRead(va, handle.AddrOfPinnedObject(), size);
                dest = success ? Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()) : default;
                return success;
            }
            finally
            {
                handle.Free();
            }
        }


        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool DbgMemWrite(nuint va, IntPtr src, nuint size);

        public static unsafe bool DbgMemWrite<T>(nuint va, T[] buffer, nuint size) where T : unmanaged
        {
            if (buffer is null || size > (nuint)buffer.Length * (nuint)sizeof(T))
                return false;

            fixed (T* ptr = buffer)
            {
                return DbgMemWrite(va, (IntPtr)ptr, size);
            }
        }

        public static unsafe bool DbgMemWrite<T>(nuint va, ref T src, nuint size) where T : struct
        {
            if (size > (nuint)Marshal.SizeOf<T>())
                return false;

            var handle = GCHandle.Alloc(src, GCHandleType.Pinned);
            try
            {
                return DbgMemWrite(va, handle.AddrOfPinnedObject(), size);
            }
            finally
            {
                handle.Free();
            }
        }






        public const uint MEM_IMAGE = 0x1000000; // Memory type constant
        // Define MEMORY_BASIC_INFORMATION matching Windows API for the target platform
        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect; // PROTECT_FLAGS enum
#if AMD64 // PartitionId exists on 64-bit and >= Win8. Check if needed.
            public ushort PartitionId;
        // Packing might require explicit padding if PartitionId isn't always present or if alignment dictates
        // public ushort ReservedPadding; // Example
#endif
            public nuint RegionSize;      // SIZE_T maps to nuint
            public uint State;             // MEM_STATE enum (e.g., MEM_COMMIT)
            public uint Protect;           // PROTECT_FLAGS enum (e.g., PAGE_EXECUTE_READ)
            public uint Type;              // MEM_TYPE enum (e.g., MEM_IMAGE)
        }

        // Define MEMPAGE matching C++ struct
        [StructLayout(LayoutKind.Sequential, Pack = NativePacking, CharSet = CharSet.Ansi)]
        public struct MEMPAGE
        {
            public MEMORY_BASIC_INFORMATION mbi;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] // MAX_MODULE_SIZE = 256
            public string info; // This likely holds module path/name
        }

        // Define MEMMAP_NATIVE matching C++ MEMMAP struct
        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct MEMMAP_NATIVE
        {
            public int count;      // C++ uses int
            public IntPtr page;    // C++ uses MEMPAGE* pointer
        }

        // --- P/Invoke Signatures ---

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern bool DbgMemMap(ref MEMMAP_NATIVE memmap); // Use the native struct

#if AMD64 // Define X64 symbol in your project properties for x64 builds
        public const int NativePacking = 16;
            public const bool Is64Bit = true;
#else // Assuming x86 otherwise
                public const int NativePacking = 8;
                public const bool Is64Bit = false;
#endif

        [StructLayout(LayoutKind.Sequential, Pack = NativePacking, CharSet = CharSet.Ansi)]
        public struct THREADINFO_NATIVE
        {
            public int ThreadNumber;
            public IntPtr Handle;       // HANDLE maps to IntPtr (matches target architecture size)
            public uint ThreadId;       // DWORD maps to uint

#if AMD64
            public ulong ThreadStartAddress; // duint maps to ulong on x64
        public ulong ThreadLocalBase;   // duint maps to ulong on x64
#else
            public uint ThreadStartAddress; // duint maps to uint on x86
            public uint ThreadLocalBase;   // duint maps to uint on x86
#endif

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] // MAX_THREAD_NAME_SIZE = 256
            public string threadName;
        }

        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct THREADALLINFO
        {
            public THREADINFO_NATIVE BasicInfo;

#if AMD64
            public ulong ThreadCip;        // duint maps to ulong on x64
#else
            public uint ThreadCip;        // duint maps to uint on x86
#endif

            public uint SuspendCount;      // DWORD maps to uint
            public int Priority;           // THREADPRIORITY likely maps to int
            public int WaitReason;         // THREADWAITREASON likely maps to int - CORRECT ORDER
            public uint LastError;         // DWORD maps to uint - CORRECT ORDER
            public FILETIME UserTime;      // CORRECT ORDER
            public FILETIME KernelTime;    // CORRECT ORDER
            public FILETIME CreationTime;  // CORRECT ORDER
            public ulong Cycles;           // ULONG64 maps to ulong (always 64-bit) - CORRECT ORDER
        }

        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct THREADLIST_NATIVE
        {
            public int count;
            public IntPtr list;            // Correct order: pointer first
            public int CurrentThread;     // Correct order: index second
        }

        // --- P/Invoke Signatures ---

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void DbgGetThreadList(ref THREADLIST_NATIVE list);


        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgXrefGet(nuint addr, ref XREF_INFO info);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgLoopAdd(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgLoopGet(int depth, nuint addr, out nuint start, out nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgLoopDel(int depth, nuint addr);

        public enum SEGMENTREG
        {
            SEG_DEFAULT,
            SEG_ES,
            SEG_DS,
            SEG_FS,
            SEG_GS,
            SEG_CS,
            SEG_SS
        }

        public enum DISASM_INSTRTYPE
        {
            instr_normal,
            instr_branch,
            instr_stack
        }

        public enum DISASM_ARGTYPE
        {
            arg_normal,
            arg_memory
        }

        public enum XREFTYPE
        {
            XREF_NONE,
            XREF_DATA,
            XREF_JMP,
            XREF_CALL
        }        

        #region Definitions for BASIC_INSTRUCTION_INFO.type
        public const uint TYPE_VALUE = 1;
        public const uint TYPE_MEMORY = 2;
        public const uint TYPE_ADDR = 4;
        #endregion

        public enum MEMORY_SIZE
        {
            size_byte = 1,
            size_word = 2,
            size_dword = 4,
            size_qword = 8,
            size_xmmword = 16,
            size_ymmword = 32
        }

        [Serializable]
        public struct VALUE_INFO
        {
            public nuint value;
            public MEMORY_SIZE size;
        }

        [Serializable]
        public unsafe struct MEMORY_INFO
        {
            public nuint value; //displacement / addrvalue (rip-relative)
            public MEMORY_SIZE size; //byte/word/dword/qword

            private fixed byte mnemonicBytes[MAX_MNEMONIC_SIZE];
            public string mnemonic
            {
                get
                {
                    fixed (byte* ptr = mnemonicBytes)
                        return new IntPtr(ptr).MarshalToStringUTF8(MAX_MNEMONIC_SIZE);
                }
            }
        }

        [Serializable]
        public unsafe struct BASIC_INSTRUCTION_INFO
        {
            public uint type; //value|memory|addr
            public VALUE_INFO value; //immediat
            public MEMORY_INFO memory;
            public nuint addr; //addrvalue (jumps + calls)
            public BlittableBoolean branch; //jumps/calls
            public BlittableBoolean call; //instruction is a call

            public int size;

            private fixed byte instructionBytes[MAX_MNEMONIC_SIZE * 4];
            public string instruction
            {
                get
                {
                    fixed (byte* ptr = instructionBytes)
                        return new IntPtr(ptr).MarshalToStringUTF8(MAX_MNEMONIC_SIZE * 4);
                }
                set
                {
                    fixed (byte* ptr = instructionBytes)
                        value.MarshalToPtrUTF8(new IntPtr(ptr), MAX_MNEMONIC_SIZE * 4);
                }
            }
        }

        [Serializable]
        public unsafe struct DISASM_ARG
        {
            public DISASM_ARGTYPE type;
            public SEGMENTREG segment;
            private fixed byte _mnemonic[MAX_MNEMONIC_SIZE];
            public string mnemonic
            {
                get
                {
                    fixed (byte* ptr = _mnemonic)
                        return new IntPtr(ptr).MarshalToStringUTF8(MAX_MNEMONIC_SIZE);
                }
            }
            public nuint constant;
            public nuint value;
            public nuint memvalue;
        }

        [Serializable]
        public unsafe struct DISASM_INSTR
        {
            private fixed byte _instruction[MAX_MNEMONIC_SIZE];
            public string instruction
            {
                get
                {
                    fixed (byte* ptr = _instruction)
                        return new IntPtr(ptr).MarshalToStringUTF8(MAX_MNEMONIC_SIZE);
                }
            }
            public DISASM_INSTRTYPE type;
            public int argcount;
            public int instr_size;

            public DISASM_ARG arg0; // Maps to arg[0]
            public DISASM_ARG arg1; // Maps to arg[1]
            public DISASM_ARG arg2; // Maps to arg[2]
        }

        [Serializable]
        public unsafe struct XREF_INFO
        {
            public nuint refcount;

            private XREF_RECORD* _references;
            public XREF_RECORD[] references
            {
                get
                {
                    if (_references == null || refcount == UIntPtr.Zero)
                        return new XREF_RECORD[0];

                    var result = new XREF_RECORD[(int)refcount];
                    for (int i = 0; i < (int)refcount; i++)
                    {
                        result[i] = _references[i];
                    }

                    return result;
                }
            }
        }

        [Serializable]
        public unsafe struct XREF_RECORD
        {
            public nuint addr;
            public XREFTYPE type;
        }


        public enum SCRIPTBRANCHTYPE
        {
            scriptnobranch,
            scriptjmp,
            scriptjnejnz,
            scriptjejz,
            scriptjbjl,
            scriptjajg,
            scriptjbejle,
            scriptjaejge,
            scriptcall
        }

        [StructLayout(LayoutKind.Sequential, Pack = NativePacking, CharSet = CharSet.Ansi)]
        public struct SCRIPTBRANCH
        {
            public SCRIPTBRANCHTYPE type;
            public int dest;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string branchlabel;
        }

        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct SELECTIONDATA
        {
            public nuint start;
            public nuint end;
        }

        [StructLayout(LayoutKind.Sequential, Pack = NativePacking)]
        public struct TYPEDESCRIPTOR
        {
            public BlittableBoolean expanded;
            public BlittableBoolean reverse;
            public IntPtr name; // const char*
            public nuint addr;
            public nuint offset;
            public int id;
            public int size;
            public IntPtr callback; // TYPETOSTRING function pointer
            public IntPtr userdata; // void*
        }


        #region Core Bridge Functions

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr BridgeInit(); // Returns const wchar_t* error string or null

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr BridgeStart(); // Returns const wchar_t* error string or null

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr BridgeAlloc(nuint size);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void BridgeFree(IntPtr ptr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool BridgeSettingGet([MarshalAs(UnmanagedType.LPUTF8Str)] string section, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, IntPtr value);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool BridgeSettingGetUint([MarshalAs(UnmanagedType.LPUTF8Str)] string section, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, ref nuint value);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool BridgeSettingSet([MarshalAs(UnmanagedType.LPUTF8Str)] string section, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool BridgeSettingSetUint([MarshalAs(UnmanagedType.LPUTF8Str)] string section, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, nuint value);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool BridgeSettingFlush();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool BridgeSettingRead(ref int errorLine);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int BridgeGetDbgVersion();

        #endregion

        #region Remaining Debugger (Dbg) Subsystem Functions

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr DbgInit(); // Returns const char*

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgExit();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgMemGetPageSize(nuint baseAddr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgIsValidExpression([MarshalAs(UnmanagedType.LPUTF8Str)] string expression);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgIsJumpGoingToExecute(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgGetBookmarkAt(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetBookmarkAt(nuint addr, bool isbookmark);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern uint DbgGetBpxTypeAt(nuint addr); // Returns BPXTYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgGetRegDump(IntPtr regdump); // ref REGDUMP structure

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgMemIsValidReadPtr(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgGetBpList(uint type, IntPtr list); // Passing BPMAP structure pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgGetFunctionTypeAt(nuint addr); // Returns FUNCTYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgGetLoopTypeAt(nuint addr, int depth); // Returns LOOPTYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgScriptLoad([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgScriptUnload();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgScriptRun(int destline);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgScriptStep();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgScriptBpToggle(int line);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgScriptBpGet(int line);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgScriptCmdExec([MarshalAs(UnmanagedType.LPUTF8Str)] string command);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgScriptAbort();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgScriptGetLineType(int line); // Returns SCRIPTLINETYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgScriptSetIp(int line);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgScriptGetBranchInfo(int line, ref SCRIPTBRANCH info);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgSymbolEnum(nuint baseAddr, IntPtr cbSymbolEnum, IntPtr user);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgSymbolEnumFromCache(nuint baseAddr, IntPtr cbSymbolEnum, IntPtr user);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgAssembleAt(nuint addr, [MarshalAs(UnmanagedType.LPUTF8Str)] string instruction);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgStackCommentGet(nuint addr, IntPtr comment); // ref STACK_COMMENT structure

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgSettingsUpdated();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgMenuEntryClicked(int hEntry);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgFunctionGet(nuint addr, out nuint start, out nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgFunctionOverlaps(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgFunctionAdd(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgFunctionDel(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgArgumentGet(nuint addr, out nuint start, out nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgArgumentOverlaps(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgArgumentAdd(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgArgumentDel(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgLoopOverlaps(int depth, nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgXrefAdd(nuint addr, nuint from);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgXrefDelAll(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgGetXrefCountAt(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgGetXrefTypeAt(nuint addr); // Returns XREFTYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgIsBpDisabled(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetAutoBookmarkAt(nuint addr);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgClearAutoBookmarkRange(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetAutoFunctionAt(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgClearAutoFunctionRange(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgGetStringAt(nuint addr, IntPtr text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr DbgFunctions(); // Returns const DBGFUNCTIONS*

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgWinEvent(IntPtr message, ref nint result); // MSG* pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgWinEventGlobal(IntPtr message); // MSG* pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgGetTimeWastedCounter();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgGetArgTypeAt(nuint addr); // Returns ARGTYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr DbgGetEncodeTypeBuffer(nuint addr, out nuint size);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgReleaseEncodeTypeBuffer(IntPtr buffer);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int DbgGetEncodeTypeAt(nuint addr, nuint size); // Returns ENCODETYPE enum value

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgGetEncodeSizeAt(nuint addr, nuint codesize);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgSetEncodeType(nuint addr, nuint size, int type); // ENCODETYPE enum integer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgDelEncodeTypeRange(nuint start, nuint end);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgDelEncodeTypeSegment(nuint start);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgGetWatchList(IntPtr list); // ListOf macro object pointer

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgSelChanged(int hWindow, nuint VA);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr DbgGetProcessHandle();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern IntPtr DbgGetThreadHandle();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern uint DbgGetProcessId();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern uint DbgGetThreadId();

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgGetPebAddress(uint ProcessId);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgGetTebAddress(uint ThreadId);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool DbgAnalyzeFunction(nuint entry, IntPtr graph); // BridgeCFGraphList*

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern nuint DbgEval([MarshalAs(UnmanagedType.LPUTF8Str)] string expression, ref bool success);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void DbgMenuPrepare(int hMenu);

        #endregion

        







    }
}
