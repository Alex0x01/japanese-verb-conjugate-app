rem - check the game is there first
if not exist ..\bin\Release\netcoreapp3.1\jp_verb_console.exe goto End
cd ..\bin\Release\netcoreapp3.1
start jp_verb_console.exe -help -mode 0 -num_questions 25 
echo Finished
:End