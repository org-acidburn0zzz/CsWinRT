for /f "delims=" %i in ('dir project.assets.json *.rsp /s/b') do @del /s "%i"
for /f "delims=" %i in ('dir "generated files" /s /b') do @rd /s/q "%i"
rd /s/q _build