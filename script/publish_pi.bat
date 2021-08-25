@echo off
if "%1"=="" GOTO HELP
if "%1"=="-h" GOTO HELP
IF NOT "%2"=="-s" GOTO BUILD
echo Stopping services
plink -no-antispoof -pw glimmrtv glimmrtv@%1 "echo glimmrtv | sudo -S service glimmr stop"
plink -no-antispoof -pw glimmrtv glimmrtv@%1 "echo glimmrtv | sudo -S pkill -f Glimmr"

:BUILD
IF NOT "%2"=="-k" GOTO BUILD2
echo Killing Glimmr task...
plink -no-antispoof -pw glimmrtv glimmrtv@%1 "echo glimmrtv | sudo -S service glimmr stop"
plink -no-antispoof -pw glimmrtv glimmrtv@%1 "echo glimmrtv | sudo -S pkill -f Glimmr"

:BUILD2
dotnet publish ..\src\Glimmr\Glimmr.csproj /p:PublishProfile=LinuxARM -o ..\src\Glimmr\bin\linuxARM
cd ..\src\Glimmr\bin\linuxARM
IF "%2"=="-j" GOTO JS
IF "%2"=="-c" GOTO CSS
IF "%2"=="-w" GOTO WEB

echo Copying new files...
IF "%3"=="-f" GOTO FULL
pscp -P 22 -r -pw glimmrtv .\Glimmr.Views.dll glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr.Views.dll
pscp -P 22 -r -pw glimmrtv .\Glimmr.deps.json glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr.deps.json
pscp -P 22 -r -pw glimmrtv .\Glimmr.Views.pdb glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr.Views.pdb
pscp -P 22 -r -pw glimmrtv .\wwwroot\js\* glimmrtv@%1:/home/glimmrtv/glimmr/bin/wwwroot/js/
pscp -P 22 -r -pw glimmrtv .\wwwroot\css\* glimmrtv@%1:/home/glimmrtv/glimmr/bin/wwwroot/css/
pscp -P 22 -r -pw glimmrtv .\Glimmr.dll glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr.dll
pscp -P 22 -r -pw glimmrtv .\Glimmr.pdb glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr.pdb
pscp -P 22 -r -pw glimmrtv .\Glimmr.runtimeconfig.json glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr.runtimeconfig.json
pscp -P 22 -r -pw glimmrtv .\Glimmr glimmrtv@%1:/home/glimmrtv/glimmr/bin/Glimmr
GOTO NEXT
:JS
pscp -P 22 -r -pw glimmrtv .\wwwroot\js\* glimmrtv@%1:/home/glimmrtv/glimmr/bin/wwwroot/js/
GOTO END
:CSS
pscp -P 22 -r -pw glimmrtv .\wwwroot\css\* glimmrtv@%1:/home/glimmrtv/glimmr/bin/wwwroot/css/
GOTO END
:WEB
pscp -P 22 -r -pw glimmrtv .\wwwroot\* glimmrtv@%1:/home/glimmrtv/glimmr/bin/wwwroot/
GOTO END
:FULL
pscp -P 22 -r -pw glimmrtv .\* glimmrtv@%1:/home/glimmrtv/glimmr/bin/
:NEXT
IF NOT "%2"=="-s" GOTO END

echo Restarting glimmr...
plink -no-antispoof -pw glimmrtv glimmrtv@%1 "echo glimmrtv | sudo -S service glimmr start"
GOTO END
:HELP
echo _______________________________
echo Script Usage:
echo publish_pi.bat <IP_OF_GLIMMR> -c/-f/-k/-j/-s
echo OR
echo publish_pi.bat -h (Show Help)
echo _______________________________
echo Flags:
echo -c: Copy CSS only, don't restart
echo -f: Full copy, use AFTER -s or -k flags
echo -k: Kill Glimmr, assumes is being run manually. Can be used before the -f flag;
echo -j: Copy Java only, don't restart
echo -s: Stop glimmr service, then copy. Can be used before the -f flag.
echo _______________________________
:END
cd ../../../..
