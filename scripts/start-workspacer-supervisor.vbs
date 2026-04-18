Option Explicit

Dim fso, scriptDir, repoRoot, ps1Path, command
Dim processService, processStartup, processClass, processId, returnCode

Set fso = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
repoRoot = fso.GetParentFolderName(scriptDir)
ps1Path = fso.BuildPath(scriptDir, "start-workspacer-supervisor.ps1")
command = "powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & ps1Path & """"

Set processService = GetObject("winmgmts:\\.\root\cimv2")
Set processStartup = processService.Get("Win32_ProcessStartup").SpawnInstance_
processStartup.ShowWindow = 0

Set processClass = processService.Get("Win32_Process")
returnCode = processClass.Create(command, repoRoot, processStartup, processId)

If returnCode <> 0 Then
    WScript.Quit returnCode
End If
