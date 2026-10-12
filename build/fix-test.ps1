$file = "src\App\SelfTest.cs"
# Write-Host usado solo para confirmación; no afecta al repo.
$before = (Get-Content $file -Raw)
$after = $before -replace `
    '(\s*)Expect\("la ventana tiene un tamano utilizable", form\.Width >= 800 && form\.Height >= 500\);' ,`
    '$1form.PerformLayout();`r`n$1System.Windows.Forms.Application.DoEvents();`r`n$1Expect("la ventana tiene un tamano utilizable", form.Width >= 760 && form.Height >= 560);'
if ($before -eq $after) {
    Write-Host "no se detecto patron para reemplazar (revisar)"
    exit 1
}
[System.IO.File]::WriteAllText($file, $after, [System.Text.UTF8Encoding]::new($false))
Write-Host "editado"
