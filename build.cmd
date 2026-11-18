@echo off
rem Builds PokeCreator on Windows with the C# compiler that ships with .NET Framework 4.x.
rem No Visual Studio needed. Output goes to bin\  (or open PokeCreator.sln in Visual Studio).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo Could not find csc.exe from .NET Framework 4. & goto :fail)
if not exist bin mkdir bin
set OPTS=/nologo /optimize+ /nowarn:1591,0168,0219,0649
echo == Core
"%CSC%" %OPTS% /target:library /out:bin\PokeCreator.Core.dll /recurse:src\PokeCreator.Core\*.cs /r:System.Core.dll || goto :fail
echo == Import
"%CSC%" %OPTS% /target:library /out:bin\PokeCreator.Import.dll /recurse:src\PokeCreator.Import\*.cs /r:bin\PokeCreator.Core.dll /r:System.Core.dll || goto :fail
echo == Import tool
"%CSC%" %OPTS% /target:exe /out:bin\PokeCreatorImport.exe src\PokeCreator.Tools\ImportCli.cs /r:bin\PokeCreator.Core.dll /r:bin\PokeCreator.Import.dll || goto :fail
echo == Game runtime
"%CSC%" %OPTS% /target:winexe /platform:x64 /unsafe /out:bin\Game.exe /recurse:src\PokeCreator.Runtime\*.cs /r:bin\PokeCreator.Core.dll /r:System.Core.dll || goto :fail
echo == Studio
"%CSC%" %OPTS% /target:winexe /out:bin\PokeCreatorStudio.exe /recurse:src\PokeCreator.Studio\*.cs /r:bin\PokeCreator.Core.dll /r:bin\PokeCreator.Import.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Data.dll || goto :fail
copy /y lib\win-x64\*.dll bin\ >nul
if not exist bin\BaseData mkdir bin\BaseData
copy /y data\base\*.json bin\BaseData\ >nul
echo.
echo Build OK. Run bin\PokeCreatorStudio.exe
exit /b 0
:fail
echo.
echo BUILD FAILED
exit /b 1
