$message = "Test 01";Write-Host $message;
$myExpr = "get-process | $sorting"
invoke-expression $myExpr

$c=' get-service | where {$_.status -eq "Running"}'
iex $c | measure-object


