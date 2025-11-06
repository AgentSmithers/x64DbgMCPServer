# X64Dbg MCP Server (plugin)
This project is a starting point for building an MCP (Memory Command Protocol) server plugin for x96/x64/x32dbg https://github.com/x64dbg/x64dbg/ using C# on the classic Windows-only .NET Framework platform (No ASP.NET Core hosting required).

The plugin acts as a lightweight HTTP interface bridge between an MCP client and the debugger, allowing you to have an LLM MCP client interactively send commands to inspect memory, disassemble, query registers, manipulate labels/comments, and more—all remotely and programmatically.

On top of essential bindings to the x64dbg debugger engine, this template offers a clean project structure, a built-in command system, and a simple HTTP listener that exposes your commands through a text-based API. 
![image](https://github.com/user-attachments/assets/4b3c3a02-edc0-48e2-93eb-a8c1727b5017)

## Features
* ✅ Self-hosted HTTP command interface (no ASP.NET Core required)
* ✅ Lightweight, zero-dependency binary deployment
* ✅ Modular commands with parameter mapping
* ✅ Direct interaction with registers, memory, threads, disassembly
* ✅ Bi-directional AI/LLM command support
* ✅ Plugin reload without restarting x64dbg
* ✅ Expression function and menu extension support

## Cursor Support
Cursor Connection:
```json
{
  "mcpServers": {
    "AgentSmithers X64Dbg MCP Server": {
      "url": "http://127.0.0.1:50300/sse"
    }
  }
}
```
![image](https://github.com/user-attachments/assets/22414a30-d41e-4c3d-9b4f-f168f0498736)

![image](https://github.com/user-attachments/assets/53ba58e6-c97c-4c31-b57c-832951244951)

## Claude Desktop support

### MCPProxy STIDO<->SSE Bridge required: https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE/tree/master
Claude Configuration Connection:
```
{
  "mcpServers": {
    "x64Dbg": {
      "command": "C:\\MCPProxy-STDIO-to-SSE.exe",
      "args": ["http://localhost:50300"]
    }
  }
}
```
![image](https://github.com/user-attachments/assets/0b089015-2270-4b39-ae23-42ce4322ba75)


![image](https://github.com/user-attachments/assets/3ef4cb69-0640-4ea0-b313-d007cdb003a8)


## Windsurf support

### MCPProxy STIDO<->SSE Bridge required: https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE/tree/master
Claude Configuration Connection:
```
{
  "mcpServers": {
    "AgentSmithers x64Dbg STDIO<->SSE": {
      "command": "C:\\MCPProxy-STDIO-to-SSE.exe",
      "args": ["http://localhost:50300"]
    }
  }
}
```
![image](https://github.com/user-attachments/assets/df900c88-2291-47af-9789-1b17ff51cfa9)

Known: Context deadline exceeded (timeout) issue with directly using SSE.

# X64Dbg MCP Client - Need a client to sample?
[mcp-csharp-sdk-client.zip](https://github.com/user-attachments/files/19697365/mcp-csharp-sdk-client.zip)

Open the project
Edit line 590 in Program.cs and enter your GeminiAI key from Google Cloud API.
Edit line 615 in Program.cs and enter in your MCP Server IP: Location = "http://192.168.x.x:50300/sse",
Open your x96 debugger, your logs should reflect that the server automatically loaded.
To interact with the server by hand instead of using the AI, uncomment line 634 and comment out line 635.
Hit start debug on the client and the AI should automatically execute the Prompt located on line 434 (Program.cs)

![image](https://github.com/user-attachments/assets/ebf2ad81-0672-4ceb-be6e-a44c625cd6d0)

Access the latest sample client to use as a starting point of integration with this project: https://github.com/AgentSmithers/mcp-csharp-sdk-client/

## Sample Conversations:
### AI Tasked with loading a file, counting the internal modules and begin labeling important material functions.
https://github.com/AgentSmithers/x64DbgMCPServer/blob/master/Sample1

### Singleshot Speedhack identification
https://github.com/AgentSmithers/x64DbgMCPServer/blob/master/Sample2

## Prerequisites
To build and run this project, you'll need:
- Visual Studio Build Tools (2019 v16.7 or later)
- .NET Framework 4.7.2 SDK
- 3F/DllExport

## Getting Started
Clone or fork the project: git clone https://github.com/AgentSmithers/x64DbgMCPServer

Download [DLlExport.bat](https://github.com/3F/DllExport/releases/download/1.8/DllExport.bat) and place it in the root folder of the project (Where the solutions[.sln] file is located). Then, run the `DllExport.bat`.

# For X86 / 32bit support, you must have .NET Framework 2.0 and .NET Framework 3.5 installed through "add/remove windows components" in Add or remove programed (appwiz.cpl).

In the DllExport GUI,
1. Check the `Installed` checkbox.
2. Set the Namespace for DllExport to `System.Runtime.InteropServices`.
3. Choose the target platform(`x64` or `x86`).
4. Click Apply.

<img width="518" height="462" alt="image" src="https://github.com/user-attachments/assets/5148316e-37fe-48d4-baec-73fb2ef1d3ed" />

Open the .sln solution file and build.

If you get this error, clean and rebuild the DotNetPlugin.Stub
<img width="998" height="155" alt="image" src="https://github.com/user-attachments/assets/a4bd8b06-3b35-4e3d-bdea-d7f8627178b3" />


📌 Tip: If you see `x64DbgMCPServer.dll` in the output folder, rename it to `x64DbgMCPServer.dp64` so that x64dbg can load the plugin.

copy the files (x64DbgMCPServer\bin\x64\Debug) into the x64DBG plugin (x96\release\x64\plugins\x64DbgMCPServer) folder to run.
Note: If the plugin folder does not exist, create it and create a x64DbgMCPServer subfolder and copy the files within.
![image](https://github.com/user-attachments/assets/8511452e-b65c-4bc8-83ff-885c384d0bbe)

<img width="880" height="463" alt="image" src="https://github.com/user-attachments/assets/05994544-1b00-4b2d-9998-bf61c72b1425" />


Sample Debug log when loaded

![image](https://github.com/user-attachments/assets/02eb35d8-8584-46de-83c6-b535d23976b9)

Start the Debugger, goto plugins -> Click "Start MCP Server"

Connect to it with your prefered MCP Client on port 50300 via SSE.

### Checking command results

Some x64dbg commands don't return meaningful booleans. Use these helpers:

- ExecuteDebuggerCommandWithVar: run a command and read a debugger variable afterwards.
  Example:
  - `ExecuteDebuggerCommandWithVar command="init notepad.exe" resultVar=$pid pollMs=100 pollTimeoutMs=5000`
  - Returns the value of `$pid` (e.g., `0x1234`) after init; non-zero means started

- ExecuteDebuggerCommandWithOutput: run a command and capture the log output.
  Example:
  - `ExecuteDebuggerCommandWithOutput command="bplist"`
  - Returns the log text produced by the command

## Troubleshooting

### "Access is denied" when starting MCP server

If you see `Failed to start MCP server: Access is denied` in the x64dbg logs (Alt+L), this is because Windows requires special permissions to listen on HTTP URLs. You have two options:

**Option 1: Run as Administrator (Quick fix)**
- Right-click `x64dbg.exe` and select "Run as administrator"

**Option 2: Grant URL permissions (Recommended)**
Run these commands in an elevated PowerShell/Command Prompt:
```cmd
netsh http add urlacl url=http://+:50300/sse/ user=Everyone
netsh http add urlacl url=http://+:50300/message/ user=Everyone
```

After running these commands, you can start x64dbg normally and the MCP server will work.


### Sample Commands using the X64Dbg MCP Client
I've validated several commands already and they are working wonders. I'm especially excited to be using this system to explore how AI-assisted reverse engineering could streamline security workflows.
Once the MCP server is running (via the plugin menu in x64dbg), you can issue commands like:
```
ExecuteDebuggerCommand command=init C:\InjectGetTickCount\InjectSpeed.exe
ExecuteDebuggerCommand command="AddFavouriteCommand Log s, NameOfCmd"
ReadDismAtAddress addressStr=0x000000014000153f, byteCount=5
ReadMemAtAddress addressStr=00007FFA1AC81000, byteCount=5
WriteMemToAddress addressStr=0x000000014000153f, byteString=90 90 90 90 90 90
CommentOrLabelAtAddress addressStr=0x000000014000153f, value=Test, mode=Comment
CommentOrLabelAtAddress addressStr=0x000000014000153f, value=
GetAllRegisters
GetLabel addressStr=0x000000014000153f
GetAllActiveThreads
GetAllModulesFromMemMap
GetCallStack
These commands return JSON or text-formatted output that's suitable for ingestion by AI models or integration scripts. Example:
```
![image](https://github.com/user-attachments/assets/f954feab-4518-4368-8b0a-d6ec07212122)
![image](https://github.com/user-attachments/assets/2952e4eb-76ef-460c-9124-0e3c1167fa3d)

## Debugging
DotNetPlugin.Impl contains the following within the project build post commands. Update it to reflect the corret path to x64dbg for faster debugging.
Upon rebuilding X64Dbg will autoload the new plugin and you can reattach to the X64Dbg instance if needed.
```
xcopy /Y /I "$(TargetDir)*.*" "C:\Users\User\Desktop\x96\release\x64\plugins\x64DbgMCPServer"
C:\Users\User\Desktop\x96\release\x64\x64dbg.exe
```
## Actively working on implementing several functions
Not every command is fully implemented althrough I am actively working on getting this project moving to support full stack, thread and module dumps for the AI to query.

## How It Works
The MCP server runs a simple HTTP listener and routes incoming commands to C# methods marked with the [Command] attribute. These methods can perform any logic (e.g., memory reads, disassembly, setting breakpoints) and return data in a structured format back to a MCP client.

## Known Issues
ExecuteDebuggerCommand always returns true as it pertains to the comment successfully being execute and not the results of the actual command.(Fix was implemented,needs checking.)\
Currently the already compiled version is set to listen on all IP's on port 50300 thus requiring Administrative privileges. Future releases will look to detect this and will listen only on 127.0.0.1 so it may be used without administrative privileges.(See the `Troubleshooting` section)

If upon launch x64/x32 dbg crashes, ensure the DLL's are not being blocked by windows.
This causes .NET Framework to refuse loading the assemblies for security reasons.

## Special thanks
⚡ With the help of DotNetPluginCS by Adams85. That and roughly ~20 hours of focused coding, MCP Protocol review resulted in a decent proof-of-concept self-contained HTTP MCP server plugin for x64dbg.

## Integration Notes
One of the most satisfying aspects of this project was overcoming the challenge of building an HTTP server entirely self-contained — no Kestrel, no ASP.NET, just raw HttpListener powering your reverse engineering automation.

I plan to continue improving this codebase as part of my journey into AI-assisted analysis, implementation security, and automation tooling.

If you'd like help creating your own integration, extending this plugin, or discussing potential use cases — feel free to reach out (see contact info in the repo or my profile). I'm eager to collaborate and learn with others exploring this space.

💻 Let's reverse engineer smarter. Not harder.

Cheers 🎉

Https://ControllingTheInter.net
